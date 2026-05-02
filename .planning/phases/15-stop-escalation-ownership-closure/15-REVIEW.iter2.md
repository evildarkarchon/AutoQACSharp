---
phase: 15-stop-escalation-ownership-closure
reviewed: 2026-05-02T01:38:05Z
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

**Reviewed:** 2026-05-02T01:38:05Z
**Depth:** deep
**Files Reviewed:** 6
**Status:** issues_found

## Summary

Reviewed the termination coordinator split, orchestrator ownership handoff, and related stop/escalation UI tests at deep depth. The new pending-target flow covers the happy GracePeriodExpired confirmation path, but session finalization still forgets other “may still be running” terminal states, and the coordinator can leave the terminating state stuck on protected/no-op stop paths.

## Critical Issues

### CR-01: BLOCKER — Finalization clears unresolved force-failure/left-running state

**File:** `AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs:328-332`

**Issue:** `CompleteSessionFinalization` preserves unresolved state only when `_lastTerminationResult == GracePeriodExpired`. The same class defines `ForceKillFailed` and `LeftRunningByUser` as “may still be running” states at lines 348-349, but finalization clears `_pendingForceEscalationTarget` and `_lastTerminationResult` for both. After a failed confirmed force kill, or after the user leaves xEdit running and the session completes, the singleton coordinator reports no unresolved process even though xEdit may still be alive. This loses the safety signal and prevents a later retry/accurate status from using the retained target.

**Fix:** Preserve all unresolved states through finalization, and clear only confirmed terminal results. Add a test where `ForceStopCleaningAsync` returns `ForceKillFailed`, the session finalizes, and `LastTerminationResult`/`MayStillBeRunning` remain true until `ResetForNewSession`.

```csharp
lock (_processLock)
{
    _currentProcess = null;

    if (!MayProcessStillBeRunning(_lastTerminationResult))
    {
        _pendingForceEscalationTarget = null;
        _lastTerminationResult = null;
    }
}
```

## Warnings

### WR-01: WARNING — Protected/no-op stop paths leave IsTerminating true

**File:** `AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs:122-139`

**Issue:** `StopAsync` sets `_stateService.SetTerminating(true)` before it knows whether it will actually terminate anything. If the attached process is AutoQAC itself, the method returns at line 139 without resetting the flag. The no-active-process path at line 171 also returns without clearing it. In normal orchestrated cancellation finalization may eventually reset the flag, but direct coordinator callers and protected early-return paths can leave the UI in a permanently terminating/disabled state.

**Fix:** Reset the flag before every no-op/protected return, or delay setting it until there is a real external process to stop. Add assertions to the self-process and no-active-process coordinator tests.

```csharp
if (proc.Id == Environment.ProcessId)
{
    _logger.Error(null, "[Termination] Refusing to terminate the AutoQAC process during stop request");
    _stateService.SetTerminating(false);
    return new StopCleaningResult(null, MayStillBeRunning: false);
}

// Before the final cached-result return when no process was available:
_stateService.SetTerminating(false);
return new StopCleaningResult(_lastTerminationResult, MayProcessStillBeRunning(_lastTerminationResult));
```

---

_Reviewed: 2026-05-02T01:38:05Z_
_Reviewer: the agent (gsd-code-reviewer)_
_Depth: deep_
