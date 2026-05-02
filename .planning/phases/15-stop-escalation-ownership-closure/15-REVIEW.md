---
phase: 15-stop-escalation-ownership-closure
reviewed: 2026-05-02T00:24:14Z
depth: deep
files_reviewed: 6
files_reviewed_list:
  - AutoQAC/Services/Cleaning/CleaningOrchestrator.cs
  - AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs
  - AutoQAC/Services/Cleaning/ICleaningTerminationCoordinator.cs
  - AutoQAC.Tests/Services/CleaningOrchestratorTests.cs
  - AutoQAC.Tests/Services/Cleaning/CleaningTerminationCoordinatorTests.cs
  - AutoQAC.Tests/ViewModels/ProgressViewModelTests.cs
findings:
  critical: 2
  warning: 1
  info: 0
  total: 3
status: issues_found
---

# Phase 15: Code Review Report

**Reviewed:** 2026-05-02T00:24:14Z
**Depth:** deep
**Files Reviewed:** 6
**Status:** issues_found

## Summary

Reviewed the stop-escalation ownership changes across the orchestrator, termination coordinator, and related tests. The implementation still has a process-lifetime ownership bug in the detached force-stop path, and the UI currently prevents the coordinator's documented second-stop escalation path from being invoked.

Note: the requested `AutoQAC.Tests/Services/Cleaning/CleaningOrchestratorTests.cs` path does not exist in the working tree; the matching source file reviewed was `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`.

## Critical Issues

### CR-01: Detached force escalation retains a `Process` object that production code disposes before the user confirms

**File:** `AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs:145-151`
**Issue:** `StopAsync` retains the live `Process` instance for later confirmation after `GracePeriodExpired`. In the real call chain, that same instance is created inside `ProcessExecutionService.ExecuteAsync` with `using var process` and is disposed as soon as `ExecuteAsync` returns. `PluginCleaningRunner` then detaches the active process, leaving `_pendingForceEscalationProcess` pointing at a disposed wrapper while the OS process may still be running. A delayed confirmation can therefore return `ForceKillFailed` or be unable to kill xEdit, exactly when the user asked for force termination.
**Fix:** Retain force-escalation ownership by stable process identity rather than by the disposable `Process` wrapper, then reopen/verify the process at confirmation time. For example:
```csharp
private sealed record PendingForceTarget(int ProcessId, DateTime StartTime);

private void RetainPendingForceEscalationProcess(Process process)
{
    lock (_processLock)
    {
        _pendingForceEscalationTarget = new PendingForceTarget(process.Id, process.StartTime);
    }
}

private static Process? TryOpenPendingTarget(PendingForceTarget target)
{
    try
    {
        var process = Process.GetProcessById(target.ProcessId);
        return Math.Abs((process.StartTime - target.StartTime).TotalSeconds) < 5 ? process : null;
    }
    catch (ArgumentException)
    {
        return null;
    }
}
```
Then have `ForceStopAsync` use the active `Process` while attached, or reopen the pending target by PID/start time once detached. Add an integration-style test where `CleanPluginAsync` goes through `ProcessExecutionService.ExecuteAsync`, returns `GracePeriodExpired`, lets `ExecuteAsync` dispose its local `Process`, then confirms force termination.

### CR-02: Progress UI disables Stop during termination, making the coordinator's second-click force escalation unreachable

**File:** `AutoQAC/ViewModels/ProgressViewModel.cs:205`
**Issue:** `CanStop()` returns `IsCleaning && !IsTerminating`, and `CleaningTerminationCoordinator.StopAsync` sets `IsTerminating` as soon as the first graceful stop starts. That disables `StopCommand` during the grace window, so the documented `StopAsync` second-call path (`CleaningTerminationCoordinator.cs:111-116`) cannot be triggered from the UI. This is a behavioral regression against the two-stage stop requirement: users cannot click Stop again to immediately force-kill while xEdit is still in the grace period.
**Fix:** Keep the Stop command executable during active cleaning and route a second click to the coordinator, while separately guarding only modal prompt reentrancy if needed. For example:
```csharp
private bool CanStop() => IsCleaning;
```
If the UI needs to prevent duplicate confirmation dialogs, add a separate `_isStopPromptOpen` flag around `ShowChoiceAsync` rather than disabling the stop escalation command when `IsTerminating` is true. Update `StopCommand_ShouldBeDisabled_WhenTerminating` to assert the required second-click behavior instead of asserting the regression.

## Warnings

### WR-01: Detached force-stop tests do not exercise production process ownership

**File:** `AutoQAC.Tests/Services/Cleaning/CleaningTerminationCoordinatorTests.cs:237-256`
**Issue:** The detached force escalation tests attach a test-owned `Process` and then call `DetachProcess()`, but they never simulate the actual production owner (`ProcessExecutionService.ExecuteAsync`) disposing the `Process` wrapper before the confirmation arrives. That test shape masks CR-01: it proves the coordinator works with an externally-owned, still-valid `Process` object, not with the disposed wrapper it will commonly hold after the real runner/finalizer path unwinds.
**Fix:** Add coverage through the real execution path or explicitly dispose the retained wrapper before `ForceStopAsync` to reproduce production ownership. The test should fail unless force escalation stores PID/start-time identity and reopens the process safely.

---

_Reviewed: 2026-05-02T00:24:14Z_
_Reviewer: the agent (gsd-code-reviewer)_
_Depth: deep_
