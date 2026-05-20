---
id: T04
parent: S10
milestone: M001
provides:
  - Plugin-list selected approximation refresh command gated by visible checked rows and game capability
  - Plugin-list cancel refresh command bound to active approximation refresh state
  - Avalonia plugin-list toolbar buttons for refresh and cancel actions
requires: []
affects: []
key_files: []
key_decisions: []
patterns_established: []
observability_surfaces: []
drill_down_paths: []
duration: 3 min
verification_result: passed
completed_at: 2026-04-30
blocker_discovered: false
---
# T04: 09-plugin-refresh-approximation-performance 04

**# Phase 09 Plan 04: Selected Approximation Refresh UI Summary**

## What Happened

# Phase 09 Plan 04: Selected Approximation Refresh UI Summary

**Checked-row approximation refresh and cancel controls wired from the plugin-list toolbar to the refresh coordinator**

## Performance

- **Duration:** 3 min
- **Started:** 2026-04-30T07:50:47Z
- **Completed:** 2026-04-30T07:54:21Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments

- Added `RefreshSelectedApproximationsCommand` and `CancelApproximationRefreshCommand` to `PluginListViewModel` without moving analysis workflow into the ViewModel.
- Added command gating for loaded plugin rows, cleaning state, checked visible rows, current game, and refresh capability.
- Added Avalonia toolbar buttons for `Refresh selected approximations` and `Cancel refresh` next to the existing plugin-list controls.

## Task Commits

Each task was committed atomically:

1. **Task 1 RED: Add failing selected refresh command tests** - `cad8189` (test)
2. **Task 1 GREEN: Implement selected approximation refresh commands** - `077606d` (feat)
3. **Task 2: Add plugin-list toolbar controls per UI-SPEC** - `46b58e8` (feat)

**Plan metadata:** pending final docs commit

_Note: Task 1 followed the required TDD RED → GREEN sequence._

## Files Created/Modified

- `AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs` - Injects refresh coordinator/capability policy, exposes refresh/cancel commands, snapshots checked visible rows, and tracks active refresh state.
- `AutoQAC/ViewModels/MainWindowViewModel.cs` - Passes refresh coordinator and capability policy into the plugin-list child ViewModel.
- `AutoQAC/Views/MainWindow.axaml` - Adds refresh selected and cancel refresh toolbar buttons near `All`, `None`, and `Manage Skip List`.
- `AutoQAC.Tests/ViewModels/PluginListViewModelTests.cs` - Adds command gating and snapshot behavior coverage with test doubles.

## Decisions Made

- Plugin-list refresh commands delegate to `IPluginRefreshCoordinator`; the ViewModel only snapshots selected rows and tracks UI command state.
- `IPluginRefreshCapabilityPolicy` is used for command enablement so unsupported approximation games stay disabled without broadening app-level game support.
- Cancel refresh remains a standard button command with no confirmation dialog, matching the UI-SPEC cancellation contract.

## Deviations from Plan

None - plan executed exactly as written.

## Verification

- RED: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~PluginListViewModel"` failed before implementation with missing constructor/command compile errors, proving the new behavior was absent.
- GREEN/Task 1: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~PluginListViewModel"` — PASS (11 tests).
- Task 2: `dotnet build AutoQAC/AutoQAC.csproj` — PASS (0 warnings, 0 errors).
- Plan-level verification: both required commands were re-run successfully after commits.
- Acceptance criteria scans confirmed required command names, required test names, toolbar bindings, and no direct `PluginIssueApproximationService` reference from `PluginListViewModel`.

## Issues Encountered

- Running `dotnet test` and `dotnet build` in parallel caused a transient `AutoQAC.pdb` file lock from concurrent compilation. The build succeeded and the test command passed when re-run sequentially; no code change was required.

## User Setup Required

None - no external service configuration required.

## Known Stubs

None. Optional `null` constructor defaults and internal no-op fallbacks preserve existing test construction compatibility; they do not feed placeholder data into the UI.

## TDD Gate Compliance

- RED gate: `cad8189` (`test(09-04): add failing selected refresh command tests`)
- GREEN gate: `077606d` (`feat(09-04): implement selected approximation refresh commands`)
- REFACTOR gate: not needed

## Next Phase Readiness

- Ready for 09-05 to validate full phase behavior and any remaining polish around refresh status/cancellation integration.
- The plugin list now exposes user-facing selected refresh and cancel controls over the coordinator APIs introduced in 09-03.

## Self-Check: PASSED

- Verified modified files and `09-04-SUMMARY.md` exist on disk.
- Verified task commits `cad8189`, `077606d`, and `46b58e8` exist in git history.

---
*Phase: 09-plugin-refresh-approximation-performance*
*Completed: 2026-04-30*
