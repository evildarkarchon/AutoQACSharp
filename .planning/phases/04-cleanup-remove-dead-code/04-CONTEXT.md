# Phase 4: Cleanup -- Remove Dead Code - Context

**Gathered:** 2026-03-31
**Status:** Ready for planning

<domain>
## Phase Boundary

Remove all dead stdout parsing code paths, stale timestamp-based log detection, unused IProgress parameters, and stale test infrastructure so the codebase reflects the log-file-only parsing reality established in Phases 1-3. No new features, no architectural changes -- pure dead code removal.

</domain>

<decisions>
## Implementation Decisions

### Obsolete Method Removal
- **D-01:** Delete `ReadLogFileAsync(string, DateTime, CancellationToken)` and `GetLogFilePath(string)` from `XEditLogFileService` and `IXEditLogFileService`. Remove the `[Obsolete]` markers and the methods themselves. Delete all 3 legacy timestamp-based tests in `XEditLogFileServiceTests.cs` (lines 520-576 region). Clean cut, no deprecation period.

### OutputParser Dependency Cleanup
- **D-02:** Remove `IXEditOutputParser` constructor parameter from `CleaningService` only. The orchestrator keeps its `IXEditOutputParser` dependency -- it's actively used there for `ParseOutput()` and `IsCompletionLine()`. Remove corresponding `_mockOutputParser` field from `CleaningServiceTests`. Update DI registration if needed.

### IProgress Parameter Removal
- **D-03:** Remove `IProgress<string>?` parameter from all methods in the chain: `IProcessExecutionService.ExecuteAsync`, `ProcessExecutionService.ExecuteAsync`, `ICleaningService.CleanPluginAsync`, `CleaningService.CleanPluginAsync`. Update all callers (CleaningOrchestrator passes a debug Progress -- remove that too). Update all test call sites.

### ProcessResult Field Removal
- **D-04:** Remove both `OutputLines` and `ErrorLines` properties from the `ProcessResult` record in `IProcessExecutionService.cs`. The startup failure path that populates `ErrorLines = [ex.Message]` is redundant -- the exception is already thrown/logged. Remove all test assertions that reference these fields (e.g., `ProcessExecutionServiceTests` line 89).

### Claude's Discretion
- Order of removals within the plan (dependency-safe sequencing)
- Whether to clean up any imports that become unused after removals
- Whether to update DI registration in `ServiceCollectionExtensions.cs` if constructor signatures change

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### CleaningService (primary target -- dead parser param)
- `AutoQAC/Services/Cleaning/CleaningService.cs` -- Line 20: dead `IXEditOutputParser outputParser` constructor param
- `AutoQAC/Services/Cleaning/ICleaningService.cs` -- Interface contract, `IProgress<string>?` param to remove

### ProcessExecutionService (dead fields and params)
- `AutoQAC/Services/Process/ProcessExecutionService.cs` -- Line 33: dead `IProgress<string>?` param; lines 138-139: empty OutputLines/ErrorLines
- `AutoQAC/Services/Process/IProcessExecutionService.cs` -- Lines 15, 51-52: `IProgress<string>?` param and `OutputLines/ErrorLines` on ProcessResult record

### XEditLogFileService (obsolete methods)
- `AutoQAC/Services/Cleaning/XEditLogFileService.cs` -- Lines 187-223: `[Obsolete] ReadLogFileAsync` with timestamp staleness
- `AutoQAC/Services/Cleaning/IXEditLogFileService.cs` -- Lines 28, 41: `[Obsolete]` interface entries for legacy methods

### CleaningOrchestrator (caller updates)
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` -- Lines 332-335: passes debug `IProgress<string>` to CleanPluginAsync (remove)

### DI Registration
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` -- May need updates if constructor signatures change

### Tests (mock and assertion cleanup)
- `AutoQAC.Tests/Services/CleaningServiceTests.cs` -- Line 29: dead `_mockOutputParser` field
- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs` -- Line 30: dead `_outputParserMock` field (only passed to constructor, never configured)
- `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs` -- Line 89: stale `ErrorLines.Should().NotBeEmpty()` assertion
- `AutoQAC.Tests/Services/XEditLogFileServiceTests.cs` -- Lines 520-576: 3 legacy tests for obsolete ReadLogFileAsync

</canonical_refs>

<code_context>
## Existing Code Insights

### What Gets Removed
- `IXEditOutputParser` param from CleaningService constructor (dead since Phase 3)
- `IProgress<string>?` params from 4 method signatures (2 interfaces + 2 implementations)
- `OutputLines` and `ErrorLines` from ProcessResult record
- `ReadLogFileAsync()` and `GetLogFilePath(string)` from XEditLogFileService + interface
- 3 legacy tests + stale mock fields + stale assertions

### What Stays
- `IXEditOutputParser` on CleaningOrchestrator (actively used for log parsing)
- `ReadLogContentAsync()` and `CaptureOffset()` on XEditLogFileService (the new API)
- All offset-based reading infrastructure from Phase 1
- All orchestrator log integration from Phase 3

### Ripple Effects
- Removing `IProgress<string>?` from interfaces requires updating every caller and test mock
- Removing `OutputLines/ErrorLines` from ProcessResult requires updating every test that constructs a ProcessResult
- Removing `IXEditOutputParser` from CleaningService requires updating DI registration if it was explicitly wired

</code_context>

<specifics>
## Specific Ideas

No specific requirements -- all decisions are clear-cut removals with well-defined boundaries.

</specifics>

<deferred>
## Deferred Ideas

None -- discussion stayed within phase scope.

</deferred>

---

*Phase: 04-cleanup-remove-dead-code*
*Context gathered: 2026-03-31*
