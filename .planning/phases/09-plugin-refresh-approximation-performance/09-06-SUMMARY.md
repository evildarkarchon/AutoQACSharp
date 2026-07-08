---
phase: 09-plugin-refresh-approximation-performance
plan: 06
subsystem: plugin-refresh
tags: [plugin-refresh, approximation, regression-tests, state-preservation, verification]

# Dependency graph
requires:
  - phase: 09-05
    provides: Refresh lifecycle cancellation and full Phase 09 integration verification
provides:
  - Non-destructive selected approximation refresh state publication
  - Regression coverage for preserving non-selected plugin rows and approximation values
  - Source and automated verification that selected refresh no longer replaces the visible row list
affects: [plugin-refresh, selected-refresh, phase-09-verification, phase-10]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - In-place selected target row updates through IStateService.UpdateState
    - Single-row approximation completion merges through IStateService.MergePluginApproximation

key-files:
  created: []
  modified:
    - AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs
    - AutoQAC.Tests/Services/PluginRefreshCoordinatorTests.cs

key-decisions:
  - "Selected approximation refresh preserves the existing PluginsToClean row set and updates only selected target rows to Pending before analysis results merge back."
  - "The empty-current-list selected refresh fallback remains for compatibility, but non-empty row sets use UpdateState instead of SetPluginsToClean(pendingRows)."

patterns-established:
  - "Selected-refresh gap closures should prove both source semantics and row/value preservation through coordinator-level regression tests."

requirements-completed: [REF-02, PERF-01]

# Metrics
duration: 3 min
completed: 2026-04-30
---

# Phase 09 Plan 06: Selected Refresh Row Preservation Summary

**Selected approximation refresh now marks only targeted rows pending and merges completed results without dropping non-targeted visible plugin rows or prior approximation values.**

## Performance

- **Duration:** 3 min
- **Started:** 2026-04-30T08:35:15Z
- **Completed:** 2026-04-30T08:37:59Z
- **Tasks:** 2
- **Files modified:** 2 implementation/test files plus this summary and planning metadata

## Accomplishments

- Confirmed `RefreshSelectedApproximationsAsync_ShouldPreserveNonSelectedPluginRows` locks the exact regression: two preloaded rows remain visible, the selected row becomes available, and `Unselected.esp` keeps `PluginIssueApproximation.Available(9, 8, 7)`.
- Confirmed `PluginRefreshCoordinator.RefreshSelectedApproximationsAsync` maps over `s.PluginsToClean`, sets only target rows to `PluginIssueApproximation.Pending`, and publishes `PluginsToClean = rows.AsReadOnly()` instead of replacing non-empty lists with selected-only pending rows.
- Verified the selected-refresh source guard, targeted coordinator/plugin-list tests, and the full solution test suite all pass.

## Task Commits

Each task was accounted for atomically:

1. **Task 1: Lock non-destructive selected refresh behavior** - `b48b61e` (fix; pre-existing code-review fix contained the regression test and implementation before this gap-closure plan was created)
2. **Task 2: Verify the destructive selected-refresh gap is closed** - verification-only; no source changes were needed after Task 1 was already present

**Plan metadata:** pending final docs commit

## Files Created/Modified

- `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs` - Selected refresh now uses `UpdateState` to mark matching rows pending while preserving non-targeted rows, with an empty-list compatibility fallback.
- `AutoQAC.Tests/Services/PluginRefreshCoordinatorTests.cs` - Adds the non-selected row preservation regression with the prior `Available(9, 8, 7)` approximation value.
- `.planning/phases/09-plugin-refresh-approximation-performance/09-06-SUMMARY.md` - Records the gap-closure outcome and verification evidence.

## Decisions Made

- Selected refresh is a targeted row update, not a plugin-list replacement; full-list replacement remains reserved for full game/load-order refresh paths.
- Completion callbacks remain on `MergePluginApproximation(approximation)` so selected target results update one matching row at a time and preserve non-targeted values.

## Deviations from Plan

### Auto-fixed Issues

None during this executor run. The planned code/test gap had already been auto-fixed by the prior code-review fix commit `b48b61e` before `09-06-PLAN.md` was created.

---

**Total deviations:** 0 auto-fixed during this executor run.
**Impact on plan:** No additional scope was added; this run verified and documented the already-present gap closure.

## Verification

- Acceptance source scan: `RefreshSelectedApproximationsAsync_ShouldPreserveNonSelectedPluginRows` — PASS.
- Acceptance source scan: `PluginIssueApproximation.Available(9, 8, 7)` — PASS.
- Acceptance source scan: `PluginsToClean.Should().HaveCount(2` — PASS.
- Acceptance source scan: `s.PluginsToClean.Select(plugin =>`, `plugin with { Approximation = PluginIssueApproximation.Pending }`, and `PluginsToClean = rows.AsReadOnly()` — PASS.
- Source guard: filtered search for `_stateService.SetPluginsToClean(pendingRows)` — PASS (no matches).
- Targeted coordinator tests: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~PluginRefreshCoordinator"` — PASS (6 tests).
- Targeted coordinator/plugin-list tests: `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~PluginRefreshCoordinator|FullyQualifiedName~PluginListViewModel"` — PASS (19 tests).
- Full solution: `dotnet test AutoQACSharp.slnx` — PASS (QueryPlugins.Tests 61 tests; AutoQAC.Tests 844 tests).

## Issues Encountered

- `gsd-sdk query verify-work 9` is not registered in the local SDK/legacy bridge, so the plan's slash-command re-verification step could not be invoked from the executor environment. Automated source guards and test suites passed and are recorded above.

## User Setup Required

None - no external service configuration required.

## Known Stubs

None. Stub-pattern scans only matched optional nullable/default parameters in the coordinator and test doubles; no UI data placeholder or incomplete selected-refresh behavior remains.

## Threat Flags

None. No new network endpoints, auth paths, file access patterns, or schema/trust-boundary surfaces were introduced.

## TDD Gate Compliance

- RED gate: not present for `09-06` because the regression test and production fix were already committed together in `b48b61e` before this plan was created.
- GREEN gate: not present for `09-06` for the same reason; the current source and tests satisfy the planned behavior.
- Impact: documented as a gate-compliance warning only. The regression test now fails against the prior destructive `SetPluginsToClean(pendingRows)` behavior and passes with the in-place update implementation.

## Next Phase Readiness

- Phase 09's selected-refresh verification gaps are closed by source semantics and automated regression coverage.
- Ready to mark Phase 09 complete and proceed to Phase 10 configuration persistence hardening after any desired human UI smoke test.

## Self-Check: PASSED

- Verified `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs`, `AutoQAC.Tests/Services/PluginRefreshCoordinatorTests.cs`, and this summary exist on disk.
- Verified commit `b48b61e` exists in git history and contains the selected-refresh row preservation implementation/test changes.
- Verified the selected-refresh source guard, targeted tests, and full solution tests exit 0.

---
*Phase: 09-plugin-refresh-approximation-performance*
*Completed: 2026-04-30*
