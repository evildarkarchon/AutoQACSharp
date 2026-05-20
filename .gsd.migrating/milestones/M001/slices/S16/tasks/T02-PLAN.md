# T02: 15-stop-escalation-ownership-closure 02

**Slice:** S16 — **Milestone:** M001

## Description

Wire the retained coordinator target through session finalization and preserve the Progress-window confirmation/failure proof.

Purpose: Plan 01 fixes the coordinator mechanism; this plan ensures `CleaningOrchestrator` does not erase unresolved pending escalation during normal finalization and that Progress Stop still gates force stop behind user confirmation.
Output: TDD tests and minimal orchestration/ViewModel-adjacent verification for the detached escalation user flow.

## Must-Haves

- [ ] "D-02: Normal session finalization preserves an unresolved GracePeriodExpired pending target until the user resolves the confirmation."
- [ ] "D-09: Confirmed detached-target and invalid-target failures use StopTerminationDialogContent.ForceFailureTitle and ForceFailureMessage, with no new user-facing copy."
- [ ] "D-12: Progress Stop does not call ForceStopCleaningAsync before the affirmative confirmation dialog result."
- [ ] "D-13: Progress Stop displays shared safe force-failure dialog and persistent warning when confirmed detached force escalation returns ForceKillFailed."

## Files

- `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs`
- `AutoQAC/Services/Cleaning/ICleaningTerminationCoordinator.cs`
- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`
- `AutoQAC.Tests/ViewModels/ProgressViewModelTests.cs`
