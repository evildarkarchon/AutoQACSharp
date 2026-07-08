---
phase: 10-configuration-persistence-hardening
plan: 03
subsystem: configuration
tags: [configuration, persistence, filesystem-watcher, tdd, yaml]

requires:
  - phase: 10-01
    provides: UserConfiguration.Copy() deep-copy primitive
  - phase: 10-02
    provides: ConfigPersistenceCoordinator, ConfigPersistenceResult, IUserConfigFileStore
provides:
  - IConfigurationService facade delegates persistence methods and status streams to ConfigPersistenceCoordinator
  - ConfigWatcherService acts as FileSystemWatcher event source only for Changed/Created/Renamed/Deleted
  - Typed FlushPendingSavesAsync result and recoverable failure streams reach public configuration boundary
  - ConfigurationService YAML clone round-trip removed from user-config paths
affects: [configuration, cleaning-preflight, settings-viewmodel, phase-10]

tech-stack:
  added: []
  patterns: [coordinator-backed facade, event-source watcher, typed persistence results]

key-files:
  created:
    - AutoQAC/Services/Configuration/IConfigPersistenceCoordinator.cs
  modified:
    - AutoQAC/Services/Configuration/IConfigurationService.cs
    - AutoQAC/Services/Configuration/ConfigurationService.cs
    - AutoQAC/Services/Configuration/ConfigWatcherService.cs
    - AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs
    - AutoQAC/Services/Configuration/ConfigPersistenceOperation.cs
    - AutoQAC/Services/Configuration/UserConfigFileStore.cs
    - AutoQAC/Infrastructure/ServiceCollectionExtensions.cs
    - AutoQAC.Tests/Services/ConfigurationServiceTests.cs
    - AutoQAC.Tests/Services/ConfigWatcherServiceTests.cs

key-decisions:
  - "ConfigurationService now starts and observes ConfigPersistenceCoordinator, while preserving legacy test construction through a compatibility constructor."
  - "ConfigWatcherService no longer validates YAML, checks hashes, or defers cleaning-time changes; all such policy is coordinator-owned."
  - "Invalid user YAML on facade load is now surfaced through LastFailure instead of throwing from LoadUserConfigAsync."

patterns-established:
  - "Public configuration facade exposes typed persistence result/failure streams while keeping skip-list and game helpers unchanged."
  - "FileSystemWatcher services submit signal-only events and never own ordering policy."

requirements-completed: [REF-03, TEST-03, PERF-03]

duration: 8 min
completed: 2026-04-30
---

# Phase 10 Plan 03: Configuration Persistence Facade Wiring Summary

**Coordinator-backed configuration persistence facade with typed flush/failure results, signal-only watcher events, and YAML-free user-config cloning.**

## Performance

- **Duration:** 8 min
- **Started:** 2026-04-30T23:00:08Z
- **Completed:** 2026-04-30T23:08:04Z
- **Tasks:** 2
- **Files modified:** 10

## Accomplishments

- `IConfigurationService` now exposes `Task<ConfigPersistenceResult> FlushPendingSavesAsync`, `Failures`, `PersistenceResults`, and `LastFailure`.
- `ConfigurationService` delegates save/load/flush/reload/status to `ConfigPersistenceCoordinator` and no longer contains the YAML clone round-trip.
- `ConfigWatcherService` is now event-source-only for `Changed`, `Created`, `Renamed`, and `Deleted` signals.
- DI registers `IUserConfigFileStore`, `ConfigPersistenceCoordinator`, and `IConfigPersistenceCoordinator` before dependent services.
- Watcher tests were reduced to Phase 10 D-33 smoke coverage; deterministic race coverage remains in Plan 02 coordinator tests.

## Task Commits

Each task was committed atomically:

1. **Task 1: Adjust service + watcher tests to new contracts** - `8c3f80b` (test)
2. **Task 2: Wire service + watcher to coordinator; remove YAML clone** - `d3a6aa4` (feat)

**Plan metadata:** pending final docs commit

## Files Created/Modified

- `AutoQAC/Services/Configuration/IConfigPersistenceCoordinator.cs` - Internal seam used by the facade, watcher, DI, and NSubstitute tests.
- `AutoQAC/Services/Configuration/IConfigurationService.cs` - Public typed flush/failure/status surface.
- `AutoQAC/Services/Configuration/ConfigurationService.cs` - Coordinator-backed facade; skip-list/game helpers preserved.
- `AutoQAC/Services/Configuration/ConfigWatcherService.cs` - Signal-only watcher for all required FSW event kinds.
- `AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs` - Implements the new interface and acknowledges save-intent processing before returning to facade callers.
- `AutoQAC/Services/Configuration/ConfigPersistenceOperation.cs` - SaveIntent carries an async completion so optimistic facade reads see processed state.
- `AutoQAC/Services/Configuration/UserConfigFileStore.cs` - Reads raw YAML text so invalid YAML is classified by coordinator validation, not as a file read failure.
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` - Registers file store and coordinator singletons.
- `AutoQAC.Tests/Services/ConfigurationServiceTests.cs` - Typed flush/failure facade tests and adjusted invalid-YAML expectation.
- `AutoQAC.Tests/Services/ConfigWatcherServiceTests.cs` - Smoke-only watcher test with D-33 documentation.

## Removed / Rehomed Logic

### ConfigurationService fields and methods removed

- `_saveRequests`
- `_debounceSubscription`
- `_pendingConfig`
- `_lastKnownGoodConfig`
- `_lastWrittenHash`
- `_serializer`
- `SaveToDiskWithRetryAsync`
- `ComputeFileHash`
- `CloneConfig` YAML serialize/deserialize round-trip

### ConfigWatcherService fields and methods removed

- `_lastKnownExternalHash`
- `_hasDeferredChanges`
- `_yamlValidator`
- `_disposables`
- `HandleFileChangedAsync`
- `ApplyDeferredChangesAsync`
- `TryValidateYaml`
- `ComputeFileHash`
- Rx `Throttle(...)` / cleaning-end subscription pipeline

### Deleted watcher test cases

These timing/policy tests were removed from `ConfigWatcherServiceTests` because Plan 02 `ConfigPersistenceCoordinatorTests` owns deterministic coverage for those behaviors:

- `StartWatching_ShouldReload_WhenExternalValidChangeDetected`
- `StartWatching_ShouldSkipReload_WhenHashMatchesAppWrite`
- `StartWatching_ShouldDeferReloadDuringCleaning_AndApplyAfterCleaningEnds`
- `StartWatching_ShouldContinueWatching_WhenReloadFromDiskThrows_OnSubsequentValidChange`
- `StartWatching_ShouldApplyDeferredChangesOnlyOnce_WhenMultipleNonCleaningStateTransitionsOccur`

## Decisions Made

- Kept a legacy `ConfigurationService(ILoggingService, string?)` constructor for existing direct tests while DI uses the coordinator-injected constructor.
- `SaveUserConfigAsync` now awaits coordinator save-intent processing before returning, preserving legacy immediate-read behavior without forcing a disk flush on every save.
- `UserConfigFileStore.ReadAsync` returns raw YAML; the coordinator classifies invalid YAML as `InvalidExternalYaml` instead of `ReadFailed`.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Preserved immediate facade reads after SaveUserConfigAsync**
- **Found during:** Task 2 (GREEN verification)
- **Issue:** Existing service and skip-list tests read immediately after `SaveUserConfigAsync`; the queued save intent had not always been processed yet.
- **Fix:** Added a completion to `SaveIntent` and made coordinator `SaveUserConfigAsync` await processing before returning.
- **Files modified:** `ConfigPersistenceOperation.cs`, `ConfigPersistenceCoordinator.cs`
- **Verification:** `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~Configuration --nologo`
- **Committed in:** `d3a6aa4`

**2. [Rule 1 - Bug] Avoided missing-file default creation overwriting pending optimistic saves**
- **Found during:** Task 2 (GREEN verification)
- **Issue:** `LoadUserConfigAsync` treated a missing settings file as first-run default even when an in-memory save was pending, hiding the just-saved optimistic state.
- **Fix:** Added facade tracking for loaded/pending user-config state and returned the coordinator snapshot when a save is pending.
- **Files modified:** `ConfigurationService.cs`
- **Verification:** Configuration filter and full solution test suite passed.
- **Committed in:** `d3a6aa4`

**3. [Rule 1 - Bug] Classified invalid YAML at the coordinator validation boundary**
- **Found during:** Task 2 (GREEN verification)
- **Issue:** `UserConfigFileStore.ReadAsync` deserialized YAML, causing invalid YAML to surface as `ReadFailed` instead of the expected `InvalidExternalYaml` validation failure.
- **Fix:** Removed read-time deserialization from the file store; coordinator validation now owns candidate parsing/classification.
- **Files modified:** `UserConfigFileStore.cs`, `ConfigurationServiceTests.cs`
- **Verification:** `LoadUserConfigAsync_ShouldKeepDefaultAndExposeFailure_WhenYamlIsCorrupted` passes.
- **Committed in:** `d3a6aa4`

---

**Total deviations:** 3 auto-fixed (3 Rule 1 bugs)
**Impact on plan:** All fixes preserved the plan boundary and existing public behavior while making the coordinator-backed facade test-green.

## Issues Encountered

- Initial GREEN run exposed stale/pending facade read regressions; fixed via save-intent completion and pending-state handling.
- Existing corrupted YAML test expected an exception; adjusted to Phase 10's recoverable failure contract.

## Known Stubs

None. Stub-pattern scan only found legitimate null checks/default optional parameters, not placeholder UI or mock data paths.

## Threat Flags

None. This plan exposes typed failure/status surfaces already covered by the plan threat model and does not add network, auth, or new file trust boundaries beyond the existing settings YAML flow.

## Verification

- `dotnet build AutoQACSharp.slnx --nologo` — passed
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~Configuration --nologo` — passed (109 tests)
- `dotnet test AutoQACSharp.slnx --nologo` — passed (AutoQAC.Tests 893, QueryPlugins.Tests 61)
- Source checks verified `_coordinator` delegation, watcher event-source handlers, no user-config YAML clone calls, and DI registrations.
- `CleaningPreflight.PrepareAsync` still compiles by awaiting and discarding the typed flush result; Plan 04 surfaces that result semantically.

## TDD Gate Compliance

- RED commit present: `8c3f80b test(10-03): adjust service tests to typed flush + smoke-only watcher tests`
- GREEN commit present after RED: `d3a6aa4 feat(10-03): wire IConfigurationService and ConfigWatcherService through ConfigPersistenceCoordinator`
- REFACTOR commit: not needed.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

Plan 04 can consume `ConfigPersistenceResult` from `FlushPendingSavesAsync` in cleaning preflight and block xEdit launch on save failure. Plan 05 can map `Failures`/`PersistenceResults` to ViewModel status text.

## Self-Check: PASSED

- Created file exists: `AutoQAC/Services/Configuration/IConfigPersistenceCoordinator.cs`
- Summary file exists: `.planning/phases/10-configuration-persistence-hardening/10-03-SUMMARY.md`
- Task commits found: `8c3f80b`, `d3a6aa4`
- Final verification passed: `dotnet test AutoQACSharp.slnx --nologo`

---
*Phase: 10-configuration-persistence-hardening*
*Completed: 2026-04-30*
