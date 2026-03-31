# Phase 2: Process Layer -- Stop Stdout Capture - Context

**Gathered:** 2026-03-30
**Status:** Ready for planning

<domain>
## Phase Boundary

Remove dead stdout/stderr redirection from `ProcessExecutionService` so xEdit launches without capturing empty output streams. This phase modifies only the internal plumbing of `ExecuteAsync` — the public interface (`IProcessExecutionService`, `ProcessResult` fields, `IProgress<string>` parameter) stays intact for Phase 4 cleanup. No orchestration changes, no parser changes, no UI changes.

</domain>

<decisions>
## Implementation Decisions

### Scope Boundary (Phase 2 vs Phase 4)
- **D-01:** Minimal scope — Phase 2 only stops the redirect and removes internal capture plumbing (`BeginOutputReadLine`, `BeginErrorReadLine`, `OutputDataReceived`/`ErrorDataReceived` handlers, `ConcurrentQueue<string>` buffers). `ProcessResult.OutputLines`/`ErrorLines` fields and the `IProgress<string>` parameter remain in the interface for Phase 4 cleanup.
- **D-02:** `UseShellExecute` stays `false` — required for PID tracking, `WaitForExitAsync`, and `CreateNoWindow` control. Not discussed (clear-cut).

### Test Strategy
- **D-03:** Delete the `ExecuteAsync_ShouldCaptureConcurrentStdoutAndStderrWithoutLoss` test — it validates behavior that no longer exists. No replacement test needed; existing tests (startup failure, disposal, orchestrator-level termination/orphan cleanup) provide sufficient coverage.

### MO2 Mode
- **D-04:** MO2 wrapping is safe without stdout redirect. MO2 is a GUI app that doesn't write meaningful stdout. `ProcessExecutionService` tracks the MO2 PID and uses `WaitForExitAsync` — neither depends on stdout redirection. No additional verification or comments needed.

### Claude's Discretion
- Whether to leave `OutputLines`/`ErrorLines` as empty lists in the return value or skip populating them entirely (both are valid since the fields stay in the record)

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Process Execution (primary target)
- `AutoQAC/Services/Process/ProcessExecutionService.cs` — Lines 50-86 contain the redirect setup, event handlers, and BeginOutputReadLine/BeginErrorReadLine calls to be removed
- `AutoQAC/Services/Process/IProcessExecutionService.cs` — Interface contract and `ProcessResult` record definition (NOT modified in this phase)

### Command Building (MO2 wrapping reference)
- `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs` — Lines 52-65 show MO2 wrapping; returns ProcessStartInfo that ProcessExecutionService consumes

### Downstream Consumer (Phase 3/4 concern, read-only reference)
- `AutoQAC/Services/Cleaning/CleaningService.cs` — Line 107 consumes `result.OutputLines` via `outputParser.ParseOutput()` — NOT modified in this phase

### Tests (to be modified)
- `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs` — Lines 133-155: `ExecuteAsync_ShouldCaptureConcurrentStdoutAndStderrWithoutLoss` to be deleted

</canonical_refs>

<code_context>
## Existing Code Insights

### What Changes
- `ProcessExecutionService.ExecuteAsync()` lines 50-86: Remove `RedirectStandardOutput = true`, `RedirectStandardError = true`, the `ConcurrentQueue<string>` buffers, `OutputDataReceived`/`ErrorDataReceived` event handlers, and `BeginOutputReadLine()`/`BeginErrorReadLine()` calls
- Return value at line 155-161: `OutputLines` and `ErrorLines` will be empty lists (fields kept for interface compatibility)

### What Stays Intact
- `UseShellExecute = false` — needed for `WaitForExitAsync`, PID tracking, process tree kill
- `CreateNoWindow = false` — xEdit is a GUI app, users see its window
- All PID tracking, timeout handling, termination escalation logic — unchanged
- `IProgress<string>` parameter — stays in signature but won't be called (Phase 4 cleanup)
- `ProcessResult.OutputLines`/`ErrorLines` — stay as empty lists (Phase 4 cleanup)

### Integration Points
- `CleaningService.CleanPluginAsync()` calls `processService.ExecuteAsync()` and reads `result.OutputLines` — will get empty list (Phase 3 replaces this with log-based parsing)
- `CleaningOrchestrator` calls `CleanPluginAsync()` — unaffected by this change
- `ServiceCollectionExtensions` registers `ProcessExecutionService` as singleton — no registration changes

</code_context>

<specifics>
## Specific Ideas

No specific requirements — the changes are surgical and well-defined by the codebase analysis.

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 02-process-layer-stop-stdout-capture*
*Context gathered: 2026-03-30*
