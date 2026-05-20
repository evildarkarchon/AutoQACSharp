# T01: 15-stop-escalation-ownership-closure 01

**Slice:** S16 — **Milestone:** M001

## Description

Create the coordinator-owned retained process handle required for confirmed detached force escalation.

Purpose: Phase 15 must close the audit blocker where `DetachProcess()` clears `_currentProcess` and later confirmed `ForceStopAsync()` silently returns cached `GracePeriodExpired` instead of killing or reporting failure.
Output: TDD tests and implementation in the termination coordinator that keep active cleaning semantics separate from pending force-escalation ownership.

## Must-Haves

- [ ] "D-01/D-03: Coordinator retains a separate pending force-escalation Process handle after GracePeriodExpired without counting it as active cleaning."
- [ ] "D-04: HasActiveProcess, hang monitoring, backup-cancel gating, and current-plugin active state continue to represent active cleaning only."
- [ ] "D-05: PID/start-time fallback is not part of the planned primary implementation unless retained-handle evidence fails a required timing edge."
- [ ] "D-06: Confirmed ForceStopAsync after detach returns a terminal result, never cached GracePeriodExpired."
- [ ] "D-07/D-08: Already-exited retained targets return AlreadyExited; unavailable or unprovable targets return ForceKillFailed."
- [ ] "D-10/D-11: Coordinator proof attaches a controlled helper process, drives GracePeriodExpired, detaches, force-stops, and asserts ForceKilled in the controlled success case."
- [ ] "D-14: Tests use existing helper-process and service/ViewModel patterns, with no Avalonia.Headless or real xEdit/MO2 setup."

## Files

- `AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs`
- `AutoQAC/Services/Cleaning/ICleaningTerminationCoordinator.cs`
- `AutoQAC.Tests/Services/Cleaning/CleaningTerminationCoordinatorTests.cs`
