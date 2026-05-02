---
phase: 15-stop-escalation-ownership-closure
reviewed: 2026-05-02T01:30:00Z
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
  critical: 0
  warning: 2
  info: 0
  total: 2
status: issues_found
---

# Phase 15: Code Review Report

**Reviewed:** 2026-05-02T01:30:00Z
**Depth:** deep
**Files Reviewed:** 6
**Status:** issues_found

## Summary

Reviewed the stop-escalation ownership closure implementation and its targeted tests at deep depth, including the orchestrator-to-runner attach/detach flow, pending force-target reopening, UI-facing stop result semantics, and process termination service boundaries. The previous PID-reuse blocker appears remediated by requiring both PID and start-time proof before retaining a pending target. Two robustness issues remain in `CleaningTerminationCoordinator`: a non-terminating stop path can leave the UI terminating flag stuck, and the reopened-process helper can leak a process handle on validation exceptions.

## Warnings

### WR-01: WARNING - Stop path can leave terminating state stuck when no termination actually runs

**File:** `AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs:122-171`

**Issue:** `StopAsync` publishes `_stateService.SetTerminating(true)` before proving there is a terminable external xEdit process. If the attached process is AutoQAC itself (`lines 136-139`) or the process has already exited before the first stop request (`lines 142-171` fall through), the method returns without resetting `IsTerminatingChanged` to `false`. In those paths no graceful termination is in progress, but `ProgressViewModel.CanStop()` disables Stop while `IsTerminating` is true, so the UI can be left in a stale terminating/spinner state until a later session reset or finalization happens. The existing tests assert the self-protection return value, but do not assert that `SetTerminating(false)` is emitted for non-terminating exits.

**Fix:** Only set the terminating flag after a real external process termination is about to be attempted, or explicitly clear it on every early return that does not start termination.

```csharp
// After reading proc under _processLock:
if (proc is null)
{
    return new StopCleaningResult(_lastTerminationResult, MayProcessStillBeRunning(_lastTerminationResult));
}

try
{
    if (proc.Id == Environment.ProcessId)
    {
        _logger.Error(null, "[Termination] Refusing to terminate the AutoQAC process during stop request");
        _stateService.SetTerminating(false);
        return new StopCleaningResult(null, MayStillBeRunning: false);
    }

    if (proc.HasExited)
    {
        _lastTerminationResult = TerminationResult.AlreadyExited;
        _stateService.SetTerminating(false);
        ReleasePendingForceEscalationTarget();
        return ToStopCleaningResult(TerminationResult.AlreadyExited);
    }

    _stateService.SetTerminating(true);
    var result = await _processService.TerminateProcessAsync(proc, forceKill: false, ct: CancellationToken.None)
        .ConfigureAwait(false);
    // existing result handling...
}
catch (InvalidOperationException)
{
    _stateService.SetTerminating(false);
    _lastTerminationResult = TerminationResult.AlreadyExited;
    return ToStopCleaningResult(TerminationResult.AlreadyExited);
}
```

Add tests covering both self-refusal and already-exited first-stop paths with `_stateMock.Received().SetTerminating(false)`.

### WR-02: WARNING - Reopened process handle leaks when start-time validation throws

**File:** `AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs:431-442`

**Issue:** `TryReopenPendingTarget` owns the `Process` returned by `DiagnosticsProcess.GetProcessById`, but if `process.StartTime` throws `InvalidOperationException` or `Win32Exception`, control jumps to the catch block and returns `null` without disposing the opened handle. This is a resource ownership defect in the confirmed force-stop fallback path. It is not just theoretical: `TryGetStartTime` already treats `Win32Exception` as expected for protected/unavailable processes, so the reopen validation path should apply the same cleanup discipline.

**Fix:** Dispose the process handle in the exceptional validation path. One simple pattern is to keep the handle in an outer variable and dispose it in the catch before returning `null`.

```csharp
private static DiagnosticsProcess? TryReopenPendingTarget(PendingForceTarget target)
{
    DiagnosticsProcess? process = null;
    try
    {
        process = DiagnosticsProcess.GetProcessById(target.ProcessId);
        if (process.StartTime != target.StartTime)
        {
            process.Dispose();
            return null;
        }

        var reopened = process;
        process = null;
        return reopened;
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
    {
        process?.Dispose();
        return null;
    }
}
```

Add a small unit seam or wrapper for process reopening if direct coverage is otherwise impractical; at minimum, keep the ownership rule explicit in the helper.

---

_Reviewed: 2026-05-02T01:30:00Z_
_Reviewer: the agent (gsd-code-reviewer)_
_Depth: deep_
