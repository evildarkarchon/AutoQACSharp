---
phase: 09-plugin-refresh-approximation-performance
plan: 02
subsystem: plugin-refresh
tags: [plugin-refresh, state-service, tdd, cancellation, approximation]

# Dependency graph
requires:
  - phase: 09-01
    provides: QueryPlugins cancellation-aware exact approximation foundation
provides:
  - Refresh coordinator API contracts for Phase 09 implementation plans
  - Typed refresh request, target, status, cancel, and capability policy DTOs
  - Wave 0 RED coordinator behavior tests for row-first, selected-target, supersede, and cancel semantics
  - StateService regression test for selected-target merge preservation
affects: [09-03, 09-04, plugin-refresh, state-service, configuration-viewmodel]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - Service-owned refresh generation contract with typed status events
    - Selected target snapshot contract for approximation refresh
    - Single-row state merge for targeted approximation updates

key-files:
  created:
    - AutoQAC/Services/Plugin/IPluginRefreshCoordinator.cs
    - AutoQAC/Services/Plugin/PluginRefreshRequest.cs
    - AutoQAC/Services/Plugin/PluginRefreshStatus.cs
    - AutoQAC/Services/Plugin/IPluginRefreshCapabilityPolicy.cs
    - AutoQAC.Tests/Services/PluginRefreshCoordinatorTests.cs
  modified:
    - AutoQAC.Tests/Services/StateServiceTests.cs

key-decisions:
  - "Phase 09 refresh status uses typed PluginRefreshStatus values with canonical display text helpers until ViewModels map them in later plans."
  - "Wave 0 coordinator tests intentionally reference the not-yet-implemented PluginRefreshCoordinator so Plan 09-03 receives executable RED behavior requirements."
  - "Targeted approximation refresh is pinned to StateService.MergePluginApproximation, not MergePluginApproximations, to preserve non-targeted row values."

patterns-established:
  - "Public refresh contracts carry XML documentation because downstream executors will implement against them."
  - "Selected refresh target sets are IReadOnlyList snapshots passed into the coordinator instead of mutable ViewModel selection state."

requirements-completed: [REF-02, PERF-01]

# Metrics
duration: 2 min
completed: 2026-04-30
---

# Phase 09 Plan 02: Refresh Coordinator Contract and Wave 0 Tests Summary

**Typed plugin-refresh coordinator contracts with RED behavior tests for row-first targeted approximation refresh and state merge preservation**

## Performance

- **Duration:** 2 min
- **Started:** 2026-04-30T07:36:50Z
- **Completed:** 2026-04-30T07:39:12Z
- **Tasks:** 2
- **Files modified:** 6

## Accomplishments

- Added executor-facing refresh coordinator contracts, request/target DTOs, status/cancel types, and refresh-scoped capability policy.
- Added Wave 0 coordinator tests for row-first publication, selected snapshot targeting, empty-target status, stale generation rejection, and manual cancellation behavior.
- Added StateService regression coverage proving a single-row approximation merge updates only the target and preserves existing non-targeted Available counts.

## Task Commits

Each task was committed atomically:

1. **Task 1: Create refresh contracts and coordinator behavior tests** - `873065b` (test)
2. **Task 2: Pin targeted merge preservation in StateService tests** - `9ae0d35` (test)

**Plan metadata:** pending final docs commit

_Note: This is a Wave 0 TDD contract plan; the coordinator implementation and GREEN behavior are intentionally deferred to Plan 09-03._

## Files Created/Modified

- `AutoQAC/Services/Plugin/IPluginRefreshCoordinator.cs` - Defines refresh, selected-refresh, cancel, and status observable contract.
- `AutoQAC/Services/Plugin/PluginRefreshRequest.cs` - Defines immutable refresh request and selected target snapshot records.
- `AutoQAC/Services/Plugin/PluginRefreshStatus.cs` - Defines typed status/cancel outcomes and canonical display text mapping.
- `AutoQAC/Services/Plugin/IPluginRefreshCapabilityPolicy.cs` - Defines refresh-scoped plugin loading and approximation support policy.
- `AutoQAC.Tests/Services/PluginRefreshCoordinatorTests.cs` - Adds RED tests for downstream coordinator implementation.
- `AutoQAC.Tests/Services/StateServiceTests.cs` - Adds targeted single-row merge preservation regression test.

## Decisions Made

- Phase 09 refresh status uses typed `PluginRefreshStatus` values with canonical display text helpers until ViewModels map them in later plans.
- Wave 0 coordinator tests intentionally reference the not-yet-implemented `PluginRefreshCoordinator` so Plan 09-03 receives executable RED behavior requirements.
- Targeted approximation refresh is pinned to `StateService.MergePluginApproximation`, not `MergePluginApproximations`, to preserve non-targeted row values.

## Deviations from Plan

None - plan executed as a Wave 0 contract/TDD plan with intentionally RED coordinator tests.

## TDD Gate Compliance

- RED commits present: `873065b`, `9ae0d35`.
- GREEN commit absent: expected for this Wave 0 plan because production `PluginRefreshCoordinator` implementation is explicitly assigned to Plan 09-03.
- Verification currently fails at compile time with `CS0246: PluginRefreshCoordinator could not be found`, which is the intended RED state for the coordinator behavior tests.

## Verification

- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~PluginRefreshCoordinator"` — expected RED failure: `CS0246` for missing `PluginRefreshCoordinator`.
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~StateService"` — blocked by the same project compile failure from intentional coordinator RED tests.
- Acceptance criteria were checked with source scans for required contract members, status strings, and all five coordinator test names.

## Issues Encountered

- StateService filtered test execution cannot compile independently while the Wave 0 coordinator RED tests intentionally reference the missing production coordinator. This is expected until Plan 09-03 implements `PluginRefreshCoordinator`.

## User Setup Required

None - no external service configuration required.

## Known Stubs

None. The missing production `PluginRefreshCoordinator` is not a stub in this plan; it is the planned RED implementation target for Plan 09-03.

## Next Phase Readiness

- Plan 09-03 can now implement `PluginRefreshCoordinator` against stable contracts and RED behavior tests.
- Plan 09-03 should make the coordinator test filter compile and pass, then re-run the StateService filter to confirm the targeted merge preservation test remains green.

## Self-Check: PASSED

- Verified all created/modified files listed in this summary exist on disk.
- Verified task commits `873065b` and `9ae0d35` exist in git history.

---
*Phase: 09-plugin-refresh-approximation-performance*
*Completed: 2026-04-30*
