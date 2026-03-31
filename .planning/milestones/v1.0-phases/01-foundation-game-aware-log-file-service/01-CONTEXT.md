# Phase 1: Foundation -- Game-Aware Log File Service - Context

**Gathered:** 2026-03-30
**Status:** Ready for planning

<domain>
## Phase Boundary

Build correct log file naming and offset-based reading as the foundation for all downstream parsing. This phase delivers a service that can resolve the correct log filename for any supported game type and read only new content appended during a single xEdit run. No orchestration wiring, no parser changes, no UI changes.

</domain>

<decisions>
## Implementation Decisions

### Log Filename Resolution
- **D-01:** Log filename must be derived from game type, not executable stem. Researcher must verify xEdit's actual naming convention from xEdit source code — specifically whether the log prefix is game-aware (e.g., `SSEEdit_log.txt` when running `xEdit.exe -SSE`) or executable-stem-based, and what casing rules apply.
- **D-02:** The service must accept `GameType` as a parameter (not just executable path) to support universal `xEdit.exe` with game flags.

### Exception Log Handling
- **D-03:** Exception log uses the same offset-based approach as the main log — capture byte offset before xEdit launch, read only new content after exit. The exception file does not get cleared between runs, so a full read would include stale exceptions from prior sessions.
- **D-04:** The service contract should return exception log content (not just a path), applying the offset to isolate this run's exceptions only.

### Claude's Discretion
- Retry strategy for file contention (OFF-04): Claude decides the retry count, delay pattern, and total timeout based on research into typical antivirus/indexer lock durations on Windows.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Existing Service (to be modified)
- `AutoQAC/Services/Cleaning/XEditLogFileService.cs` — Current implementation using executable-stem-based naming and timestamp staleness detection (both to be replaced)
- `AutoQAC/Services/Cleaning/IXEditLogFileService.cs` — Current interface contract (will need GameType parameter and offset-based API)

### Game Detection (mapping reference)
- `AutoQAC/Services/GameDetection/GameDetectionService.cs` — Has `ExecutablePatterns` dict mapping executable names to GameType; `GetGameFlag()` in XEditCommandBuilder maps GameType to CLI flags
- `AutoQAC/Models/GameType.cs` — Enum of all supported game types

### Command Building (naming reference)
- `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs` — `GetGameFlag()` maps GameType to xEdit flags (-SSE, -FO4, etc.); useful for understanding game-to-prefix relationships

### Existing Tests
- `AutoQAC.Tests/Services/XEditLogFileServiceTests.cs` — Tests verify current (incorrect) behavior; must be rewritten to match new game-aware, offset-based contract

### xEdit Source (for researcher)
- Researcher should verify log naming convention from xEdit source code (GitHub: TES5Edit/TES5Edit repository) to confirm exact prefix-per-game and casing rules

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `GameDetectionService.ExecutablePatterns`: Maps executable names to GameType — can inform the reverse mapping (GameType to log prefix)
- `XEditCommandBuilder.GetGameFlag()`: Maps GameType to CLI flags — parallel structure to what the log prefix mapping needs
- `XEditOutputParser`: Regex patterns for parsing log content are correct and will be consumed by Phase 3 against content this service provides

### Established Patterns
- Primary constructors with `ILoggingService` injection: `XEditLogFileService(ILoggingService logger)`
- `ConfigureAwait(false)` on all async calls in service layer
- `CancellationToken ct = default` as last parameter on async methods
- Return tuples for result+error: `Task<(List<string> lines, string? error)>`

### Integration Points
- `CleaningOrchestrator` calls `logFileService.ReadLogFileAsync()` at line ~407 — will need to pass GameType and use offset-based API
- `ServiceCollectionExtensions` registers `IXEditLogFileService` as singleton
- `ProcessExecutionServiceTests` references `XEditLogFileService` (likely for integration-style tests)

</code_context>

<specifics>
## Specific Ideas

- Exception log append behavior was confirmed by the user — treat it identically to the main log with offset tracking, not as a one-shot read.

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 01-foundation-game-aware-log-file-service*
*Context gathered: 2026-03-30*
