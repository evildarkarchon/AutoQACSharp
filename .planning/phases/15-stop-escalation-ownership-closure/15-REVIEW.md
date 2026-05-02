---
phase: 15-stop-escalation-ownership-closure
reviewed: 2026-05-02T00:54:50Z
depth: deep
files_reviewed: 6
files_reviewed_list:
  - AutoQAC.Tests/Services/Cleaning/CleaningTerminationCoordinatorTests.cs
  - AutoQAC.Tests/Services/CleaningOrchestratorTests.cs
  - AutoQAC.Tests/ViewModels/ProgressViewModelTests.cs
  - AutoQAC/Services/Cleaning/CleaningOrchestrator.cs
  - AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs
  - AutoQAC/Services/Cleaning/ICleaningTerminationCoordinator.cs
findings:
  critical: 1
  warning: 1
  info: 0
  total: 2
status: issues_found
---

# Phase 15: Code Review Report

**Reviewed:** 2026-05-02T00:54:50Z
**Depth:** deep
**Files Reviewed:** 6
**Status:** issues_found

## Summary

Re-reviewed the same stop-escalation scope at deep depth for auto iteration 3/3. The remediation preserves pending force-stop ownership after detach/finalization and keeps startup cancellation wired through orphan cleanup and preflight, but the same two ship-blocking robustness problems remain: retained PID-only targets are unsafe when start-time proof is unavailable, and first-stop ownership is still non-atomic under concurrent callers.

## Critical Issues

### CR-01: BLOCKER - Unverified PID-only pending target can kill the wrong process

**File:** `AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs:365`

**Issue:** `RetainPendingForceEscalationProcess` stores a pending force target even when `TryGetStartTime(process)` returns `null`. Later, `TryReopenPendingTarget` only rejects recycled PIDs when `target.StartTime` has a value (`AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs:432`); if start time was unavailable, any process currently using the same PID is accepted and passed to `TerminateProcessAsync(... forceKill: true)`. After `GracePeriodExpired`, the original xEdit process can exit before the user confirms force termination, and Windows can recycle the PID. A delayed confirmation can therefore kill an unrelated process tree, creating a data-loss/safety risk.

**Fix:** Treat missing start-time proof as not safely reopenable. Either do not retain a pending force target unless both PID and start time were captured, or make `TryReopenPendingTarget` return `null` when `StartTime` is missing so the caller reports `ForceKillFailed` instead of killing an unverified PID.

```csharp
private void RetainPendingForceEscalationProcess(DiagnosticsProcess process)
{
    var processId = TryGetProcessId(process);
    var startTime = TryGetStartTime(process);
    if (processId is null || startTime is null)
    {
        _logger.Warning("[Termination] Could not retain pending force target because process identity was not fully verifiable");
        return;
    }

    lock (_processLock)
    {
        _pendingForceEscalationTarget = new PendingForceTarget(processId.Value, startTime.Value);
    }
}

private static DiagnosticsProcess? TryReopenPendingTarget(PendingForceTarget target)
{
    if (target.StartTime is not { } expectedStartTime)
    {
        return null;
    }

    var process = DiagnosticsProcess.GetProcessById(target.ProcessId);
    if (process.StartTime != expectedStartTime)
    {
        process.Dispose();
        return null;
    }

    return process;
}
```

## Warnings

### WR-01: WARNING - Concurrent first stop calls can both take the graceful path

**File:** `AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs:115-123`

**Issue:** `StopAsync` checks `_isStopRequested` and then sets it in separate unsynchronized operations. Two near-simultaneous stop requests can both read `false`, both call `SetTerminating(true)`, and both invoke `TerminateProcessAsync(... forceKill: false)` instead of one caller owning the graceful path and the other escalating through `ForceStopAsync`. This violates the two-stage stop contract for reentrant/concurrent UI paths or service callers.

**Fix:** Make the first-stop transition atomic with `Interlocked.Exchange` (or protect the check/set with a lock) so exactly one caller can enter the graceful path.

```csharp
private int _isStopRequested;

public bool IsStopRequested => Volatile.Read(ref _isStopRequested) != 0;

public async Task<StopCleaningResult> StopAsync()
{
    if (Interlocked.Exchange(ref _isStopRequested, 1) == 1)
    {
        _logger.Information("[Termination] Second stop requested -- escalating to force kill");
        return await ForceStopAsync().ConfigureAwait(false);
    }

    _stateService.SetTerminating(true);
    // existing graceful stop path...
}
```

---

_Reviewed: 2026-05-02T00:54:50Z_
_Reviewer: the agent (gsd-code-reviewer)_
_Depth: deep_
