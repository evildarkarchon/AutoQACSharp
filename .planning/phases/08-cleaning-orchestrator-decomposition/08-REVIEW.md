---
phase: 08-cleaning-orchestrator-decomposition
reviewed: 2026-04-29T00:00:00Z
depth: deep
files_reviewed: 22
files_reviewed_list:
  - AutoQAC/Infrastructure/ServiceCollectionExtensions.cs
  - AutoQAC/Services/Cleaning/BackupSessionCoordinator.cs
  - AutoQAC/Services/Cleaning/BackupSessionModels.cs
  - AutoQAC/Services/Cleaning/CleaningOrchestrator.cs
  - AutoQAC/Services/Cleaning/CleaningPreflight.cs
  - AutoQAC/Services/Cleaning/CleaningPreflightModels.cs
  - AutoQAC/Services/Cleaning/CleaningTerminationCoordinator.cs
  - AutoQAC/Services/Cleaning/IBackupSessionCoordinator.cs
  - AutoQAC/Services/Cleaning/ICleaningPreflight.cs
  - AutoQAC/Services/Cleaning/ICleaningTerminationCoordinator.cs
  - AutoQAC/Services/Cleaning/IPluginCleaningRunner.cs
  - AutoQAC/Services/Cleaning/IPluginResultFinalizer.cs
  - AutoQAC/Services/Cleaning/PluginCleaningRunner.cs
  - AutoQAC/Services/Cleaning/PluginResultFinalizer.cs
  - AutoQAC/Services/Cleaning/RunnerFinalizerModels.cs
  - AutoQAC.Tests/Services/Cleaning/BackupSessionCoordinatorTests.cs
  - AutoQAC.Tests/Services/Cleaning/CleaningPreflightTests.cs
  - AutoQAC.Tests/Services/Cleaning/CleaningTerminationCoordinatorTests.cs
  - AutoQAC.Tests/Services/Cleaning/PluginCleaningRunnerTests.cs
  - AutoQAC.Tests/Services/Cleaning/PluginResultFinalizerTests.cs
  - AutoQAC.Tests/Services/CleaningOrchestratorTests.cs
  - AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs
findings:
  critical: 2
  warning: 1
  info: 0
  total: 3
status: issues_found
---

# Phase 08: Code Review Report

**Reviewed:** 2026-04-29T00:00:00Z
**Depth:** deep
**Files Reviewed:** 22
**Status:** issues_found

## Summary

Deep review of the Phase 08 cleaning decomposition found two behavioral regressions in cancellation/finalization paths and one result-consistency defect. The most serious issue is that a user stop requested during preflight/orphan cleanup can be lost because the session CTS does not exist yet, allowing xEdit to launch after Stop was clicked. The finalizer can also convert an xEdit failure into `AlreadyClean`, which can make a failed plugin/session appear successful.

## Critical Issues

### CR-01: Stop requests before session CTS creation are ignored and cleaning can still launch xEdit

**File:** `AutoQAC/Services/Cleaning/CleaningOrchestrator.cs:56-69,223-226`

**Issue:** `StartCleaningAsync` does not create `_cleaningCts` until after orphan cleanup and the full preflight pipeline complete. `StopCleaningAsync` only cancels the existing `_cleaningCts`; if the user clicks Stop while `CleanOrphanedProcessesAsync` or `preflight.PrepareAsync` is running, `_cleaningCts` is still null, so `CancelSessionCts()` is a no-op. After preflight returns, line 68 creates a fresh uncanceled CTS and the plugin loop proceeds to launch xEdit even though the user already requested Stop. `terminationCoordinator.StopAsync()` cannot help in this window because there is no active process yet.

**Fix:** Create and publish the session CTS before any cancellable startup work, pass its token into orphan cleanup and preflight, and do not replace it with a fresh token after a stop request. For example:

```csharp
terminationCoordinator.ResetForNewSession();
var cts = CreateSessionCts(ct);

await processService.CleanOrphanedProcessesAsync(cts.Token).ConfigureAwait(false);
var preflightPlan = await preflight.PrepareAsync(cts.Token).ConfigureAwait(false);
cts.Token.ThrowIfCancellationRequested();

sessionDir = await backupCoordinator.BeginSessionAsync(preflightPlan, cts.Token).ConfigureAwait(false);
```

Add a test that blocks inside `ICleaningPreflight.PrepareAsync`, calls `StopCleaningAsync`, then releases preflight and asserts no plugin is cleaned and the final session is canceled.

### CR-02: Failed xEdit attempts can be reclassified as AlreadyClean

**File:** `AutoQAC/Services/Cleaning/PluginResultFinalizer.cs:27-53,70-80`

**Issue:** The finalizer reads logs for every non-skipped result, including `CleaningStatus.Failed`. If a failed xEdit run returns a non-zero exit code but the newly-read log slice contains a completion line with zero parsed stats, lines 49-52 set `finalStatus = CleaningStatus.AlreadyClean`. `CleaningSessionResult` counts failures by `Status`, not by `Success`, so this can hide an actual xEdit failure and make the session appear successful.

**Fix:** Only apply "nothing to clean" reclassification to successful cleaned results:

```csharp
if (result.Success && result.Status == CleaningStatus.Cleaned)
{
    var hasCompletionLine = logResult.LogLines.Any(outputParser.IsCompletionLine);
    if (hasCompletionLine && logStats is { ItemsRemoved: 0, ItemsUndeleted: 0, ItemsSkipped: 0, PartialFormsCreated: 0 })
    {
        finalStatus = CleaningStatus.AlreadyClean;
    }
}
```

Add a finalizer test where `LastAttemptResult` is `Failed`/`Success = false`, log lines contain `Done.`, parsed stats are zero, and the final status must remain `Failed`.

## Warnings

### WR-01: Exception-log failures keep the original Success flag

**File:** `AutoQAC/Services/Cleaning/PluginResultFinalizer.cs:57-60,70-80`

**Issue:** When exception log content is present, the finalizer changes `finalStatus` to `Failed` but returns `Success = result.Success`. A successful process result with an exception log therefore produces a contradictory row: `Status = Failed` and `Success = true`. That inconsistency can mislead any current or future consumers that use `PluginCleaningResult.Success` directly instead of recomputing from `Status`.

**Fix:** Derive the returned success flag from the final status after log parsing overrides:

```csharp
var finalSuccess = finalStatus is CleaningStatus.Cleaned or CleaningStatus.AlreadyClean;

return new PluginCleaningResult
{
    PluginName = plugin.FileName,
    Status = finalStatus,
    Success = finalSuccess,
    // ...
};
```

Add/update the exception-log finalizer test to assert `Success` is false when the final status is failed.

---

_Reviewed: 2026-04-29T00:00:00Z_
_Reviewer: the agent (gsd-code-reviewer)_
_Depth: deep_
