---
phase: 15-stop-escalation-ownership-closure
reviewed: 2026-05-01T00:00:00Z
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
  warning: 2
  info: 0
  total: 3
status: issues_found
---

# Phase 15: Code Review Report

**Reviewed:** 2026-05-01T00:00:00Z
**Depth:** deep
**Files Reviewed:** 6
**Status:** issues_found

## Summary

Reviewed the termination coordinator, orchestrator integration, and related tests at deep depth, including the process execution/finalization call chain. The implementation still has a cancellation accounting bug for stop requests during the final/no-backup plugin, a force-stop UI state gap, and tests that miss the real production path.

## Critical Issues

### CR-01: Stop during the last plugin can finish as a non-cancelled session

**File:** `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:92-113`

**Issue:** `StartCleaningAsync` only marks `context.WasCancelled` when cancellation is observed before the next loop iteration or when `ProcessPluginAsync` returns `false`. If the user stops during the last plugin, `ProcessPluginAsync` can return `true` after `CleaningService` converts the cancelled process execution into a failed result. With no next plugin and no backup finalization that observes the cancelled token, the loop falls through to `FinishSession(context)` with `WasCancelled = false`. This misreports a user-cancelled run as an ordinary failed/completed session and can show the wrong summary/UI state.

**Fix:** Re-check cancellation/stop state immediately after each plugin before normal finalization.

```csharp
var processed = await ProcessPluginAsync(
    plugin, preflightPlan, sessionDir, backupEntries, onTimeout, onBackupFailure,
    maxRetryAttempts, context, cts.Token).ConfigureAwait(false);

if (!processed || cts.Token.IsCancellationRequested || terminationCoordinator.IsStopRequested)
{
    context = context with { WasCancelled = true };
    break;
}

if (context.ReturnedEarly)
{
    return;
}
```

Also consider mapping `ProcessResult.TerminationResult` from user-requested termination into a cancelled/skipped `CleaningResult` in `CleaningService` so downstream status and session cancellation cannot diverge.

## Warnings

### WR-01: Direct force-stop path does not publish terminating state

**File:** `AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs:180-183`

**Issue:** `ForceStopAsync` starts a potentially long-running kill/wait operation without calling `_stateService.SetTerminating(true)`. The graceful stop path does publish this state, but hang-warning force kill and confirmed detached force kill do not. This can leave the UI without termination feedback and allows other UI state to behave as if no termination operation is in progress until finalization later clears the flag.

**Fix:** Publish terminating state for force-stop operations and clear it on terminal/no-target exits where the session will not clear it immediately.

```csharp
public async Task<StopCleaningResult> ForceStopAsync()
{
    _stateService.SetTerminating(true);
    try
    {
        // existing force-stop logic
    }
    finally
    {
        if (!MayProcessStillBeRunning(_lastTerminationResult))
        {
            _stateService.SetTerminating(false);
        }
    }
}
```

### WR-02: Stop tests miss the production cancellation/accounting path

**File:** `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs:910-963`

**Issue:** `StopCleaningAsync_ShouldTerminateActiveProcess_Gracefully_AndStoreGracePeriodExpiredResult` verifies the termination call and cached result, but it releases the mocked plugin normally and never asserts that the final `CleaningSessionResult.WasCancelled` is true. Because the mock does not model `CleaningService` returning a failed `CleaningResult` after `ProcessExecutionService` returns `GracePeriodExpired`, this test gives false confidence and would not catch CR-01.

**Fix:** Add a regression test for a single-plugin/no-backup session where stop returns `GracePeriodExpired`, the plugin returns a failed result after cancellation, and `FinishCleaningWithResults` must receive `WasCancelled == true`.

```csharp
_cleaningServiceMock.CleanPluginAsync(...)
    .Returns(async callInfo =>
    {
        callInfo.ArgAt<Action<Process>?>(2)?.Invoke(sleeper);
        await WaitForCancellationAsync(callInfo.ArgAt<CancellationToken>(1));
        return new CleaningResult { Status = CleaningStatus.Failed, Success = false, Message = "xEdit exited with code -1" };
    });

_stateServiceMock.Received(1).FinishCleaningWithResults(
    Arg.Is<CleaningSessionResult>(session => session.WasCancelled));
```

---

_Reviewed: 2026-05-01T00:00:00Z_
_Reviewer: the agent (gsd-code-reviewer)_
_Depth: deep_
