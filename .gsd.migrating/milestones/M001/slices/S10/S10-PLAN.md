# S10: Plugin Refresh Approximation Performance

**Goal:** Optimize the QueryPlugins ITM hot path so Phase 9 approximation work can be canceled inside record/context loops while preserving exact count semantics.
**Demo:** Optimize the QueryPlugins ITM hot path so Phase 9 approximation work can be canceled inside record/context loops while preserving exact count semantics.

## Must-Haves


## Tasks

- [x] **T01: 09-plugin-refresh-approximation-performance 01** `est:4 min`
  - Optimize the QueryPlugins ITM hot path so Phase 9 approximation work can be canceled inside record/context loops while preserving exact count semantics.

Purpose: Satisfies PERF-02 and the count-accuracy decisions D-17 through D-24 before the app-level refresh coordinator depends on cancellation-aware analysis.
Output: Cancellation-aware QueryPlugins analysis contracts, streaming ITM traversal, and regression tests.
- [x] **T02: 09-plugin-refresh-approximation-performance 02** `est:2 min`
  - Define and test the Phase 9 plugin refresh coordinator contract before implementing the service extraction.

Purpose: Prevents downstream executors from inventing incompatible refresh APIs and pins D-01 through D-16 and D-25 through D-36 in executable tests.
Output: Service contracts, typed request/status DTOs, refresh-scoped capability policy contract, and Wave 0 tests.
- [x] **T03: 09-plugin-refresh-approximation-performance 03** `est:6 min`
  - Implement the plugin refresh coordinator and move the game/data-folder/load-order refresh workflow out of `ConfigurationViewModel`.

Purpose: Satisfies REF-02 and the workflow ownership decisions D-25 through D-31 while preserving row-first/stale-result behavior from existing code.
Output: DI-registered refresh coordinator, refresh-scoped capability policy, and a thinner ConfigurationViewModel.
- [x] **T04: 09-plugin-refresh-approximation-performance 04** `est:3 min`
  - Add the user-facing selected approximation refresh and cancel controls in the plugin list surface.

Purpose: Satisfies D-02, D-03, D-06, D-07, D-08, D-13, D-16, D-32, D-34, D-35, and the approved UI-SPEC without moving business workflow into the ViewModel.
Output: PluginList commands, XAML controls, and ViewModel tests.
- [x] **T05: 09-plugin-refresh-approximation-performance 05** `est:9 min`
  - Complete cross-ViewModel refresh cancellation/lifecycle wiring and run final Phase 9 verification.

Purpose: Ensures background Mutagen/file I/O cannot compete with sequential xEdit cleaning and that all Phase 9 source decisions are connected in the application shell.
Output: Cleaning-start cancellation, lifecycle cancellation tests, DI assertions, and full solution verification.
- [x] **T06: 09-plugin-refresh-approximation-performance 06** `est:3 min`
  - Close Phase 9 verification gaps for destructive selected approximation refresh.

Purpose: Selected refresh must be a targeted approximation update, not a plugin-list replacement. This preserves existing non-targeted row visibility and prior approximation values while still narrowing analysis work to the selected target snapshot per D-02, D-04, D-06, D-08, and D-27.
Output: A coordinator regression test and implementation/source guard ensuring selected refresh marks or merges only target rows and leaves non-targeted rows unchanged.
- [x] **T07: 09-plugin-refresh-approximation-performance 07** `est:3 min`
  - Close VERIFICATION.md gap: "Disable Skip Lists is ignored by coordinator refresh row publication."

`PluginRefreshCoordinator.RefreshForGameAsync` currently calls `ApplySkipListStatus(..., disableSkipLists: false, ...)` at line 90, hardcoding the flag to `false`. `PluginRefreshRequest` has no `DisableSkipLists` field, so `ConfigurationViewModel` cannot pass `DisableSkipListsEnabled` (already loaded from `UserConfiguration.Settings.DisableSkipLists`) into the coordinator. Result: refreshed plugin rows always treat skip-list entries as hidden, contradicting the user's Disable Skip Lists toggle.

Purpose: Restore the user-visible truth that toggling Disable Skip Lists changes which plugin rows are marked `IsInSkipList` and therefore which rows are eligible approximation targets.

Output:
- `PluginRefreshRequest` gains a `DisableSkipLists` boolean field (default `false`).
- `PluginRefreshCoordinator.RefreshForGameAsync` passes `request.DisableSkipLists` into `ApplySkipListStatus` instead of `false`.
- `ConfigurationViewModel.RefreshPluginsForGameAsync` populates the new field from `DisableSkipListsEnabled`.
- A new xUnit regression test asserts that when the request carries `DisableSkipLists: true`, no row in `state.PluginsToClean` has `IsInSkipList: true` even when the loaded plugin name matches a configured skip-list entry.
- [x] **T08: 09-plugin-refresh-approximation-performance 08** `est:3 min`
  - Close VERIFICATION.md gap: "Full-list approximation refresh does not publish a terminal status to clear running/cancel UI state."

After `RefreshForGameAsync` finishes its `AnalyzeTargetsAsync` call (line 115), the coordinator returns without publishing a non-running status. `PluginListViewModel.OnPluginRefreshStatusChanged` (lines 281-285) therefore keeps `IsApproximationRefreshRunning` at the last truthy value (set when `LoadingPlugins` or `AnalyzingSelected` arrived), and the cancel-refresh button stays enabled even though there is no work to cancel.

Purpose: Restore the user-visible truth that successful full-list refresh ends in a clean, non-running UI state with the cancel control disabled.

Output:
- A new `FullRefreshCompleted` value on `PluginRefreshStatusKind` and a static factory `PluginRefreshStatus.FullRefreshCompleted(int updatedCount)` that mirrors the existing `SelectedRefreshCompleted` shape.
- `PluginRefreshCoordinator.RefreshForGameAsync` publishes `PluginRefreshStatus.FullRefreshCompleted(updated)` after a successful `AnalyzeTargetsAsync` call, gated on the same `IsCurrent(generation, token)` check used elsewhere so superseded generations stay silent (per D-10).
- `PluginListViewModel.OnPluginRefreshStatusChanged` continues to set `IsApproximationRefreshRunning` true only for `LoadingPlugins` / `AnalyzingSelected`; the new `FullRefreshCompleted` kind falls into the non-running branch (false), unblocking the cancel UI.
- Two regression tests: one at the coordinator level proving the terminal status is published, one at the ViewModel level proving `IsApproximationRefreshRunning` flips back to false when the status arrives.
- [x] **T09: 09-plugin-refresh-approximation-performance 09** `est:35min`
  - Close the remaining Phase 9 verification blockers for refresh cancellation lifetime safety, failure-terminal status cleanup, and variant-specific skip-list consistency.

Purpose: PERF-01 remains blocked until cancellation is cooperative under active async work, refresh failures leave the UI in a non-running state, and refresh row skip-list decisions match cleaning preflight for TTW/Enderal variants.
Output: Hardened `PluginRefreshCoordinator` behavior plus focused regression tests proving the three verification gaps are closed.

## Files Likely Touched

- `QueryPlugins/IPluginQueryService.cs`
- `QueryPlugins/PluginQueryService.cs`
- `QueryPlugins/Detectors/IItmDetector.cs`
- `QueryPlugins/Detectors/ItmDetector.cs`
- `QueryPlugins.Tests/Detectors/ItmDetectorTests.cs`
- `AutoQAC/Services/Plugin/PluginIssueApproximationService.cs`
- `AutoQAC.Tests/Services/PluginIssueApproximationServiceTests.cs`
- `AutoQAC/Services/Plugin/IPluginRefreshCoordinator.cs`
- `AutoQAC/Services/Plugin/PluginRefreshRequest.cs`
- `AutoQAC/Services/Plugin/PluginRefreshStatus.cs`
- `AutoQAC/Services/Plugin/IPluginRefreshCapabilityPolicy.cs`
- `AutoQAC.Tests/Services/PluginRefreshCoordinatorTests.cs`
- `AutoQAC.Tests/Services/StateServiceTests.cs`
- `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs`
- `AutoQAC/Services/Plugin/PluginRefreshCapabilityPolicy.cs`
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`
- `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs`
- `AutoQAC/ViewModels/MainWindowViewModel.cs`
- `AutoQAC.Tests/Services/PluginRefreshCoordinatorTests.cs`
- `AutoQAC.Tests/Integration/DependencyInjectionTests.cs`
- `AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs`
- `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs`
- `AutoQAC/ViewModels/MainWindowViewModel.cs`
- `AutoQAC/Views/MainWindow.axaml`
- `AutoQAC.Tests/ViewModels/PluginListViewModelTests.cs`
- `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs`
- `AutoQAC/ViewModels/MainWindowViewModel.cs`
- `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs`
- `AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs`
- `AutoQAC.Tests/ViewModels/PluginListViewModelTests.cs`
- `AutoQAC.Tests/Integration/DependencyInjectionTests.cs`
- `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs`
- `AutoQAC.Tests/Services/PluginRefreshCoordinatorTests.cs`
- `AutoQAC/Services/Plugin/PluginRefreshRequest.cs`
- `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs`
- `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs`
- `AutoQAC.Tests/Services/PluginRefreshCoordinatorTests.cs`
- `AutoQAC/Services/Plugin/PluginRefreshStatus.cs`
- `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs`
- `AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs`
- `AutoQAC.Tests/Services/PluginRefreshCoordinatorTests.cs`
- `AutoQAC.Tests/ViewModels/PluginListViewModelTests.cs`
- `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs`
- `AutoQAC.Tests/Services/PluginRefreshCoordinatorTests.cs`
- `AutoQAC.Tests/ViewModels/PluginListViewModelTests.cs`
