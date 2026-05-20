# T08: 09-plugin-refresh-approximation-performance 08

**Slice:** S10 — **Milestone:** M001

## Description

Close VERIFICATION.md gap: "Full-list approximation refresh does not publish a terminal status to clear running/cancel UI state."

After `RefreshForGameAsync` finishes its `AnalyzeTargetsAsync` call (line 115), the coordinator returns without publishing a non-running status. `PluginListViewModel.OnPluginRefreshStatusChanged` (lines 281-285) therefore keeps `IsApproximationRefreshRunning` at the last truthy value (set when `LoadingPlugins` or `AnalyzingSelected` arrived), and the cancel-refresh button stays enabled even though there is no work to cancel.

Purpose: Restore the user-visible truth that successful full-list refresh ends in a clean, non-running UI state with the cancel control disabled.

Output:
- A new `FullRefreshCompleted` value on `PluginRefreshStatusKind` and a static factory `PluginRefreshStatus.FullRefreshCompleted(int updatedCount)` that mirrors the existing `SelectedRefreshCompleted` shape.
- `PluginRefreshCoordinator.RefreshForGameAsync` publishes `PluginRefreshStatus.FullRefreshCompleted(updated)` after a successful `AnalyzeTargetsAsync` call, gated on the same `IsCurrent(generation, token)` check used elsewhere so superseded generations stay silent (per D-10).
- `PluginListViewModel.OnPluginRefreshStatusChanged` continues to set `IsApproximationRefreshRunning` true only for `LoadingPlugins` / `AnalyzingSelected`; the new `FullRefreshCompleted` kind falls into the non-running branch (false), unblocking the cancel UI.
- Two regression tests: one at the coordinator level proving the terminal status is published, one at the ViewModel level proving `IsApproximationRefreshRunning` flips back to false when the status arrives.

## Must-Haves

- [ ] "After a successful full-list approximation refresh, the coordinator publishes a non-running terminal status."
- [ ] "PluginListViewModel.IsApproximationRefreshRunning returns to false once the terminal status arrives, so the cancel-refresh affordance clears."
- [ ] "The cancel button (bound to CanCancelApproximationRefresh) is no longer enabled after a successful full-list refresh completes."
- [ ] "Manual cancel before completion still publishes the existing Canceled status (no regression in cancellation UX)."

## Files

- `AutoQAC/Services/Plugin/PluginRefreshStatus.cs`
- `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs`
- `AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs`
- `AutoQAC.Tests/Services/PluginRefreshCoordinatorTests.cs`
- `AutoQAC.Tests/ViewModels/PluginListViewModelTests.cs`
