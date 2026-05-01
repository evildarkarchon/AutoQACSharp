# Phase 15: Stop Escalation Ownership Closure - Specification

**Created:** 2026-05-01
**Ambiguity score:** 0.08 (gate: <= 0.20)
**Requirements:** 5 locked

## Goal

After Progress-window Stop reaches `GracePeriodExpired`, selecting `Force Terminate` must force-kill the still-running xEdit process even if the runner has detached or process execution has returned, while genuine OS/process force-kill failures are reported through the shared safe failure outcome.

## Background

Phase 12 wired the Progress-window Stop path to the shared confirmation dialog and failure copy. The current code still has an ownership gap in the confirmed force-escalation flow: `CleaningOrchestrator.StopCleaningAsync` cancels the session token before delegating to `CleaningTerminationCoordinator.StopAsync`, `ProcessExecutionService.ExecuteAsync` maps user cancellation to `GracePeriodExpired` and preserves PID evidence, and `PluginCleaningRunner` invokes `detachProcess()` in a trailing `finally`. `CleaningTerminationCoordinator.DetachProcess()` then clears `_currentProcess`, so a later confirmed `ForceStopCleaningAsync` can return the cached `GracePeriodExpired` result instead of killing xEdit or surfacing a failure. The milestone audit records this as `INT-STOP-01` and `FLOW-STOP-ESCALATION-01`, leaving `SAF-01`, `SAF-02`, and `TEST-01` pending for Phase 15.

## Requirements

1. **Confirmed detached force termination**: A Progress-window Stop flow that reaches `GracePeriodExpired` must retain or recover enough xEdit ownership for a later confirmed `Force Terminate` choice to force-kill the still-running process after runner detach or process execution return.
   - Current: `CleaningTerminationCoordinator.DetachProcess()` clears `_currentProcess`; `ForceStopAsync()` with no current process returns the cached last termination result and may not kill anything.
   - Target: After the user confirms `Force Terminate`, the still-running xEdit process is force-killed in the detached/returned case instead of silently reusing the cached `GracePeriodExpired` state.
   - Acceptance: An automated test drives `GracePeriodExpired`, simulates runner detach or returned process execution, confirms `Force Terminate`, and verifies the external process exits and the stop result is `ForceKilled`.

2. **No pre-confirm force kill**: The ownership fix must preserve the user-confirmation boundary before force-killing xEdit on user-initiated Stop.
   - Current: Phase 12 tests prove the confirmation dialog is shown before `ForceStopCleaningAsync`, but Phase 15 must not regress this while changing ownership lifetime.
   - Target: User-initiated Progress Stop never force-kills xEdit until `GracePeriodExpired` has been surfaced and the user selects `Force Terminate`.
   - Acceptance: An automated Progress-window Stop test holds the confirmation dialog open and verifies no force-stop request or force-kill attempt occurs before the dialog returns affirmative confirmation.

3. **Force-failure reporting remains explicit**: If a confirmed force-kill attempt fails for genuine OS, access, or process-state reasons, the user must receive the shared safe force-failure dialog and persistent Progress warning.
   - Current: When `_currentProcess` has been detached, `ForceStopAsync()` can return cached `GracePeriodExpired`; `ProgressViewModel` reports failure only for `ForceKillFailed`, so the confirmed force path can fail silently.
   - Target: An unable-to-kill outcome after confirmed force termination is represented as `ForceKillFailed` or an equivalent user-visible failure path that uses `StopTerminationDialogContent.ForceFailureTitle` and `ForceFailureMessage`.
   - Acceptance: Automated tests simulate confirmed force termination followed by an unable-to-kill result and verify the shared force-failure dialog is requested and `StopOutcomeWarningText` is set to the shared force-failure message.

4. **Detached escalation coverage**: Phase 15 must add or update automated evidence for the specific audit gap: `GracePeriodExpired` followed by runner detach/returned execution and confirmed `Force Terminate`.
   - Current: Existing Progress Stop tests cover prompt/decline/`ForceKillFailed`, and process/PID tests cover user cancellation preserving PID evidence, but no test covers the integration sequence where detach happens before confirmed force escalation.
   - Target: Tests cover the detached escalation sequence and guard against a silent cached `GracePeriodExpired` result when xEdit may still be running.
   - Acceptance: Targeted test output includes a passing test whose setup and assertions explicitly include `GracePeriodExpired`, detach or returned execution, affirmative `Force Terminate`, and either process exit via `ForceKilled` or explicit `ForceKillFailed` reporting for genuine kill failure.

5. **Requirement and audit closure evidence**: Phase 15 must produce current verification evidence that maps `SAF-01`, `SAF-02`, `TEST-01`, `INT-STOP-01`, and `FLOW-STOP-ESCALATION-01` to passing code and test evidence.
   - Current: `.planning/v1.0-MILESTONE-AUDIT.md` marks `SAF-01`, `SAF-02`, and `TEST-01` unsatisfied because Phase 12 evidence is undermined by the detached escalation ownership gap.
   - Target: Phase 15 verification states that the ownership gap is closed, cites the detached escalation tests and relevant stop/process code, and explains why the affected requirements and audit gaps are satisfied.
   - Acceptance: `.planning/phases/15-stop-escalation-ownership-closure/15-VERIFICATION.md` exists after verification, lists all three requirement IDs plus both audit gap IDs, marks them satisfied/passed, and references exact test commands and source files.

## Boundaries

**In scope:**
- Progress-window Stop flow after `GracePeriodExpired`, affirmative `Force Terminate`, runner detach or process execution return, and final force-kill/failure outcome.
- Minimal shared stop/termination ownership changes required so confirmed force escalation still has a real target after detach.
- Persistent Progress-window warning and shared safe force-failure dialog behavior for genuine force-kill failures.
- Automated unit/integration evidence for the detached escalation sequence and relevant stop/process/PID behavior.
- Phase 15 verification evidence for `SAF-01`, `SAF-02`, `TEST-01`, `INT-STOP-01`, and `FLOW-STOP-ESCALATION-01`.

**Out of scope:**
- Broad termination architecture rewrite - this phase closes one ownership gap, not a new decomposition pass.
- Redesigning timeout-driven cleanup, orphan cleanup, backup cancellation, or Hang Kill behavior - they are adjacent stop/process flows and are not the audit blocker.
- Command launch escaping or MO2 missing-path behavior - Phase 13 owns that scope.
- Milestone marker reconciliation in `ROADMAP.md` or `REQUIREMENTS.md` - Phase 16 owns final audit/marker reconciliation.
- Adding Avalonia.Headless or new UI test infrastructure - this repository does not currently include a headless UI test project.
- Parallelizing plugin cleaning or external process execution - sequential xEdit cleaning and the single process slot remain hard runtime requirements.
- Mutagen or `QueryPlugins` changes - Phase 15 concerns AutoQAC stop escalation only.

## Constraints

- User-initiated Stop must not force-kill xEdit before the `Force Terminate` confirmation is shown and affirmed.
- Confirmed `Force Terminate` must make a real force-kill attempt against the still-running xEdit target in the detached/returned case.
- Genuine Windows/process refusal to kill, such as access denied or unsupported process state, remains possible and must surface as the shared safe force-failure outcome rather than being hidden.
- Existing Phase 11 diagnostics boundaries apply: user-facing failure copy must not include raw exception text, stack traces, full command lines, or unnecessary local paths.
- The implementation must preserve sequential cleaning and `ProcessExecutionService`'s single process slot.
- ViewModels must keep MVVM/dialog boundaries and must not directly manipulate Avalonia controls.
- Real process evidence must use the existing controlled helper-process style instead of requiring xEdit or manual OS setup.

## Acceptance Criteria

- [ ] Progress-window Stop with `GracePeriodExpired` shows the shared `Force Terminate` / `Leave Running` confirmation before any force-kill attempt.
- [ ] Confirmed `Force Terminate` after `GracePeriodExpired` and runner detach or returned process execution force-kills the still-running process and returns `ForceKilled` in the controlled success case.
- [ ] The confirmed detached escalation path cannot complete as a silent cached `GracePeriodExpired` no-op while xEdit may still be running.
- [ ] Genuine force-kill refusal after confirmation shows `StopTerminationDialogContent.ForceFailureTitle` / `ForceFailureMessage` and persists the Progress warning.
- [ ] Automated tests cover `GracePeriodExpired` -> detach/returned execution -> confirmed `Force Terminate` for the detached escalation gap.
- [ ] Targeted Progress Stop, termination coordinator/orchestrator, and process/PID evidence passes, or any unrelated pre-existing failures are explicitly documented.
- [ ] `15-VERIFICATION.md` maps `SAF-01`, `SAF-02`, `TEST-01`, `INT-STOP-01`, and `FLOW-STOP-ESCALATION-01` to passing evidence.
- [ ] Full solution tests pass, or any unrelated pre-existing failures are explicitly documented in verification.

## Ambiguity Report

| Dimension           | Score | Min   | Status | Notes |
|---------------------|-------|-------|--------|-------|
| Goal Clarity        | 0.96  | 0.75  | met    | The target user-visible outcome is the detached Progress Stop force-escalation path. |
| Boundary Clarity    | 0.94  | 0.70  | met    | Scope is limited to detached escalation ownership, failure reporting, tests, and verification. |
| Constraint Clarity  | 0.83  | 0.65  | met    | Confirmation ordering, Windows force-kill failure reporting, MVVM, diagnostics, and sequential cleaning constraints are explicit. |
| Acceptance Criteria | 0.91  | 0.70  | met    | Pass/fail checks cover confirmation ordering, detached force kill, failure reporting, tests, and verification. |
| **Ambiguity**       | 0.08  | <=0.20 | met   | Gate passed after round 3. |

Status: met = dimension reached minimum; below = planner treats as assumption.

## Interview Log

| Round | Perspective | Question summary | Decision locked |
|-------|-------------|------------------|-----------------|
| 0 | Initial assessment | Score from `ROADMAP.md` and `REQUIREMENTS.md` before questioning. | Goal 0.90, Boundary 0.78, Constraint 0.72, Acceptance 0.86, Ambiguity 0.17. |
| 1 | Researcher | What outcome must be guaranteed after `GracePeriodExpired` and confirmed `Force Terminate` when the original process handle was detached? | User chose actual kill required; silent cached `GracePeriodExpired` or warning-only fallback is not sufficient for the controlled detached success case. |
| 1 | Researcher | What evidence is required to close `TEST-01` and the audit gap? | Integration plus unit evidence is required, including `GracePeriodExpired` -> runner detach -> confirmed `Force Terminate`. |
| 1 | Scoring | Updated ambiguity after Round 1. | Goal 0.93, Boundary 0.78, Constraint 0.64, Acceptance 0.88, Ambiguity 0.18; constraint needed clarification. |
| 2 | Researcher + Simplifier | How should the irreducible confirmed force rule be phrased, and what is the smallest successful scope? | User chose must always kill for the controlled target-retention case and detached escalation only as the minimum scope. |
| 2 | Scoring | Updated ambiguity after Round 2. | Goal 0.95, Boundary 0.86, Constraint 0.60, Acceptance 0.88, Ambiguity 0.16; OS/process refusal feasibility needed clarification. |
| 3 | Boundary Keeper | Are OS/access-denied force-kill failures in scope to eliminate? | Genuine OS/process force-kill failures remain possible and must be reported; the phase must not lose ownership before the force-kill attempt. |
| 3 | Boundary Keeper | What adjacent work is explicitly out of scope? | No broader rewrites; exclude all-stop-path redesign, timeout/orphan redesign, command launch/MO2 work, UI test infrastructure, and marker reconciliation. |
| 3 | Scoring | Final gate check. | Goal 0.96, Boundary 0.94, Constraint 0.83, Acceptance 0.91, Ambiguity 0.08; gate passed and user approved writing SPEC.md. |

---

*Phase: 15-stop-escalation-ownership-closure*
*Spec created: 2026-05-01*
*Next step: /gsd-discuss-phase 15 - implementation decisions (how to preserve or recover the confirmed force-termination target)*
