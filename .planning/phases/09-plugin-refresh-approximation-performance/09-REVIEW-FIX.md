---
phase: 09-plugin-refresh-approximation-performance
fixed_at: 2026-04-30T08:26:21Z
review_path: .planning/phases/09-plugin-refresh-approximation-performance/09-REVIEW.md
iteration: 1
findings_in_scope: 5
fixed: 5
skipped: 0
status: all_fixed
---

# Phase 09: Code Review Fix Report

**Fixed at:** 2026-04-30T08:26:21Z
**Source review:** `.planning/phases/09-plugin-refresh-approximation-performance/09-REVIEW.md`
**Iteration:** 1

**Summary:**
- Findings in scope: 5
- Fixed: 5
- Skipped: 0

## Fixed Issues

### CR-01: Refresh status callbacks mutate UI-bound ViewModel properties from worker threads

**Files modified:** `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs`, `AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs`, `AutoQAC/ViewModels/MainWindowViewModel.cs`, `AutoQAC.Tests/ViewModels/MainWindowThreadingTests.cs`
**Commit:** `cab8201`
**Applied fix:** Injected the main `IUiDispatcher` into refresh-status subscribers and routed `PluginRefreshStatus` handling through `Post` before mutating UI-bound ViewModel properties.

### CR-02: Selected approximation refresh drops every non-selected plugin row

**Files modified:** `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs`, `AutoQAC.Tests/Services/PluginRefreshCoordinatorTests.cs`
**Commit:** `b48b61e`
**Applied fix:** Changed selected refresh to mark matching existing rows as pending in place instead of replacing `PluginsToClean`, preserving non-selected rows and their previous approximations.

### CR-03: Command continuation updates observable properties off the UI thread

**Files modified:** `AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs`
**Commit:** `cab8201`
**Applied fix:** Removed `ConfigureAwait(false)` from the UI command handler path so the `finally` continuation that clears `IsApproximationRefreshRunning` resumes on the UI context.

### IN-01: Missing regression coverage for preserving non-selected rows during targeted refresh

**Files modified:** `AutoQAC.Tests/Services/PluginRefreshCoordinatorTests.cs`
**Commit:** `b48b61e`
**Applied fix:** Added a regression test that seeds multiple visible plugin rows, refreshes one selected target, and asserts the non-selected row remains unchanged.

### IN-02: Missing regression coverage for refresh status UI dispatch

**Files modified:** `AutoQAC.Tests/ViewModels/MainWindowThreadingTests.cs`
**Commit:** `cab8201`
**Applied fix:** Added a dispatcher regression test that publishes refresh status from a background task and verifies both Configuration and PluginList updates are posted through `IUiDispatcher`.

## Skipped Issues

None.

## Verification

- `dotnet test "AutoQAC.Tests/AutoQAC.Tests.csproj" --filter "FullyQualifiedName~PluginRefreshCoordinatorTests.RefreshSelectedApproximationsAsync_ShouldPreserveNonSelectedPluginRows|FullyQualifiedName~MainWindowThreadingTests.MainWindowViewModel_ShouldPostRefreshStatusChangesThroughIUiDispatcher|FullyQualifiedName~PluginListViewModelTests.RefreshSelectedApproximationsCommand_ShouldSnapshotCheckedVisibleRows|FullyQualifiedName~PluginRefreshCoordinatorTests.RefreshSelectedApproximationsAsync_UsesSnapshotAndDoesNotAnalyzeDeselectedRows"` passed.
- `dotnet test "AutoQACSharp.slnx"` passed: QueryPlugins.Tests 61/61, AutoQAC.Tests 844/844.

---

_Fixed: 2026-04-30T08:26:21Z_
_Fixer: OpenCode (manual fallback after gsd-code-fixer worktree setup failed)_
_Iteration: 1_
