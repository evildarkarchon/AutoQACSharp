---
phase: 09-plugin-refresh-approximation-performance
reviewed: 2026-04-30T09:30:17Z
depth: deep
files_reviewed: 27
files_reviewed_list:
  - AutoQAC/Infrastructure/ServiceCollectionExtensions.cs
  - AutoQAC/Services/Plugin/IPluginRefreshCapabilityPolicy.cs
  - AutoQAC/Services/Plugin/IPluginRefreshCoordinator.cs
  - AutoQAC/Services/Plugin/PluginIssueApproximationService.cs
  - AutoQAC/Services/Plugin/PluginRefreshCapabilityPolicy.cs
  - AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs
  - AutoQAC/Services/Plugin/PluginRefreshRequest.cs
  - AutoQAC/Services/Plugin/PluginRefreshStatus.cs
  - AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs
  - AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs
  - AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs
  - AutoQAC/ViewModels/MainWindowViewModel.cs
  - AutoQAC/Views/MainWindow.axaml
  - AutoQAC.Tests/Integration/DependencyInjectionTests.cs
  - AutoQAC.Tests/Services/PluginIssueApproximationServiceTests.cs
  - AutoQAC.Tests/Services/PluginRefreshCoordinatorTests.cs
  - AutoQAC.Tests/Services/StateServiceTests.cs
  - AutoQAC.Tests/ViewModels/CleaningCommandsViewModelTests.cs
  - AutoQAC.Tests/ViewModels/ErrorDialogTests.cs
  - AutoQAC.Tests/ViewModels/MainWindowThreadingTests.cs
  - AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs
  - AutoQAC.Tests/ViewModels/PluginListViewModelTests.cs
  - QueryPlugins/IPluginQueryService.cs
  - QueryPlugins/PluginQueryService.cs
  - QueryPlugins/Detectors/IItmDetector.cs
  - QueryPlugins/Detectors/ItmDetector.cs
  - QueryPlugins.Tests/Detectors/ItmDetectorTests.cs
findings:
  critical: 3
  warning: 2
  info: 0
  total: 5
status: issues_found
---

# Phase 09: Code Review Report

**Reviewed:** 2026-04-30T09:30:17Z
**Depth:** deep
**Files Reviewed:** 27
**Status:** issues_found

## Summary

Deep review found cancellation-lifetime bugs, stale UI state after refresh failures, and a skip-list behavioral regression relative to the cleaning preflight path. The implementation also has weaker matching semantics and cancellation propagation in secondary analysis paths.

## Critical Issues

### CR-01: BLOCKER - Active refresh token sources are disposed while still in use

**File:** `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs:220-237`, `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs:254-259`, `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs:390-408`

**Issue:** `CancelActiveRefresh` and `CancelAndDispose` both cancel and dispose a `CancellationTokenSource` owned by an async refresh method that is still executing and still passing the linked token to plugin loading / approximation code. Disposing a CTS from another thread while consumers may still register callbacks or inspect wait handles can surface `ObjectDisposedException` instead of normal cancellation. Superseding refreshes have the same problem because `CreateAndActivateGeneration` disposes the previous generation immediately.

**Fix:** Only signal cancellation from outside the owning async method. Let the owning method's `using`/`finally` dispose its own CTS after all awaits have unwound.

```csharp
private CancellationTokenSource CreateAndActivateGeneration(CancellationToken externalToken)
{
    var cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
    var previous = Interlocked.Exchange(ref _activeRefreshCts, cts);
    previous?.Cancel(); // do not dispose another generation's live CTS
    return cts;
}

public void CancelActiveRefresh(PluginRefreshCancelReason reason)
{
    var cts = Volatile.Read(ref _activeRefreshCts);
    if (cts is null) return;

    try { cts.Cancel(); }
    catch (ObjectDisposedException) { }

    if (reason == PluginRefreshCancelReason.Manual)
        Publish(new PluginRefreshStatus(PluginRefreshStatusKind.Canceled));
}
```

Add a regression test that starts a delayed refresh, cancels/supersedes it, and asserts the task completes as cancellation without `ObjectDisposedException`.

### CR-02: BLOCKER - Refresh failures leave the UI permanently in a running/cancel state

**File:** `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs:79-84`, `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs:123-136`, `AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs:294-299`

**Issue:** The coordinator publishes `LoadingPlugins`, and `PluginListViewModel` maps that status to `IsApproximationRefreshRunning = true`. If plugin loading, skip-list retrieval, or approximation analysis throws, no terminal status is published. The approximation catch block marks rows unavailable but still exits without `FullRefreshCompleted`, `Canceled`, `Idle`, or error status, so the cancel affordance can remain visible/enabled after work is over.

**Fix:** Publish a terminal status on all non-superseded failure paths. Prefer adding an explicit error status; at minimum clear the running state with `Idle`/`ApproximationUnavailable` after fallback merges complete.

```csharp
catch (Exception ex) when (ex is not OperationCanceledException)
{
    _logger?.Error(ex, "Failed to refresh plugin issue approximations");
    foreach (var target in targets)
    {
        if (!IsCurrent(generation, token)) return;
        _stateService.MergePluginApproximation(new PluginIssueApproximationResult
        {
            FileName = target.FileName,
            FullPath = target.FullPath,
            Approximation = PluginIssueApproximation.Unavailable
        });
    }

    if (IsCurrent(generation, token))
        Publish(new PluginRefreshStatus(PluginRefreshStatusKind.Idle, Message: "Approximation refresh failed."));
}
```

Add tests for load failure, skip-list failure, and approximation failure verifying `IsApproximationRefreshRunning` is cleared.

### CR-03: BLOCKER - Refresh skip-list handling ignores detected game variants

**File:** `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs:84`, `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs:309-316`

**Issue:** The refresh path always calls `GetSkipListAsync(gameType, ct: ct)`, which uses `GameVariant.None`. The cleaning preflight path detects variants from plugin names and passes the detected variant before applying skip lists (`AutoQAC/Services/Cleaning/CleaningPreflight.cs:82-120`). For TTW and Enderal, the UI refresh can show plugins as cleanable while preflight later skips them, so the main window misrepresents what will actually be cleaned.

**Fix:** Detect the variant from the loaded plugin names before fetching skip lists, matching preflight behavior, and pass it through.

```csharp
var pluginNames = loadedPlugins.Select(plugin => plugin.FileName).ToList();
var variant = _gameDetectionService.DetectVariant(request.GameType, pluginNames);
var skipList = await GetSkipListAsync(request.GameType, variant, token).ConfigureAwait(false);

private async Task<List<string>> GetSkipListAsync(GameType gameType, GameVariant variant, CancellationToken ct) =>
    _configurationService is null
        ? []
        : await _configurationService.GetSkipListAsync(gameType, variant, ct).ConfigureAwait(false) ?? [];
```

Add regression tests for TTW and Enderal refresh rows matching preflight skip-list decisions.

## Warnings

### WR-01: WARNING - Approximation matching falls back to file name and can update the wrong row

**File:** `AutoQAC/Services/State/StateService.cs:314-327`, `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs:182-193`, `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs:331-359`

**Issue:** Both targeted refresh and state merging match by full path OR file name. If two rows share a file name but have different paths, or if an old callback has the same file name as a current row, the fallback updates rows that were not selected or not analyzed. This weakens the path-keyed selection model added elsewhere to prevent cross-list bleed.

**Fix:** Use full path as the authoritative identity whenever both sides have a path. Only fall back to file name when one side has no usable path and the file name is unique in the current row set.

### WR-02: WARNING - Game-specific detectors ignore cancellation during detector traversal

**File:** `QueryPlugins/PluginQueryService.cs:84-88`

**Issue:** `Analyse` checks cancellation before/after detector calls, but `FindDeletedReferences` and `FindDeletedNavmeshes` receive no cancellation token. A cancellation request during those detector traversals cannot interrupt work until the entire detector returns, which violates the public interface contract that cancellation should interrupt detector work without producing partial results.

**Fix:** Extend `IGameSpecificDetector` methods to accept `CancellationToken` and check it inside traversal loops, then pass `ct` from `PluginQueryService.Analyse`.

---

_Reviewed: 2026-04-30T09:30:17Z_
_Reviewer: the agent (gsd-code-reviewer)_
_Depth: deep_
