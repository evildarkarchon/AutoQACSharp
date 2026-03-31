# Phase 3: Integration -- Log-First Parsing - Context

**Gathered:** 2026-03-31
**Status:** Ready for planning

<domain>
## Phase Boundary

Wire the orchestrator to read xEdit log files post-exit and parse results from log content, replacing the broken stdout-based pipeline. This phase modifies CleaningOrchestrator and CleaningService to use the game-aware XEditLogFileService (Phase 1) and offset-based reading, now that stdout capture is removed (Phase 2). No new services are created -- existing services are rewired.

</domain>

<decisions>
## Implementation Decisions

### Log Reading Wiring
- **D-01:** Log reading stays in CleaningOrchestrator. The orchestrator already has game type, xEdit path, and timing context (lines 400-423 currently call the legacy `ReadLogFileAsync`). Switch the orchestrator to call `ReadLogContentAsync` with game-aware paths and offsets. CleaningService returns process result only; orchestrator enriches with log-parsed statistics.
- **D-02:** `CleaningService.CleanPluginAsync` should stop calling `outputParser.ParseOutput(result.OutputLines)` (line 107 currently parses empty lists). Parsing moves entirely to the orchestrator's post-exit log read path.

### Offset Capture Granularity
- **D-03:** Capture offset per-plugin, not per-session. Each xEdit launch appends to the same log file. Orchestrator must call `logFileService.CaptureOffset()` before each plugin's `CleanPluginAsync` call, then `ReadLogContentAsync` after. This isolates each plugin's log output.

### Force-Kill Handling (ORC-02)
- **D-04:** Check `TerminationResult` (or process exit status) before attempting log read. If xEdit was force-killed, skip log parse and return `CleaningStatus.Failed` with a descriptive message like "xEdit was terminated -- no log available". Force-killed xEdit does not reliably flush its log.

### Nothing-to-Clean Detection (PAR-02)
- **D-05:** If the parser finds a completion line (via `IsCompletionLine`) but zero Removing/Undeleting/Skipping/PartialForm counts, treat the plugin as "already clean". This should map to a distinct status (not Failed, not Skipped) so the UI can show "nothing to clean" instead of misleading zero counts. The exact status value is Claude's discretion.

### Exception Log Surfacing (PAR-03)
- **D-06:** Exception log content flows through the existing `PluginCleaningResult.LogParseWarning` field. When `ReadLogContentAsync` returns non-null `ExceptionContent`, set `LogParseWarning` to the exception text and mark the result as failed. This gives users actionable error info without adding new fields.

### Claude's Discretion
- Multi-pass QAC aggregation policy: If a plugin is retried (timeout retry loop), Claude decides whether to sum log stats across retries or report only the final pass. The retry loop already exists in the orchestrator.
- Exact status enum value for "nothing to clean" -- could be a new `CleaningStatus` value or reuse existing ones with a message.
- Whether to add `HasCompletionLine` or similar to `CleaningStatistics` for the nothing-to-clean check, or keep the check in the orchestrator.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Orchestrator (primary modification target)
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` -- Lines 400-423 are the legacy log read path to be replaced. Lines 330-448 are the per-plugin cleaning loop.
- `AutoQAC/Services/Cleaning/ICleaningOrchestrator.cs` -- Interface contract (may need TerminationResult check additions)

### CleaningService (secondary modification)
- `AutoQAC/Services/Cleaning/CleaningService.cs` -- Line 107 calls `outputParser.ParseOutput(result.OutputLines)` which now parses empty lists. Must be rewired.
- `AutoQAC/Services/Cleaning/ICleaningService.cs` -- Interface contract

### Log File Service (Phase 1 output -- consumed, not modified)
- `AutoQAC/Services/Cleaning/XEditLogFileService.cs` -- Game-aware `ReadLogContentAsync` with offset-based reading
- `AutoQAC/Services/Cleaning/IXEditLogFileService.cs` -- `CaptureOffset()`, `ReadLogContentAsync()`, `GetLogFilePath()`, `GetExceptionLogFilePath()`

### Parser (consumed, may need minor extension)
- `AutoQAC/Services/Cleaning/XEditOutputParser.cs` -- `ParseOutput()` regex patterns and `IsCompletionLine()` check
- No `IXEditOutputParser.cs` file -- interface is defined inline in `XEditOutputParser.cs`

### Models
- `AutoQAC/Models/CleaningResult.cs` -- `CleaningResult`, `PluginCleaningResult`, `CleaningStatistics`, `CleaningStatus`
- `AutoQAC/Models/LogReadResult.cs` -- `LogReadResult` record with `LogLines`, `ExceptionContent`, `Warning`

### Process Layer (Phase 2 output -- context only)
- `AutoQAC/Services/Process/ProcessExecutionService.cs` -- `OutputLines` and `ErrorLines` are now empty; `ProcessResult` unchanged
- `AutoQAC/Services/Process/IProcessExecutionService.cs` -- `ProcessResult` record shape

### Existing Tests
- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs` -- Orchestrator test suite (needs updated mocks for log-based parsing)
- `AutoQAC.Tests/Services/CleaningServiceTests.cs` -- CleaningService test suite (needs updated assertions)

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `XEditLogFileService.ReadLogContentAsync()`: Game-aware, offset-based log reading with retry -- ready to consume
- `XEditLogFileService.CaptureOffset()`: Byte offset capture -- call before each plugin clean
- `XEditOutputParser.ParseOutput()`: Regex patterns already correct for log content (Removing, Undeleting, Skipping, Making Partial Form)
- `XEditOutputParser.IsCompletionLine()`: Detects "Done." and "Cleaning completed" -- useful for nothing-to-clean check
- `PluginCleaningResult.LogParseWarning`: Existing field for log parse issues -- reuse for exception content

### Established Patterns
- Orchestrator uses `stateService.CurrentState` for game type and xEdit path
- `ConfigureAwait(false)` on all async service calls
- `CleaningStatus` enum determines UI display (Cleaned, Failed, Skipped)
- `PluginCleaningResult` aggregates per-plugin results for the session
- Orchestrator's per-plugin loop (lines 258-448) handles backup, retry, hang monitoring, and result collection

### Integration Points
- Orchestrator lines 400-423: Replace `ReadLogFileAsync(xEditPath, pluginStartTime)` with offset-based `ReadLogContentAsync`
- Orchestrator line 338-339: `pluginStartTime` can be replaced with offset capture
- CleaningService line 107: Remove `outputParser.ParseOutput(result.OutputLines)` -- parsing moves to orchestrator
- `stateService.CurrentState.XEditExecutablePath`: Provides xEdit directory for log path resolution

</code_context>

<specifics>
## Specific Ideas

- The orchestrator already has a "log enrichment" pattern (lines 400-423) that prefers log-file stats over stdout stats. Phase 3 replaces the legacy log read with the game-aware one and removes the stdout fallback entirely.
- Exception log append behavior was confirmed in Phase 1 context -- offset-based reading isolates current session's exceptions.
- STATE.md blocker note: "Verify MO2 wrapping works correctly without stdout redirect" -- this is a manual smoke test, not an automated concern for this phase.

</specifics>

<deferred>
## Deferred Ideas

- Multi-pass QAC aggregation policy noted in STATE.md as a Phase 3 concern -- auto-deferred to Claude's discretion since the retry loop structure already handles this.

</deferred>

---

*Phase: 03-integration-log-first-parsing*
*Context gathered: 2026-03-31*
