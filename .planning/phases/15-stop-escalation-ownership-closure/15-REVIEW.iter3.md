---
phase: 15-stop-escalation-ownership-closure
reviewed: 2026-05-02T00:50:14Z
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

**Reviewed:** 2026-05-02T00:50:14Z
**Depth:** deep
**Files Reviewed:** 6
**Status:** issues_found

## Summary

Re-reviewed the same stop-escalation scope after the iteration-2 remediation. The previous disposed-process ownership issue is substantially addressed by reopening a retained PID/start-time target, and the preflight/orphan-cleanup cancellation gap is covered by moving session CTS creation before cancellable startup work. However, the new durable-target implementation can still force-kill an unrelated process when start-time capture fails, and the stop request transition remains non-atomic under concurrent stop calls.

## Critical Issues

### CR-01: BLOCKER - Unverified PID-only pending target can kill the wrong process

**File:** `AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs:365`

**Issue:** `RetainPendingForceEscalationProcess` stores a pending target even when `TryGetStartTime(process)` returns `null`. Later, `TryReopenPendingTarget` only rejects recycled PIDs when `target.StartTime` has a value (`line 432`); if start time was unavailable, any process currently using the same PID is accepted and passed to `TerminateProcessAsync(forceKill: true)`. Because the original xEdit process may have exited after `GracePeriodExpired` and Windows can reuse PIDs, a delayed user confirmation can kill an unrelated process tree. This is a safety/data-loss risk.

**Fix:** Treat missing start-time proof as not safely reopenable. Either do not retain a pending force target when start time cannot be captured, or make `TryReopenPendingTarget` return `null` for `StartTime == null` so the UI receives `ForceKillFailed` instead of killing an unverified PID.

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

    // Existing GetProcessById + start-time equality check follows.
}
```

## Warnings

### WR-01: WARNING - Concurrent first stop calls can both take the graceful path

**File:** `AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs:115-123`

**Issue:** `StopAsync` checks `_isStopRequested` and then sets it in separate unsynchronized operations. Two near-simultaneous stop requests can both read `false`, both set terminating state, and both call `TerminateProcessAsync(... forceKill: false)` instead of one call escalating through `ForceStopAsync`. That violates the two-stage stop contract under reentrant/concurrent UI paths or service callers.

**Fix:** Make the first-stop transition atomic with `Interlocked.Exchange` (or protect it with the same coordinator lock) so exactly one caller owns the graceful path and all later callers escalate.

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

_Reviewed: 2026-05-02T00:50:14Z_
_Reviewer: the agent (gsd-code-reviewer)_
_Depth: deep_
