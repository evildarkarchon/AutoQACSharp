---
phase: 09-plugin-refresh-approximation-performance
plan: 03
subsystem: plugin-refresh
tags: [plugin-refresh, mvvm, dependency-injection, cancellation, approximation]

# Dependency graph
requires:
  - phase: 09-02
    provides: Refresh coordinator contracts and RED behavior tests
provides:
  - DI-registered PluginRefreshCoordinator implementation
  - Refresh-scoped PluginRefreshCapabilityPolicy implementation
  - ConfigurationViewModel refresh workflow delegation to the coordinator
affects: [09-04, 09-05, plugin-refresh, configuration-viewmodel, plugin-list]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - Service-owned refresh generation and CTS replacement with Interlocked guards
    - Typed refresh status mapping at the ViewModel boundary
    - Targeted row approximation updates through StateService.MergePluginApproximation

key-files:
  created:
    - AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs
    - AutoQAC/Services/Plugin/PluginRefreshCapabilityPolicy.cs
  modified:
    - AutoQAC.Tests/Services/PluginRefreshCoordinatorTests.cs
    - AutoQAC/Infrastructure/ServiceCollectionExtensions.cs
    - AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs
    - AutoQAC/ViewModels/MainWindowViewModel.cs
    - AutoQAC.Tests/Integration/DependencyInjectionTests.cs

key-decisions:
  - "Plugin refresh generation, cancellation, plugin loading, skip-list application, and approximation publication now live in PluginRefreshCoordinator instead of ConfigurationViewModel."
  - "PluginRefreshCapabilityPolicy remains refresh-scoped and only enables issue approximation for Skyrim and Fallout 4 families."
  - "ConfigurationViewModel maps typed PluginRefreshStatus values into UI strings and cancels the coordinator on disposal."

patterns-established:
  - "Use Interlocked.Exchange for refresh replacement and CompareExchange before active CTS disposal to avoid disposing a newer generation."
  - "Use MergePluginApproximation for targeted refresh callbacks so non-targeted rows keep existing preview values."

requirements-completed: [REF-02, PERF-01]

# Metrics
duration: 6 min
completed: 2026-04-30
---

# Phase 09 Plan 03: Plugin Refresh Coordinator Implementation Summary

**Service-owned plugin refresh workflow with generation-safe cancellation, targeted approximation merges, and ViewModel status delegation**

## Performance

- **Duration:** 6 min
- **Started:** 2026-04-30T07:42:01Z
- **Completed:** 2026-04-30T07:47:52Z
- **Tasks:** 2
- **Files modified:** 7

## Accomplishments

- Added `PluginRefreshCoordinator` with one active refresh generation, CTS cancel/replace semantics, row-first publication, and per-row approximation merges.
- Added `PluginRefreshCapabilityPolicy` for refresh-scoped loading and approximation support decisions.
- Registered coordinator services in DI and changed `ConfigurationViewModel` into a refresh shell that delegates workflow work and maps typed statuses.

## Task Commits

Each task was committed atomically:

1. **Task 1: Implement coordinator and capability policy** - `12c42eb` (feat)
2. **Task 2: Register coordinator and make ConfigurationViewModel a refresh shell** - `b2560c4` (refactor)

**Plan metadata:** pending final docs commit

_Note: Plan 09-02 supplied the RED coordinator behavior tests; this plan produced the GREEN implementation commits._

## Files Created/Modified

- `AutoQAC/Services/Plugin/PluginRefreshCoordinator.cs` - Coordinates game/load-order refresh, active generation cancellation, typed status, and targeted approximation publication.
- `AutoQAC/Services/Plugin/PluginRefreshCapabilityPolicy.cs` - Defines refresh-scoped plugin loading and approximation game support.
- `AutoQAC.Tests/Services/PluginRefreshCoordinatorTests.cs` - Replaced placeholder substitutes with deterministic test doubles for GREEN coordinator tests.
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` - Registers coordinator and capability policy singletons.
- `AutoQAC/ViewModels/MainWindow/ConfigurationViewModel.cs` - Delegates refresh workflow and status mapping to the coordinator.
- `AutoQAC/ViewModels/MainWindowViewModel.cs` - Passes optional coordinator dependency into the configuration child ViewModel.
- `AutoQAC.Tests/Integration/DependencyInjectionTests.cs` - Asserts refresh coordinator services resolve through DI.

## Decisions Made

- Plugin refresh generation, cancellation, plugin loading, skip-list application, and approximation publication now live in `PluginRefreshCoordinator` instead of `ConfigurationViewModel`.
- `PluginRefreshCapabilityPolicy` remains refresh-scoped and only enables issue approximation for Skyrim and Fallout 4 families.
- `ConfigurationViewModel` maps typed `PluginRefreshStatus` values into UI strings and cancels the coordinator on disposal.

## Deviations from Plan

None - plan executed exactly as written.

## Verification

- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~PluginRefreshCoordinator"` — PASS (5 tests).
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~PluginRefreshCoordinator|FullyQualifiedName~DependencyInjection"` — PASS (6 tests).
- Acceptance criteria source scans confirmed required `Interlocked` operations, `SetPluginsToClean` before approximation analysis, targeted `MergePluginApproximation`, DI registrations, and ViewModel status mappings.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Known Stubs

None. Optional `null` defaults in constructor signatures are compatibility/default-injection parameters, not UI data stubs.

## Next Phase Readiness

- Ready for 09-04 to add selected approximation refresh and cancel controls near the plugin list.
- Coordinator exposes `RefreshSelectedApproximationsAsync`, typed status, and `CancelActiveRefresh` for the upcoming UI commands.

## Self-Check: PASSED

- Verified created files exist on disk: `PluginRefreshCoordinator.cs`, `PluginRefreshCapabilityPolicy.cs`, and `09-03-SUMMARY.md`.
- Verified task commits `12c42eb` and `b2560c4` exist in git history.

---
*Phase: 09-plugin-refresh-approximation-performance*
*Completed: 2026-04-30*
