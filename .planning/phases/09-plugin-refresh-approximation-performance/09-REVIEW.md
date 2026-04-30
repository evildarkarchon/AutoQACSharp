---
phase: 09-plugin-refresh-approximation-performance
reviewed: 2026-04-30T00:00:00Z
depth: deep
files_reviewed: 27
files_reviewed_list:
  - QueryPlugins/IPluginQueryService.cs
  - QueryPlugins/PluginQueryService.cs
  - QueryPlugins/Detectors/IItmDetector.cs
  - QueryPlugins/Detectors/ItmDetector.cs
  - QueryPlugins.Tests/Detectors/ItmDetectorTests.cs
  - AutoQAC/Services/Plugin/PluginIssueApproximationService.cs
  - AutoQAC.Tests/Services/PluginIssueApproximationServiceTests.cs
  - AutoQAC/Services/Plugin/IPluginRefreshCoordinator.cs
  - AutoQAC/Services/Plugin/PluginRefreshRequest.cs
  - AutoQAC/Services/Plugin/PluginRefreshStatus.cs
  - AutoQAC/Services/Plugin/IPluginRefreshCapabilityPolicy.cs
  - AutoQAC.Tests/Services/PluginRefreshCoordinatorTests.cs
  - AutoQAC.Tests/Services/StateServiceTests.cs
  - AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs
  - AutoQAC/Services/Plugin/PluginRefreshCapabilityPolicy.cs
  - AutoQAC/Infrastructure/ServiceCollectionExtensions.cs
  - AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs
  - AutoQAC/ViewModels/MainWindowViewModel.cs
  - AutoQAC.Tests/Integration/DependencyInjectionTests.cs
  - AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs
  - AutoQAC/Views/MainWindow.axaml
  - AutoQAC.Tests/ViewModels/PluginListViewModelTests.cs
  - AutoQAC.Tests/ViewModels/CleaningCommandsViewModelTests.cs
  - AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs
  - AutoQAC.Tests/ViewModels/MainWindowThreadingTests.cs
  - AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs
  - AutoQAC.Tests/ViewModels/ErrorDialogTests.cs
findings:
  critical: 2
  warning: 2
  info: 0
  total: 4
status: issues_found
---

# Phase 09: Code Review Report

**Reviewed:** 2026-04-30T00:00:00Z
**Depth:** deep
**Files Reviewed:** 27
**Status:** issues_found

## Summary

Deep review covered the QueryPlugins detector changes, approximation service, refresh coordinator, DI wiring, main-window ViewModels/XAML, and related tests. The implementation has two behavioral blockers: the existing “Disable Skip Lists” setting is ignored by the new coordinator path, and full-list approximation refreshes leave the UI in a perpetual running state. I also found cancellation/thread-safety robustness gaps in refresh status publication and detector cancellation propagation.

## Critical Issues

### CR-01: BLOCKER - Disable Skip Lists setting is ignored during coordinator refresh

**File:** `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs:87-91`  
**Issue:** `ApplySkipListStatus` is always called with `disableSkipLists: false`, so the UI setting saved by `ConfigurationViewModel.HandleDisableSkipListsChangedAsync` never reaches the refresh coordinator. Toggling “Disable Skip Lists” still marks skip-list plugins as `IsInSkipList`, hides them from `PluginListViewModel`, and prevents them from being selected/cleaned. This is a behavioral regression for an existing cleaning control.

**Fix:** Carry the setting into the refresh request (or read it from configuration inside the coordinator) and pass it through to `ApplySkipListStatus`; add a regression test that toggling the setting publishes skip-list plugins as visible/selectable.

```csharp
public sealed record PluginRefreshRequest(
    GameType GameType,
    string? DataFolderPath = null,
    string? LoadOrderPath = null,
    bool DisableSkipLists = false);

// ConfigurationViewModel.RefreshPluginsForGameAsync
await _pluginRefreshCoordinator.RefreshForGameAsync(
    new PluginRefreshRequest(gameType, GameDataFolder, LoadOrderPath, DisableSkipListsEnabled));

// PluginRefreshCoordinator.RefreshForGameAsync
var rows = ApplySkipListStatus(
    loadedPlugins,
    skipList,
    request.GameType,
    request.DisableSkipLists,
    initialApproximation);
```

### CR-02: BLOCKER - Full approximation refresh never publishes a terminal status

**File:** `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs:113-130`, `AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs:281-285`  
**Issue:** `PluginListViewModel` sets `IsApproximationRefreshRunning` to true for `LoadingPlugins` and `AnalyzingSelected`, but `RefreshForGameAsync` emits no completion/idle status after a successful full-list approximation run. After selecting a game or refreshing the full plugin list, the cancel-refresh button can remain visible forever even though no refresh is active.

**Fix:** Emit a terminal status after full-refresh analysis completes, or add a dedicated `FullRefreshCompleted` status kind and clear the running flag for all terminal statuses.

```csharp
var updated = await AnalyzeTargetsAsync(request.GameType, dataFolder, targets, generation, token)
    .ConfigureAwait(false);
if (IsCurrent(generation, token))
{
    Publish(new PluginRefreshStatus(
        PluginRefreshStatusKind.Idle,
        Message: $"Updated {updated} plugin approximations."));
}

// PluginListViewModel: any non-running status should clear this flag.
IsApproximationRefreshRunning = status.Kind is
    PluginRefreshStatusKind.LoadingPlugins or
    PluginRefreshStatusKind.AnalyzingSelected;
```

## Warnings

### WR-01: WARNING - Refresh status Subject can receive concurrent OnNext calls

**File:** `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs:27`, `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs:233-235`, `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs:340-342`, `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs:382`  
**Issue:** `_statusChanged` is a raw `Subject<PluginRefreshStatus>`. Approximation callbacks publish from the background analysis path while `CancelActiveRefresh` can publish from the UI thread. Rx `Subject<T>` is not safe for concurrent `OnNext` calls; overlapping progress/cancel emissions can corrupt observer state or throw, even though subscribers marshal their mutations to `IUiDispatcher` after receipt.

**Fix:** Serialize publication with a lock or a synchronized subject.

```csharp
private readonly object _statusGate = new();

private void Publish(PluginRefreshStatus status)
{
    lock (_statusGate)
    {
        _statusChanged.OnNext(status);
    }
}
```

### WR-02: WARNING - Cancellation is not propagated into deleted-reference/navmesh detectors

**File:** `QueryPlugins/PluginQueryService.cs:84-88`, `QueryPlugins/Detectors/IGameSpecificDetector.cs:25-33`  
**Issue:** ITM detection receives the cancellation token, but deleted reference and navmesh scans do not. For large plugins, a canceled refresh can remain stuck inside `FindDeletedReferences` or `FindDeletedNavmeshes` until the entire traversal finishes, delaying manual cancel, superseded refreshes, and cleaning-start cancellation.

**Fix:** Add `CancellationToken` parameters to `IGameSpecificDetector` methods and check the token during traversal in each game-specific detector.

```csharp
IEnumerable<PluginIssue> FindDeletedReferences(IModGetter plugin, CancellationToken ct = default);
IEnumerable<PluginIssue> FindDeletedNavmeshes(IModGetter plugin, CancellationToken ct = default);

issues.AddRange(gameDetector.FindDeletedReferences(plugin, ct));
ct.ThrowIfCancellationRequested();
issues.AddRange(gameDetector.FindDeletedNavmeshes(plugin, ct));
```

---

_Reviewed: 2026-04-30T00:00:00Z_  
_Reviewer: the agent (gsd-code-reviewer)_  
_Depth: deep_
