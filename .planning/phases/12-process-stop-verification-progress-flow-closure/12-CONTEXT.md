# Phase 12: Process Stop Verification & Progress Flow Closure - Context

**Gathered:** 2026-05-01
**Status:** Ready for planning

<domain>
## Phase Boundary

Phase 12 closes the Progress-window Stop flow gap by making Progress Stop reach the same confirmed two-stage xEdit termination outcomes as the main Stop command, then produces current verification evidence for `SAF-01`, `SAF-02`, `REF-04`, and `TEST-01`. This phase may minimally adjust shared Stop dialog/outcome handling and Progress-window result messaging, but it must not rework termination architecture, command launch behavior, MO2 handling, or cleaning sequencing.

</domain>

<spec_lock>
## Requirements (locked via SPEC.md)

**5 requirements are locked.** See `12-SPEC.md` for full requirements, boundaries, and acceptance criteria.

Downstream agents MUST read `12-SPEC.md` before planning or implementing. Requirements are not duplicated here.

**In scope (from SPEC.md):**
- Progress-window Stop behavior for grace-expired confirmation, confirmed force-stop, force-kill failure reporting, and declined leave-running outcome.
- Automated tests for the Progress-window Stop flow and requirement-relevant process/PID safety behavior.
- Current verification artifact(s) that close the Phase 5 orphaned requirement and Progress-window integration audit gaps.
- Preservation of existing `ProcessExecutionService`, `CleaningTerminationCoordinator`, and orchestrator two-stage stop semantics unless a minimal change is required to expose the same behavior through the Progress window.
- Verification that the existing process/PID tests remain current evidence for `REF-04` and `TEST-01`.

**Out of scope (from SPEC.md):**
- Reworking all termination architecture or replacing `CleaningTerminationCoordinator` - Phase 12 is a gap-closure phase, not another decomposition phase.
- Command launch escaping or MO2 missing-path behavior - those belong to Phase 13.
- Orchestrator decomposition reverification beyond stop/PID evidence - that belongs to Phase 14.
- Adding Avalonia.Headless or new UI test infrastructure - the repository currently has no headless UI test project.
- Parallelizing plugin cleaning or external process execution - sequential xEdit cleaning remains a hard runtime requirement.
- Mutagen or `QueryPlugins` changes - this phase only concerns AutoQAC process stop/progress flow and verification evidence.

</spec_lock>

<decisions>
## Implementation Decisions

### Stop Dialog Parity
- **D-01:** Progress-window Stop and main Stop must use the same shared confirmation, force-failure, and leave-running warning copy. Do not create Progress-specific wording for the same outcome.
- **D-02:** Treat the shared Stop dialog text as an exact text contract in tests. Tests should catch drift between main Stop and Progress Stop copy.
- **D-03:** Replace the generic Yes/No Stop confirmation with explicit action labels, shaped as `Force Terminate` and `Leave Running`. This may require a small, scoped dialog-service/API extension, but it must update both Stop surfaces together.
- **D-04:** While adding explicit action labels, refresh the shared Stop copy to Phase 11-safe wording. User-facing text must avoid raw exception text, stack traces, full local paths, command lines, and unnecessary path detail; use latest AutoQAC log guidance when technical details are needed.

### Outcome Visibility
- **D-05:** If the user declines force termination from the Progress-window Stop confirmation, the Progress window should enter the normal cancelled/results state and show a persistent result-summary warning that xEdit was left running by choice.
- **D-06:** If confirmed force termination from Progress Stop returns `ForceKillFailed`, show the shared force-failure dialog and keep a persistent result-summary warning that xEdit may still be running and the user should close it manually before starting another cleaning session.
- **D-07:** Persistent left-running and failed-stop messages belong in or near the Progress-window result summary area, not only near the transient Stop button/spinner area.
- **D-08:** Main Stop status text should align with the same outcome wording used by Progress Stop. The persistent Progress summary is Progress-specific, but the main-window status should not contradict or lag the refreshed shared outcome language.

### Hang Kill Boundary
- **D-09:** The existing Progress-window Hang warning `Kill` button remains an explicit immediate force action. Do not add the Stop confirmation to that button.
- **D-10:** If Hang warning `Kill` returns `ForceKillFailed`, use the same shared force-failure dialog and persistent Progress result-summary warning as confirmed Progress Stop force failure.
- **D-11:** If Hang warning `Kill` succeeds, do not add new success copy. The normal cancelled/results flow is sufficient.
- **D-12:** Add direct automated coverage for the Hang warning `Kill` boundary: it should call force-stop directly, skip confirmation, and report `ForceKillFailed` through the shared failure/outcome path.

### Verification Scope
- **D-13:** Phase 12 verification is the source of truth for closing the audit gap. Create `12-VERIFICATION.md`; do not create or update `05-VERIFICATION.md`, Phase 5 summary frontmatter, or older Phase 5 artifacts as part of this phase.
- **D-14:** `12-VERIFICATION.md` must include a requirement table plus evidence for `SAF-01`, `SAF-02`, `REF-04`, and `TEST-01`. For each ID, list status, relevant source files, relevant automated tests, commands run, and which audit gap(s) are closed.
- **D-15:** Verification evidence must include targeted Progress Stop/Hang Kill tests, process execution integration evidence, PID storage evidence, and a full solution test run. If the full suite has unrelated pre-existing failures, document them explicitly instead of marking the requirement evidence ambiguous.
- **D-16:** Do not update `REQUIREMENTS.md` or `ROADMAP.md` completion markers in Phase 12. Leave milestone-wide marker reconciliation to milestone completion or the relevant state/roadmap workflow after verification.

### the agent's Discretion
- Exact helper, service, DTO, method, and test class names are planner discretion.
- Whether the shared Stop outcome flow lives in a small helper, a ViewModel-adjacent service, or narrowly shared ViewModel code is planner discretion, as long as MVVM boundaries are preserved and the main/Progress surfaces cannot drift.
- Exact final refreshed copy may be chosen during implementation, but once chosen it must be shared by both Stop surfaces and asserted exactly in tests.
- Exact Progress result-summary layout is planner discretion, but it must be visible after cancelled/left-running and force-failure outcomes without adding a UI test infrastructure project.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase Scope And Requirements
- `.planning/phases/12-process-stop-verification-progress-flow-closure/12-SPEC.md` - Locked Phase 12 requirements, boundaries, constraints, acceptance criteria, and interview log. MUST read first.
- `.planning/ROADMAP.md` - Phase 12 goal, dependency, mapped requirements, audit gap closure note, and success criteria.
- `.planning/REQUIREMENTS.md` - Definitions and traceability for `SAF-01`, `SAF-02`, `REF-04`, and `TEST-01`.
- `.planning/PROJECT.md` - Cleanup milestone intent, project constraints, sequential xEdit requirement, MVVM boundaries, and prior key decisions.
- `.planning/v1.0-MILESTONE-AUDIT.md` - Source audit finding for orphaned Phase 5 requirements, `INT-01`, and `FLOW-01`; Phase 12 verification must explain how these are closed.

### Prior Locked Decisions
- `.planning/phases/11-user-facing-diagnostics-boundaries/11-CONTEXT.md` - Safe user-facing diagnostics and log-boundary decisions that apply to refreshed Stop failure/warning copy.
- `.planning/phases/10-configuration-persistence-hardening/10-CONTEXT.md` - Prior pre-cleaning flush and failure surfacing decisions; relevant mainly as carried-forward cleanup milestone context.
- `.planning/phases/09-plugin-refresh-approximation-performance/09-CONTEXT.md` - Existing ViewModel/service status-publication patterns and test conventions carried forward from recent phases.
- `.planning/phases/05-process-stop-pid-safety/05-CONTEXT.md` - Historical two-stage stop and PID safety context. Read for background only; Phase 12 must not rewrite old Phase 5 artifacts.
- `.planning/phases/05-process-stop-pid-safety/05-VALIDATION.md` - Existing Phase 5 validation evidence referenced by the audit; use as background for current evidence mapping.

### Codebase Constraints And Existing Patterns
- `.planning/codebase/TESTING.md` - xUnit, FluentAssertions, NSubstitute, helper-process, temp-file, and ViewModel test patterns; confirms no Avalonia.Headless project exists.
- `.planning/codebase/ARCHITECTURE.md` - Stop/termination flow, MVVM/dialog ownership, process execution constraints, state hub, and single process slot.
- `.planning/codebase/CONVENTIONS.md` - C# style, comments/XML docs, service/ViewModel conventions, logging and test naming expectations.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs` - Existing main Stop flow handles `GracePeriodExpired`, prompts for force termination, calls `ForceStopCleaningAsync`, reports `ForceKillFailed`, and calls `MarkLeftRunningByUser` on decline. This is the parity source, but copy should be refreshed/shared per D-01 through D-04.
- `AutoQAC/ViewModels/ProgressViewModel.cs` - Current `StopAsync` only awaits `_orchestrator.StopCleaningAsync()` and ignores the returned `StopCleaningResult`; `KillHungProcessAsync` calls `ForceStopCleaningAsync()` directly and needs shared failure handling.
- `AutoQAC/Services/Cleaning/ICleaningOrchestrator.cs` - Existing public stop surface already provides `StopCleaningAsync`, `ForceStopCleaningAsync`, `MarkLeftRunningByUser`, `LastTerminationResult`, and `HangDetected`; preserve this contract unless a minimal testability adjustment is required.
- `AutoQAC/Services/Cleaning/ICleaningTerminationCoordinator.cs` and `CleaningTerminationCoordinator.cs` - Own the two-stage termination semantics; Phase 12 should expose existing outcomes through Progress, not replace this policy.
- `AutoQAC/Services/UI/IMessageDialogService.cs` and `MessageDialogService.cs` - Existing dialog abstraction supports `ShowConfirmAsync` Yes/No and generic `ShowAsync` button sets, but not custom action labels. Explicit Stop labels may require a scoped extension here.
- `AutoQAC/Views/ProgressWindow.axaml` - Existing progress/results layout has a Stop/spinner area and a results summary panel. Persistent left-running and failed-stop messages should be placed in or near the result summary.
- `AutoQAC/Models/TerminationResult.cs` and `StopCleaningResult` - Existing result contracts include `GracePeriodExpired`, `ForceKillFailed`, and `LeftRunningByUser` semantics that should drive UI outcomes.

### Established Patterns
- Dialog/window operations must stay behind ViewModel interactions or UI services; ViewModels must not manipulate Avalonia controls directly.
- User-facing diagnostic text follows Phase 11 boundaries: concise safe copy, no raw exceptions, no stack traces, no full command lines, no unnecessary full local paths, and latest AutoQAC log guidance for technical details.
- Process control remains sequential and single-slot. Do not parallelize plugin cleaning or launch process APIs outside `ProcessExecutionService`.
- Tests should be targeted ViewModel/service/integration tests using xUnit, FluentAssertions, NSubstitute, and the existing controlled helper-process pattern. Do not add Avalonia.Headless for this phase.
- Existing `ProgressViewModelTests`, `MainWindowViewModelTests`, `CleaningOrchestratorTests`, `CleaningTerminationCoordinatorTests`, `ProcessExecutionServiceTests`, `ProcessExecutionIntegrationTests`, and `JsonPidStoreTests` are the natural test families to extend or reference.

### Integration Points
- `ProgressViewModel.StopCommand` should branch on `StopCleaningAsync()` result the same way the main Stop command does, including confirmation before force stop.
- `ProgressViewModel.KillHungProcessCommand` should remain direct force-stop, then route `ForceKillFailed` into the shared failure/outcome path.
- Main and Progress Stop should share copy and exact text assertions to prevent future drift.
- Any dialog-service change for explicit button labels must update production `MessageDialogService`, tests, and both Stop callers while preserving existing callers.
- `12-VERIFICATION.md` must cite the Progress Stop/Hang Kill tests, process execution integration tests, PID storage tests, and full solution test command output.

</code_context>

<specifics>
## Specific Ideas

- Use explicit confirmation buttons shaped as `Force Terminate` and `Leave Running` instead of generic Yes/No.
- Force-failure copy should clearly say xEdit may still be running, the user should close it manually before starting another cleaning session, and technical details are in the latest AutoQAC log.
- Leave-running copy should clearly say AutoQAC stopped/cancelled the session and xEdit was left running by the user's choice.
- Persistent Progress outcome copy belongs in the result summary because it must remain visible after the transient Stop/spinner state is gone.
- Verification should name audit gaps `INT-01` and `FLOW-01` explicitly and state how the new Progress Stop tests close them.

</specifics>

<deferred>
## Deferred Ideas

None - discussion stayed within phase scope. Broader milestone marker reconciliation, Phase 5 artifact rewriting, command launch/MO2 work, orchestrator reverification, and new UI test infrastructure remain outside Phase 12.

</deferred>

---

*Phase: 12-process-stop-verification-progress-flow-closure*
*Context gathered: 2026-05-01*
