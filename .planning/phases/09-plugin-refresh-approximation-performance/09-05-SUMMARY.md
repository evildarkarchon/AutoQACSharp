---
phase: 09-plugin-refresh-approximation-performance
plan: 05
subsystem: plugin-refresh-lifecycle
tags: [plugin-refresh, cancellation, mvvm, dependency-injection, testing]

# Dependency graph
requires:
  - phase: 09-01
    provides: Cancellation-aware QueryPlugins approximation analysis
  - phase: 09-04
    provides: Selected approximation refresh and cancel UI commands
provides:
  - Cleaning-start cancellation of active approximation refresh work
  - Reset/disposal lifecycle cancellation reasons for refresh coordinator work
  - End-to-end DI and full solution verification for Phase 09
affects: [plugin-refresh, cleaning-start, configuration-viewmodel, main-window, phase-10]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - Shared IPluginRefreshCoordinator injection across MainWindow child ViewModels
    - Cleaning-start refresh cancellation before progress display and xEdit orchestration
    - Coordinator-backed load-order refresh source compatibility tests

key-files:
  created:
    - AutoQAC.Tests/ViewModels/CleaningCommandsViewModelTests.cs
  modified:
    - AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs
    - AutoQAC/ViewModels/MainWindowViewModel.cs
    - AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs
    - AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs
    - AutoQAC/Services/Plugin/PluginRefreshStatus.cs
    - AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs
    - AutoQAC.Tests/ViewModels/PluginListViewModelTests.cs
    - AutoQAC.Tests/ViewModels/MainWindowThreadingTests.cs
    - AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs
    - AutoQAC.Tests/ViewModels/ErrorDialogTests.cs
    - AutoQAC.Tests/Integration/DependencyInjectionTests.cs

key-decisions:
  - "CleaningCommandsViewModel cancels active refresh work with CleaningStarted after validation and before progress display or xEdit orchestration."
  - "MainWindowViewModel passes the DI-provided IPluginRefreshCoordinator to all child ViewModels so lifecycle and command cancellation share one coordinator instance."
  - "Manual load-order refresh remains coordinator-backed even when no game is selected, preserving file-based plugin loading while Phase 09 moves workflow out of ConfigurationViewModel."

patterns-established:
  - "Use behavior tests with call-order recording when cancellation ordering is security/performance critical."
  - "Keep legacy manual load-order tests pointed at IPluginLoadingService once refresh workflow is coordinator-owned."

requirements-completed: [REF-02, PERF-01, PERF-02]

# Metrics
duration: 9 min
completed: 2026-04-30
---

# Phase 09 Plan 05: Refresh Lifecycle Cancellation and Final Verification Summary

**Active approximation refreshes now cancel before xEdit cleaning starts, reset/disposal lifecycle paths request coordinator cancellation, and Phase 09 passes the full solution suite.**

## Performance

- **Duration:** 9 min
- **Started:** 2026-04-30T07:56:39Z
- **Completed:** 2026-04-30T08:05:17Z
- **Tasks:** 2
- **Files modified:** 11

## Accomplishments

- Added RED/GREEN coverage proving cleaning start cancels active approximation refresh before progress display and before `ICleaningOrchestrator.StartCleaningAsync`.
- Wired `IPluginRefreshCoordinator` through `MainWindowViewModel` into `CleaningCommandsViewModel`, and added reset lifecycle cancellation with a typed `Reset` reason.
- Added final DI assertions proving `Configuration`, `PluginList`, and `Commands` share the DI-provided refresh coordinator singleton.
- Ran targeted ViewModel/integration tests and `dotnet test AutoQACSharp.slnx` successfully.

## Task Commits

Each task was committed atomically:

1. **Task 1 RED: Add failing refresh lifecycle tests** - `b416704` (test)
2. **Task 1 GREEN: Implement cleaning/reset refresh cancellation** - `51c268a` (feat)
3. **Task 2: Final integration verification and source guards** - `d121f04` (fix)

**Plan metadata:** pending final docs commit

_Note: Task 1 followed the required TDD RED → GREEN sequence._

## Files Created/Modified

- `AutoQAC.Tests/ViewModels/CleaningCommandsViewModelTests.cs` - Adds call-order coverage for cleaning-start refresh cancellation.
- `AutoQAC/ViewModels/MainWindow/CleaningCommandsViewModel.cs` - Injects the refresh coordinator and cancels active refresh work before cleaning starts.
- `AutoQAC/ViewModels/MainWindowViewModel.cs` - Passes the shared refresh coordinator into the commands child ViewModel.
- `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs` - Cancels active refresh work during settings reset.
- `AutoQAC/Services/Plugin/PluginRefreshStatus.cs` - Adds the typed `Reset` cancellation reason.
- `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs` - Preserves manual load-order refresh and unavailable-row fallback behavior under coordinator ownership.
- `AutoQAC.Tests/Integration/DependencyInjectionTests.cs` - Verifies coordinator and capability policy resolution plus shared child ViewModel coordinator wiring.
- `AutoQAC.Tests/ViewModels/PluginListViewModelTests.cs` - Adds checked-row vs focused-row targeting coverage and manual cancel reason coverage.
- `AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs` - Updates coordinator-owned refresh expectations and fallback assertions.
- `AutoQAC.Tests/ViewModels/ErrorDialogTests.cs` - Updates load-order tests for the coordinator-backed plugin loading path.
- `AutoQAC.Tests/ViewModels/MainWindowThreadingTests.cs` - Updates command ViewModel construction for the new coordinator dependency.

## Decisions Made

- Cleaning-start cancellation is requested after validation succeeds, but before opening the progress interaction, so invalid pre-clean attempts do not cancel unrelated refresh work.
- The DI-provided refresh coordinator is the only shared production coordinator; command-only compatibility construction uses a no-op fallback solely when tests or legacy construction omit the dependency.
- Manual load-order refresh should bypass game capability checks when an explicit load-order path is provided, because file-based loading can be valid before game selection is known.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Preserved coordinator-backed manual load-order refresh**
- **Found during:** Task 2 (full solution verification)
- **Issue:** `PluginRefreshCoordinator.RefreshForGameAsync` returned early for `GameType.Unknown`, so manual load-order file refreshes could publish an empty list instead of parsing the selected file.
- **Fix:** Allow explicit load-order paths to flow through `IPluginLoadingService.GetPluginsFromFileAsync` even when no game is selected.
- **Files modified:** `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs`, `AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs`, `AutoQAC.Tests/ViewModels/ErrorDialogTests.cs`
- **Verification:** `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~DependencyInjection|FullyQualifiedName~PluginListViewModel|FullyQualifiedName~CleaningCommandsViewModel|FullyQualifiedName~MainWindowViewModelTests|FullyQualifiedName~ErrorDialogTests"`; `dotnet test AutoQACSharp.slnx`
- **Committed in:** `d121f04`

**2. [Rule 1 - Bug] Kept row state when approximation service fails**
- **Found during:** Task 2 (full solution verification)
- **Issue:** A service-level approximation exception could skip unavailable-row publication after rows were loaded.
- **Fix:** Log the failure and merge `PluginIssueApproximation.Unavailable` per target through the coordinator's single-row merge path.
- **Files modified:** `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs`, `AutoQAC.Tests/ViewModels/MainWindowViewModelTests.cs`
- **Verification:** targeted affected tests and full solution tests passed.
- **Committed in:** `d121f04`

---

**Total deviations:** 2 auto-fixed (2 Rule 1 bugs)
**Impact on plan:** Both fixes preserve Phase 09 coordinator ownership while maintaining existing manual load-order and failure semantics. No scope expansion beyond final integration correctness.

## Verification

- RED: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~PluginListViewModel|FullyQualifiedName~CleaningCommandsViewModel"` failed before implementation with missing `CleaningCommandsViewModel` coordinator constructor wiring.
- Task 1 GREEN: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~PluginListViewModel|FullyQualifiedName~CleaningCommandsViewModel"` — PASS (15 tests).
- Task 2 targeted: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~DependencyInjection|FullyQualifiedName~PluginListViewModel|FullyQualifiedName~CleaningCommandsViewModel|FullyQualifiedName~MainWindowViewModelTests|FullyQualifiedName~ErrorDialogTests"` — PASS (57 tests).
- Full solution: `dotnet test AutoQACSharp.slnx` — PASS (QueryPlugins.Tests 61 tests; AutoQAC.Tests 842 tests).
- Acceptance source scans confirmed `CleaningStarted`, `Manual`, and `Reset` cancellation reasons; DI assertions for coordinator/capability services; and checked-row behavior independent of `SelectedPlugin` focus.

## Issues Encountered

- Full solution verification surfaced stale/manual-load-order and approximation-failure expectations after coordinator ownership. These were resolved as Rule 1 integration bugs in `d121f04`.

## User Setup Required

None - no external service configuration required.

## Known Stubs

None. Stub scan matches in modified files are optional constructor defaults, nullable state resets, and empty test collections; none are UI placeholder data or incomplete Phase 09 behavior.

## Threat Flags

None. No new network endpoints, auth paths, file access trust boundaries, or schema changes were introduced beyond the planned refresh/cleaning lifecycle coordination.

## TDD Gate Compliance

- RED gate: `b416704` (`test(09-05): add failing refresh cancellation lifecycle tests`)
- GREEN gate: `51c268a` (`feat(09-05): cancel approximation refresh across cleaning lifecycle`)
- REFACTOR gate: not needed

## Next Phase Readiness

- Phase 09 implementation and full automated verification are complete.
- Ready for Phase 09 verification/UAT and then Phase 10 configuration persistence hardening.

## Self-Check: PASSED

- Verified `AutoQAC.Tests/ViewModels/CleaningCommandsViewModelTests.cs` and `09-05-SUMMARY.md` exist on disk.
- Verified task commits `b416704`, `51c268a`, and `d121f04` exist in git history.
- Verified `dotnet test AutoQACSharp.slnx` exits 0 after all task changes.

---
*Phase: 09-plugin-refresh-approximation-performance*
*Completed: 2026-04-30*
