# Phase 12: Process Stop Verification & Progress Flow Closure - Specification

**Created:** 2026-05-01
**Ambiguity score:** 0.19 (gate: <= 0.20)
**Requirements:** 5 locked

## Goal

Progress-window Stop uses the same confirmed two-stage xEdit termination outcomes as the main cleaning Stop command, and current verification artifacts prove `SAF-01`, `SAF-02`, `REF-04`, and `TEST-01` are satisfied.

## Background

Phase 5 implemented the process termination and PID safety foundation: `ProcessExecutionService` tracks external process PIDs through injectable PID storage, preserves PID evidence for user-stop/left-running/failure outcomes, and uses a two-stage termination model where user cancellation returns `GracePeriodExpired` instead of force-killing immediately. `CleaningTerminationCoordinator` owns active-process state, stop/force-stop behavior, hang monitoring, and the last termination result.

The current main-window Stop path in `CleaningCommandsViewModel.StopCleaningAsync` handles `GracePeriodExpired` by showing a force-termination confirmation before calling `ForceStopCleaningAsync`; it also reports `ForceKillFailed` and marks `LeftRunningByUser` when the user declines. The Progress-window Stop path in `ProgressViewModel.StopAsync` currently only awaits `_orchestrator.StopCleaningAsync()` and ignores the returned termination result, so the Progress window cannot reach the confirmation, decline, force-stop, or force-failure reporting outcomes.

The milestone audit also found that Phase 5 has `05-VALIDATION.md` and process/PID tests, but no `05-VERIFICATION.md`, and its plan summaries did not record completed requirement IDs. Phase 12 closes that audit gap by fixing or verifying the Progress-window flow and producing current requirement-mapped verification evidence.

## Requirements

1. **Progress Stop confirmation parity**: A Stop request from the Progress window must show the force-termination confirmation after `StopCleaningAsync` returns `GracePeriodExpired`, before any force-kill request is made.
   - Current: `ProgressViewModel.StopAsync` ignores `StopCleaningAsync` results, while `CleaningCommandsViewModel.StopCleaningAsync` shows the confirmation and controls escalation.
   - Target: The Progress-window Stop affordance reaches the same user-confirmed escalation outcome as the main Stop command when xEdit does not exit gracefully.
   - Acceptance: An automated test simulates `GracePeriodExpired` from a Progress-window Stop request and verifies that confirmation is requested and `ForceStopCleaningAsync` is not called before affirmative confirmation.

2. **Progress Stop force-failure reporting**: A confirmed force-stop request from the Progress window must report an accurate user-facing failure when force-killing xEdit fails.
   - Current: The Progress-window Stop path does not call `ForceStopCleaningAsync` after grace expiration and therefore cannot surface `ForceKillFailed`.
   - Target: When the user confirms force termination and `ForceStopCleaningAsync` returns `ForceKillFailed`, the Progress-window flow reports that xEdit may still be running and that the user should close it manually or check logs.
   - Acceptance: An automated test simulates confirmed force termination followed by `ForceKillFailed` and verifies that the failure outcome is reported through user-facing copy without claiming xEdit was stopped successfully.

3. **Progress Stop leave-running outcome**: A declined force-termination confirmation from the Progress window must preserve the left-running state and tell the user that xEdit was left running by choice.
   - Current: The main Stop path calls `MarkLeftRunningByUser()` and shows a warning when the user declines; the Progress-window Stop path cannot reach that outcome.
   - Target: If the user declines force termination from the Progress-window flow, AutoQAC marks the session as `LeftRunningByUser` and shows a warning/status outcome that xEdit was left running.
   - Acceptance: An automated test simulates `GracePeriodExpired` followed by a declined confirmation and verifies `MarkLeftRunningByUser()` is invoked and no force-kill request is made.

4. **Process and PID safety evidence**: The phase must preserve and verify the existing process/PID safety guarantees that support `REF-04` and `TEST-01`.
   - Current: `JsonPidStore`, `ProcessExecutionService`, and `ProcessExecutionIntegrationTests` cover injectable PID storage, real helper-process timeout, graceful behavior, process-tree force kill, user cancellation, PID evidence preservation, and PID cleanup, but the audit did not count them because verification mapping was missing/stale.
   - Target: Current automated evidence exists and passes for PID storage update behavior, real child-process timeout, user-stop grace expiration, force-kill failure reporting, force-kill cleanup, and PID cleanup/preservation semantics.
   - Acceptance: Targeted automated tests for process execution, process integration, PID storage, and Progress-window Stop outcomes pass and are referenced by the Phase 12 verification artifact.

5. **Requirement closure verification**: Phase 12 must produce current verification evidence that explicitly maps `SAF-01`, `SAF-02`, `REF-04`, and `TEST-01` to passing code/tests.
   - Current: `.planning/v1.0-MILESTONE-AUDIT.md` marks those four requirements orphaned because Phase 5 lacks a verification artifact and the Progress-window Stop flow is broken.
   - Target: Phase 12 verification states that all four requirements are satisfied, cites the fixed Progress-window flow and process/PID evidence, and explains why the audit blockers are closed.
   - Acceptance: `.planning/phases/12-process-stop-verification-progress-flow-closure/12-VERIFICATION.md` exists, lists all four requirement IDs, marks each as satisfied/passed, and references the relevant source files and automated test commands.

## Boundaries

**In scope:**
- Progress-window Stop behavior for grace-expired confirmation, confirmed force-stop, force-kill failure reporting, and declined leave-running outcome.
- Automated tests for the Progress-window Stop flow and requirement-relevant process/PID safety behavior.
- Current verification artifact(s) that close the Phase 5 orphaned requirement and Progress-window integration audit gaps.
- Preservation of existing `ProcessExecutionService`, `CleaningTerminationCoordinator`, and orchestrator two-stage stop semantics unless a minimal change is required to expose the same behavior through the Progress window.
- Verification that the existing process/PID tests remain current evidence for `REF-04` and `TEST-01`.

**Out of scope:**
- Reworking all termination architecture or replacing `CleaningTerminationCoordinator` - Phase 12 is a gap-closure phase, not another decomposition phase.
- Command launch escaping or MO2 missing-path behavior - those belong to Phase 13.
- Orchestrator decomposition reverification beyond stop/PID evidence - that belongs to Phase 14.
- Adding Avalonia.Headless or new UI test infrastructure - the repository currently has no headless UI test project.
- Parallelizing plugin cleaning or external process execution - sequential xEdit cleaning remains a hard runtime requirement.
- Mutagen or `QueryPlugins` changes - this phase only concerns AutoQAC process stop/progress flow and verification evidence.

## Constraints

- User-initiated Stop must not force-kill xEdit until after the force-termination confirmation path is shown and affirmed.
- Timeout-driven process cleanup may keep the existing automated safety behavior that force-kills after timeout without prompting; Phase 12 concerns user Stop paths.
- Sequential cleaning and the single process slot in `ProcessExecutionService` must be preserved.
- Progress-window changes must respect existing MVVM/dialog ownership boundaries; ViewModels must not directly manipulate Avalonia controls.
- User-facing failure text must remain compatible with Phase 11 diagnostics boundaries by avoiding raw exception text, stack traces, full command lines, or unnecessary local paths.
- Real process tests must use the existing controlled helper-process pattern rather than xEdit, shell scripts, or brittle OS-specific manual setup.

## Acceptance Criteria

- [ ] Progress-window Stop with `GracePeriodExpired` requests force-termination confirmation before any force-stop call.
- [ ] Progress-window Stop with confirmed force termination calls `ForceStopCleaningAsync` and reports `ForceKillFailed` accurately when returned.
- [ ] Progress-window Stop with declined force termination calls `MarkLeftRunningByUser()` and does not call `ForceStopCleaningAsync`.
- [ ] Targeted automated tests covering Progress Stop, process execution integration, and PID storage pass.
- [ ] Phase 12 verification artifact maps `SAF-01`, `SAF-02`, `REF-04`, and `TEST-01` to passing evidence and marks them satisfied.
- [ ] Verification evidence explains that the audit's `INT-01` and `FLOW-01` Progress-window Stop blockers are closed.
- [ ] Full solution tests pass or any unrelated pre-existing failures are explicitly documented in verification.

## Ambiguity Report

| Dimension           | Score | Min   | Status | Notes |
|---------------------|-------|-------|--------|-------|
| Goal Clarity        | 0.92  | 0.75  | met    | Progress Stop parity plus requirement closure is specific. |
| Boundary Clarity    | 0.74  | 0.70  | met    | Scope is limited to Progress Stop flow and current stop/PID evidence. |
| Constraint Clarity  | 0.67  | 0.65  | met    | Sequential cleaning, no unconfirmed force-kill, MVVM, diagnostics, and helper-process constraints are explicit. |
| Acceptance Criteria | 0.82  | 0.70  | met    | Pass/fail tests and verification artifact checks are defined. |
| **Ambiguity**       | 0.19  | <=0.20 | met   | Gate passed after round 1. |

Status: met = dimension reached minimum; below = planner treats as assumption.

## Interview Log

| Round | Perspective | Question summary | Decision locked |
|-------|-------------|------------------|-----------------|
| 1 | Researcher | What is the primary user-facing delta for Phase 12? | Progress-window Stop must reach the same confirmed two-stage termination outcomes as the main Stop command. |
| 1 | Researcher | What evidence must exist for the audit to count the stop/PID requirements as satisfied? | Automated tests plus a Phase 12 verification artifact must map `SAF-01`, `SAF-02`, `REF-04`, and `TEST-01` to passing evidence. |
| 1 | Gate | Ambiguity scored 0.19 with all dimensions above minimum. | User selected "Yes, write SPEC.md". |

---

*Phase: 12-process-stop-verification-progress-flow-closure*
*Spec created: 2026-05-01*
*Next step: /gsd-discuss-phase 12 - implementation decisions (how to expose the existing stop outcomes through the Progress window)*
