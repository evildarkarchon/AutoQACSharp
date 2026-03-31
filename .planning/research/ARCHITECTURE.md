# Architecture Patterns: xEdit Log File Parsing Integration

**Domain:** Desktop app bug fix -- switching from stdout-based to log-file-based xEdit result parsing
**Researched:** 2026-03-30 (updated with xEdit source verification)

## Current Architecture (What Exists)

The cleaning pipeline currently flows through five services in a linear chain:

```
CleaningOrchestrator
  --> CleaningService.CleanPluginAsync()
        --> XEditCommandBuilder.BuildCommand()
        --> ProcessExecutionService.ExecuteAsync()  <-- captures stdout/stderr
        --> XEditOutputParser.ParseOutput(result.OutputLines)  <-- parses stdout
  --> XEditLogFileService.ReadLogFileAsync()  <-- reads full log file post-exit
  --> XEditOutputParser.ParseOutput(logLines)  <-- re-parses log file content
```

**The bugs:**
1. `ProcessExecutionService` sets `RedirectStandardOutput = true`, collecting empty output (xEdit never writes to stdout).
2. `XEditLogFileService.GetLogFilePath()` uses the executable stem (`XEDIT_log.txt` for `xEdit.exe`), which is wrong for universal `xEdit.exe` with game flags (should be `SSEEdit_log.txt` etc.).
3. `ReadLogFileAsync` reads the entire log file, mixing historical content with the current run.

**Partial fix already in place:** `CleaningOrchestrator` already has a secondary path (lines 400-423) that calls `XEditLogFileService.ReadLogFileAsync()` after the process exits and re-parses with `XEditOutputParser.ParseOutput(logLines)`. However, this only "enriches" the results -- it treats log files as a fallback, not the primary source.

## Recommended Architecture

### Design Principle: Minimal Surgical Changes

This is a bugfix, not a rewrite. The goal is to make the log file the primary parsing source while keeping the existing service boundaries intact. No new services need to be created.

### Key Architectural Change: Game-Aware Log File Naming

The most critical change is that `XEditLogFileService.GetLogFilePath()` must accept a `GameType` parameter and compute the log filename using xEdit's internal naming convention (`wbAppName + 'Edit_log.txt'`), NOT the executable filename.

```
Current:  executable stem -> SSEEDIT_log.txt (wrong for xEdit.exe)
Fixed:    GameType -> SSEEdit_log.txt (correct for all executables)
```

This requires the orchestrator to pass the detected `GameType` through to the log file service.

### Component Boundaries After Fix

| Component | Current Role | New Role | Change Type |
|-----------|-------------|----------|-------------|
| `ProcessExecutionService` | Captures stdout/stderr + manages process lifecycle | **Process lifecycle only** -- stop capturing stdout | Modify |
| `CleaningService` | Builds command, executes, parses stdout | Builds command, executes, returns raw exit status (no parsing) | Modify |
| `XEditLogFileService` | Reads full log file with staleness detection, executable-stem naming | **Game-aware naming**, reads only new content via offset tracking | Modify |
| `XEditOutputParser` | Parses line lists into `CleaningStatistics` | **No change** -- same regex parsing, same interface | None |
| `CleaningOrchestrator` | Orchestrates session, enriches stats from log | **Primary parsing path** uses game-aware log file + offset; stdout fallback removed | Modify |

### Data Flow After Fix

```
CleaningOrchestrator (per plugin loop)
  |
  |-- 0. Compute game-aware log file path
  |      XEditLogFileService.GetLogFilePath(xEditPath, gameType)
  |          returns: string (e.g., "C:\xEdit\SSEEdit_log.txt")
  |
  |-- 1. Record log file offset BEFORE launch
  |      XEditLogFileService.GetCurrentOffset(logFilePath)
  |          returns: long (file size in bytes, or 0 if file doesn't exist)
  |
  |-- 2. Launch xEdit process
  |      CleaningService.CleanPluginAsync(plugin, ...)
  |          --> XEditCommandBuilder.BuildCommand()
  |          --> ProcessExecutionService.ExecuteAsync()
  |          returns: CleaningResult { ExitCode, TimedOut, Duration }
  |                   (no Statistics -- stdout is gone)
  |
  |-- 3. Read NEW log content after process exit (skip if force-killed)
  |      XEditLogFileService.ReadNewLogContentAsync(logFilePath, previousOffset)
  |          returns: (List<string> lines, string? error)
  |
  |-- 4. Check for exception log
  |      XEditLogFileService.ReadExceptionLogAsync(xEditPath, processStartTime)
  |          returns: (string? content, string? error)
  |
  |-- 5. Parse log lines into statistics
  |      XEditOutputParser.ParseOutput(logLines)
  |          returns: CleaningStatistics
  |
  |-- 6. Combine into PluginCleaningResult
  |      Attach statistics + any log warnings to the result
  |
  |-- 7. Report to state
  |      IStateService.AddDetailedCleaningResult(result)
```

### What Changes in Each Service

#### 1. XEditLogFileService -- Game-Aware Naming + Offset-Based Reading

**File:** `AutoQAC/Services/Cleaning/XEditLogFileService.cs`

**Critical change:** `GetLogFilePath` must use a GameType-to-prefix mapping instead of executable stem:

```csharp
private static readonly Dictionary<GameType, string> GameLogFileNames = new()
{
    { GameType.Oblivion, "TES4Edit_log.txt" },
    { GameType.SkyrimLe, "TES5Edit_log.txt" },
    { GameType.SkyrimSe, "SSEEdit_log.txt" },
    { GameType.SkyrimVr, "TES5VREdit_log.txt" },
    { GameType.Fallout3, "FO3Edit_log.txt" },
    { GameType.FalloutNewVegas, "FNVEdit_log.txt" },
    { GameType.Fallout4, "FO4Edit_log.txt" },
    { GameType.Fallout4Vr, "FO4VREdit_log.txt" },
};
```

**New methods to add:**
- `GetLogFilePath(string xEditExecutablePath, GameType gameType)` -- game-aware path
- `GetExceptionLogPath(string xEditExecutablePath)` -- still uses executable stem (different convention)
- `GetCurrentOffset(string logFilePath)` -- returns file size or 0
- `ReadNewLogContentAsync(string logFilePath, long previousOffset, CancellationToken)` -- offset-based read
- `ReadExceptionLogAsync(string xEditExecutablePath, DateTime processStartTime, CancellationToken)` -- reads exception log with staleness check

**Implementation notes:**
- Use `FileStream` with `FileShare.ReadWrite` for lock-safe reads
- Handle 3MB truncation (if offset > file.Length, read from 0)
- Exponential backoff retry: 250ms, 500ms, 750ms

**Exception log path:** Still uses executable stem (different convention from main log):
```csharp
public string GetExceptionLogPath(string xEditExecutablePath)
{
    var dir = Path.GetDirectoryName(xEditExecutablePath)!;
    var stem = Path.GetFileNameWithoutExtension(xEditExecutablePath).ToUpperInvariant();
    return Path.Combine(dir, $"{stem}Exception.log");
}
```

#### 2. ProcessExecutionService -- Stop Redirecting stdout/stderr

**File:** `AutoQAC/Services/Process/ProcessExecutionService.cs`

**Required changes:**
- Set `RedirectStandardOutput = false` and `RedirectStandardError = false`
- Remove `OutputDataReceived` and `ErrorDataReceived` handlers
- Remove `BeginOutputReadLine()` and `BeginErrorReadLine()` calls
- Keep `UseShellExecute = false` (required for MO2 wrapping, PID tracking, and `WaitForExitAsync`)
- `ProcessResult.OutputLines` and `ErrorLines` become always-empty

#### 3. CleaningService -- Remove Output Parsing

**File:** `AutoQAC/Services/Cleaning/CleaningService.cs`

**Required changes:**
- Remove `IXEditOutputParser` constructor dependency
- Remove `outputParser.ParseOutput(result.OutputLines)` call
- Return `CleaningResult` with `Statistics = null`

#### 4. CleaningOrchestrator -- Primary Log Parsing Path (Game-Aware)

**File:** `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`

The orchestrator already knows the `GameType` (detected in step 3 of `StartCleaningAsync`). It passes this through to the log file service.

**New flow in the per-plugin loop:**
```csharp
// Step 0: Compute game-aware log path
var logPath = logFileService.GetLogFilePath(xEditPath, gameType);

// Step 1: Record offset before launch
var logOffset = logFileService.GetCurrentOffset(logPath);

// Step 2: Execute cleaning (unchanged)
result = await cleaningService.CleanPluginAsync(plugin, progress, cts.Token, ...);

// Step 3: Read new log content (skip if force-killed)
if (result is { Success: true } or { Status: CleaningStatus.Cleaned })
{
    var (logLines, logError) = await logFileService.ReadNewLogContentAsync(
        logPath, logOffset, cts.Token);
    if (logError != null)
        logParseWarning = logError;
    else if (logLines.Count > 0)
        logStats = outputParser.ParseOutput(logLines);
}

// Step 4: Check exception log
var (exceptionContent, _) = await logFileService.ReadExceptionLogAsync(
    xEditPath, pluginStartTime, cts.Token);
if (exceptionContent != null)
    logger.Warning("xEdit exception for {Plugin}: {Exception}", plugin.FileName, exceptionContent);
```

## Patterns to Follow

### Pattern 1: Offset-Based Append Isolation

**What:** Record file size before operation, read only new bytes after operation completes.
**When:** Any time you read from a file that is appended to by an external process.
**Why:** Avoids parsing stale content. More reliable than timestamp comparison.

### Pattern 2: Service Responsibilities Stay Narrow

**What:** Each service owns one concern. `ProcessExecutionService` = process lifecycle. `XEditLogFileService` = file I/O + naming. `XEditOutputParser` = regex matching. `CleaningOrchestrator` = composition.
**When:** Always.

### Pattern 3: Error Aggregation at Orchestrator Level

**What:** Individual services return `(result, error?)` tuples. The orchestrator decides severity.
**When:** Log file reading, exception log reading.

### Pattern 4: Two Naming Conventions for Two Log Types

**What:** Main log uses game prefix (`SSEEdit_log.txt`). Exception log uses executable stem (`SSEEDITException.log`).
**When:** Computing log file paths.
**Why:** These are two different systems within xEdit. The main log is named by xEdit's `SaveLogs` procedure using `wbAppName + wbToolName`. The exception log is named by the `nxExceptionHook` library using the executable filename.

## Anti-Patterns to Avoid

### Anti-Pattern 1: Deriving Log Filename from Executable Name

**What:** Using `Path.GetFileNameWithoutExtension(exePath)` to construct the log filename.
**Why bad:** When using `xEdit.exe -SSE`, the executable is `xEdit.exe` but the log is `SSEEdit_log.txt`. The executable name does not determine the log filename.
**Instead:** Use the GameType-to-prefix mapping derived from xEdit source code.

### Anti-Pattern 2: Splitting Parsing Across Two Services

**What:** Having both `CleaningService` and `CleaningOrchestrator` parse xEdit output.
**Why bad:** Confusion about which statistics are authoritative.
**Instead:** Remove parsing from `CleaningService`. The orchestrator is the single owner.

### Anti-Pattern 3: Watching Log File During Execution

**What:** Using `FileSystemWatcher` or polling during xEdit execution.
**Why bad:** xEdit writes its log file **on exit** (`FormClose`), not incrementally.
**Instead:** Read once after process exits.

## Suggested Implementation Order

### Phase 1: XEditLogFileService -- Game-Aware Naming + Offset Methods

**Deliverables:**
- Add GameType-to-log-filename mapping
- Add `GetLogFilePath(string xEditPath, GameType gameType)` overload
- Add `GetExceptionLogPath(string xEditPath)`
- Add `GetCurrentOffset(string logFilePath)`
- Add `ReadNewLogContentAsync(string logFilePath, long previousOffset, ...)`
- Add `ReadExceptionLogAsync(string xEditPath, DateTime processStartTime, ...)`
- Add unit tests covering all game types and edge cases

### Phase 2: ProcessExecutionService -- Stop Stdout Capture

**Deliverables:**
- Set `RedirectStandardOutput = false`, `RedirectStandardError = false`
- Remove event handlers and `BeginOutputReadLine()`/`BeginErrorReadLine()`
- Update tests

### Phase 3: Integration -- Orchestrator + CleaningService Changes

**Deliverables:**
- Remove `IXEditOutputParser` from `CleaningService`
- Wire orchestrator to use game-aware log path + offset-based reading
- Add force-kill check before log reading
- Update orchestrator and cleaning service tests

### Phase 4: Cleanup

**Deliverables:**
- Remove old `ReadLogFileAsync` and `GetLogFilePath(string)` overload
- Remove `OutputLines`/`ErrorLines` from `ProcessResult`
- Remove `IsCompletionLine` from parser if unused
- Remove timestamp-based staleness detection
- Final test pass

## Test Impact Assessment

| Test File | Impact | Changes Needed |
|-----------|--------|----------------|
| `XEditLogFileServiceTests.cs` | **Significant** | New tests for game-aware naming, offset reading, exception log, truncation edge case |
| `XEditOutputParserTests.cs` | **Minor** | Add test for "File has not changed, removing:" to verify it does NOT match (case sensitivity) |
| `CleaningServiceTests.cs` | **Moderate** | Remove `IXEditOutputParser` mock, update assertions |
| `CleaningOrchestratorTests.cs` | **Significant** | Rewrite log parsing mocks, add GameType parameter, remove stdout fallback tests |
| `ProcessExecutionServiceTests.cs` | **Moderate** | Remove stdout collection tests |
| `DependencyInjectionTests.cs` | **Minor** | Should pass without changes |

## Sources

- xEdit source code: `xeMainForm.pas` lines 6138-6168, 11120-11600, 20260-20340 (HIGH confidence)
- xEdit source code: `xeInit.pas` lines 696-832 for wbAppName mappings (HIGH confidence)
- Direct codebase analysis of all affected AutoQAC services (HIGH confidence)
- [TES5Edit/TES5Edit GitHub Repository](https://github.com/TES5Edit/TES5Edit)

---

*Architecture analysis: 2026-03-30 -- Updated with xEdit source verification*
