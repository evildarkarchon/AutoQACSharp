# Phase 3: Integration -- Log-First Parsing - Research

**Researched:** 2026-03-30
**Domain:** xEdit log parsing integration, C# service layer rewiring
**Confidence:** HIGH

## Summary

Phase 3 rewires the CleaningOrchestrator and CleaningService to use the game-aware, offset-based `XEditLogFileService` (built in Phase 1) instead of the legacy stdout-based parsing pipeline (disabled in Phase 2). This is a pure integration phase -- no new services are created. The modifications are concentrated in two files (`CleaningOrchestrator.cs` and `CleaningService.cs`) with supporting changes to the `CleaningStatus` enum and test updates.

The current orchestrator already has a log-enrichment block (lines 400-423) that reads log files and parses them with `XEditOutputParser`. This block currently calls the legacy `ReadLogFileAsync` (timestamp-based staleness detection, executable-stem-based naming) and only triggers on `{ Success: true, Status: CleaningStatus.Cleaned }`. Phase 3 replaces this block with offset-based reading via `ReadLogContentAsync`, broadens the conditions under which log reading is attempted, and handles force-kill, nothing-to-clean, and exception log scenarios.

The `CleaningService` currently calls `outputParser.ParseOutput(result.OutputLines)` on line 107, which parses an always-empty list (Phase 2 removed stdout redirect). This call must be removed, with parsing responsibility moving entirely to the orchestrator.

**Primary recommendation:** Replace the orchestrator's lines 338-339 + 400-423 with per-plugin offset capture/read cycle using `IXEditLogFileService`, remove `CleaningService` line 107 stdout parsing, add a `CleaningStatus.AlreadyClean` enum value for the nothing-to-clean case, and surface exception log content through the existing `LogParseWarning` field.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- **D-01:** Log reading stays in CleaningOrchestrator. The orchestrator already has game type, xEdit path, and timing context (lines 400-423 currently call the legacy `ReadLogFileAsync`). Switch the orchestrator to call `ReadLogContentAsync` with game-aware paths and offsets. CleaningService returns process result only; orchestrator enriches with log-parsed statistics.
- **D-02:** `CleaningService.CleanPluginAsync` should stop calling `outputParser.ParseOutput(result.OutputLines)` (line 107 currently parses empty lists). Parsing moves entirely to the orchestrator's post-exit log read path.
- **D-03:** Capture offset per-plugin, not per-session. Each xEdit launch appends to the same log file. Orchestrator must call `logFileService.CaptureOffset()` before each plugin's `CleanPluginAsync` call, then `ReadLogContentAsync` after. This isolates each plugin's log output.
- **D-04:** Check `TerminationResult` (or process exit status) before attempting log read. If xEdit was force-killed, skip log parse and return `CleaningStatus.Failed` with a descriptive message like "xEdit was terminated -- no log available". Force-killed xEdit does not reliably flush its log.
- **D-05:** If the parser finds a completion line (via `IsCompletionLine`) but zero Removing/Undeleting/Skipping/PartialForm counts, treat the plugin as "already clean". This should map to a distinct status (not Failed, not Skipped) so the UI can show "nothing to clean" instead of misleading zero counts. The exact status value is Claude's discretion.
- **D-06:** Exception log content flows through the existing `PluginCleaningResult.LogParseWarning` field. When `ReadLogContentAsync` returns non-null `ExceptionContent`, set `LogParseWarning` to the exception text and mark the result as failed. This gives users actionable error info without adding new fields.

### Claude's Discretion
- Multi-pass QAC aggregation policy: If a plugin is retried (timeout retry loop), Claude decides whether to sum log stats across retries or report only the final pass. The retry loop already exists in the orchestrator.
- Exact status enum value for "nothing to clean" -- could be a new `CleaningStatus` value or reuse existing ones with a message.
- Whether to add `HasCompletionLine` or similar to `CleaningStatistics` for the nothing-to-clean check, or keep the check in the orchestrator.

### Deferred Ideas (OUT OF SCOPE)
- Multi-pass QAC aggregation policy noted in STATE.md as a Phase 3 concern -- auto-deferred to Claude's discretion since the retry loop structure already handles this.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| PAR-01 | Existing regex patterns applied to log file content instead of stdout | Orchestrator reads log via `ReadLogContentAsync`, passes lines to `outputParser.ParseOutput()` -- same regex patterns, different input source |
| PAR-02 | Parser detects "nothing to clean" state (completion lines present but zero cleaning actions) | New `CleaningStatus.AlreadyClean` enum value; orchestrator checks `IsCompletionLine` on log lines and zero stats |
| PAR-03 | Exception log content surfaced in cleaning results when present | `LogReadResult.ExceptionContent` piped into `PluginCleaningResult.LogParseWarning`; result marked Failed |
| ORC-01 | Orchestrator captures log offset before xEdit launch and reads log after process exit | `CaptureOffset()` before each `CleanPluginAsync`, `ReadLogContentAsync` after -- per-plugin granularity |
| ORC-02 | Orchestrator checks process termination status before attempting log read | Check `_isStopRequested` and `result.Status` for force-kill indicators; skip log parse on termination |
| ORC-03 | Hang detection and "running" status continue to display during xEdit execution | No changes needed -- hang monitoring (`StartHangMonitoring`) is independent of log parsing and already works correctly |
</phase_requirements>

## Architecture Patterns

### Modification Map

The changes are scoped to these specific locations:

```
AutoQAC/
  Models/
    CleaningResult.cs          # Add CleaningStatus.AlreadyClean enum value
  Services/
    Cleaning/
      CleaningOrchestrator.cs  # PRIMARY: Replace lines 338-339 + 400-423 with offset-based log reading
      CleaningService.cs       # SECONDARY: Remove line 107 (stdout parsing), return raw process result
AutoQAC.Tests/
  Services/
    CleaningOrchestratorTests.cs  # Update mocks for new log reading API
    CleaningServiceTests.cs       # Update assertions (no more ParseOutput call)
```

### Pattern 1: Per-Plugin Offset Capture/Read Cycle

**What:** Before each plugin's `CleanPluginAsync` call, capture the current byte offset of both main and exception log files. After the call returns, read only the content appended since the offset.

**When to use:** Every plugin in the sequential cleaning loop.

**Current code (lines 338-339 + 400-423):**
```csharp
// Line 338-339: Timestamp-based (to be replaced)
var pluginStopwatch = Stopwatch.StartNew();
var pluginStartTime = DateTime.UtcNow;

// Lines 400-423: Legacy log enrichment (to be replaced entirely)
if (result is { Success: true, Status: CleaningStatus.Cleaned })
{
    var xEditPath = config.XEditExecutablePath ?? string.Empty;
    var (logLines, logError) = await logFileService.ReadLogFileAsync(
        xEditPath, pluginStartTime, cts.Token).ConfigureAwait(false);
    // ...
}
```

**New pattern:**
```csharp
// Before CleanPluginAsync -- capture offsets
var xEditDir = Path.GetDirectoryName(config.XEditExecutablePath) ?? string.Empty;
var mainLogPath = logFileService.GetLogFilePath(xEditDir, gameType);
var exceptionLogPath = logFileService.GetExceptionLogFilePath(xEditDir, gameType);
var mainLogOffset = logFileService.CaptureOffset(mainLogPath);
var exceptionLogOffset = logFileService.CaptureOffset(exceptionLogPath);

// ... CleanPluginAsync call ...

// After CleanPluginAsync -- read log content
// (only if not force-killed)
```

### Pattern 2: Force-Kill Guard Before Log Read

**What:** Before attempting log reading, check whether the process was terminated. Force-killed xEdit does not reliably flush its log.

**Detection approach:** The orchestrator already tracks `_isStopRequested`. When the CTS is cancelled and the CleaningService returns with `Status = Skipped` (from its `OperationCanceledException` handler), the orchestrator knows the process was terminated. Additionally, `result.TimedOut` covers the timeout-kill case. The condition for skipping log read is:

```csharp
bool shouldReadLog = result.Status != CleaningStatus.Skipped  // Not cancelled
                  && !_isStopRequested;                        // Not user-stopped
```

For the force-kill case specifically: when `_isStopRequested` is true, skip log reading entirely and use the result as-is with "xEdit was terminated -- no log available" message.

### Pattern 3: Nothing-to-Clean Detection

**What:** When log parsing returns completion lines but zero cleaning actions, treat as "already clean."

**Recommendation:** Add `CleaningStatus.AlreadyClean` to the enum. This is cleaner than overloading `Cleaned` with zero stats because:
1. The UI `Summary` property in `PluginCleaningResult` already returns "No changes" for `TotalProcessed == 0` when status is `Cleaned` -- but `AlreadyClean` enables the session result counters to distinguish "cleaned with 0 changes" from "nothing needed cleaning."
2. The `CleaningSessionResult.CleanedCount` filters by `CleaningStatus.Cleaned`, so a new enum value avoids inflating the "cleaned" counter.

**Detection logic (in orchestrator, after log parse):**
```csharp
var hasCompletionLine = logResult.LogLines.Any(outputParser.IsCompletionLine);
var stats = outputParser.ParseOutput(logResult.LogLines);

if (hasCompletionLine && stats is { ItemsRemoved: 0, ItemsUndeleted: 0, ItemsSkipped: 0, PartialFormsCreated: 0 })
{
    // Plugin completed successfully but had nothing to clean
    // Use AlreadyClean status
}
```

### Pattern 4: Exception Log Surfacing

**What:** When `LogReadResult.ExceptionContent` is non-null, the plugin should be marked as failed with the exception text in `LogParseWarning`.

```csharp
if (logResult.ExceptionContent != null)
{
    logParseWarning = logResult.ExceptionContent;
    // Override status to Failed -- exceptions indicate xEdit encountered errors
}
```

### Pattern 5: Multi-Pass Retry Offset Handling

**Discretion decision: Report only the final pass stats.** Rationale:
- xEdit QAC appends to the same log file across retries
- Each retry is a fresh xEdit launch that processes the same plugin
- Only the final pass result matters for the user (it represents the definitive state)
- Summing would double-count items already cleaned in earlier passes

**Implementation:** Re-capture offsets before each retry attempt. The offset capture already happens at the top of the do-while loop iteration naturally if placed correctly. Since the decision in D-03 says "capture offset before each `CleanPluginAsync` call," the offset variables should be inside the do-while loop, not outside it.

### Anti-Patterns to Avoid
- **Parsing in CleaningService:** D-02 explicitly moves all parsing to the orchestrator. Do not leave any `ParseOutput` call in CleaningService.
- **Session-level offsets:** D-03 requires per-plugin offset capture. Do not capture offsets once outside the plugin loop.
- **Reading logs after force-kill:** D-04 says skip log parse entirely. Do not try to read partial logs.
- **Reusing `pluginStartTime`:** The timestamp-based approach is the legacy pattern. Remove `pluginStartTime` and replace with offset capture.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Log file byte offset tracking | Custom offset tracking | `IXEditLogFileService.CaptureOffset()` | Already handles missing files (returns 0) |
| Game-aware log path resolution | String manipulation on exe path | `logFileService.GetLogFilePath(dir, gameType)` | Uses xEdit `wbAppName` convention |
| Offset-based file reading with retry | Custom FileStream + retry loop | `logFileService.ReadLogContentAsync()` | Handles truncation, file contention, retry |
| Log line pattern matching | New regex patterns | `outputParser.ParseOutput()` and `IsCompletionLine()` | Existing patterns are correct for log content |

## Common Pitfalls

### Pitfall 1: Offset Capture Placement in Retry Loop
**What goes wrong:** Capturing offsets outside the do-while loop means retries read the entire log (including previous attempt's output), potentially double-counting.
**Why it happens:** Natural tendency to capture offsets near `pluginStopwatch` initialization.
**How to avoid:** Place offset capture as the first action inside the do-while loop body, so each retry attempt captures the latest offset.
**Warning signs:** Test showing inflated ITM/UDR counts on retried plugins.

### Pitfall 2: CleaningService Still Returning Statistics
**What goes wrong:** After removing `ParseOutput` from CleaningService, the `CleaningResult.Statistics` field will be null for successful cleans. The orchestrator must handle this.
**Why it happens:** The orchestrator currently reads `result.Statistics` as `logStats` (line 401) and uses it as fallback.
**How to avoid:** After Phase 3, the orchestrator should initialize stats from the log parse result, not from `result.Statistics`. The CleaningService return becomes a pure process-exit-status indicator.
**Warning signs:** Null reference on `result.Statistics` access.

### Pitfall 3: Condition for Log Reading Too Narrow
**What goes wrong:** Current condition `result is { Success: true, Status: CleaningStatus.Cleaned }` is too narrow. After Phase 3, CleaningService removes parsing, so `Status` will always be `Cleaned` for exit-code-0 processes, `Failed` for non-zero, and `Skipped` for cancellation. But we need log reading for all non-killed successful exits.
**Why it happens:** The legacy code only enriched "Cleaned" results because stdout stats were the primary source.
**How to avoid:** Read logs for any result where: (1) process exited normally (exit code 0), (2) was not force-killed or cancelled. The orchestrator should read logs even if the result is "Failed" due to non-zero exit code, because the log may contain exception info.
**Warning signs:** Missed exception log content when xEdit exits with non-zero code.

### Pitfall 4: CleaningStatus.AlreadyClean Not Handled in Session Counters
**What goes wrong:** `CleaningSessionResult` may have hardcoded filters like `PluginResults.Count(r => r.Status == CleaningStatus.Cleaned)` for `CleanedCount`. Adding `AlreadyClean` without updating these would miscount.
**Why it happens:** Enum additions require auditing all switch/match expressions.
**How to avoid:** Search all `CleaningStatus` references and add `AlreadyClean` handling. Check `PluginCleaningResult.Summary`, `CleaningSessionResult` properties, and any ViewModel switch expressions.
**Warning signs:** "AlreadyClean" plugins not appearing in any count.

### Pitfall 5: `xEditDirectory` Derivation When Path is Null
**What goes wrong:** `config.XEditExecutablePath` can be null (it's `string?`). Calling `Path.GetDirectoryName(null)` returns null.
**Why it happens:** The orchestrator already validates config before reaching the plugin loop, but edge cases exist.
**How to avoid:** Use the `?? string.Empty` pattern already used on line 406. Or derive `xEditDir` once after config validation and store it.
**Warning signs:** `ArgumentException` from `GetLogFilePath` when `xEditDirectory` is null/whitespace.

### Pitfall 6: Mock Updates in Tests
**What goes wrong:** Existing orchestrator tests mock `ReadLogFileAsync` (the legacy method). After Phase 3, these mocks need to switch to `CaptureOffset` + `ReadLogContentAsync`.
**Why it happens:** Test setups are copied from existing patterns.
**How to avoid:** Update the default mock setup in `CleaningOrchestratorTests` constructor (line 61-62) to mock the new methods. Also update any test that explicitly mocks log reading.
**Warning signs:** Tests failing with "no mock setup for CaptureOffset" or returning default values.

## Code Examples

### Current Orchestrator Log Block (Lines 400-423) -- To Be Replaced

```csharp
// Current (legacy):
var logStats = result.Statistics;
string? logParseWarning = null;

if (result is { Success: true, Status: CleaningStatus.Cleaned })
{
    var xEditPath = config.XEditExecutablePath ?? string.Empty;
    var (logLines, logError) = await logFileService.ReadLogFileAsync(
        xEditPath, pluginStartTime, cts.Token).ConfigureAwait(false);

    if (logError != null)
    {
        logger.Warning("Log parse warning for {Plugin}: {Warning}", plugin.FileName, logError);
        logParseWarning = logError;
    }
    else if (logLines.Count > 0)
    {
        logStats = outputParser.ParseOutput(logLines);
    }
}
```

### New Orchestrator Pattern (Replacement)

```csharp
// Inside do-while loop, BEFORE CleanPluginAsync:
var xEditDir = Path.GetDirectoryName(config.XEditExecutablePath ?? string.Empty) ?? string.Empty;
var mainLogPath = logFileService.GetLogFilePath(xEditDir, gameType);
var exceptionLogPath = logFileService.GetExceptionLogFilePath(xEditDir, gameType);
var mainLogOffset = logFileService.CaptureOffset(mainLogPath);
var exceptionLogOffset = logFileService.CaptureOffset(exceptionLogPath);

// ... existing CleanPluginAsync call ...

// AFTER do-while loop completes, BEFORE creating PluginCleaningResult:
CleaningStatistics? logStats = null;
string? logParseWarning = null;
var finalStatus = result.Status;

// Guard: only read logs if process was not killed/cancelled
if (!_isStopRequested && result.Status != CleaningStatus.Skipped)
{
    var logResult = await logFileService.ReadLogContentAsync(
        xEditDir, gameType, mainLogOffset, exceptionLogOffset, cts.Token).ConfigureAwait(false);

    if (logResult.Warning != null)
    {
        logger.Warning("Log read warning for {Plugin}: {Warning}", plugin.FileName, logResult.Warning);
        logParseWarning = logResult.Warning;
    }

    if (logResult.LogLines.Count > 0)
    {
        logStats = outputParser.ParseOutput(logResult.LogLines);

        // Nothing-to-clean detection (PAR-02)
        var hasCompletionLine = logResult.LogLines.Any(outputParser.IsCompletionLine);
        if (hasCompletionLine && logStats is { ItemsRemoved: 0, ItemsUndeleted: 0, ItemsSkipped: 0, PartialFormsCreated: 0 })
        {
            finalStatus = CleaningStatus.AlreadyClean;
        }
    }

    // Exception log surfacing (PAR-03)
    if (logResult.ExceptionContent != null)
    {
        logParseWarning = logResult.ExceptionContent;
        finalStatus = CleaningStatus.Failed;
        logger.Warning("xEdit exception log for {Plugin}: {Content}",
            plugin.FileName, logResult.ExceptionContent);
    }
}
else if (_isStopRequested)
{
    logParseWarning = "xEdit was terminated -- no log available";
}
```

### CleaningService Modification (Line 107)

```csharp
// Current (to be removed):
var stats = await Task.Run(() => outputParser.ParseOutput(result.OutputLines), ct).ConfigureAwait(false);
return new CleaningResult
{
    Success = true,
    Status = CleaningStatus.Cleaned,
    Message = "Cleaning completed successfully.",
    Duration = sw.Elapsed,
    Statistics = stats  // <-- This will be null/removed
};

// New:
return new CleaningResult
{
    Success = true,
    Status = CleaningStatus.Cleaned,
    Message = "Cleaning completed successfully.",
    Duration = sw.Elapsed
    // Statistics intentionally omitted -- orchestrator parses from log file
};
```

### CleaningStatus Enum Addition

```csharp
public enum CleaningStatus
{
    Cleaned,
    Skipped,
    Failed,
    AlreadyClean  // New: xEdit completed but found nothing to clean
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Stdout-based parsing in CleaningService | Log-file parsing in Orchestrator | Phase 2/3 | Statistics come from log files, not stdout |
| Timestamp-based staleness detection | Offset-based isolation | Phase 1/3 | Per-plugin content isolation instead of full-file reads |
| Executable-stem-based log naming | Game-type-based `wbAppName` naming | Phase 1/3 | Correct log resolution for `xEdit.exe` with game flags |

## Impact Analysis: CleaningStatus.AlreadyClean

Files that reference `CleaningStatus` and may need updates:

| File | Usage | Needs Update? |
|------|-------|---------------|
| `CleaningResult.cs` | Enum definition | YES -- add `AlreadyClean` |
| `PluginCleaningResult.cs` | `Summary` property switches on `Status` | YES -- add `AlreadyClean` case (show "Already clean") |
| `CleaningSessionResult.cs` | Count properties filtering by status | CHECK -- may need `AlreadyClean` in clean-count bucket |
| `CleaningOrchestrator.cs` | Sets status based on result | YES -- primary consumer |
| `CleaningService.cs` | Returns Cleaned/Failed/Skipped | NO -- never returns AlreadyClean |
| `CleaningCommandsViewModel.cs` | May switch on status | CHECK |
| `CleaningResultsViewModel.cs` | May display status | CHECK |
| `StateService.cs` | Updates sets based on status | CHECK -- which set does AlreadyClean go in? |

**Recommendation:** `AlreadyClean` should count toward `CleanedPlugins` (success bucket) not `FailedPlugins` or `SkippedPlugins`, since xEdit did process the plugin -- it just had nothing to fix.

## Open Questions

1. **CleaningSessionResult counter bucket for AlreadyClean**
   - What we know: `CleaningSessionResult` has `CleanedCount`, `SkippedCount`, `FailedCount` properties that filter by status.
   - What's unclear: Whether `AlreadyClean` should count as "Cleaned" or have its own counter.
   - Recommendation: Count as "Cleaned" in session stats (add to the `Cleaned` filter). The distinction is per-plugin-level only. Audit `CleaningSessionResult.cs` before implementing.

2. **StateService set membership for AlreadyClean**
   - What we know: `StateService.AddDetailedCleaningResult` likely adds plugins to `CleanedPlugins`, `FailedPlugins`, or `SkippedPlugins` based on status.
   - What's unclear: Whether there's a direct status-to-set mapping or if it checks `Success`.
   - Recommendation: Map `AlreadyClean` to `CleanedPlugins` set.

3. **CleaningService `outputParser` dependency after D-02**
   - What we know: After removing the `ParseOutput` call, `CleaningService` no longer uses `IXEditOutputParser`.
   - What's unclear: Whether to remove the constructor dependency now (Phase 3) or defer to Phase 4 cleanup.
   - Recommendation: Remove it now if convenient (it's a clean change), but it's acceptable to defer to Phase 4 (CLN-03 covers "stale test mocks and unused parameters").

## Sources

### Primary (HIGH confidence)
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` -- Full orchestrator source, lines 258-448 plugin loop
- `AutoQAC/Services/Cleaning/CleaningService.cs` -- Full service source, line 107 stdout parsing
- `AutoQAC/Services/Cleaning/XEditLogFileService.cs` -- Game-aware offset API
- `AutoQAC/Services/Cleaning/XEditOutputParser.cs` -- Parser patterns and `IsCompletionLine`
- `AutoQAC/Models/CleaningResult.cs` -- `CleaningStatus` enum and `CleaningResult` record
- `AutoQAC/Models/PluginCleaningResult.cs` -- `LogParseWarning` field, `Summary` property
- `AutoQAC/Models/LogReadResult.cs` -- `LogReadResult` record shape
- `AutoQAC/Models/TerminationResult.cs` -- Termination enum values
- `AutoQAC/Services/Process/ProcessExecutionService.cs` -- `ProcessResult` always returns empty `OutputLines`/`ErrorLines`
- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs` -- Test setup with legacy `ReadLogFileAsync` mock
- `AutoQAC.Tests/Services/CleaningServiceTests.cs` -- `ParseOutput` mock assertions

### Secondary (MEDIUM confidence)
- CONTEXT.md decisions D-01 through D-06 -- User-locked implementation decisions

## Metadata

**Confidence breakdown:**
- Architecture patterns: HIGH -- based on direct code reading of all involved files
- Force-kill detection: HIGH -- traced through TerminationResult enum and orchestrator stop flow
- Nothing-to-clean detection: HIGH -- `IsCompletionLine` and `ParseOutput` APIs verified in source
- AlreadyClean enum impact: MEDIUM -- need to verify all consumers, listed in Impact Analysis table
- Multi-pass retry handling: HIGH -- retry loop structure verified in orchestrator lines 343-385

**Research date:** 2026-03-30
**Valid until:** Stable -- internal code rewiring with no external dependency changes
