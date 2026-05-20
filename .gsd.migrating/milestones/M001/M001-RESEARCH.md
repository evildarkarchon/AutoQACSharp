# Project Research Summary

**Project:** AutoQAC -- xEdit Log File Parsing Fix
**Domain:** Desktop app bugfix -- migrating from dead stdout capture to game-aware log file parsing
**Researched:** 2026-03-30
**Confidence:** HIGH

## Executive Summary

AutoQAC has a fundamental data pipeline bug: it captures xEdit's stdout (which is always empty -- xEdit never writes to stdout) and tries to parse cleaning results from nothing. A secondary fallback reads the log file but uses the wrong filename for universal `xEdit.exe` setups and reads the entire file including historical content from prior sessions. The fix is surgical: four existing services need modification, zero new services are required, and the existing `XEditOutputParser` regex engine is correct as-is.

The most critical discovery from xEdit source code verification is that log file naming follows xEdit's internal game-mode resolution (`wbAppName + "Edit_log.txt"`), NOT the executable filename. When users run `xEdit.exe -SSE`, the log is `SSEEdit_log.txt`, but AutoQAC looks for `XEDIT_log.txt` -- which does not exist. This single naming bug means all universal-xEdit users get zero cleaning statistics. The fix requires a `GameType`-to-prefix lookup table derived from xEdit's `xeInit.pas` source. A second structural change -- offset-based file reading to isolate current-run content from appended history -- eliminates the inflated statistics problem caused by parsing all historical log entries.

Key risks are low because the changes are well-scoped: file I/O with `FileStream`/`FileShare.ReadWrite` (standard .NET pattern), a static lookup table (verified against source), and orchestrator rewiring that follows existing service boundaries. The only medium-confidence area is exception log format details, since the `nxExceptionHook` library is third-party and not in the xEdit repository.

## Key Findings

### Recommended Stack

No new dependencies are needed. All changes use `System.IO` and `System.Text` from .NET 10.

**Core technologies:**
- **FileStream + StreamReader**: Read log files from byte offset with `FileShare.ReadWrite` for lock-safe access
- **FileInfo.Length**: Capture pre-launch file size as the read-start offset (lightweight stat call, no file handle)
- **Encoding.UTF8 (pragmatic)**: xEdit writes ANSI/CP1252, but all regex keywords are ASCII -- UTF-8 decodes identically for the matching portion

**Rejected alternatives:**
- `RandomAccess` -- overkill for small text files, requires manual buffer/encoding management
- `File.ReadAllLinesAsync` -- no offset support, uses `FileShare.Read` which causes IOExceptions when file is still locked

### Expected Features

**Must have (table stakes):**
- Game-aware log file naming using `GameType`-to-prefix mapping (fixes the critical naming bug)
- Offset-based reading to isolate current run's log content from historical appended data
- Parse ITM removal lines (`Removing:`), UDR lines (`Undeleting:`/`Skipping:`), and completion summaries
- Exception log detection and error surfacing when xEdit crashes
- Handle force-killed xEdit (no log written -- skip parsing, report failure)
- Support universal `xEdit.exe` with game flags (`-SSE`, `-FO4`, etc.)

**Should have (differentiators):**
- Parse per-record detail (FormIDs, record paths) from cleaning lines
- Parse deleted navmesh warnings for actionable user feedback
- Parse "nothing to clean" state (completion lines with zero counts = already-clean plugin)
- Parse partial form creation lines (`Making Partial Form:` in xEdit 4.1.6+)
- Surface exception log content in cleaning results (not just "failed")

**Defer (v2+):**
- `-R:<path>` custom log path support (edge case, document as known limitation)
- Per-record detail extraction beyond counting
- "Can't remove" warning surfacing (rare occurrence)
- Elapsed time parsing from summary lines

### Architecture Approach

This is a four-service modification with no new abstractions. `XEditLogFileService` gets game-aware naming and offset-based reading. `ProcessExecutionService` stops redirecting stdout/stderr. `CleaningService` drops its parser dependency. `CleaningOrchestrator` becomes the single owner of log-based parsing, replacing the current dual-path (stdout primary + log fallback) with a single path (log file only).

**Components and responsibilities after fix:**

1. **XEditLogFileService** -- Game-aware log path computation, offset capture, new-content-only reading, exception log detection
2. **ProcessExecutionService** -- Process lifecycle only (launch, wait, kill); no stdout/stderr capture
3. **CleaningService** -- Command building and execution; returns exit status without statistics
4. **XEditOutputParser** -- Unchanged; parses line arrays into `CleaningStatistics` via regex
5. **CleaningOrchestrator** -- Single owner of the parse pipeline: compute path, record offset, launch, read log, parse, combine results

**Data flow:**
```
Orchestrator -> LogFileService.GetLogFilePath(xEditPath, gameType)
Orchestrator -> LogFileService.GetCurrentOffset(logPath)
Orchestrator -> CleaningService.CleanPluginAsync(plugin, ...)
Orchestrator -> LogFileService.ReadNewLogContentAsync(logPath, offset)
Orchestrator -> LogFileService.ReadExceptionLogAsync(xEditPath, startTime)
Orchestrator -> OutputParser.ParseOutput(logLines)
Orchestrator -> StateService.AddDetailedCleaningResult(combined)
```

### Critical Pitfalls

1. **Log file naming mismatch** -- `GetLogFilePath` uses executable stem (`XEDIT_log.txt`) instead of game prefix (`SSEEdit_log.txt`). Fix: GameType-to-prefix lookup table from xEdit source. This is the root cause of zero statistics for universal xEdit users.

2. **Reading entire log file inflates statistics** -- xEdit appends across sessions. Reading everything counts historical ITMs. Fix: Record byte offset before launch via `FileInfo.Length`, seek to that offset after exit. Handle 3MB truncation edge case (offset > file size means read from 0).

3. **File lock contention after process exit** -- `File.ReadAllLinesAsync` uses `FileShare.Read`, which throws if Windows Defender or the indexer holds a write handle. Fix: `FileStream` with `FileShare.ReadWrite` plus exponential backoff retry (250ms, 500ms, 750ms).

4. **Force-killed xEdit writes no log** -- `Process.Kill()` skips `FormClose`, so `SaveLogs` never runs. Fix: Check termination status before attempting log read; report as `CleaningStatus.Failed` if force-killed.

5. **Two naming conventions for two log types** -- Main log uses game prefix (`SSEEdit_log.txt`). Exception log uses executable stem (`SSEEDITException.log`). These are different systems within xEdit. Do not conflate them.

## Implications for Roadmap

Based on the combined research, this is a 4-phase fix with clear dependency ordering. Each phase is independently testable.

### Phase 1: Foundation -- Game-Aware Log File Service

**Rationale:** All downstream phases depend on correct log file naming and offset-based reading. This phase is purely additive -- new methods on an existing service -- so it cannot break current behavior.

**Delivers:**
- `GetLogFilePath(string xEditPath, GameType gameType)` with full game-to-prefix mapping
- `GetExceptionLogPath(string xEditPath)` using executable stem convention
- `GetCurrentOffset(string logFilePath)` returning file size or 0
- `ReadNewLogContentAsync(string logFilePath, long previousOffset, CancellationToken)` with `FileShare.ReadWrite`, truncation handling, and retry logic
- `ReadExceptionLogAsync(string xEditPath, DateTime processStartTime, CancellationToken)` with staleness check
- Full unit test coverage for all 8 game types, edge cases (missing file, truncated file, locked file, stale exception log)

**Features addressed:** Game-aware naming, offset isolation, exception detection, lock-safe reads
**Pitfalls avoided:** #1 (naming mismatch), #2 (full-file read), #4 (FileShare), #6 (encoding -- documented), #7 (stale exception), #8 (truncation)

### Phase 2: Process Layer -- Stop Stdout Capture

**Rationale:** Independent from Phase 1; can be done in parallel or sequentially. Isolated to `ProcessExecutionService` only. Removes the useless stdout/stderr redirect that creates the illusion of output.

**Delivers:**
- `RedirectStandardOutput = false`, `RedirectStandardError = false`
- Removal of `OutputDataReceived`/`ErrorDataReceived` handlers
- Removal of `BeginOutputReadLine()`/`BeginErrorReadLine()` calls
- `UseShellExecute` stays `false` (preserves MO2 wrapping, PID tracking, `WaitForExitAsync`)
- Updated process service tests

**Features addressed:** Eliminate dead stdout capture
**Pitfalls avoided:** #10 (UseShellExecute change would break MO2)

### Phase 3: Integration -- Log-First Parsing in Orchestrator

**Rationale:** Depends on Phases 1 and 2. This is where the behavioral change lands -- the orchestrator switches from "parse stdout, enrich from log" to "parse log file exclusively."

**Delivers:**
- Orchestrator computes game-aware log path using detected `GameType`
- Records offset before each plugin launch
- Reads new log content after process exit
- Checks exception log for crash information
- Skips log parsing on force-kill (reports failure directly)
- Removes `IXEditOutputParser` dependency from `CleaningService`
- Adds verification test for "File has not changed, removing:" case-sensitivity (non-issue but worth proving)
- Adds multi-pass QAC output test

**Features addressed:** Correct end-to-end parsing, exception surfacing, force-kill handling
**Pitfalls avoided:** #3 (false positive verification), #5 (force-kill), #11 (multi-pass)

### Phase 4: Cleanup -- Remove Dead Code

**Rationale:** Can only happen after all functional changes are verified working. Removes confusing dead paths that would mislead future maintainers.

**Delivers:**
- Remove old `ReadLogFileAsync` and single-arg `GetLogFilePath(string)` overload
- Remove `OutputLines`/`ErrorLines` from `ProcessResult` (or make always-empty)
- Remove timestamp-based staleness detection
- Remove `IsCompletionLine` from parser if unused
- Update all remaining test mocks to reflect new behavior
- Final test pass

**Features addressed:** Code hygiene
**Pitfalls avoided:** #14 (timestamp precision), #15 (stale test mocks)

### Phase Ordering Rationale

- **Phase 1 first** because it is purely additive. New methods on an existing service, fully unit-testable in isolation. No behavioral change to existing code paths.
- **Phase 2 is independent** and can run alongside Phase 1. It only touches `ProcessExecutionService`. Ordering it second is logical but not strictly required.
- **Phase 3 must follow 1 and 2** because it integrates the new log service methods and requires stdout to already be removed (otherwise the dual parsing path creates confusion about which statistics are authoritative).
- **Phase 4 must be last** because removing old APIs before the new path is wired up would break the application.

### Research Flags

**Phases needing verification during planning:**
- **Phase 2:** Verify that `CreateNoWindow` behavior with no stdout redirect works correctly in MO2 mode. Manual test recommended.
- **Phase 3:** Decide whether multi-pass QAC output should sum across all passes (correct total) or report only first pass (user expectation). Research says summing is correct because subsequent passes produce zero-count lines only.

**Phases with standard patterns (skip deeper research):**
- **Phase 1:** All file I/O patterns are standard .NET. GameType mapping is fully documented from xEdit source.
- **Phase 4:** Pure deletion of dead code. No research needed.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | No new dependencies; all APIs verified against .NET 10 docs |
| Features | HIGH | Verified against xEdit Delphi source (`xeMainForm.pas`, `xeInit.pas`); real log examples cross-referenced |
| Architecture | HIGH | Based on direct analysis of all 5 affected AutoQAC services plus xEdit source |
| Pitfalls | HIGH | 14 of 16 pitfalls verified from source; exception log format is MEDIUM (third-party `nxExceptionHook`) |

**Overall confidence:** HIGH

### Gaps to Address

- **Exception log format:** The `nxExceptionHook` unit is third-party and not included in the xEdit repository. Known patterns (`which can not be found`, `which it does not have`) come from PACT, not direct source verification. Handle by implementing a conservative staleness check and treating exception content as opaque text.
- **MO2 + no stdout redirect:** When MO2 wraps xEdit and stdout is not redirected, verify the process completes normally. MO2 does not depend on xEdit's stdout, so this should work, but needs a manual smoke test during Phase 2.
- **Multi-pass aggregation policy:** Should AutoQAC sum cleaning counts across all QAC passes or report only the first? Summing is technically correct (total work done). Recommend summing with a code comment explaining why subsequent passes report zeros.

## Sources

### Primary (HIGH confidence)
- xEdit source code: `xeMainForm.pas` lines 6138-6168 (SaveLog/SaveLogs), 11120-11600 (UDR/ITM cleaning output), 20260-20340 (QAC execution flow)
- xEdit source code: `xeInit.pas` lines 696-832 (wbAppName per game mode)
- [TES5Edit/TES5Edit GitHub Repository](https://github.com/TES5Edit/TES5Edit)
- [FileStream Class - Microsoft .NET 10](https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream?view=net-10.0)
- [FileShare Enum - Microsoft .NET 10](https://learn.microsoft.com/en-us/dotnet/api/system.io.fileshare?view=net-10.0)
- Direct codebase analysis of AutoQAC services (CleaningOrchestrator, ProcessExecutionService, XEditLogFileService, CleaningService, XEditOutputParser)

### Secondary (MEDIUM confidence)
- [XEdit-PACT Source](https://github.com/GuidanceOfGrace/XEdit-PACT) -- log naming conventions, exception log detection patterns
- [xEdit What's New](https://github.com/TES5Edit/TES5Edit.github.io/blob/master/whatsnew.md) -- "Log file is overwritten at 3MB" (v3.0.30), log filename conventions (v3.0.23)
- [GitHub Issue SkyrimLL/SDPlus#953](https://github.com/SkyrimLL/SDPlus/issues/953) -- real xEdit log output examples

### Tertiary (LOW confidence)
- Exception log format details from PACT (nxExceptionHook is third-party, not in xEdit repo) -- needs validation during implementation

---
*Research completed: 2026-03-30*
*Ready for roadmap: yes*

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

# Technology Stack: xEdit Log File Parsing

**Project:** AutoQAC - xEdit Log Parsing Fix
**Researched:** 2026-03-30 (updated with xEdit source verification)
**Focus:** .NET 10 APIs for reading log files written by an external process on exit

## Problem Statement

AutoQAC currently captures xEdit stdout/stderr via `ProcessExecutionService.RedirectStandardOutput`. xEdit does not write to stdout -- it writes log files to its install directory on process exit. The service needs to:

1. Compute the correct log file path using game-aware naming (NOT executable stem)
2. Record the log file's byte offset before launching xEdit
3. Wait for xEdit to exit
4. Read only the new content appended after that offset
5. Handle the brief window where xEdit may still hold the file lock after its process reports as exited

## Recommended Approach

### Use `FileStream` with Seek -- Not `RandomAccess`, Not `File.ReadAllLinesAsync`

| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| `FileStream` | .NET 10 (System.IO) | Read log file bytes from a recorded offset | Provides `FileShare.ReadWrite` for lock-safe reads, `Seek` for offset positioning, and `ReadAsync(Memory<byte>)` for async I/O. Standard, well-understood, matches codebase conventions. |
| `StreamReader` | .NET 10 (System.IO) | Decode bytes to text lines | Wraps `FileStream` to handle encoding. Must be constructed on the already-seeked stream. |
| `FileInfo.Length` | .NET 10 (System.IO) | Capture file size as pre-launch offset | Lightweight stat call, does not open the file. Returns `0` if file does not exist yet. |
| `Encoding.UTF8` or system default | .NET 10 (System.Text) | Text decoding | xEdit writes ANSI (code page 1252) but regex keywords are all ASCII, so UTF-8 decoding works for counting. See Encoding section below. |

**Confidence:** HIGH -- verified against Microsoft .NET 10 API documentation and xEdit source code

### Why Not `RandomAccess`?

`RandomAccess` (introduced .NET 6) provides offset-based, thread-safe, stateless file I/O via `File.OpenHandle()` + `RandomAccess.ReadAsync(handle, buffer, offset)`.

**However, it is wrong for this use case because:**
- It operates on raw bytes (`Memory<byte>`), not text lines. We need line-delimited text for the regex parser.
- It requires manual buffer management and encoding.
- The file is small (xEdit logs are typically 1-50 KB per run).
- The codebase uses `StreamReader` elsewhere. `RandomAccess` would be inconsistent.

### Why Not `File.ReadAllLinesAsync`?

- Reads the **entire file** every time (no offset support)
- Opens with `FileShare.Read` internally (throws `IOException` if file is still locked)
- Cannot isolate current run's content from previous runs

## Game-Aware Log File Path Mapping

**NEW REQUIREMENT (discovered from xEdit source):** The log file is NOT named after the executable. It uses `wbAppName + wbToolName + '_log.txt'` where `wbAppName` is the game prefix from xEdit's internal mode detection.

| AutoQAC GameType | xEdit AppName | Log Filename |
|-----------------|---------------|-------------|
| Oblivion | TES4 | TES4Edit_log.txt |
| SkyrimLe | TES5 | TES5Edit_log.txt |
| SkyrimSe | SSE | SSEEdit_log.txt |
| SkyrimVr | TES5VR | TES5VREdit_log.txt |
| Fallout3 | FO3 | FO3Edit_log.txt |
| FalloutNewVegas | FNV | FNVEdit_log.txt |
| Fallout4 | FO4 | FO4Edit_log.txt |
| Fallout4Vr | FO4VR | FO4VREdit_log.txt |

The exception log uses executable-stem naming: `<STEM_UPPER>Exception.log`.

**Implementation:** `GetLogFilePath` must accept a `GameType` parameter (or the method must resolve the game type internally). Use a `Dictionary<GameType, string>` or switch expression for the mapping.

## Recommended Implementation Pattern

### Step 0: Compute Log File Path (Game-Aware)

```csharp
// GameType-to-log-prefix mapping (from xeInit.pas)
private static string GetLogFileName(GameType gameType) => gameType switch
{
    GameType.Oblivion => "TES4Edit_log.txt",
    GameType.SkyrimLe => "TES5Edit_log.txt",
    GameType.SkyrimSe => "SSEEdit_log.txt",
    GameType.SkyrimVr => "TES5VREdit_log.txt",
    GameType.Fallout3 => "FO3Edit_log.txt",
    GameType.FalloutNewVegas => "FNVEdit_log.txt",
    GameType.Fallout4 => "FO4Edit_log.txt",
    GameType.Fallout4Vr => "FO4VREdit_log.txt",
    _ => throw new ArgumentException($"Unknown game type: {gameType}")
};

public string GetLogFilePath(string xEditExecutablePath, GameType gameType)
{
    var dir = Path.GetDirectoryName(xEditExecutablePath);
    return Path.Combine(dir!, GetLogFileName(gameType));
}
```

### Step 1: Capture Offset Before Launch

```csharp
var logPath = logFileService.GetLogFilePath(xEditExecutablePath, gameType);
long preOffset = File.Exists(logPath) ? new FileInfo(logPath).Length : 0;
```

### Step 2: Read From Offset After Process Exit

```csharp
using var fs = new FileStream(
    logPath,
    FileMode.Open,
    FileAccess.Read,
    FileShare.ReadWrite,
    bufferSize: 4096);

// Handle truncation: xEdit truncates at 3MB before writing new content
if (preOffset > fs.Length)
    preOffset = 0;

fs.Seek(preOffset, SeekOrigin.Begin);

using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
var newContent = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
var lines = newContent.Split('\n', StringSplitOptions.RemoveEmptyEntries)
    .Select(l => l.TrimEnd('\r'))
    .ToList();
```

### Step 3: Retry on IOException

```csharp
const int retryDelayMs = 250;
const int maxRetries = 3;

for (int attempt = 0; attempt < maxRetries; attempt++)
{
    try
    {
        return await ReadFromOffset(logPath, preOffset, ct);
    }
    catch (IOException) when (attempt < maxRetries - 1)
    {
        await Task.Delay(retryDelayMs * (attempt + 1), ct).ConfigureAwait(false);
    }
}
```

## Encoding Considerations

**CORRECTION from previous version:** xEdit log files are NOT UTF-8. xEdit writes using Delphi's `AnsiString` type (verified: `xeMainForm.pas` line 6154), which uses Windows code page 1252 (ANSI Latin I). The log files have no BOM.

**Pragmatic approach (recommended):** The regex patterns match ASCII-only keywords (`Removing:`, `Undeleting:`, `Skipping:`, `Making Partial Form:`). Code page 1252 and UTF-8 produce identical byte sequences for ASCII (0x00-0x7F). The keyword matching works correctly with UTF-8 decoding. Only the captured text after the colon (record descriptions) may contain non-ASCII characters, but these are counted, not displayed.

**If descriptions are displayed later:** Register `CodePagesEncodingProvider` and use `Encoding.GetEncoding(1252)`:
```csharp
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // once at startup
using var reader = new StreamReader(fs, Encoding.GetEncoding(1252));
```

**Line endings:** xEdit appends `#13#10` (CRLF) after the content block. Lines within the content use CRLF as well (from Delphi's `mmoMessages.Lines.Text`).

## API Surface for the Updated Interface

```csharp
public interface IXEditLogFileService
{
    // CHANGED: Now requires GameType for game-aware naming
    string GetLogFilePath(string xEditExecutablePath, GameType gameType);

    // NEW: Exception log path (still uses executable stem)
    string GetExceptionLogPath(string xEditExecutablePath);

    // NEW: Get current file size as offset, returns 0 if file does not exist
    long GetCurrentOffset(string logFilePath);

    // NEW: Read only new content from offset position
    Task<(List<string> lines, string? error)> ReadNewLogContentAsync(
        string logFilePath,
        long previousOffset,
        CancellationToken ct = default);

    // NEW: Read exception log if it exists and is from this run
    Task<(string? content, string? error)> ReadExceptionLogAsync(
        string xEditExecutablePath,
        DateTime processStartTime,
        CancellationToken ct = default);
}
```

## No New Dependencies Required

All recommended APIs are part of `System.IO` and `System.Text` in .NET 10. No NuGet packages need to be added. If code page 1252 support is needed later, `System.Text.Encoding.CodePages` is already available in .NET 10.

## Alternatives Considered

| Category | Recommended | Alternative | Why Not |
|----------|-------------|-------------|---------|
| File reading API | `FileStream` + `StreamReader` | `RandomAccess` + manual decoding | Overkill for small text files |
| File reading API | `FileStream` + `StreamReader` | `File.ReadAllLinesAsync` | No offset support; `FileShare.Read` default causes lock conflicts |
| Log file naming | GameType lookup table | Executable stem uppercased | Wrong for universal `xEdit.exe` with game flags |
| Offset tracking | `FileInfo.Length` before launch | Timestamp-based staleness check | Fragile; doesn't isolate current run's output |
| Encoding | UTF-8 (pragmatic) | Code page 1252 (correct) | UTF-8 works for ASCII keyword matching; 1252 only needed if displaying descriptions |

## Sources

- [FileStream Class - Microsoft Learn (.NET 10)](https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream?view=net-10.0)
- [FileShare Enum - Microsoft Learn (.NET 10)](https://learn.microsoft.com/en-us/dotnet/api/system.io.fileshare?view=net-10.0)
- xEdit source code: `xeMainForm.pas` line 6166 (log file naming), `xeInit.pas` lines 710-832 (wbAppName per game mode)
- [TES5Edit/TES5Edit GitHub Repository](https://github.com/TES5Edit/TES5Edit)

---

*Stack research: 2026-03-30 -- Updated with xEdit source verification*

# Feature Landscape: xEdit Log File Parsing

**Domain:** xEdit QAC log file parsing for plugin cleaning results
**Researched:** 2026-03-30
**Overall confidence:** HIGH (verified against xEdit source code)

## Table Stakes

Features users expect. Missing = product feels incomplete or broken.

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| Correct log file naming per game mode | Wrong filename = no results ever | Med | **CRITICAL BUG**: current code derives name from executable stem; must use `wbAppName + wbToolName` instead (see Log File Naming below) |
| Parse ITM removal lines (`Removing:`) | Core cleaning statistic | Low | Already implemented in `XEditOutputParser`, just needs correct log source |
| Parse UDR lines (`Undeleting:` / `Skipping:`) | Core cleaning statistic | Low | `Undeleting:` = fixed UDR, `Skipping:` = unfixable deleted navmesh/ref |
| Parse completion summary lines | Confirm cleaning actually finished | Low | `[Removing "Identical to Master" records done]` and `[Undeleting and Disabling References done]` |
| Handle appended log files | xEdit appends to existing log; must isolate current run | Med | Track file offset before launch, read only new bytes after exit |
| Detect stale/missing log files | Graceful degradation when xEdit fails to write | Low | Already partially implemented with timestamp check |
| Exception log detection | User needs to know xEdit crashed, not that "nothing was cleaned" | Med | Separate file: `<EXECUTABLE_STEM_UPPER>Exception.log` |
| Support universal `xEdit.exe` with game flags | Common setup; many users use one binary | Med | Log file name is NOT `xEdit_log.txt` -- it's `SSEEdit_log.txt` etc. based on resolved game mode |
| Maintain hang detection during execution | No stdout progress available during xEdit run | None | Already works via CPU-based monitoring, no changes needed |
| Show "running" status during execution | No real-time line-by-line progress from xEdit | None | Already shows current plugin name + hang state |

## Differentiators

Features that improve UX beyond basic functionality.

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| Parse per-record detail from Removing/Undeleting lines | Show users exactly what was cleaned | Low | Each line contains full record path: `[REFR:000F5772] (places ... in GRUP ...)` |
| Parse deleted navmesh warnings | Surface actionable warnings about mods needing manual fixes | Low | `<Warning: Plugin contains N deleted NavMeshes which can not be undeleted>` |
| Parse partial form creation lines (`Making Partial Form:`) | Track experimental feature usage | Low | Only appears with `-AllowMakePartial` flag, available in xEdit 4.1.6+ |
| Parse "nothing to clean" state | Distinguish "clean plugin" from "failed to parse" | Low | If log has completion lines but zero Removing/Undeleting/Skipping, plugin was already clean |
| Parse elapsed time from completion lines | Show per-plugin timing from xEdit's perspective | Low | `Elapsed Time: HH:MM` or `HH:MM:SS` in summary lines |
| Parse processed/removed/undeleted counts from summary | Cross-validate individual line counts | Low | `Processed Records: N, Removed Records: M` in summary |
| Detect "Can't remove" warnings | Surface records xEdit couldn't clean | Low | `Can't remove: <element name>` appears when IsRemoveable returns false |
| Surface exception log content in results | Users see WHY xEdit crashed, not just "failed" | Low | Read and include in `PluginCleaningResult` |
| Support `-R:` custom log path | Users who redirect xEdit logs to custom locations | Low | xEdit supports `-R:<path>` for alternate log file location |

## Anti-Features

Features to explicitly NOT build.

| Anti-Feature | Why Avoid | What to Do Instead |
|--------------|-----------|-------------------|
| Real-time log file tailing during xEdit execution | xEdit writes logs **only on exit** (via `SaveLogs` in `FormClose`), not incrementally | Show "running" status with hang detection; parse after exit |
| Parse stdout/stderr from xEdit process | xEdit does not write to stdout/stderr; `RedirectStandardOutput` captures nothing useful | Parse log files exclusively |
| Attempt to parse log file while xEdit is still running | Log file is written atomically on exit; reading during execution gives stale data from previous runs | Wait for process exit, then read |
| Log file locking detection during xEdit execution | File is only written on exit; no contention during execution | Only retry on IOException after process exit |
| Parse LOOT dirty info from log | This is a separate LOOT-compatible block generated by `mniNavLOManagersDirtyInfoClick`, not part of cleaning messages | Use the cleaning message lines directly |
| Truncate or manage xEdit's log file | xEdit itself truncates at 3MB via `SaveLog`; AutoQAC should not touch the file | Read-only access; let xEdit manage its own files |
| Parallel log file reading | Sequential cleaning is a hard requirement | Read log after each plugin's process exits, before starting next |
| New UI for raw log display | Scope creep beyond the parsing fix | Existing result display is sufficient |

## Log File Naming Convention

**Source:** Verified from xEdit source code (`xeMainForm.pas` line 6166, `xeInit.pas` lines 696-832). HIGH confidence.

### How xEdit Constructs the Log Filename

```
LogPath = wbProgramPath + wbAppName + wbToolName + '_log.txt'
```

Where:
- `wbProgramPath` = directory of the xEdit executable (with trailing separator)
- `wbAppName` = game-specific prefix (set from executable name OR `-game` command-line flag)
- `wbToolName` = tool mode (always `Edit` for normal xEdit/QAC usage)

### Log File Names by Game

| AutoQAC GameType | wbAppName | Log Filename | Typical Executable |
|-----------------|-----------|-------------|-------------------|
| Oblivion | `TES4` | `TES4Edit_log.txt` | `TES4Edit.exe` |
| SkyrimLe | `TES5` | `TES5Edit_log.txt` | `TES5Edit.exe` |
| SkyrimSe | `SSE` | `SSEEdit_log.txt` | `SSEEdit.exe` |
| SkyrimVr | `TES5VR` | `TES5VREdit_log.txt` | `TES5VREdit.exe` or `SkyrimVREdit.exe` |
| Fallout3 | `FO3` | `FO3Edit_log.txt` | `FO3Edit.exe` |
| FalloutNewVegas | `FNV` | `FNVEdit_log.txt` | `FNVEdit.exe` |
| Fallout4 | `FO4` | `FO4Edit_log.txt` | `FO4Edit.exe` |
| Fallout4Vr | `FO4VR` | `FO4VREdit_log.txt` | `FO4VREdit.exe` |

### Exception Log Naming

**Confidence:** MEDIUM (based on PACT implementation, not directly verified in xEdit Delphi source -- `nxExceptionHook` is a third-party unit not included in the xEdit repository)

The exception log derives from the **executable filename**, NOT from `wbAppName+wbToolName`:

```
ExceptionLogPath = <xEdit directory>/<EXECUTABLE_STEM_UPPER>Exception.log
```

Examples:
- `SSEEdit.exe` --> `SSEEDITException.log`
- `FO4Edit.exe` --> `FO4EDITException.log`
- `xEdit.exe` --> `XEDITException.log` (even when using `-SSE` flag)

### Critical: Universal xEdit.exe Behavior

When using `xEdit.exe` with a game flag (e.g., `xEdit.exe -SSE`):
- `wbAppName` is set to `SSE` (from the `-SSE` command-line switch)
- `wbToolName` is set to `Edit`
- **The log file is `SSEEdit_log.txt`**, NOT `xEdit_log.txt` or `XEDIT_log.txt`
- **The exception log is `XEDITException.log`** (based on executable stem, unchanged)

The current `XEditLogFileService.GetLogFilePath()` uses:
```csharp
var stem = Path.GetFileNameWithoutExtension(xEditExecutablePath).ToUpperInvariant();
return Path.Combine(dir, $"{stem}_log.txt");
```

This produces `XEDIT_log.txt` for `xEdit.exe`, which is **WRONG**. The method needs to be game-aware, using the same game-to-prefix mapping that xEdit uses internally.

### Log File Location

Log files are written to **the same directory as the xEdit executable** (`wbProgramPath`). This is the xEdit install directory, NOT the game data directory, NOT the user's Documents folder.

## QAC Output Format

**Source:** Verified from xEdit source code (`xeMainForm.pas`, dev-4.1.5 and dev-4.1.6 branches). HIGH confidence.

### QAC Execution Flow

Quick Auto Clean (`-QAC`) performs the following sequence (repeats up to 3 times with saves in between):

1. Load module and required masters
2. Apply filter for cleaning
3. Execute "Undelete and Disable References" (UDR pass)
4. Execute "Remove Identical to Master records" (ITM pass)
5. Save if changes were made
6. Repeat steps 2-5 up to 2 more times if the plugin was modified
7. Generate LOOT dirty info report
8. Auto-exit (with `-autoexit` flag)
9. **SaveLogs is called during FormClose** -- writes `mmoMessages.Lines.Text` to log file

### Message Format: Filter Application

Before cleaning lines:
```
[Filtering done] Processed Records: 892796 Elapsed Time: 00:04
```

### Message Format: UDR Pass

**Per-record lines:**

For records that CAN be undeleted:
```
Undeleting: [REFR:000D93C5] (places NorVineWall03NoCol [STAT:0003A1FA] in GRUP Cell Temporary Children of GuardianStones [CELL:00009B91] (in Tamriel "Skyrim" [WRLD:0000003C] at 0,-15))
```

For records that CANNOT be undeleted (navmeshes, injected refs, bad refs):
```
Skipping: [NAVM:000EAFF1] (in GRUP Cell Temporary Children of [CELL:00009694] (in Tamriel "Himmelsrand" [WRLD:0000003C] at 7,-7))
```

**Summary line:**
```
[Undeleting and Disabling References done]  Processed Records: 633, Undeleted Records: 1, Elapsed Time: 00:00
```

**Warning lines (if applicable):**
```
<Warning: Plugin contains 3 deleted NavMeshes which can not be undeleted>
<Warning: Plugin contains 1 deleted references which can not be undeleted>
```

### Message Format: ITM Pass

**Per-record lines:**
```
Removing: [REFR:000F5772] (places FXMistLow01Adjust [MSTT:00016441] in GRUP Cell Temporary Children of ShorsStoneRedbellyMine "Redbelly Mine" [CELL:0001382E])
Removing: [REFR:0005B5BF] (places Barrel02Static [STAT:0010C0E3] in GRUP Cell Temporary Children of RiverwoodAlvorsHouse "Alvor and Sigrid's House" [CELL:000133C8])
```

For non-removable records:
```
Can't remove: [CELL:00012345] (SomeCell "Some Cell" [CELL:00012345])
```

**Summary line:**
```
[Removing "Identical to Master" records done]  Processed Records: 633, Removed Records: 2, Elapsed Time: 00:00
```

### Message Format: Partial Forms (xEdit 4.1.6+ with -AllowMakePartial)

```
Making Partial Form: [CELL:00012345] (SomeCell "Some Cell" [CELL:00012345])
```

This appears INSTEAD of `Removing:` when a record has children that prevent removal and the record supports the Partial Form flag.

### Message Format: Saving

```
Saving: <plugin>.esp
```
Or on unchanged file (rare, but possible in subsequent QAC passes):
```
File has not changed, removing: <plugin>.esp.save.<timestamp>
```

**IMPORTANT:** The `removing:` in the "File has not changed" message is NOT a cleaning action. The existing regex `Removing:\s*(.*)` would match this line as an ITM removal. This is a potential false positive that should be addressed.

### Message Format: QAC Completion

```
Quick Clean mode finished.
```

### Full Example QAC Log Session

Based on real log output patterns (reconstructed from verified source code and real examples):
```
[... loading messages, timestamps, CRC32s ...]
[Filtering done] Processed Records: 892796 Elapsed Time: 00:04
Undeleting: [REFR:000D93C5] (places NorVineWall03NoCol [STAT:0003A1FA] in GRUP Cell Temporary Children of GuardianStones [CELL:00009B91] (in Tamriel "Skyrim" [WRLD:0000003C] at 0,-15))
Skipping: [NAVM:000EAFF1] (in GRUP Cell Temporary Children of [CELL:00009694])
[Undeleting and Disabling References done]  Processed Records: 633, Undeleted Records: 1, Elapsed Time: 00:00
<Warning: Plugin contains 1 deleted NavMeshes which can not be undeleted>
Removing: [REFR:000F5772] (places FXMistLow01Adjust [MSTT:00016441] in GRUP Cell Temporary Children of ShorsStoneRedbellyMine "Redbelly Mine" [CELL:0001382E])
Removing: [REFR:0005B5BF] (places Barrel02Static [STAT:0010C0E3] in GRUP Cell Temporary Children of RiverwoodAlvorsHouse "Alvor and Sigrid's House" [CELL:000133C8])
[Removing "Identical to Master" records done]  Processed Records: 633, Removed Records: 2, Elapsed Time: 00:00
Saving: MyMod.esp
[Filtering done] Processed Records: 892796 Elapsed Time: 00:03
[Undeleting and Disabling References done]  Processed Records: 633, Undeleted Records: 0, Elapsed Time: 00:00
[Removing "Identical to Master" records done]  Processed Records: 633, Removed Records: 0, Elapsed Time: 00:00
Quick Clean mode finished.
```

Note: QAC runs cleaning up to 3 times. Subsequent passes typically find nothing to clean if the first pass was successful. All passes' output appears in the same log file content.

## Exception Log Format

**Confidence:** MEDIUM (based on PACT implementation, not directly verified in xEdit source)

The exception log is created by the `nxExceptionHook` library when xEdit encounters an unhandled Delphi exception. Key patterns to detect:

| Pattern | Meaning |
|---------|---------|
| `which can not be found` | Missing master dependency |
| `which it does not have` | Record references non-existent data |

Exception log content is a stack trace plus error message. The file is created in the same directory as the xEdit executable.

## Log File Behavior

| Behavior | Detail | Source |
|----------|--------|--------|
| Write timing | On application exit only (`FormClose` event) | `xeMainForm.pas` line 6248 |
| Write method | Appends `mmoMessages.Lines.Text` to file end | `SaveLog` procedure, line 6155 |
| Truncation | File is truncated if > 3MB, but only when `aAllowReplace=True` | Line 6153 |
| When truncated | On normal exit (`FormClose` calls `SaveLogs(True)`) | Line 6248 |
| When NOT truncated | On error during `DoRename` (calls `SaveLogs(False)`) | Line 1666 |
| Error handling | Silently swallows all write errors (`except end;`) | Line 6156 |
| Encoding | AnsiString (not UTF-8) | Line 6154 |
| Line endings | CRLF (`#13#10`) appended after content block | Line 6155 |
| Append behavior | Opens existing file with `fmOpenReadWrite`, seeks to end | Lines 6148-6149 |
| Custom log path | `-R:<path>` flag causes additional copy to custom location | Lines 6167-6168 |
| Log size | Can grow very large; truncation only happens at start of next session | Append-then-check-size pattern |

## Feature Dependencies

```
Correct log file naming (game-aware) --> All other log parsing features
    |
    +--> GameType must be known before computing log path
    +--> Must handle universal xEdit.exe + game flag case

Process exit detection --> Log file reading
    |
    +--> Wait for WaitForExitAsync before reading log
    +--> Record file offset BEFORE process start

Log file offset tracking --> Isolating current run's output
    |
    +--> FileInfo.Length before launch = read start position
    +--> FileStream.Seek after exit = skip old content
    +--> Handles 3MB truncation case (offset > new file size = xEdit truncated, read from 0)

ITM parsing (Removing:) ------+
UDR parsing (Undeleting:) ----+--> Statistics display
Skipped parsing (Skipping:) --+
Partial Form parsing ----------+

Summary line parsing --> Completion detection + count validation

Exception log detection --> Error surfacing to user (separate from main log)
```

## MVP Recommendation

Prioritize:
1. **Fix log file naming** to use game-aware names instead of executable stem (CRITICAL BUG)
2. **Implement offset-based reading** to isolate current run's log output from appended history
3. **Parse Removing:/Undeleting:/Skipping: lines** using existing regex patterns against log content
4. **Parse completion summary lines** for validation and count confirmation
5. **Detect exception logs** and surface errors to user
6. **Handle "File has not changed, removing:" false positive** -- this line matches `Removing:` regex but is NOT an ITM removal

Defer:
- **Per-record detail extraction** (extracting FormID, record type, etc.): Not needed for statistics
- **Elapsed time parsing from summary**: Nice-to-have
- **`-R:` custom log path support**: Edge case
- **"Can't remove" warning surfacing**: Low priority, rare occurrence
- **Deleted navmesh warning parsing**: Useful but not blocking

## Sources

- [TES5Edit GitHub Repository](https://github.com/TES5Edit/TES5Edit) - xEdit source code (dev-4.1.5 and dev-4.1.6 branches)
- `xeMainForm.pas` lines 6138-6168: `SaveLog`/`SaveLogs` procedure (log file naming and write behavior)
- `xeMainForm.pas` lines 11120-11340: `mniNavUndeleteAndDisableReferencesClick` (UDR cleaning output format)
- `xeMainForm.pas` lines 11442-11600: `mniNavRemoveIdenticalToMasterClick` (ITM cleaning output format)
- `xeMainForm.pas` lines 20260-20340: QAC execution flow (cleaning sequence and auto-save loop)
- `xeInit.pas` lines 696-832: `wbAppName` and `wbToolName` assignments per game mode
- [XEdit-PACT Source](https://github.com/GuidanceOfGrace/XEdit-PACT) - `PACT_Start.py` (log parsing patterns, exception log naming convention)
- [GitHub Issue SkyrimLL/SDPlus#953](https://github.com/SkyrimLL/SDPlus/issues/953) - Real xEdit log output examples with actual FormIDs and record paths
- [TES5Edit.github.io whatsnew.md](https://github.com/TES5Edit/TES5Edit.github.io/blob/master/whatsnew.md) - "Saving messages to [TES5/FNV/FO3/TES4]Edit_log.txt upon exit" (v3.0.23), "Log file is overwritten at 3MB" (v3.0.30)

---
*Features analysis: 2026-03-30 -- Updated with xEdit source verification*

# Domain Pitfalls: xEdit Log File Parsing on Windows

**Domain:** Windows desktop app reading log files written by an external Delphi-based process (xEdit)
**Researched:** 2026-03-30 (updated with xEdit source verification)

## Critical Pitfalls

Mistakes that cause silent data loss, incorrect parsing results, or total failure.

### Pitfall 1: Log File Naming Mismatch with Universal xEdit.exe

**What goes wrong:** The current `XEditLogFileService.GetLogFilePath()` constructs the log filename from the executable stem: `Path.GetFileNameWithoutExtension(xEditExecutablePath).ToUpperInvariant() + "_log.txt"`. For game-specific executables like `SSEEdit.exe`, this produces `SSEEDIT_log.txt`, which is close enough (xEdit writes `SSEEdit_log.txt` -- case difference is benign on NTFS). But for the universal `xEdit.exe` with a `-SSE` flag, this produces `XEDIT_log.txt`, which **does not exist**. xEdit writes to `SSEEdit_log.txt` based on the resolved game mode.

**Why it happens:** xEdit internally constructs the log filename as `wbAppName + wbToolName + '_log.txt'` (verified: `xeMainForm.pas` line 6166). `wbAppName` is set from the detected game mode (e.g., `SSE`, `FO4`, `TES5`), NOT from the executable filename. When using `xEdit.exe -SSE`, `wbAppName` becomes `SSE` and `wbToolName` becomes `Edit`, producing `SSEEdit_log.txt`.

**Consequences:** Log file is never found for universal `xEdit.exe` users. All cleaning results show zero statistics. Users who use the common modding setup of a single `xEdit.exe` with game flags get no feedback at all.

**Detection:** PACT (the most popular xEdit automation tool) has the same convention in its code: `f"{path.stem.upper()}_log.txt"`. But PACT works around this by calling `clear_xedit_logs()` which deletes old logs before each run, meaning there is only ever one log to find. AutoQAC does not delete logs and therefore must find the correct one.

**Prevention:**
- `GetLogFilePath` must accept a `GameType` parameter and use a lookup table:
  ```
  GameType.SkyrimSe -> "SSEEdit_log.txt"
  GameType.Fallout4 -> "FO4Edit_log.txt"
  GameType.SkyrimLe -> "TES5Edit_log.txt"
  etc.
  ```
- The mapping is: `GameTypeToAppName[gameType] + "Edit_log.txt"`
- Full mapping (from `xeInit.pas` lines 710-832):
  | GameType | wbAppName | Log Filename |
  |----------|-----------|-------------|
  | Oblivion | TES4 | TES4Edit_log.txt |
  | SkyrimLe | TES5 | TES5Edit_log.txt |
  | SkyrimSe | SSE | SSEEdit_log.txt |
  | SkyrimVr | TES5VR | TES5VREdit_log.txt |
  | Fallout3 | FO3 | FO3Edit_log.txt |
  | FalloutNewVegas | FNV | FNVEdit_log.txt |
  | Fallout4 | FO4 | FO4Edit_log.txt |
  | Fallout4Vr | FO4VR | FO4VREdit_log.txt |
- For the exception log, the naming IS based on executable stem (PACT convention, `nxExceptionHook` behavior): `<STEM_UPPER>Exception.log`

**Phase:** Phase 1 -- this is the first thing to fix.

---

### Pitfall 2: Reading the Entire Log File Instead of New Content

**What goes wrong:** xEdit appends to its log file across runs. If the app reads the entire file (as the current `ReadLogFileAsync` does with `File.ReadAllLinesAsync`), it parses all historical content from every previous cleaning session, inflating statistics. A plugin with "3 ITMs removed" this run appears as "47 ITMs removed" because 44 came from prior sessions.

**Why it happens:** The current timestamp-based staleness detection (comparing `File.GetLastWriteTimeUtc` to `processStartTime`) only verifies the file was touched during this run. It does not isolate which lines are new. The file always passes the staleness check because xEdit just wrote to it, so all historical lines get parsed.

**Consequences:** Wildly inflated cleaning statistics. Each successive run reports cumulatively larger numbers.

**Prevention:**
- Record the log file size (byte offset) before launching xEdit: `new FileInfo(logPath).Length` (or 0 if file does not exist).
- After process exit, open the file and `Seek(previousOffset, SeekOrigin.Begin)` to read only newly-appended content.
- Handle the edge case where xEdit truncated the file (offset > file.Length): read from 0.

**Phase:** Phase 1 (XEditLogFileService) -- `GetLogFileOffset` and `ReadNewLogContentAsync` methods.

---

### Pitfall 3: "File has not changed, removing:" False Positive

**What goes wrong:** During QAC, if a plugin was not modified in a cleaning pass, xEdit outputs: `File has not changed, removing: MyMod.esp.save.2026_03_30_12_00_00`. The existing `Removing:\s*(.*)` regex matches this line and counts it as an ITM removal.

**Why it happens:** The regex is not anchored to require `Removing:` at the start of the line, and even if it were, the "removing:" in this message follows the exact `Removing:` pattern. The word is being used in a file-management context (removing a temp save file), not a record-cleaning context.

**Consequences:** Inflated ITM count by 1 per QAC pass where the plugin was unchanged. With 3 QAC passes, a clean plugin could show "2 ITMs removed" (from passes 2 and 3 where nothing changed) when the correct answer is 0.

**Prevention:** Two options:
1. **Filter approach:** Before parsing, filter out lines that start with `File has not changed, removing:` or `Saving:`.
2. **Regex refinement:** Tighten the regex to require the `Removing:` token at the start of the line: `^\s*Removing:\s*(.*)`. This won't help because `File has not changed, removing:` has `removing:` in lowercase. Actually -- check: in the xEdit source, the save message uses lowercase "removing:" while the ITM message uses `Operation+'ing:'` which produces `Removing:` (capital R). So the regex IS case-sensitive and the `R` vs `r` distinguishes them.
3. **Verification:** Check the existing regex: `[GeneratedRegex(@"Removing:\s*(.*)")]`. This is case-sensitive by default. Since xEdit outputs `Removing:` (capital R) for ITMs and `removing:` (lowercase r) for save file cleanup, the current regex actually does NOT match the false positive. **This pitfall may be a non-issue due to case sensitivity.**

**Confidence:** HIGH that this is a non-issue. xEdit source confirms: ITM removal uses `Operation+'ing: '` where Operation is `'Remov'`, producing `Removing:`. File save uses `'removing: '` (lowercase). The generated regex is case-sensitive.

**Phase:** Phase 3 -- verify during integration testing. Add a test case with the `"File has not changed, removing:"` line to confirm it does not match.

---

### Pitfall 4: File.ReadAllLinesAsync Uses FileShare.Read -- Sharing Violation

**What goes wrong:** `File.ReadAllLinesAsync()` (the current implementation) opens the file with `FileShare.Read` internally. This throws `IOException` if xEdit or any other process still holds a write handle on the log file.

**Why it happens:** Three concurrent lock sources: (1) xEdit handle release delay after process exit, (2) Windows Defender scanning newly-written files (100-500ms), (3) Windows Search Indexer.

**Consequences:** The log file read fails, returning zero statistics for a successfully cleaned plugin.

**Prevention:**
- Use `FileStream` with explicit `FileShare.ReadWrite`:
  ```csharp
  using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
  using var reader = new StreamReader(fs);
  ```
- Implement exponential backoff retry: 200ms, 400ms, 800ms.

**Phase:** Phase 1 -- use `FileStream` in the new `ReadNewLogContentAsync` method.

---

### Pitfall 5: Force-Killed xEdit Does Not Write Log File

**What goes wrong:** If the user cancels or hang detection triggers force termination, xEdit is killed via `Process.Kill(entireProcessTree: true)`. A killed process does not execute `FormClose`, so `SaveLogs` never runs. The log file contains no new content.

**Why it happens:** xEdit writes its log file during `FormClose` (verified: `xeMainForm.pas` line 6248). `Process.Kill()` calls `TerminateProcess()`, which destroys the process without running any Delphi cleanup.

**Consequences:** After a forced kill, offset-based reading finds no new content. If interpreted as "nothing to clean," the user gets a misleading result.

**Prevention:**
- Check `CleaningResult` for cancellation or force-kill status BEFORE attempting to read the log file.
- If force-killed, skip log parsing and report as `CleaningStatus.Failed` with message "Process was terminated."
- If gracefully stopped (`CloseMainWindow`), the log might have been written. Attempt to read.

**Phase:** Phase 3 (Orchestrator integration).

---

### Pitfall 6: Log File Encoding is ANSI, Not UTF-8

**What goes wrong:** xEdit writes log content as `AnsiString` (verified: `xeMainForm.pas` line 6154), which is Windows code page 1252. `StreamReader` defaults to UTF-8 when no BOM is present. Plugin names with accented characters will be decoded incorrectly.

**Why it happens:** xEdit is a Delphi application. The `SaveLog` procedure explicitly uses `AnsiString(mmoMessages.Lines.Text)`.

**Prevention:** The regex patterns match only ASCII keywords (`Removing:`, `Undeleting:`, etc.). The keyword matching portion works correctly with UTF-8 decoding. Only the captured group text (record descriptions) after the colon may be garbled, but these are only counted, not displayed.

**Pragmatic approach:** Use UTF-8 (default) and add a code comment documenting the assumption. If descriptions are displayed later, switch to `Encoding.GetEncoding(1252)`.

**Phase:** Phase 1 -- document in code comments.

## Moderate Pitfalls

### Pitfall 7: Exception Log Persists Across Runs

**What goes wrong:** xEdit's exception log from a previous crash persists. After a successful run, the app reads the stale exception log and incorrectly reports an error.

**Prevention:**
- Check `LastWriteTimeUtc` against process start time, or record byte offset before launch.
- The offset approach is more reliable (timestamps have second-level granularity issues).

**Phase:** Phase 1 -- implement exception log reading with staleness check.

---

### Pitfall 8: Offset Becomes Invalid If Log File Is Truncated

**What goes wrong:** xEdit truncates the log file at 3MB on normal exit (verified: `xeMainForm.pas` line 6153, `if fs.Size > 3 * 1024 * 1024 then fs.Size := 0`). If the pre-launch offset was > 0 and xEdit truncated the file before appending new content, the offset exceeds the new file size.

**Important detail:** The truncation check happens BEFORE writing new content (with `aAllowReplace=True`). So xEdit opens the file, checks size, optionally truncates to 0, then appends new content. The resulting file contains ONLY the current session's messages.

**Prevention:**
```csharp
if (previousOffset > currentFileLength)
{
    // File was truncated by xEdit (>3MB cleanup) -- read from beginning
    previousOffset = 0;
}
```

**Phase:** Phase 1 -- add bounds check in `ReadNewLogContentAsync`.

---

### Pitfall 9: MO2 Mode Log File Path is Unchanged

**What goes wrong:** Developers might assume MO2's VFS redirects xEdit's log file. It does not. Log files are written to xEdit's install directory, which is outside VFS scope.

**Prevention:** Use the same log file path resolution in both modes. The current code correctly uses `xEditExecutablePath` from config. Preserve this.

**Phase:** All phases -- design invariant. Write a test.

---

### Pitfall 10: Breaking MO2 with UseShellExecute

**What goes wrong:** Changing `ProcessExecutionService` to `UseShellExecute = true` (to "fix" stdout) breaks MO2 wrapping because `ShellExecute` handles argument quoting differently and prevents `Process.Id` access.

**Prevention:** Keep `UseShellExecute = false`. Set `RedirectStandardOutput = false` and `RedirectStandardError = false`. Remove `BeginOutputReadLine()` / `BeginErrorReadLine()` calls.

**Phase:** Phase 2.

---

### Pitfall 11: Multi-Pass QAC Output Accumulation

**What goes wrong:** QAC runs cleaning up to 3 times. The log contains output from ALL passes. If the parser does not account for this, it may double-count items from the first pass that appear in log content from subsequent passes.

**Why it happens:** Each QAC pass runs "Undelete and Disable References" and "Remove Identical to Master" sequentially. Subsequent passes typically find 0 items (the first pass cleaned everything). But if the parser sums Removing: lines across all passes AND xEdit writes per-record lines for all passes, counts could be wrong.

**In practice:** This is NOT a real problem because subsequent passes that find 0 items produce only summary lines (`Processed Records: N, Removed Records: 0`), not per-record `Removing:` lines. The per-record lines only appear when something is actually removed. So summing all `Removing:` lines across passes gives the correct total.

**Prevention:** No special handling needed, but add a test with multi-pass log output to verify.

**Phase:** Phase 3 -- add test case.

---

### Pitfall 12: xEdit -R: Flag Overrides Log File Location

**What goes wrong:** xEdit supports `-R:<path>` to redirect logs to a custom location. AutoQAC does not pass this flag, but users may have it configured in shortcuts or mod manager profiles.

**Prevention:** Document as a known limitation. Do not attempt to detect external `-R:` configuration.

**Phase:** Not addressed in this milestone. Document in release notes.

## Minor Pitfalls

### Pitfall 13: Partial First Line After Byte Offset Seek

**What goes wrong:** If seeking to a byte offset and the previous content did not end with a newline, the first "line" is a partial garbled fragment.

**Prevention:** xEdit's `SaveLog` appends `#13#10` (CRLF) after content. This means the new content starts on a fresh line. Defensive check: if first character at offset is not the start of a recognized line, skip to the next newline.

**Phase:** Phase 1 -- defensive check.

---

### Pitfall 14: DateTime Precision in Staleness Detection

**What goes wrong:** NTFS timestamps have ~2-second granularity. If cleaning starts within the same 2-second window as a previous log write, timestamp detection fails.

**Prevention:** The offset-based approach eliminates this. Remove timestamp-based staleness detection after offset reading is implemented.

**Phase:** Phase 4 (cleanup).

---

### Pitfall 15: Existing Test Mocks Return stdout Output Lines

**What goes wrong:** Test mocks set `ProcessResult.OutputLines` with xEdit-like content. After the fix, these lines will be empty. Tests pass for the wrong reason.

**Prevention:** Update all test mocks to reflect the new behavior. Update assertions.

**Phase:** Phases 2-4 -- update tests as each service changes.

---

### Pitfall 16: Log Filename Casing Detail

**What goes wrong:** The existing code uppercases the entire stem: `SSEEDIT_log.txt`. xEdit actually writes `SSEEdit_log.txt` (mixed case: `wbAppName` is `SSE`, `wbToolName` is `Edit`). On case-insensitive NTFS this doesn't matter, but it's technically wrong.

**Prevention:** Use the correct casing from the mapping table. No practical impact on Windows, but cleaner code.

**Phase:** Phase 1 -- use correct casing in the lookup table.

## Phase-Specific Warnings

| Phase Topic | Likely Pitfall | Mitigation |
|-------------|---------------|------------|
| Phase 1: Game-aware log naming | Pitfall 1 (naming mismatch) | GameType-to-prefix lookup table from xEdit source |
| Phase 1: Offset methods | Pitfall 2 (appended content), Pitfall 4 (FileShare), Pitfall 8 (truncation) | Record offset before launch, use FileStream(FileShare.ReadWrite), bounds check |
| Phase 1: Exception log | Pitfall 7 (stale exception) | Timestamp or offset check |
| Phase 2: Stdout removal | Pitfall 10 (UseShellExecute) | Keep UseShellExecute = false, only remove redirect flags |
| Phase 3: Orchestrator integration | Pitfall 3 (false positive), Pitfall 5 (force-kill), Pitfall 11 (multi-pass) | Verify case sensitivity handles "removing:" vs "Removing:", check termination status, add multi-pass test |
| Phase 4: Cleanup | Pitfall 14 (timestamp removal), Pitfall 15 (test mocks) | Remove timestamp detection, update all tests |

## Sources

- xEdit source code (`xeMainForm.pas`, `xeInit.pas`) on [TES5Edit GitHub](https://github.com/TES5Edit/TES5Edit), branches dev-4.1.5 and dev-4.1.6 (HIGH confidence)
- [XEdit-PACT Source](https://github.com/GuidanceOfGrace/XEdit-PACT) - `PACT_Start.py` for log naming conventions and exception log patterns (HIGH confidence)
- [xEdit What's New](https://github.com/TES5Edit/TES5Edit.github.io/blob/master/whatsnew.md) - "Log file is overwritten at 3MB" (v3.0.30), "Saving messages to [TES5/FNV/FO3/TES4]Edit_log.txt upon exit" (v3.0.23) (HIGH confidence)
- [.NET FileShare Enum](https://learn.microsoft.com/en-us/dotnet/api/system.io.fileshare) (HIGH confidence)
- Direct codebase analysis of `XEditLogFileService.cs`, `CleaningOrchestrator.cs`, `ProcessExecutionService.cs`, `XEditCommandBuilder.cs` (HIGH confidence)

---
*Pitfalls analysis: 2026-03-30 -- Updated with xEdit source verification*