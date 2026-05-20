---
id: S11
parent: M001
milestone: M001
provides:
  - Typed Watcher/ReadFailed failure and failed result publication for watcher hash-read exceptions
  - Deterministic regression coverage for a transient watcher hash failure followed by a later valid reload
  - Closure of the remaining Phase 10 verification gap from 10-VERIFICATION.md
  - Explicit IConfigurationService DI factory using the registered shared IConfigPersistenceCoordinator
  - Integration regression proving facade, watcher, and service provider share one coordinator instance
  - Watcher error data-flow proof through IConfigurationService.PersistenceResults
  - Model-owned UserConfiguration.Copy() deep-copy primitive
  - Nested configuration Copy() methods for load order, MO2, xEdit, settings, retention, and backup models
  - Behavior tests proving deep-copy independence, null normalization, YAML alias preservation, and YAML round-trip parity
  - Single-reader ConfigPersistenceCoordinator for serialized settings save/flush/reload/watcher ordering
  - Typed ConfigPersistenceResult and ConfigPersistenceFailure payloads for safe failure branching
  - IUserConfigFileStore seam and UserConfigFileStore temp-file Replace/Move persistence implementation
  - Deterministic coordinator race-matrix tests and production file-store tests
  - IConfigurationService facade delegates persistence methods and status streams to ConfigPersistenceCoordinator
  - ConfigWatcherService acts as FileSystemWatcher event source only for Changed/Created/Renamed/Deleted
  - Typed FlushPendingSavesAsync result and recoverable failure streams reach public configuration boundary
  - ConfigurationService YAML clone round-trip removed from user-config paths
  - Typed ConfigPersistenceFailureException for aborting required workflows on persistence failures
  - CleaningPreflight guard that blocks cleaning on failed or rejected pre-cleaning flush results
  - Focused tests proving flush failure aborts before downstream preflight/process-launch-adjacent collaborators
  - Settings dialog persistence failure banner bound to typed IConfigurationService failure/result streams
  - Text-only user-facing mappings for save, flush, reload, missing-file, and invalid-YAML persistence failures
  - Clear-on-success behavior for successful saves, flush/no-op results, and accepted external reloads
  - DI launch fix for ConfigWatcherService resolving through IConfigPersistenceCoordinator
  - Safe observer publication helpers for configuration accepted, persistence result, and failure streams
  - Explicit reload guard that flushes pending app saves before disk reads
  - Regression tests covering observer exceptions and reload-vs-pending-save races
  - ConfigurationService facade flags synchronized under _stateLock
  - ReloadFromDiskAsync preserves pending-save state after failed reload results
  - FlushPendingSavesAsync honors facade pending-save state before invoking coordinator flushes
  - Explicit reload failed/rejected prerequisite flush short-circuit
  - Regression coverage for pending app-save write failure with readable old disk content
  - Verification closure for the remaining Phase 10 explicit reload masking gap
  - ConfigurationService forced flush always awaits the coordinator queue barrier
  - Regression coverage proving no-pending facade flush returns the coordinator barrier result
  - Gap closure for the Phase 10 CR-01 facade flush bypass verification blocker
requires: []
affects: []
key_files: []
key_decisions:
  - Watcher hash-read exceptions are handled inside ApplyWatcherAsync as typed Watcher/ReadFailed outcomes so transient external writer locks do not become log-only dropped operations.
  - The hash-read race is covered through FakeUserConfigFileStore.HashFailure instead of FileSystemWatcher timing, OS locks, or production debounce waits.
  - Production DI constructs IConfigurationService with the registered shared IConfigPersistenceCoordinator instead of relying on public constructor selection.
  - The DI regression asserts both coordinator identity and watcher-originated result flow through the IConfigurationService facade.
  - Phase 10 Plan 01 implements model-owned manual Copy() methods and leaves ConfigurationService.CloneConfig unchanged for Plan 03.
  - Behavior parity with the existing YAML round-trip clone is proven through xUnit tests instead of source-regex clone guards.
  - AutoQAC.csproj does not enable implicit usings, so UserConfiguration.cs explicitly imports System and System.Linq for StringComparer and LINQ copy helpers.
  - ConfigPersistenceCoordinator uses a single-reader Channel<ConfigPersistenceOperation> with barrier TCS completions for deterministic flush/reload results.
  - InternalsVisibleTo was already available for AutoQAC.Tests, so no csproj change was needed for test access; internal test-only delegates were used to inject Replace/Move failures.
  - B16/B17 atomic write sequencing is covered on the production UserConfigFileStore rather than simulated in the fake.
  - ConfigurationService now starts and observes ConfigPersistenceCoordinator, while preserving legacy test construction through a compatibility constructor.
  - ConfigWatcherService no longer validates YAML, checks hashes, or defers cleaning-time changes; all such policy is coordinator-owned.
  - Invalid user YAML on facade load is now surfaced through LastFailure instead of throwing from LoadUserConfigAsync.
  - ConfigPersistenceFailureException derives from InvalidOperationException so existing cleaning catch sites remain compatible while preserving typed failure payloads.
  - CleaningPreflight treats Failed and defensive Rejected flush results as hard blockers before validation, game detection, skip-list loading, MO2 validation, or cleaning service calls.
  - Legacy orchestrator/process test substitutes default FlushPendingSavesAsync to NoOp so tests model the production no-pending-save happy path explicitly.
  - Settings persistence failures surface as a concise SettingsWindow text banner instead of modal dialogs or retry controls.
  - Explicit Settings saves close only after FlushPendingSavesAsync reports Success or NoOp, preventing late async save failures from appearing after the dialog closes.
  - ConfigWatcherService depends on IConfigPersistenceCoordinator so DI resolves the watcher against the registered coordinator abstraction.
  - ConfigPersistenceCoordinator now treats observer callbacks as untrusted and catches/logs observer exceptions before completing caller barriers.
  - Explicit reload preserves pending app saves by flushing them before reading disk, making the flushed values the reload source of truth.
  - ConfigurationService now treats _stateLock as the single synchronization boundary for facade pending/loaded flags.
  - Facade FlushPendingSavesAsync returns a typed NoOp without calling the coordinator when no app-initiated save is pending.
  - Explicit reload returns a failed or rejected pending-save flush result directly instead of reading stale disk content.
  - The regression test proves no disk Read occurs after the failed prerequisite Write in the explicit reload path.
  - ConfigurationService.FlushPendingSavesAsync always delegates to IConfigPersistenceCoordinator.FlushPendingSavesAsync so forced flushes drain queued watcher/reload work even with no facade pending app save.
  - Successful reload tests now assert that later forced flushes remain coordinator barriers instead of expecting a facade-local NoOp.
patterns_established:
  - ComputeHashAsync failures in watcher handling publish both ConfigPersistenceFailure and ConfigPersistenceResult before returning from the operation handler.
  - A later valid watcher signal after a transient hash failure must still apply external content through the same coordinator loop.
  - When a service has compatibility constructors, DI registrations should use explicit factories for required shared collaborators.
  - DI smoke tests should verify critical shared singleton identity and observable data flow, not only service resolution.
  - Copy methods normalize null nested config objects and collections to safe defaults before copying.
  - SkipLists are deep-copied at both dictionary and nested List<string> levels.
  - All in-memory coordinator snapshots use UserConfiguration.Copy(); YamlDotNet remains limited to disk read/write and reload validation.
  - Recoverable persistence failures use safe category summaries and never expose exception instances or stack traces.
  - Public configuration facade exposes typed persistence result/failure streams while keeping skip-list and game helpers unchanged.
  - FileSystemWatcher services submit signal-only events and never own ordering policy.
  - Preflight persistence barriers must inspect ConfigPersistenceResult before continuing to process-launch-adjacent workflow steps.
  - Persistence failure exceptions expose safe summary text only and keep raw exception details out of user-facing messages.
  - ViewModel observer streams should use CallbackObserver<T> and IUiDispatcher.Post before mutating observable properties.
  - Recoverable config persistence UI copy uses fixed safe strings for known operation/kind pairs and reserves SafeSummary only for fall-through categories.
  - Settings dialog persistence banners are cleared by successful persistence barriers and accepted reload notifications.
  - SafePublish* helpers are the only places where coordinator subjects call OnNext.
  - Explicit reloads use the same pending-save durability barrier as forced flush paths before accepting disk content.
  - Read multi-flag facade bookkeeping through snapshot helpers instead of direct async-method field access.
  - Clear pending-save facade state only after accepted coordinator success semantics, not after failed reload attempts.
  - When a reload depends on an app-save durability barrier, barrier failure is the reload result and terminates the operation.
  - Facade pending-save state controls when to clear local bookkeeping, not whether a forced flush reaches the coordinator queue.
  - No-pending flush regression asserts BeSameAs(coordinatorResult) and coordinator Received(1) to prevent synthetic result reintroduction.
observability_surfaces: []
drill_down_paths: []
duration: 12 min
verification_result: passed
completed_at: 2026-05-01
blocker_discovered: false
---
# S11: Configuration Persistence Hardening

**# Phase 10 Plan 10: Watcher Hash-Read Failure Gap Closure Summary**

## What Happened

# Phase 10 Plan 10: Watcher Hash-Read Failure Gap Closure Summary

**Watcher hash-read file-lock races now surface as typed Watcher/ReadFailed failures/results while preserving the coordinator loop for later valid watcher reloads.**

## Performance

- **Duration:** 8 min
- **Started:** 2026-05-01T02:20:00Z
- **Completed:** 2026-05-01T02:28:00Z
- **Tasks:** 3
- **Files modified:** 4

## Accomplishments

- Confirmed `FakeUserConfigFileStore.HashFailure` can deterministically throw from `ComputeHashAsync` for coordinator race coverage.
- Confirmed `Watcher_HashFailure_EmitsReadFailedAndProcessesLaterReload` asserts both the typed `Watcher/ReadFailed` failure/result and a later successful reload to timeout `44`.
- Confirmed `ApplyWatcherAsync` catches non-cancellation hash exceptions, logs the hash-specific failure, publishes a typed failed watcher result, and returns without reaching the top-level log-only operation catch.
- Verified the focused regression, all `ConfigPersistenceCoordinatorTests`, and the full solution test suite.

## Task Commits

Each task's code outcome already existed in the repository before this executor started:

1. **Task 1: RED — prove watcher hash failures are typed and non-fatal** - `5f5b9d2` (fix; included the regression test and fake-store seam)
2. **Task 2: GREEN — publish typed watcher ReadFailed results for hash exceptions** - `5f5b9d2` (fix; included the coordinator implementation)
3. **Task 3: Verify Phase 10 gap closure and summarize** - this summary and metadata commit

**Plan metadata:** pending final docs commit

_Note: The hash-failure code and test were present in commit `5f5b9d2` before Plan 10-10 execution began, so this executor verified and documented the already-applied gap closure rather than creating new code commits._

## Files Created/Modified

- `AutoQAC.Tests/Services/Configuration/Fakes/FakeUserConfigFileStore.cs` - Provides `HashFailure` injection and throws it from `ComputeHashAsync` after logging the hash call.
- `AutoQAC.Tests/Services/Configuration/ConfigPersistenceCoordinatorTests.cs` - Contains `Watcher_HashFailure_EmitsReadFailedAndProcessesLaterReload` and result/failure assertions for `Watcher/ReadFailed`.
- `AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs` - Converts watcher hash exceptions into typed `ReadFailed` watcher failures/results.
- `.planning/phases/10-configuration-persistence-hardening/10-10-SUMMARY.md` - Records verification and gap closure.

## Decisions Made

- Watcher hash computation is a read of untrusted live filesystem state, so `IOException`/access races belong in the same safe typed read-failure surface as reload read failures.
- The regression stays deterministic by injecting `HashFailure` through the existing file-store seam and by pumping the coordinator queue through `FlushPendingSavesAsync`.

## Deviations from Plan

### Auto-fixed Issues

None during this executor run. The required implementation and regression were already present in prior commit `5f5b9d2` when execution started.

---

**Total deviations:** 0 auto-fixed.
**Impact on plan:** No new scope was added; execution focused on verification and documentation of the pre-applied gap closure.

## Issues Encountered

- The plan was a TDD plan, but the RED and GREEN changes already existed before this executor started. The focused test therefore passed immediately during this run, so this executor could not reproduce the RED failure without rewriting existing history.

## Known Stubs

None. Stub-pattern scans found no `TODO`, `FIXME`, placeholder, `coming soon`, or `not available` text in the touched coordinator/fake/test files.

## Threat Flags

None. The plan mitigated the declared external settings file → configuration coordinator boundary without adding new network endpoints, auth paths, schema changes, or new file-access surfaces beyond the already-planned settings-file hash/read handling.

## TDD Gate Compliance

- **RED:** No `test(10-10): ...` commit exists. The regression was already present in prior combined commit `5f5b9d2`, and the focused test passed at executor start.
- **GREEN:** No `feat(10-10): ...` commit exists. The implementation was already present in prior combined commit `5f5b9d2`.
- **Warning:** Plan-level TDD gate sequence could not be reconstructed for `10-10` because the code/test landed before this executor run under `fix(10): CR-01 surface watcher hash read failures`.

## Verification

- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ConfigPersistenceCoordinatorTests&FullyQualifiedName~Watcher_HashFailure_EmitsReadFailedAndProcessesLaterReload" --nologo` — passed (1 test).
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ConfigPersistenceCoordinatorTests" --nologo` — passed (29 tests).
- `dotnet test AutoQACSharp.slnx --nologo` — passed (AutoQAC.Tests 922, QueryPlugins.Tests 61).
- Source inspection confirmed `HashFailure = new IOException("locked")`, `throw HashFailure;`, `Could not hash settings file for watcher reload`, and typed `Watcher/ReadFailed` result publication are present.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

The remaining `10-VERIFICATION.md` gap is closed: watcher hash exceptions now publish typed `Watcher/ReadFailed` failure/result events, and a later valid watcher reload still updates active configuration. Phase 10 is ready for `/gsd-verify-work` re-verification.

## Self-Check: PASSED

- Found `AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs`.
- Found `AutoQAC.Tests/Services/Configuration/ConfigPersistenceCoordinatorTests.cs`.
- Found `AutoQAC.Tests/Services/Configuration/Fakes/FakeUserConfigFileStore.cs`.
- Found `.planning/phases/10-configuration-persistence-hardening/10-10-SUMMARY.md`.
- Found prior code/test commit `5f5b9d2`.

---
*Phase: 10-configuration-persistence-hardening*
*Completed: 2026-05-01*

# Phase 10 Plan 11: Production DI Shared-Coordinator Wiring Summary

**Production configuration DI now builds the facade and watcher around the same shared IConfigPersistenceCoordinator, with a regression proving watcher failures reach facade result streams.**

## Performance

- **Duration:** 3 min
- **Started:** 2026-05-01T02:26:45Z
- **Completed:** 2026-05-01T02:29:25Z
- **Tasks:** 3
- **Files modified:** 3

## Accomplishments

- Added `AddConfiguration_ShouldWireConfigurationFacadeAndWatcherThroughSharedCoordinator` as a deterministic integration regression.
- Proved the resolved `IConfigurationService` and `IConfigWatcherService` private `_coordinator` fields both reference `provider.GetRequiredService<IConfigPersistenceCoordinator>()`.
- Proved a `ConfigFileSignalKind.Error` submitted to the shared coordinator is observed through `IConfigurationService.PersistenceResults` as a failed `Watcher` result with `ReadFailed` failure kind.
- Replaced type-based `IConfigurationService` registration with an explicit factory that passes the registered shared coordinator and logger into `ConfigurationService`.
- Verified the focused regression, all dependency-injection tests, and the full solution test suite.

## Task Commits

Each task was committed atomically:

1. **Task 1 RED: Prove DI facade and watcher must share the registered coordinator** - `289bad6` (test)
2. **Task 2 GREEN: Explicitly construct IConfigurationService with the shared coordinator** - `2ac0e50` (feat)
3. **Task 3: Verify Phase 10 DI gap closure and summarize** - final metadata commit

**Plan metadata:** final docs commit for this summary/state update.

## Files Created/Modified

- `AutoQAC.Tests/Integration/DependencyInjectionTests.cs` - Adds the shared-coordinator DI regression and watcher-to-facade result-flow assertion.
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs` - Constructs `IConfigurationService` via an explicit factory using `IConfigPersistenceCoordinator` and `ILoggingService` from the service provider.
- `.planning/phases/10-configuration-persistence-hardening/10-11-SUMMARY.md` - Records the gap closure, verification, and TDD gate compliance.

## Decisions Made

- Production DI uses an explicit `IConfigurationService` factory instead of `AddSingleton<IConfigurationService, ConfigurationService>()` because Microsoft.Extensions.DependencyInjection cannot select the internal coordinator-taking constructor.
- The regression asserts data flow in addition to object identity so future constructor-selection regressions cannot pass by only resolving services.

## Deviations from Plan

None - plan executed exactly as written.

**Total deviations:** 0 auto-fixed.
**Impact on plan:** No scope changes; the implementation stayed limited to DI wiring and deterministic regression coverage.

## Issues Encountered

- An initial GREEN verification attempt ran the focused regression and all DI tests in parallel, causing a transient MSBuild file lock on `QueryPlugins.dll`. The commands were rerun sequentially and passed.

## Known Stubs

None. Stub-pattern scans found no `TODO`, `FIXME`, placeholder, `coming soon`, `not available`, or UI-facing hardcoded empty data in the touched code files.

## Threat Flags

None. This plan mitigated the declared production DI graph and watcher-signal trust boundaries without adding new network endpoints, auth paths, file-access surfaces, or schema changes.

## Verification

- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~DependencyInjectionTests&FullyQualifiedName~AddConfiguration_ShouldWireConfigurationFacadeAndWatcherThroughSharedCoordinator" --nologo` — RED failed before Task 2 with a coordinator identity mismatch, then passed after Task 2 (1 test).
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~DependencyInjectionTests" --nologo` — passed (2 tests).
- `dotnet test AutoQACSharp.slnx --nologo` — passed (AutoQAC.Tests 923, QueryPlugins.Tests 61).
- Acceptance checks confirmed the test contains `GetPrivateField<IConfigPersistenceCoordinator>(configuration, "_coordinator")`, `GetPrivateField<IConfigPersistenceCoordinator>(watcher, "_coordinator")`, `NotifySettingsFileChanged(ConfigFileSignalKind.Error)`, `ConfigPersistenceOperationKind.Watcher`, and `ConfigPersistenceFailureKind.ReadFailed`.
- Acceptance checks confirmed `ServiceCollectionExtensions.AddConfiguration` contains `services.AddSingleton<IConfigurationService>(sp => new ConfigurationService(` with `sp.GetRequiredService<IConfigPersistenceCoordinator>()` and `sp.GetRequiredService<ILoggingService>()`, and no longer contains `AddSingleton<IConfigurationService, ConfigurationService>()`.

## TDD Gate Compliance

- RED commit present: `289bad6 test(10-11): add shared coordinator DI regression`
- GREEN commit present after RED: `2ac0e50 feat(10-11): wire configuration facade to shared coordinator`
- REFACTOR commit: not needed.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

The `10-VERIFICATION.md` production wiring regression is closed: the running app now constructs `IConfigurationService` with the registered shared `IConfigPersistenceCoordinator`, so watcher-originated results/failures are visible through the facade streams consumed by Settings UI and cleaning-adjacent workflows. Phase 10 is ready for `/gsd-verify-work` re-verification.

## Self-Check: PASSED

- Found `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`.
- Found `AutoQAC.Tests/Integration/DependencyInjectionTests.cs`.
- Found `.planning/phases/10-configuration-persistence-hardening/10-11-SUMMARY.md`.
- Found task commits `289bad6` and `2ac0e50` in git history.
- Final verification passed: `dotnet test AutoQACSharp.slnx --nologo`.

---
*Phase: 10-configuration-persistence-hardening*
*Completed: 2026-05-01*

# Phase 10 Plan 01: Manual Configuration Copy Primitive Summary

**Model-owned UserConfiguration.Copy() deep-copy primitive with 14 behavior tests proving parity with the prior YAML round-trip clone.**

## Performance

- **Duration:** 3 min
- **Started:** 2026-04-30T22:47:04Z
- **Completed:** 2026-04-30T22:50:19Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments

- Added 14 xUnit/FluentAssertions behavior tests covering scalar preservation, each nested model, all mutable dictionaries, SkipLists nested lists, null normalization, YAML alias preservation, idempotence, and YAML round-trip parity.
- Added documented public `Copy()` methods to `UserConfiguration`, `LoadOrderConfig`, `ModOrganizerConfig`, `XEditConfig`, `AutoQacSettings`, `BackupSettings`, and `RetentionSettings`.
- Preserved YAML aliases and left `ConfigurationService.CloneConfig` untouched for Plan 03, which will swap the caller and remove the YAML clone path.

## Task Commits

Each task was committed atomically:

1. **Task 1: RED — failing UserConfigurationCopy tests** - `5dc561e` (test)
2. **Task 2: GREEN — add Copy() methods to model graph** - `140f1ef` (feat)

**Plan metadata:** pending final docs commit

## Files Created/Modified

- `AutoQAC.Tests/Models/UserConfigurationCopyTests.cs` - New behavior-first TDD coverage for manual copy semantics and YAML parity.
- `AutoQAC/Models/Configuration/UserConfiguration.cs` - Added parent and nested config `Copy()` methods with null normalization and deep-copy logic.
- `AutoQAC/Models/Configuration/BackupSettings.cs` - Added documented `BackupSettings.Copy()`.
- `AutoQAC/Models/Configuration/RetentionSettings.cs` - Added documented `RetentionSettings.Copy()`.

## Verification

- `dotnet build AutoQAC.Tests/AutoQAC.Tests.csproj --nologo` failed in RED with `CS1061` missing `Copy()` methods.
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~UserConfigurationCopyTests --nologo` passed: 14/14.
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~Configuration --nologo` passed: 81/81.
- `dotnet build AutoQACSharp.slnx --nologo` passed.

## Decisions Made

- Implemented only the copy primitive in this plan; `ConfigurationService.CloneConfig` remains YAML-based until Plan 03 performs the caller swap and clone-path deletion.
- Kept the review-requested behavior parity test as the guarantee surface and deliberately did not add the brittle `Copy_DoesNotInstantiateYamlSerializer_StaticSourceGuard` source-regex test.
- Added explicit `System`/`System.Linq` imports because the production `AutoQAC.csproj` does not currently enable implicit usings, despite the plan context expecting it.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Added explicit System/System.Linq imports**
- **Found during:** Task 2 (GREEN — add Copy() methods to model graph)
- **Issue:** The plan stated implicit usings were enabled in `AutoQAC.csproj`, but the production project file does not contain `<ImplicitUsings>enable</ImplicitUsings>`. `StringComparer`, `.ToDictionary()`, and `.ToList()` therefore did not compile without explicit imports.
- **Fix:** Added `using System;` and `using System.Linq;` to `UserConfiguration.cs` while preserving all existing YAML aliases and model members.
- **Files modified:** `AutoQAC/Models/Configuration/UserConfiguration.cs`
- **Verification:** `dotnet build AutoQACSharp.slnx --nologo` and configuration tests passed.
- **Committed in:** `140f1ef`

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** The deviation was necessary for compilation and does not change runtime behavior or phase scope.

## Issues Encountered

None.

## Known Stubs

None.

## TDD Gate Compliance

- **RED:** `5dc561e` — failing tests committed before production copy methods existed.
- **GREEN:** `140f1ef` — implementation committed after `UserConfigurationCopyTests` passed.
- **REFACTOR:** Not needed; Copy() bodies are small and idiomatic.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Ready for Plan 10-02/10-03 consumers to call `UserConfiguration.Copy()`.
- Plan 03 should replace `ConfigurationService.CloneConfig` callers with the new Copy() primitive, then remove YAML serialize/deserialize calls from normal clone paths to close the remaining PERF-03 milestone.

## Self-Check: PASSED

- Found all four created/modified files on disk.
- Found task commits `5dc561e` and `140f1ef` in git history.

---
*Phase: 10-configuration-persistence-hardening*
*Completed: 2026-04-30*

# Phase 10 Plan 02: Configuration Persistence Coordinator Summary

**Single-reader Channel coordinator with typed flush/reload results, safe failure streams, deterministic race tests, and temp-file Replace/Move settings persistence**

## Performance

- **Duration:** 7 min
- **Started:** 2026-04-30T22:51:13Z
- **Completed:** 2026-04-30T22:58:00Z
- **Tasks:** 2
- **Files modified:** 8

## Accomplishments

- Added `ConfigPersistenceCoordinator` as the internal serialized authority for app saves, flush barriers, watcher reloads, cleaning deferral, shutdown flush, generation/hash rejection, and typed failure state.
- Added `ConfigPersistenceResult` / `ConfigPersistenceFailure` status contracts plus `IUserConfigFileStore` and production `UserConfigFileStore` temp-file persistence.
- Added deterministic coordinator tests covering B1..B21 and file-store tests covering B16/B17 without production 500 ms throttle sleeps.

## Behavior Matrix Coverage

- B1/B2 lifecycle: `Lifecycle_StartThenStop_DrainsCleanly`, `Submit_AfterStop_Throws_ObjectDisposedException`
- B3..B7 save/flush/failure: `Save_ThenSave_ThenFlush_CoalescesToLatestPending_OneWrite`, `Flush_AfterPending_ReturnsSuccessAndPersistsLatest`, `Flush_NoPending_ReturnsNoOpAndZeroWrites`, `Save_WriteFails_ReturnsFailedAndRollsBackToLastKnownGood`, `Save_AfterFailure_Succeeds_ClearsLastFailure`
- B8..B11 watcher/hash races: `Watcher_HashEcho_SilentSkip_NoFailureNoStateChange`, `Watcher_DistinctExternalContent_NoPendingApp_AcceptsAndAppliesCandidate`, `Watcher_DuringPendingAppSave_RejectsRacingExternalEdit_AppSaveWins`, `Watcher_AfterAppSaveSettled_NewExternalContentIsAppliedWhenGenerationGreater`
- B12..B14 cleaning deferral: `Watcher_DuringCleaning_DefersLatestExternal_NoActiveMutation`, `Watcher_AfterCleaningEnds_AppliesLatestDeferredOnly`, `Watcher_DeferredInvalidYaml_RejectsAndKeepsCurrent_DoesNotApplyEarlierValid`
- B15/B18/B19/B20/B21: missing file, invalid YAML, stale observation, shutdown flush failure, and no-throttle source guard tests.
- B16/B17 atomic file store: `WriteAsync_NewFile_CreatesViaTempThenMove`, `WriteAsync_ExistingFile_UsesReplace`, `WriteAsync_ReplaceThrows_RethrowsAndCleansTemp`.

## Task Commits

1. **Task 1: RED — coordinator contracts + failing race-matrix tests** - `95ba5d4` (test)
2. **Task 2: GREEN — implement coordinator + production file store** - `83682e3` (feat)

**Plan metadata:** pending final docs commit

## Files Created/Modified

- `AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs` - Single-reader Channel coordinator for persistence intent ordering and typed status/failure emission.
- `AutoQAC/Services/Configuration/ConfigPersistenceOperation.cs` - Internal operation records and watcher signal enum.
- `AutoQAC/Services/Configuration/ConfigPersistenceStatus.cs` - Public typed result/failure enums and records with D-28 safe summary guidance.
- `AutoQAC/Services/Configuration/IUserConfigFileStore.cs` - Internal read/write/hash seam for settings-file persistence.
- `AutoQAC/Services/Configuration/UserConfigFileStore.cs` - Production YamlDotNet disk persistence using same-directory temp writes followed by `File.Replace` or `File.Move`.
- `AutoQAC.Tests/Services/Configuration/ConfigPersistenceCoordinatorTests.cs` - Deterministic race-matrix coverage for coordinator behavior.
- `AutoQAC.Tests/Services/Configuration/Fakes/FakeUserConfigFileStore.cs` - In-memory file-store fake for deterministic read/write/hash/missing/failure tests.
- `AutoQAC.Tests/Services/Configuration/UserConfigFileStoreTests.cs` - Production file-store temp/replace/move/read/hash tests.

## Decisions Made

- Used the existing `InternalsVisibleTo Include="AutoQAC.Tests"` csproj entry; no assembly attribute or csproj mutation was required for test visibility.
- Covered atomic-save sequence on the production `UserConfigFileStore` with injected Replace/Move delegates instead of simulating it in the fake, because B16/B17 are file-store behavior rather than coordinator behavior.
- Kept `IConfigurationService`, `ConfigWatcherService`, DI, and ViewModels untouched for the planned Plan 03/04/05 handoff.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

- RED static guard initially matched its own forbidden pattern string. Fixed the test to concatenate the forbidden source pattern so `Select-String` can verify no production throttle sleep call exists without the guard self-matching.

## Known Stubs

None - no placeholder or unimplemented production code remains in the files created by this plan.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Ready for Plan 03 to wire `IConfigurationService` and `ConfigWatcherService` to the coordinator.
- Plan 03 should route save/load/flush/reload through `ConfigPersistenceCoordinator`, map `ConfigurationAccepted` to `UserConfigurationChanged`, and expose typed result/failure flow to downstream preflight/ViewModel work.

## Verification

- `dotnet build AutoQACSharp.slnx --nologo` — passed
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ConfigPersistenceCoordinatorTests|FullyQualifiedName~UserConfigFileStoreTests" --nologo` — passed (25 tests)
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~Configuration --nologo` — passed (106 tests)
- Source checks for no 500 ms throttle sleeps, `RunContinuationsAsynchronously`, `File.Replace`, `File.Move`, and `WriteAllTextAsync` — passed

## Self-Check: PASSED

- Verified all eight created production/test files exist on disk.
- Verified summary file exists at `.planning/phases/10-configuration-persistence-hardening/10-02-SUMMARY.md`.
- Verified task commits `95ba5d4` and `83682e3` exist in git history.

---
*Phase: 10-configuration-persistence-hardening*
*Completed: 2026-04-30*

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

# Phase 10 Plan 04: Pre-cleaning Flush Failure Guard Summary

**Cleaning preflight now blocks xEdit launch when required configuration flushes fail, preserving a typed safe persistence payload for existing failure handling.**

## Performance

- **Duration:** 9 min
- **Started:** 2026-04-30T23:11:03Z
- **Completed:** 2026-04-30T23:19:59Z
- **Tasks:** 2
- **Files modified:** 5

## Accomplishments

- Added `ConfigPersistenceFailureException : InvalidOperationException` with a typed `ConfigPersistenceFailure` payload and safe summary message.
- Updated `CleaningPreflight.PrepareAsync` to inspect the typed flush result and abort on `Failed` or defensive `Rejected` before downstream preflight work can run.
- Added five focused `CleaningPreflightTests` cases covering typed throw, downstream collaborator non-invocation, Success/NoOp happy paths, and safe-summary logging.
- Confirmed `CleaningOrchestrator.cs` and `CleaningCommandsViewModel.cs` were not modified; existing exception handling continues to catch the new type through `InvalidOperationException`/`Exception` compatibility.

## Task Commits

Each task was committed atomically:

1. **Task 1: RED — failing CleaningPreflight flush-failure tests** - `fec3d64` (test)
2. **Task 2: GREEN — add ConfigPersistenceFailureException; guard PrepareAsync on Failed** - `79a6420` (feat)

**Plan metadata:** `1bf4491` (docs)

_Note: This was a TDD plan and produced RED then GREEN commits._

## Files Created/Modified

- `AutoQAC/Services/Configuration/ConfigPersistenceStatus.cs` - Added the typed persistence failure exception beside related status/result types.
- `AutoQAC/Services/Cleaning/CleaningPreflight.cs` - Branches on flush result status before validation and throws the typed exception with safe summary logging.
- `AutoQAC.Tests/Services/Cleaning/CleaningPreflightTests.cs` - Adds five flush-failure/happy-path tests plus a default NoOp flush setup for existing tests.
- `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs` - Defaults orchestrator config substitute flushes to NoOp for existing happy-path tests.
- `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs` - Defaults orchestrator config substitutes to NoOp in process-level orchestrator helpers.

## Decisions Made

- `ConfigPersistenceFailureException` derives from `InvalidOperationException` to preserve current cleaning failure catch behavior while making the payload available to future UI mapping.
- `Rejected` flush results are treated as hard blockers even though the Plan 02 contract does not currently emit them for flushes; this prevents silent cleaning if the upstream contract broadens later.
- Plan 05 remains responsible for richer ViewModel banners via `IConfigurationService.Failures`/`PersistenceResults`; Plan 04 covers only the cleaning preflight abort surface.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Explicitly defaulted legacy preflight test substitutes to NoOp flush results**
- **Found during:** Task 2 (GREEN verification)
- **Issue:** Existing orchestrator/process tests that build a real `CleaningPreflight` with an `IConfigurationService` substitute returned `null` for the new typed flush result, causing `NullReferenceException` before the happy path could proceed.
- **Fix:** Added explicit `FlushPendingSavesAsync(...).Returns(NoOp)` setup in affected test fixtures/helpers so they model the production no-pending-save path.
- **Files modified:** `AutoQAC.Tests/Services/CleaningPreflightTests.cs`, `AutoQAC.Tests/Services/CleaningOrchestratorTests.cs`, `AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs`
- **Verification:** `dotnet test AutoQACSharp.slnx --nologo` passed with 959 total tests.
- **Committed in:** `79a6420`

---

**Total deviations:** 1 auto-fixed (1 Rule 1 bug).
**Impact on plan:** The fix was limited to test substitutes affected by the new typed return contract; production behavior and plan scope remained unchanged.

## Issues Encountered

- An initial parallel run of focused test filters hit a transient MSBuild file lock on `AutoQAC.runtimeconfig.json`. Rerunning the focused test sequentially passed; final full-suite verification also passed.

## Known Stubs

None. Stub-pattern scans only found existing intentional null/empty test data and the pre-existing `skipSet = null` local used by `CleaningPreflight` control flow; no goal-blocking stubs were introduced.

## Threat Flags

None. The new typed exception/trust-boundary behavior was already covered by the plan threat model, and no new network endpoints, file access patterns, auth paths, or schema trust boundaries were introduced.

## TDD Gate Compliance

- RED commit present: `fec3d64 test(10-04): add failing CleaningPreflight flush-failure tests`
- GREEN commit present after RED: `79a6420 feat(10-04): block cleaning when pre-cleaning config flush fails`
- REFACTOR commit: not needed; implementation remained a single guarded branch plus explicit test substitute setup.

## Verification

- `dotnet build AutoQACSharp.slnx --nologo` — passed.
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~CleaningPreflightTests --nologo` — passed (14 tests).
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~Configuration --nologo` — passed (109 tests).
- `dotnet test AutoQACSharp.slnx --nologo` — passed (AutoQAC.Tests: 898; QueryPlugins.Tests: 61).
- `Select-String` checks for `flushResult.Status`, `ConfigPersistenceFailureException`, and `D-26` — passed.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Ready for Plan 05 to add richer ViewModel status/banner mapping using `IConfigurationService.Failures` and `PersistenceResults`.
- The cleaning path now aborts deterministically on failed pre-cleaning flushes and preserves the safe typed payload for any future UI-specific handling.

## Self-Check: PASSED

- Verified all key modified files and this summary exist on disk.
- Verified task commits `fec3d64` and `79a6420` exist in git history.

---
*Phase: 10-configuration-persistence-hardening*
*Completed: 2026-04-30*

# Phase 10 Plan 05: Settings Persistence Banner Summary

**Settings persistence failures now appear as a text-only Settings dialog banner driven by typed failure streams, with successful saves/reloads clearing stale warnings.**

## Performance

- **Duration:** 15 min active execution plus manual UAT checkpoint
- **Started:** 2026-04-30T23:24:29Z
- **Completed:** 2026-04-30T23:39:18Z
- **Tasks:** 2 completed, plus one launch-blocker auto-fix
- **Files modified:** 6

## Accomplishments

- Added `SettingsViewModel.PersistenceBannerText` and `HasPersistenceBanner`, fed by `IConfigurationService.Failures` and cleared by successful `PersistenceResults` or accepted `UserConfigurationChanged` reloads.
- Added a visible warning `Border` in `SettingsWindow.axaml` bound to `HasPersistenceBanner` / `PersistenceBannerText` near the top of the settings content.
- Changed explicit Settings saves to flush pending persistence before closing, keeping the dialog open and showing a restore banner if the flush fails.
- Added 11 `SettingsViewModelTests` covering failure mappings, successful save/reload clearing, subscription disposal, static XAML binding guards, no-modal behavior, and no retry UI.
- Fixed a launch-time DI blocker by changing `ConfigWatcherService` to consume `IConfigPersistenceCoordinator`, matching the registered coordinator abstraction.
- Recorded the user's blocking manual UAT response: **approved**.

## Task Commits

Each implementation task was committed atomically:

1. **Task 1 RED: Add SettingsViewModel persistence banner tests** - `f326c37` (test)
2. **Task 1 GREEN: Surface typed persistence failures as SettingsViewModel banner** - `45c844f` (feat)
3. **Task 1a: Fix launch blocker: config watcher DI resolution** - `8a116f3` (fix)

**Plan metadata:** final docs commit for this summary/state update.

## Files Created/Modified

- `AutoQAC/ViewModels/SettingsViewModel.cs` - Adds banner observable state, typed failure/result/reload subscriptions, safe banner mapping, flush-before-close save behavior, and subscription disposal.
- `AutoQAC/Views/SettingsWindow.axaml` - Adds the visible text-only warning banner bound to `PersistenceBannerText` and `HasPersistenceBanner`.
- `AutoQAC.Tests/ViewModels/SettingsViewModelTests.cs` - Adds deterministic tests for banner mappings, clearing, disposal, XAML binding, no modal dialog, and no retry UI.
- `AutoQAC/Services/Configuration/ConfigWatcherService.cs` - Uses `IConfigPersistenceCoordinator` so DI resolves the watcher against the registered abstraction.
- `AutoQAC/Services/Configuration/IConfigPersistenceCoordinator.cs` - Keeps the coordinator contract available for watcher injection.
- `AutoQAC.Tests/Integration/DependencyInjectionTests.cs` - Ensures `IConfigWatcherService` resolves in the integrated service provider.

## Banner Mapping Shipped

| Failure.Operation | Failure.Kind | Banner text |
|-------------------|--------------|-------------|
| Save | WriteFailed | `Could not save settings. Settings were restored to last saved values.` |
| Flush | WriteFailed | `Could not save settings before cleaning. Cleaning was blocked. Settings were restored to last saved values.` |
| Flush | Other | `Could not save settings (flush). Cleaning was blocked.` |
| Reload / DeferredReload | InvalidExternalYaml | `External settings file has invalid YAML. Active settings were not changed.` |
| Reload / DeferredReload | MissingFile | `Settings file is missing or unreadable. Active settings were not changed.` |
| Reload / DeferredReload | ReadFailed | `Could not read settings file. Active settings were not changed.` |
| Other | Fall-through | `Could not persist settings: {failure.SafeSummary}` |

## Subscription and UI Binding Pattern

- `SettingsViewModel` subscribes to `Failures`, `PersistenceResults`, and `UserConfigurationChanged` with `CallbackObserver<T>`.
- Each observer posts back through the injected `IUiDispatcher` before mutating observable state.
- `Dispose()` releases all three subscriptions plus existing debounced path validators.
- `SettingsWindow.axaml` binds:
  - `Border.IsVisible="{Binding HasPersistenceBanner}"`
  - `TextBlock.Text="{Binding PersistenceBannerText}"`

## Manual UAT Approval

- **Checkpoint:** Task 2 — Manual UAT: banner appears + clears + no dialog.
- **User response:** approved.
- **Recorded outcome:** The user approved the four manual scenarios from the plan: save failure banner, clear on next successful save, invalid external YAML banner, and no retry UI.
- **Visible surface:** `SettingsWindow.axaml` displays the banner inside the Settings dialog near the top of the settings content.
- **Dialog/retry behavior:** UAT approval confirms no modal dialog appeared for persistence failures and no Retry/Try Again control was visible.

## Decisions Made

- Settings persistence failures are recoverable UI state, so they are shown as text-only banner copy rather than modal dialogs.
- Save success is defined by the explicit flush barrier (`Success`/`NoOp`), not merely by enqueueing the save request.
- Accepted external reload notifications clear stale failure banners, matching D-27's successful save/reload/flush rule.
- The watcher constructor depends on `IConfigPersistenceCoordinator` to match the registered DI abstraction and prevent startup resolution failures.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Fixed config watcher DI launch blocker**
- **Found during:** Task 2 manual UAT launch preparation.
- **Issue:** The app could not launch because `ConfigWatcherService` was not resolvable through DI after coordinator wiring; the watcher needed the registered coordinator abstraction.
- **Fix:** Changed `ConfigWatcherService` to inject `IConfigPersistenceCoordinator`, kept the contract public, and added a DI integration assertion that `IConfigWatcherService` resolves.
- **Files modified:** `AutoQAC/Services/Configuration/ConfigWatcherService.cs`, `AutoQAC/Services/Configuration/IConfigPersistenceCoordinator.cs`, `AutoQAC.Tests/Integration/DependencyInjectionTests.cs`
- **Verification:** `dotnet test AutoQACSharp.slnx --nologo` passed after the fix; manual UAT checkpoint was approved.
- **Committed in:** `8a116f3`

---

**Total deviations:** 1 auto-fixed (1 Rule 3 blocking issue).
**Impact on plan:** The fix was necessary to launch the app for UAT and aligned DI with the existing coordinator abstraction; no new feature scope was added.

## Issues Encountered

- An initial full-suite verification run was started in parallel with the focused SettingsViewModel test run and hit a transient MSBuild file lock on `AutoQAC.dll`. Rerunning the full suite sequentially passed.

## Known Stubs

None. Stub-pattern scans found only existing nullable field initializers/design-time null guards, subscription clear assignments, and test-local nullable setup variables; no UI-facing placeholder/mock data or goal-blocking stubs were introduced.

## Threat Flags

None. The new trust boundaries were already in the Plan 05 threat model: typed coordinator failures to SettingsViewModel and text-only binding to `TextBlock`. No new network endpoints, auth paths, file access trust boundaries, or schema changes were introduced beyond the planned config persistence UI surface.

## Verification

- `Select-String -Path AutoQAC/ViewModels/SettingsViewModel.cs -Pattern "PersistenceBannerText"` — passed (multiple matches).
- `Select-String -Path AutoQAC/ViewModels/SettingsViewModel.cs -Pattern "restored to last saved values"` — passed.
- `Select-String -Path AutoQAC/ViewModels/SettingsViewModel.cs -Pattern "using System\.Reactive|using ReactiveUI"` — passed (zero matches).
- `Select-String -Path AutoQAC/ViewModels/SettingsViewModel.cs -Pattern "MessageBox|ShowError|ShowAlert"` — passed (zero matches).
- `Select-String -Path AutoQAC/ViewModels/SettingsViewModel.cs -Pattern "RetryCommand|RetryButton|RetryAttempt"` — passed (zero matches).
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter FullyQualifiedName~SettingsViewModelTests --nologo` — passed (11 tests).
- `dotnet test AutoQACSharp.slnx --nologo` — passed (AutoQAC.Tests: 909; QueryPlugins.Tests: 61).
- Manual UAT Task 2 — approved by user.

## TDD Gate Compliance

- RED commit present: `f326c37 test(10-05): add SettingsViewModel persistence banner tests`
- GREEN commit present after RED: `45c844f feat(10-05): surface typed persistence failures as SettingsViewModel banner`
- Follow-up fix commit: `8a116f3 fix(10-05): make config watcher resolvable at startup`
- REFACTOR commit: not needed.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Phase 10 is ready to close: typed persistence now flows from serialized coordinator saves/reloads through pre-cleaning blockers and Settings UI banner feedback.
- Phase 11 can build on the safe-summary and text-only diagnostics patterns when tightening user-facing diagnostics boundaries.

## Self-Check: PASSED

- Verified key modified files and this summary exist on disk.
- Verified commits `f326c37`, `45c844f`, and `8a116f3` exist in git history.
- Verified full-suite automated tests pass after manual UAT approval.

---
*Phase: 10-configuration-persistence-hardening*
*Completed: 2026-04-30*

# Phase 10 Plan 06: Coordinator Observer Safety and Reload Guard Summary

**Config persistence coordinator now catches observer exceptions at Subject publication boundaries and flushes queued app saves before explicit disk reloads.**

## Performance

- **Duration:** 3 min
- **Started:** 2026-05-01T00:42:59Z
- **Completed:** 2026-05-01T00:46:05Z
- **Tasks:** 2 completed
- **Files modified:** 2

## Accomplishments

- Added five regression tests for throwing observers and explicit reload behavior under pending-save races.
- Added `SafePublishAccepted`, `SafePublishResult`, and `SafePublishFailure` so observer exceptions are logged but cannot prevent caller TCS completion.
- Updated explicit reload handling to flush pending app saves before reading disk, preserving user edits instead of silently overwriting them.
- Verified coordinator tests, solution build, full solution tests, and source grep for bare subject publication calls.

## Task Commits

Each task was committed atomically:

1. **Task 1: RED — Add failing tests for observer-exception safety and reload-vs-pending-save race** - `ba54750` (test)
2. **Task 2: GREEN — Implement safe observer publication and reload pending-save guard** - `e32d2dd` (feat)

**Plan metadata:** pending final docs commit

_Note: This was a TDD plan and produced the required test → feat commit sequence._

## Files Created/Modified

- `AutoQAC.Tests/Services/Configuration/ConfigPersistenceCoordinatorTests.cs` - Added regression tests for observer exceptions during save/flush/reload and explicit reload with/without pending app saves.
- `AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs` - Replaced direct subject publication with safe helpers and flushed pending app saves before explicit reload disk reads.

## Decisions Made

- Observer callbacks are treated as untrusted code at the coordinator boundary; exceptions are caught and logged so serialized persistence operations can still complete their TaskCompletionSources.
- Explicit reloads flush pending app saves before disk reads instead of rejecting, because this preserves user edits and keeps reload semantics successful when the pending save can be persisted.

## Deviations from Plan

None - plan executed exactly as written.

**Total deviations:** 0 auto-fixed.
**Impact on plan:** No scope changes; implementation stayed within the planned coordinator hardening and tests.

## TDD Gate Compliance

- **RED:** `ba54750` added failing tests. The observer tests failed with deterministic `TimeoutException` and the pending-save reload test failed because no write occurred before reload.
- **GREEN:** `e32d2dd` implemented safe publication and the explicit reload flush guard; all coordinator tests passed afterward.
- **REFACTOR:** Not needed; no behavior-neutral cleanup commit was required.

## Verification

- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ConfigPersistenceCoordinatorTests&(FullyQualifiedName~ThrowingObserver|FullyQualifiedName~ExplicitReload)" --nologo` — RED failures observed before implementation.
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~Save_WithThrowingAcceptedObserver_StillCompletesAndActiveUpdates" --nologo` — RED failure observed for the accepted-configuration observer path before implementation.
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ConfigPersistenceCoordinatorTests" --nologo` — passed, 25 tests.
- `dotnet build AutoQACSharp.slnx --nologo` — passed with 0 warnings and 0 errors.
- `dotnet test AutoQACSharp.slnx --nologo` — passed, QueryPlugins.Tests 61 tests and AutoQAC.Tests 914 tests.
- Source grep confirmed no bare `_persistenceResults.OnNext(`, `_configurationAccepted.OnNext(`, or `_failures.OnNext(` calls remain outside `SafePublish*` helper lines.

## Known Stubs

None. Stub scan matches were nullable defaults and state resets, not UI-facing placeholder data.

## Threat Flags

None. The plan mitigated the declared observer DoS and reload tampering surfaces without adding new endpoints, auth paths, file access patterns, or schema boundaries.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

Ready for Plan 10-07. Coordinator-level gap closures are in place, and the full solution test suite passes.

## Self-Check: PASSED

- Found `AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs`.
- Found `AutoQAC.Tests/Services/Configuration/ConfigPersistenceCoordinatorTests.cs`.
- Found `.planning/phases/10-configuration-persistence-hardening/10-06-SUMMARY.md`.
- Found task commit `ba54750`.
- Found task commit `e32d2dd`.

---
*Phase: 10-configuration-persistence-hardening*
*Completed: 2026-05-01*

# Phase 10 Plan 07: Facade State Synchronization Summary

**ConfigurationService facade bookkeeping now uses locked pending/loaded flags with failed reloads preserving unsaved user edits.**

## Performance

- **Duration:** 4 min
- **Started:** 2026-05-01T00:48:32Z
- **Completed:** 2026-05-01T00:51:58Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments

- Added regression tests proving failed explicit reloads keep pending facade state pointed at the user's unsaved edit.
- Added regression coverage proving successful reloads clear the facade pending-save marker and make later flushes a typed no-op.
- Synchronized every `_hasPendingUserSave` and `_loadedUserConfigFromDisk` read/write under `_stateLock`.
- Updated `ReloadFromDiskAsync` to clear pending-save state only for `ConfigPersistenceStatusKind.Success`.
- Added a facade-level no-pending flush short-circuit so the synchronized flag has observable behavior and cannot diverge silently.

## Task Commits

Each task was committed atomically:

1. **Task 1: RED — Add failing reload facade state tests** - `ee74f55` (test)
2. **Task 2: GREEN — Synchronize facade flags and conditional reload clearing** - `adc4bd7` (feat)

**Plan metadata:** pending final docs commit

## Files Created/Modified

- `AutoQAC.Tests/Services/ConfigurationServiceTests.cs` - Added coordinator-backed facade regression tests for failed/successful reload pending-save behavior.
- `AutoQAC/Services/Configuration/ConfigurationService.cs` - Added locked facade flag snapshots, locked writes, no-pending flush NoOp, and success-only reload flag clearing.
- `.planning/phases/10-configuration-persistence-hardening/10-07-SUMMARY.md` - Execution summary and verification record.

## Decisions Made

- `ConfigurationService` keeps its facade flags but all access now goes through `_stateLock`-protected reads/writes instead of direct async-method field access.
- `FlushPendingSavesAsync` now returns a typed `NoOp` when the facade has no pending app save, matching the successful-reload clearing test and making the facade marker authoritative.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing Critical] Made facade pending flag observable via flush short-circuit**
- **Found during:** Task 1/2 (RED/GREEN verification)
- **Issue:** The plan's proposed failed-reload test expected coordinator flush invocation to prove the pending flag remained set, but the pre-existing facade always called `FlushPendingSavesAsync` regardless of `_hasPendingUserSave`, so that assertion could not distinguish correct from incorrect facade state.
- **Fix:** Added behavior-focused tests that observe `LoadUserConfigAsync` and flush NoOp semantics, then implemented a no-pending flush short-circuit so the synchronized pending flag controls facade behavior.
- **Files modified:** `AutoQAC.Tests/Services/ConfigurationServiceTests.cs`, `AutoQAC/Services/Configuration/ConfigurationService.cs`
- **Verification:** RED tests failed on current code; after implementation, focused reload tests, all `ConfigurationServiceTests`, and the full solution suite passed.
- **Committed in:** `ee74f55`, `adc4bd7`

---

**Total deviations:** 1 auto-fixed (1 Rule 2 missing critical)
**Impact on plan:** The deviation tightened the intended correctness contract without adding new architecture or scope.

## Issues Encountered

- The initial RED tests exposed that coordinator flush invocation alone was not a valid observable for facade pending state because the facade previously called flush unconditionally. The tests were adjusted before GREEN to assert behavior that fails for the real gap.

## Known Stubs

None. Stub-pattern scan found only legitimate null checks/default optional parameters in the modified files.

## Threat Flags

None. The plan only changes in-memory facade synchronization at an existing configuration persistence boundary covered by the plan threat model.

## Verification

- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ConfigurationServiceTests&FullyQualifiedName~ReloadFromDiskAsync" --nologo` — RED failed before implementation; passed after implementation (2 tests).
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ConfigurationServiceTests" --nologo` — passed (33 tests).
- `dotnet test AutoQACSharp.slnx --nologo` — passed (AutoQAC.Tests 916, QueryPlugins.Tests 61).
- Source scan found `_hasPendingUserSave` and `_loadedUserConfigFromDisk` accesses only in `_stateLock`-protected helpers/blocks or declarations.

## TDD Gate Compliance

- RED commit present: `ee74f55 test(10-07): add failing reload facade state tests`
- GREEN commit present after RED: `adc4bd7 feat(10-07): synchronize configuration facade state`
- REFACTOR commit: not needed.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

Phase 10's final gap closure is complete. Configuration persistence can proceed to phase/milestone verification with coordinator and facade race gaps closed.

## Self-Check: PASSED

- Modified files exist: `AutoQAC/Services/Configuration/ConfigurationService.cs`, `AutoQAC.Tests/Services/ConfigurationServiceTests.cs`
- Summary file exists: `.planning/phases/10-configuration-persistence-hardening/10-07-SUMMARY.md`
- Task commits found: `ee74f55`, `adc4bd7`
- Final verification passed: `dotnet test AutoQACSharp.slnx --nologo`

---
*Phase: 10-configuration-persistence-hardening*
*Completed: 2026-05-01*

# Phase 10 Plan 08: Explicit Reload Flush-Failure Guard Summary

**Explicit reload now preserves pending-save failure semantics by returning failed flush results before any stale disk reload can run.**

## Performance

- **Duration:** 3 min
- **Started:** 2026-05-01T01:11:18Z
- **Completed:** 2026-05-01T01:13:42Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments

- Added a deterministic regression test for explicit reload during a pending app save when the prerequisite write fails but old disk content remains readable.
- Updated `ApplyReloadRequestAsync` so failed or rejected prerequisite flush results complete the reload request directly.
- Verified the focused explicit reload tests, all coordinator tests, and the full solution suite pass.

## Task Commits

Each task was committed atomically:

1. **Task 1: RED — Add failing explicit reload flush-failure regression test** - `7dbc1b4` (test)
2. **Task 2: GREEN — Return failed/rejected prerequisite flush result from explicit reload** - `33aa876` (feat)

**Plan metadata:** pending final docs commit

_Note: This was a TDD plan and produced the required test → feat commit sequence._

## Files Created/Modified

- `AutoQAC.Tests/Services/Configuration/ConfigPersistenceCoordinatorTests.cs` - Added `ExplicitReload_DuringPendingAppSave_WhenFlushFails_ReturnsFlushFailureWithoutReadingDisk` covering the write-failure/readable-old-disk race.
- `AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs` - Added the failed/rejected flush short-circuit before `ReadForReloadAsync` in explicit reload handling.
- `.planning/phases/10-configuration-persistence-hardening/10-08-SUMMARY.md` - Execution summary and verification record.

## Decisions Made

- Explicit reload treats the pending app-save flush as a prerequisite barrier; if that barrier fails or is rejected, the reload result is the flush result.
- `SafePublishResult(flushResult)` remains in place so observers still see the flush failure before the caller receives it.
- No retry UI or watcher semantics changed; recovery remains through later valid persistence operations.

## Deviations from Plan

None - plan executed exactly as written.

**Total deviations:** 0 auto-fixed.
**Impact on plan:** No scope changes; implementation stayed within the planned Phase 10 gap closure.

## Issues Encountered

None.

## Known Stubs

None. Stub-pattern review found no placeholder, TODO/FIXME, or mock-data paths introduced by this plan.

## Threat Flags

None. The plan mitigated the declared pending app-save → explicit reload and settings-file → active-config boundaries without adding new network, auth, file-access, or schema surfaces.

## TDD Gate Compliance

- **RED:** `7dbc1b4` added the failing regression test; it failed because `ReloadFromDiskAsync` returned `Success` after reading old disk content.
- **GREEN:** `33aa876` added the failed/rejected flush branch and all focused/full verification passed.
- **REFACTOR:** Not needed; implementation remained a small guarded branch with a WHY comment.

## Verification

- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ConfigPersistenceCoordinatorTests&FullyQualifiedName~ExplicitReload_DuringPendingAppSave_WhenFlushFails" --nologo` — failed in RED with `Expected result.Status to be Failed ... but found Success`.
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ConfigPersistenceCoordinatorTests&FullyQualifiedName~ExplicitReload" --nologo` — passed (3 tests).
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ConfigPersistenceCoordinatorTests" --nologo` — passed (26 tests).
- `dotnet test AutoQACSharp.slnx --nologo` — passed (AutoQAC.Tests 917, QueryPlugins.Tests 61).
- Source inspection confirmed `flushResult.Status is ConfigPersistenceStatusKind.Failed or ConfigPersistenceStatusKind.Rejected`, `reload.Completion.TrySetResult(flushResult);`, and a return before the reload disk read path.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

Phase 10's remaining verification blocker is closed. Configuration persistence hardening is ready for phase-level verification and transition to Phase 11 diagnostics boundary planning.

## Self-Check: PASSED

- Found `AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs`.
- Found `AutoQAC.Tests/Services/Configuration/ConfigPersistenceCoordinatorTests.cs`.
- Found `.planning/phases/10-configuration-persistence-hardening/10-08-SUMMARY.md`.
- Found task commit `7dbc1b4`.
- Found task commit `33aa876`.

---
*Phase: 10-configuration-persistence-hardening*
*Completed: 2026-05-01*

# Phase 10 Plan 09: Facade Flush Barrier Gap Closure Summary

**ConfigurationService forced flush now always drains the coordinator queue barrier, closing the no-pending facade bypass that could leave watcher reload work queued before pre-cleaning continued.**

## Performance

- **Duration:** 12 min
- **Started:** 2026-05-01T01:24:00Z
- **Completed:** 2026-05-01T01:36:08Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments

- Replaced the old no-pending facade NoOp test with `FlushPendingSavesAsync_NoPending_DrainsCoordinatorBarrier`, which fails if the facade synthesizes a local generation-0 NoOp.
- Removed the `ConfigurationService.FlushPendingSavesAsync` early return so every forced flush awaits `_coordinator.FlushPendingSavesAsync(ct)`.
- Preserved pending-save flag clearing only after coordinator `Success` or `NoOp` results.
- Updated the successful reload regression to reflect the new invariant: subsequent forced flushes still drain the coordinator barrier after reloads clear local pending state.
- Verified the focused regression, all `ConfigurationServiceTests`, and the full solution test suite.

## Task Commits

Each task was committed atomically:

1. **Task 1: RED — Prove no-pending facade flush must drain coordinator barrier** - `54b7a71` (test)
2. **Task 2: GREEN — Always await coordinator flush barrier from facade** - `2614854` (feat)

**Plan metadata:** pending final docs commit

_Note: This was a TDD plan and produced the required test → feat commit sequence._

## Files Created/Modified

- `AutoQAC.Tests/Services/ConfigurationServiceTests.cs` - Adds the no-pending coordinator barrier regression and updates the successful reload follow-up flush expectation.
- `AutoQAC/Services/Configuration/ConfigurationService.cs` - Removes the facade-local NoOp bypass and documents why forced flushes must always drain the coordinator queue.
- `.planning/phases/10-configuration-persistence-hardening/10-09-SUMMARY.md` - Execution summary and verification record.

## Decisions Made

- Forced flush is a queue barrier across app saves and watcher/reload work, so absence of a facade pending-save marker is not sufficient to skip the coordinator.
- The local `_hasPendingUserSave` flag remains useful for load/reload bookkeeping and is cleared after coordinator `Success`/`NoOp`, but it no longer gates the barrier call.
- The obsolete `HasPendingUserSave()` helper and its XML comment were removed because the flush bypass code it supported was deleted.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Updated obsolete successful-reload flush expectation**
- **Found during:** Task 2 (GREEN verification)
- **Issue:** `ReloadFromDiskAsync_SuccessfulReload_ClearsPendingSaveFlag` still expected a later forced flush to return a facade-local `NoOp` and avoid the coordinator, which directly contradicted the Plan 09 barrier requirement.
- **Fix:** Renamed the test to `ReloadFromDiskAsync_SuccessfulReload_DrainsSubsequentFlushBarrier` and asserted the subsequent flush returns the coordinator `Success` result with `Received(1)`.
- **Files modified:** `AutoQAC.Tests/Services/ConfigurationServiceTests.cs`
- **Verification:** `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ConfigurationServiceTests" --nologo` passed.
- **Committed in:** `2614854`

---

**Total deviations:** 1 auto-fixed (1 Rule 1 bug).
**Impact on plan:** The fix aligned an older Plan 10-07 assertion with the new gap-closure invariant; no architecture or feature scope was added.

## Issues Encountered

None.

## Known Stubs

None. Stub-pattern scans found only legitimate nullable/default checks in the modified files, not placeholder implementation or UI-facing mock data.

## Threat Flags

None. The plan mitigated the declared facade caller → coordinator queue and settings-file watcher queue → active config boundaries without adding new network endpoints, auth paths, schema boundaries, or file-access surfaces.

## TDD Gate Compliance

- **RED:** `54b7a71` added `FlushPendingSavesAsync_NoPending_DrainsCoordinatorBarrier`; it failed because the facade returned a synthetic generation-0 NoOp instead of the coordinator generation-99 result.
- **GREEN:** `2614854` removed the no-pending bypass and all focused/configuration/full-suite verification passed.
- **REFACTOR:** Not needed; the implementation was a small control-flow deletion plus a WHY comment.

## Verification

- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ConfigurationServiceTests&FullyQualifiedName~FlushPendingSavesAsync_NoPending_DrainsCoordinatorBarrier" --nologo` — failed in RED, then passed after implementation.
- `dotnet test AutoQAC.Tests/AutoQAC.Tests.csproj --filter "FullyQualifiedName~ConfigurationServiceTests" --nologo` — passed (33 tests).
- `dotnet test AutoQACSharp.slnx --nologo` — passed (AutoQAC.Tests 917, QueryPlugins.Tests 61).
- Source inspection confirmed no facade-local `ConfigPersistenceResult(NoOp, Flush, 0, null)` bypass remains and `_coordinator.FlushPendingSavesAsync(ct)` is awaited before returning.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

Phase 10's CR-01 verification blocker is closed. Configuration persistence hardening is ready for phase-level verification and milestone continuation.

## Self-Check: PASSED

- Found `AutoQAC/Services/Configuration/ConfigurationService.cs`.
- Found `AutoQAC.Tests/Services/ConfigurationServiceTests.cs`.
- Found `.planning/phases/10-configuration-persistence-hardening/10-09-SUMMARY.md`.
- Found task commit `54b7a71`.
- Found task commit `2614854`.

---
*Phase: 10-configuration-persistence-hardening*
*Completed: 2026-05-01*
