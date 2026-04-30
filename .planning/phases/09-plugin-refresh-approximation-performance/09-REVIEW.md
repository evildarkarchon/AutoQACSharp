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
  - AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs
  - AutoQAC/Views/MainWindow.axaml
  - AutoQAC.Tests/ViewModels/PluginListViewModelTests.cs
  - AutoQAC.Tests/ViewModels/CleaningCommandsViewModelTests.cs
  - AutoQAC.Tests/ViewModels/MainWindowThreadingTests.cs
  - AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs
  - AutoQAC.Tests/ViewModels/ErrorDialogTests.cs
findings:
  critical: 3
  warning: 0
  info: 2
  total: 5
status: issues_found
---

# Phase 09: Code Review Report

**Reviewed:** 2026-04-30T00:00:00Z
**Depth:** deep
**Files Reviewed:** 27
**Status:** issues_found

## Summary

Deep review found multiple correctness defects in the new refresh/approximation flow. The most serious issues are UI-thread violations from service status callbacks and targeted refresh logic that discards non-selected plugins from the state list. These are behavioral regressions that can break the Avalonia UI and lose visible plugin rows during ordinary use.

## Critical Issues

### CR-01: BLOCKER - Refresh status callbacks mutate UI-bound ViewModel properties from worker threads

**File:** `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs:127-145`, `AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs:70-82`, `AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs:277-282`, `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs:315-329`

**Issue:** `ConfigurationViewModel` and `PluginListViewModel` subscribe directly to `IPluginRefreshCoordinator.StatusChanged` and mutate `StatusText` / `IsApproximationRefreshRunning` inside the observer. Those statuses are published from `PluginRefreshCoordinator.AnalyzeTargetsAsync` callbacks while `PluginIssueApproximationService.GetApproximationsAsync` runs analysis on `Task.Run`, so the observer can execute on a thread-pool thread. This violates the repository rule that service `IObservable<T>` streams must be marshaled through `IUiDispatcher` before touching ViewModel/UI state, and can cause Avalonia cross-thread access failures or nondeterministic UI corruption.

**Fix:** Inject `IUiDispatcher` into the ViewModels that subscribe to refresh status and marshal property updates onto the UI thread, or centralize status-to-state mapping through `IStateService` so the existing parent dispatcher path handles it.

```csharp
_pluginRefreshStatusSubscription = _pluginRefreshCoordinator.StatusChanged.Subscribe(
    new CallbackObserver<PluginRefreshStatus>(status =>
        _uiDispatcher.Post(() => OnPluginRefreshStatusChanged(status))));
```

Add a regression test that publishes `PluginRefreshStatus.AnalyzingSelected(...)` from a background task and asserts the ViewModel update goes through the injected dispatcher.

### CR-02: BLOCKER - Selected approximation refresh drops every non-selected plugin row

**File:** `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs:167-177`

**Issue:** `RefreshSelectedApproximationsAsync` builds `pendingRows` from only the selected target snapshot and then calls `_stateService.SetPluginsToClean(pendingRows)`. In the real UI, the user can have a full plugin list loaded and refresh only one checked row; this code replaces the entire application plugin list with that selected subset. Deselected rows, already-analyzed rows, and skip-list context disappear from state/UI even though the command is supposed to refresh approximations only for selected rows.

**Fix:** Do not replace `PluginsToClean` for targeted refresh. Mark only matching existing rows as pending (add a targeted state method if necessary), then merge streaming results into the existing list.

```csharp
// Instead of SetPluginsToClean(pendingRows), update only matching rows.
_stateService.UpdateState(s => s with
{
    PluginsToClean = s.PluginsToClean.Select(plugin =>
        snapshot.Any(t => string.Equals(t.FullPath, plugin.FullPath, StringComparison.OrdinalIgnoreCase))
            ? plugin with { Approximation = PluginIssueApproximation.Pending }
            : plugin).ToList().AsReadOnly()
});
```

Add a coordinator/ViewModel test with two visible plugins, select one, refresh, and assert the unselected row remains in `PluginsToClean` with its previous approximation.

### CR-03: BLOCKER - Command continuation updates observable properties off the UI thread

**File:** `AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs:149-156`

**Issue:** `RefreshSelectedApproximationsAsync` awaits the coordinator with `.ConfigureAwait(false)` and then sets `IsApproximationRefreshRunning = false` in `finally`. Because this command is invoked from the UI and the continuation is explicitly allowed to resume on a thread-pool thread, it can raise `PropertyChanged` for an Avalonia-bound property off the UI thread. This is a second, independent UI-thread violation even if status callbacks are fixed.

**Fix:** Do not use `ConfigureAwait(false)` in UI command handlers when the continuation mutates ViewModel state. If background work is needed, keep it inside services and resume the command on the UI context before changing properties.

```csharp
try
{
    await _pluginRefreshCoordinator.RefreshSelectedApproximationsAsync(
        new PluginRefreshRequest(CurrentGameType),
        targets);
}
finally
{
    IsApproximationRefreshRunning = false;
}
```

## Info

### IN-01: Missing regression coverage for preserving non-selected rows during targeted refresh

**File:** `AutoQAC.Tests/Services/PluginRefreshCoordinatorTests.cs:26-45`

**Issue:** Existing selected-refresh tests start from an empty state or only assert that late additions are not analyzed. They do not cover the ordinary UI path where a full plugin list exists and only selected rows should be refreshed in place. That gap allowed CR-02 to ship.

**Fix:** Add a test that seeds `StateService` with multiple plugins and an existing approximation, calls `RefreshSelectedApproximationsAsync` for one target, then asserts all original rows remain and only the target row changes.

### IN-02: Missing regression coverage for refresh status UI dispatch

**File:** `AutoQAC.Tests/ViewModels/MainWindowThreadingTests.cs:21-87`, `AutoQAC.Tests/ViewModels/PluginListViewModelTests.cs:161-187`

**Issue:** Threading tests cover `StateChanged` dispatch through `IUiDispatcher`, but refresh-status subscriptions are a separate observable path and are not tested for dispatcher use. The direct subscriptions in CR-01 therefore look correct in tests while violating the project threading convention in production.

**Fix:** Add tests that publish coordinator status from a background task and verify `ConfigurationViewModel` / `PluginListViewModel` property mutation is scheduled through `IUiDispatcher` rather than executing inline on the publisher thread.

---

_Reviewed: 2026-04-30T00:00:00Z_
_Reviewer: the agent (gsd-code-reviewer)_
_Depth: deep_
