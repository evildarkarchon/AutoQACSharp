# Phase 5: Process Stop & PID Safety - Context

**Gathered:** 2026-04-28
**Status:** Ready for planning

<domain>
## Phase Boundary

Phase 5 delivers reliable two-stage xEdit stop/force-stop behavior and testable PID/orphan cleanup. It is limited to process termination coordination, PID tracking safety, and controlled process test coverage. It must preserve sequential xEdit cleaning and should not broaden into command escaping, backup restore safety, general orchestrator decomposition, or diagnostics polish from later phases.

</domain>

<decisions>
## Implementation Decisions

### Stop Ownership
- **D-01:** User-initiated force-kill escalation belongs in the orchestrator/ViewModel confirmation flow, not as automatic escalation inside `ProcessExecutionService.ExecuteAsync`.
- **D-02:** For user-initiated Stop, the process service should report `GracePeriodExpired` after graceful termination fails; AutoQAC should prompt before force-killing.
- **D-03:** If the user declines the force-terminate prompt, leave xEdit running and report that AutoQAC stopped/cancelled while xEdit was left running by user choice.
- **D-04:** A second Stop click during the grace/confirmation path is explicit escalation and may force-kill immediately without another prompt.
- **D-05:** Timeout handling may still auto-force after the grace period. The no-auto-force rule is specifically for user-initiated Stop.

### Kill Outcomes
- **D-06:** Add a distinct termination result for failed force-kill attempts, such as `ForceKillFailed`; do not report `ForceKilled` when `Process.Kill` or the post-kill wait fails.
- **D-07:** `ForceKilled` means `Kill(entireProcessTree: true)` was invoked and the tracked/root process exited. Descendant-process uncertainty may be logged but does not require proving every descendant exited.
- **D-08:** Force-kill failure should propagate to user-visible state and a concise dialog; detailed exception information belongs in logs.
- **D-09:** Failed force-kill should block post-exit log parsing for the affected plugin because xEdit may still be running or flushing logs.

### PID Storage
- **D-10:** Keep the JSON PID store, but introduce injected PID storage/path abstractions so tests can control storage without reflection.
- **D-11:** Protect PID store read-modify-write operations with an interprocess file lock.
- **D-12:** If the PID file is corrupt, preserve a timestamped copy, log corruption metadata, and recreate a clean PID store.
- **D-13:** Add a session ID to PID entries so orphan cleanup can distinguish current-run and prior-run entries without adding extra executable path exposure.
- **D-14:** Add a single-instance app lock that blocks multiple AutoQAC instances.

### Process Tests
- **D-15:** Real child-process tests should live under `AutoQAC.Tests` as integration/process coverage rather than in a new test project.
- **D-16:** The real-process tests should run by default with `dotnet test`, provided they use controlled short-lived local helper processes and reliable cleanup.
- **D-17:** Use a tiny test helper executable/project that can sleep, ignore graceful close, exit on command, and support force-kill scenarios.
- **D-18:** Process-test timing should be generous but bounded, with cleanup in `finally` paths to avoid flaky hangs or orphaned helper processes.

### the agent's Discretion
- Exact names for new interfaces/classes, result enum values, session ID format, lock implementation details, and test helper project naming are left to downstream research/planning.
- The planner may choose the smallest internal API changes that satisfy the decisions above, as long as user-visible stop semantics are preserved.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Planning Scope
- `.planning/ROADMAP.md` — Phase 5 goal, requirements, dependencies, and success criteria.
- `.planning/REQUIREMENTS.md` — Requirements `SAF-01`, `SAF-02`, `REF-04`, and `TEST-01` mapped to Phase 5.
- `.planning/PROJECT.md` — Project constraints: sequential xEdit cleaning, MVVM boundaries, Windows-only assumptions, and cleanup milestone intent.
- `.planning/STATE.md` — Current milestone state and Phase 5 blocker notes.

### Codebase Analysis
- `.planning/codebase/CONCERNS.md` — Source audit identifying stop-flow bypass, force-kill misreporting, private PID storage, and missing process test harness.
- `.planning/codebase/ARCHITECTURE.md` — Process execution, cleaning orchestration, state, and ViewModel interaction responsibilities.
- `.planning/codebase/TESTING.md` — Existing test organization and patterns; notes that current process tests avoid real processes and PID path logic is hard to test.

### Runtime/API Semantics
- `https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.closemainwindow` — `CloseMainWindow` is a graceful request and does not force process exit.
- `https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.kill` — `Kill` is abnormal termination, can throw, and `Kill(true)` has descendant-process caveats.
- `https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.waitforexitasync` — `WaitForExitAsync` cancellation cancels the wait operation and must not be conflated with termination success.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `AutoQAC/Services/Process/IProcessExecutionService.cs` — Existing process abstraction to extend with precise termination semantics and testable PID storage dependencies.
- `AutoQAC/Models/TerminationResult.cs` — Existing result enum to extend with a failed force-kill outcome.
- `AutoQAC/Services/Process/ProcessExecutionService.cs` — Current single-slot executor, orphan cleanup, PID tracking, and termination code. This is the primary implementation target.
- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs` — Owns current process reference, stop flags, user-stop coordination, and hang monitor cleanup. This is where user-initiated escalation should be coordinated.
- `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs` — Existing UI prompt path for `GracePeriodExpired`; use this boundary for user confirmation and concise failure surfacing.
- `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs` and `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs` — Existing test coverage and helper patterns for cancellation, orchestrator stop flow, and process-related assertions.

### Established Patterns
- Process execution is intentionally serialized by `SemaphoreSlim(1, 1)` in `ProcessExecutionService`; preserve this hard constraint.
- ViewModels should not terminate processes directly; they call orchestrator commands and show dialogs through UI services.
- Tests use xUnit, FluentAssertions, NSubstitute, `TaskCompletionSource` coordination, temp directories, and `try/finally` cleanup for disposable or external resources.
- Existing code comments document important safety constraints. Preserve and update comments where behavior changes, especially around stop escalation and empty catch/finally handling.

### Integration Points
- DI registration in `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` must register any new PID store, path provider, single-instance guard, or process helper services.
- Cleaning flow passes process references from `CleaningService.CleanPluginAsync` into `CleaningOrchestrator` through `onProcessStarted`; this remains the bridge for stop coordination.
- State updates flow through `IStateService`; any force-kill failure or "left running" outcome should be represented without bypassing state service patterns.
- Startup and pre-clean orphan cleanup currently call `CleanOrphanedProcessesAsync`; PID storage changes must preserve both flows.

</code_context>

<specifics>
## Specific Ideas

- The key product behavior is explicit user control: first Stop requests graceful exit, prompt before force-kill, second Stop force-kills immediately.
- Leaving xEdit running after the user declines force termination is acceptable and should be clearly reported.
- Single-instance blocking is in scope for Phase 5 even though file locking is also required for PID store safety.
- Process tests should be normal `dotnet test` coverage, not hidden behind environment flags, as long as they are reliable and bounded.

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 05-process-stop-pid-safety*
*Context gathered: 2026-04-28*
