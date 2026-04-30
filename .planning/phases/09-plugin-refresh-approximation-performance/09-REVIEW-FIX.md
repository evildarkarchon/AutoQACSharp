---
phase: 09-plugin-refresh-approximation-performance
fixed_at: 2026-04-30T03:10:00-07:00
review_path: .planning/phases/09-plugin-refresh-approximation-performance/09-REVIEW.md
iteration: 1
findings_in_scope: 5
fixed: 5
skipped: 0
status: all_fixed
---

# Phase 09: Code Review Fix Report

**Fixed at:** 2026-04-30T03:10:00-07:00
**Source review:** .planning/phases/09-plugin-refresh-approximation-performance/09-REVIEW.md
**Iteration:** 1

**Summary:**
- Findings in scope: 5
- Fixed: 5
- Skipped: 0

## Fixed Issues

### CR-01: BLOCKER - Active refresh token sources are disposed while still in use

**Files modified:** `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs`
**Commit:** 9b27fa9
**Applied fix:** Changed `CancelActiveRefresh` to use `Volatile.Read` (no dispose), changed `CreateAndActivateGeneration` to only `Cancel()` previous CTS (no dispose), updated `Dispose()` to use `Interlocked.Exchange` + cancel + dispose for final cleanup, removed `CancelAndDispose` helper. Each async method's `using var linkedCts` ensures the CTS is disposed only after all awaits unwind in the owning method's `finally`/scope exit.

### CR-02: BLOCKER - Refresh failures leave the UI permanently in a running/cancel state

**Files modified:** `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs`
**Commit:** d4efa1a
**Applied fix:** Added `Publish(new PluginRefreshStatus(PluginRefreshStatusKind.Idle, Message: "Approximation refresh failed."))` at the end of the approximation catch block, guarded by `IsCurrent(generation, token)`. This ensures `IsApproximationRefreshRunning` clears even when the approximation phase throws.

### CR-03: BLOCKER - Refresh skip-list handling ignores detected game variants

**Files modified:** `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs`, `AutoQAC.Tests/Services/PluginRefreshCoordinatorTests.cs`, `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs`, `AutoQAC/ViewModels/MainWindowViewModel.cs`
**Commit:** e311d2b (coordinator + tests), 4742f59 (ViewModels — bundled with WR-02 due to shared compile dependency)
**Applied fix:** Added `IGameDetectionService` dependency to the `PluginRefreshCoordinator` constructor. Before fetching skip lists, the coordinator now calls `_gameDetectionService.DetectVariant(request.GameType, pluginNames)` and passes the detected variant through to `GetSkipListAsync(gameType, variant, ct)`. The `GetSkipListAsync` signature was updated to accept `GameVariant`. `ConfigurationViewModel` and test helpers were updated to supply the new parameter. `IConfigurationService.GetSkipListAsync` already accepted `GameVariant` as an optional parameter.

### WR-01: WARNING - Approximation matching falls back to file name and can update the wrong row

**Files modified:** `AutoQAC/Services/State/StateService.cs`, `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs`
**Commit:** a0225bc
**Applied fix:** Strengthened three matching sites to prefer full path when both sides have one, only falling back to filename when one side lacks a usable path:
- `StateService.IsApproximationMatch`: path-only match when both have path, filename fallback otherwise
- `StateService.TryGetApproximationMatch`: filename fallback only when `plugin.FullPath` is blank
- `PluginRefreshCoordinator.IsTarget`: path-first check, filename fallback only when approximation has no path
- `RefreshSelectedApproximationsAsync` state update: path-based check with name fallback only when row has no path

This is a logic change; marked for human verification.

### WR-02: WARNING - Game-specific detectors ignore cancellation during detector traversal

**Files modified:** `QueryPlugins/Detectors/IGameSpecificDetector.cs`, `QueryPlugins/Detectors/Games/SkyrimDetector.cs`, `QueryPlugins/Detectors/Games/Fallout4Detector.cs`, `QueryPlugins/Detectors/Games/StarfieldDetector.cs`, `QueryPlugins/Detectors/Games/OblivionDetector.cs`, `QueryPlugins/PluginQueryService.cs`
**Commit:** 4742f59
**Applied fix:** Added `CancellationToken ct = default` parameter to both `FindDeletedReferences` and `FindDeletedNavmeshes` in `IGameSpecificDetector`. All four detector implementations now call `ct.ThrowIfCancellationRequested()` inside each `.Where()` predicate, enabling cooperative cancellation during record traversal. `PluginQueryService.Analyse` now passes `ct` to both detector calls. Default parameter values preserve backward compatibility.

## Skipped Issues

None — all findings were fixed.

---

_Fixed: 2026-04-30T03:10:00-07:00_
_Fixer: the agent (gsd-code-fixer)_
_Iteration: 1_
