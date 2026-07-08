---
phase: 09-plugin-refresh-approximation-performance
plan: 08
subsystem: plugin-refresh
tags: [plugin-refresh, refresh-status, cancellation-ui, viewmodel, regression-tests]

# Dependency graph
requires:
  - phase: 09-07
    provides: Disable Skip Lists request propagation through PluginRefreshCoordinator
  - phase: 09-04
    provides: PluginListViewModel cancel-refresh affordance bound to refresh running state
provides:
  - FullRefreshCompleted typed terminal status for successful full-list approximation refreshes
  - Coordinator success-path publication of terminal refresh status gated by current generation
  - Regression coverage proving full refresh completion clears running/cancel UI state
affects: [09-VERIFICATION, plugin-refresh, plugin-list-viewmodel, approximation-refresh]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - Terminal refresh statuses are typed PluginRefreshStatus values with canonical display text
    - Full-list completion publication is generation-gated with IsCurrent before notifying observers

key-files:
  created: []
  modified:
    - AutoQAC/Services/Plugin/PluginRefreshStatus.cs
    - AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs
    - AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs
    - AutoQAC.Tests/Services/PluginRefreshCoordinatorTests.cs
    - AutoQAC.Tests/ViewModels/PluginListViewModelTests.cs

key-decisions:
  - "Full-list approximation refresh now has its own FullRefreshCompleted terminal status instead of reusing selected-refresh completion text."
  - "The coordinator publishes FullRefreshCompleted only after successful AnalyzeTargetsAsync completion and only while the generation remains current."
  - "PluginListViewModel keeps the running set intentionally narrow: LoadingPlugins and AnalyzingSelected are running; terminal statuses clear the cancel affordance."

patterns-established:
  - "Add new PluginRefreshStatusKind terminal statuses with matching factory and ToDisplayText case."
  - "Regression-test both the service status stream and the ViewModel command state for refresh lifecycle gaps."

requirements-completed: [PERF-01, REF-02]

# Metrics
duration: 3 min
completed: 2026-04-30
---

# Phase 09 Plan 08: Full Refresh Terminal Status Summary

**Full-list approximation refreshes now publish a generation-safe FullRefreshCompleted status so PluginListViewModel clears the running flag and disables the cancel-refresh command after successful analysis**

## Performance

- **Duration:** 3 min
- **Started:** 2026-04-30T09:21:32Z
- **Completed:** 2026-04-30T09:24:45Z
- **Tasks:** 2
- **Files modified:** 5

## Accomplishments

- Added `PluginRefreshStatusKind.FullRefreshCompleted`, `PluginRefreshStatus.FullRefreshCompleted(int)`, and canonical full-refresh completion display text.
- Updated `PluginRefreshCoordinator.RefreshForGameAsync` to capture the `AnalyzeTargetsAsync` updated count and publish the terminal status only when `IsCurrent(generation, token)` still passes.
- Added coordinator regression coverage proving successful full-list refresh publishes the terminal status with a non-zero update count.
- Added ViewModel regression coverage proving `FullRefreshCompleted` clears `IsApproximationRefreshRunning` and disables `CancelApproximationRefreshCommand`.
- Documented the intentionally narrow running-status set in `PluginListViewModel` so future terminal statuses do not get added to the active set accidentally.

## Task Commits

Each task was committed atomically:

1. **Task 1 RED: Add failing full-refresh completion status test** - `a1a4c49` (test)
2. **Task 1 GREEN: Add and publish FullRefreshCompleted** - `b87caee` (feat)
3. **Task 2 regression: Cover running-state cleanup** - `0529029` (test)
4. **Task 2 documentation: Explain running-status transitions** - `f1baa67` (docs)

**Plan metadata:** pending final docs commit

## Files Created/Modified

- `AutoQAC/Services/Plugin/PluginRefreshStatus.cs` - Adds the full-list terminal status kind, factory, and display text.
- `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs` - Publishes `FullRefreshCompleted(updated)` after successful full-list analysis when the generation remains current.
- `AutoQAC/ViewModels/MainWindow/PluginListViewModel.cs` - Documents status-driven running flag transitions and terminal status cleanup semantics.
- `AutoQAC.Tests/Services/PluginRefreshCoordinatorTests.cs` - Adds the coordinator regression for terminal full-refresh status publication.
- `AutoQAC.Tests/ViewModels/PluginListViewModelTests.cs` - Adds the ViewModel regression for clearing running/cancel UI state on full-refresh completion.

## Decisions Made

- Full-list approximation refresh uses a distinct `FullRefreshCompleted` kind so selected-refresh and full-refresh completion text remain explicit and testable.
- The terminal publish stays in the success path, after `AnalyzeTargetsAsync`, and is not emitted for cancellation, unsupported approximation, empty rows, or the non-cancellation exception recovery path.
- `PluginListViewModel` continues treating only `LoadingPlugins` and `AnalyzingSelected` as active running states; all terminal statuses, including `FullRefreshCompleted`, clear the cancel affordance.

## Deviations from Plan

### Auto-fixed Issues

None - plan executed exactly as written.

---

**Total deviations:** 0 auto-fixed.
**Impact on plan:** No scope changes; implementation matched the gap-closure plan.

## Verification

- `dotnet test "AutoQAC.Tests/AutoQAC.Tests.csproj" --filter "FullyQualifiedName~PluginRefreshCoordinatorTests"` — PASS (9/9 tests).
- `dotnet test "AutoQAC.Tests/AutoQAC.Tests.csproj" --filter "FullyQualifiedName~PluginListViewModelTests"` — PASS (12/12 tests).
- `dotnet test "AutoQAC.Tests/AutoQAC.Tests.csproj" --filter "FullyQualifiedName~PluginListViewModelTests|FullyQualifiedName~PluginRefreshCoordinatorTests"` — PASS (21/21 tests).
- `dotnet build "AutoQACSharp.slnx"` — PASS (0 warnings, 0 errors) on final sequential run.
- `dotnet test "AutoQACSharp.slnx"` — PASS (`QueryPlugins.Tests` 61/61, `AutoQAC.Tests` 848/848).
- Source check: `PluginRefreshCoordinator.cs` contains exactly one `Publish(PluginRefreshStatus.FullRefreshCompleted(...))` call.

## Issues Encountered

- An intermediate parallel build/test invocation briefly produced file-lock warnings from `testhost`; final sequential `dotnet build` and all test commands passed without errors.

## User Setup Required

None - no external service configuration required.

## Known Stubs

None. Stub-pattern scan found only the existing user-facing `ApproximationUnavailable` display string (`"Approximation refresh is not available for this game."`), not placeholder or mock UI data.

## Threat Flags

None. The change only adds a typed in-process status across the existing coordinator-to-ViewModel observable boundary described in the plan threat model; no new network, auth, file-access, or schema surface was introduced.

## TDD Gate Compliance

- Task 1 followed RED/GREEN: `a1a4c49` failed because `PluginRefreshStatusKind.FullRefreshCompleted` did not exist, then `b87caee` made the coordinator tests pass.
- Task 2's regression test passed immediately because the existing ViewModel running-set logic already treated unknown terminal statuses as non-running once Task 1 introduced the new kind. This was expected by the plan text; the test was still committed before the documentation-only implementation commit to preserve behavior coverage.

## Next Phase Readiness

- VERIFICATION gap 2 can now be re-checked as closed: full-list approximation refresh publishes a terminal status and the cancel-refresh UI clears when that status reaches `PluginListViewModel`.
- Phase 09 gap-closure implementation is complete and ready for final phase verification.

## Self-Check: PASSED

- Verified all modified files listed in this summary exist on disk.
- Verified task commits `a1a4c49`, `b87caee`, `0529029`, and `f1baa67` exist in git history.
- Verified `09-08-SUMMARY.md` exists in the phase directory.

---
*Phase: 09-plugin-refresh-approximation-performance*
*Completed: 2026-04-30*
