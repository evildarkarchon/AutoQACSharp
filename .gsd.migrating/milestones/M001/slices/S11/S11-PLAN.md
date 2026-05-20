# S11: Configuration Persistence Hardening

**Goal:** Replace the YAML round-trip clone in `ConfigurationService.
**Demo:** Replace the YAML round-trip clone in `ConfigurationService.

## Must-Haves


## Tasks

- [x] **T01: 10-configuration-persistence-hardening 01** `est:3 min`
  - Replace the YAML round-trip clone in `ConfigurationService.CloneConfig` with public, model-owned `Copy()` methods on `UserConfiguration` and every nested config class. This plan delivers requirement PERF-03 by proving (a) deep copy independence holds for all mutable containers including the `Dictionary<string, List<string>> SkipLists` field flagged in research as a common pitfall, and (b) the new `Copy()` is BEHAVIORALLY equivalent to the existing YAML clone (round-trip parity).

Purpose: PERF-03 closes the YAML clone overhead concern from `.planning/codebase/CONCERNS.md` and unblocks Plan 02's coordinator (which calls `UserConfiguration.Copy()` in the consumer loop). Wave 1 — independent of Plan 02 at the model layer; Plan 02 declares `depends_on: [01]` so Wave 1 still parallelizes inside the wave (Plan 02 RED step can be authored alongside Plan 01) but Wave 2 (Plan 03) does not start until BOTH 01 and 02 commit (already enforced by 03's `depends_on: [01, 02]`).

Output: New `Copy()` methods on `UserConfiguration`, `LoadOrderConfig`, `ModOrganizerConfig`, `XEditConfig`, `AutoQacSettings`, `BackupSettings`, `RetentionSettings`, plus a new test file `UserConfigurationCopyTests.cs` proving deep-copy independence, null normalization, and behavior parity with the existing YAML clone.

### Review Feedback Addressed
- **Codex MEDIUM (10-01)** "Source-regex tests around `Copy()` bodies are brittle; behavior tests are more valuable." — Removed source-regex YAML-free guard (case 12 in original plan). Replaced with a **behavior parity test** that round-trips a populated `UserConfiguration` through YAML serialize/deserialize and asserts `Copy()` produces a value-equal graph. The "no YAML in the clone path" guarantee is now enforced by Plan 03 deleting `_serializer.Serialize` from `ConfigurationService` (the actual clone caller) — that is where it matters.
- **OpenCode MEDIUM (10-01)** "Static YAML-free guard fragility" (path-resolution in CI) — Same fix; the brittle source-walking helper is gone.
- **Codex MEDIUM (10-01)** "Reword success as 'copy primitive delivered; clone-path replacement completes in 10-03.'" — Updated objective and success criteria to reflect that Plan 01 ships the primitive and Plan 03 closes PERF-03.
- **OpenCode LOW (10-01)** "Wave-1 parallel dependency risk" — Resolved: Plan 02 frontmatter now declares `depends_on: [01]` (see 10-02-PLAN.md). Plan 02 RED authoring can still proceed alongside Plan 01; Plan 02 GREEN waits for Plan 01 commit.
- [x] **T02: 10-configuration-persistence-hardening 02** `est:7min`
  - Build the internal serialized persistence authority for `AutoQAC Settings.yaml`. Introduce a `ConfigPersistenceCoordinator` driven by a single-reader `System.Threading.Channels.Channel<ConfigPersistenceOperation>` that owns every save/flush/reload/watcher/defer ordering decision (D-01, D-02), an injectable file-store seam (`IUserConfigFileStore` + production `UserConfigFileStore` with temp+`File.Replace`/`File.Move` atomicity per D-10), typed result and failure records (D-06, D-24, D-28), and a deterministic test suite that proves the SPEC race matrix without real watcher timing (D-32, D-36, D-39).

Plan 03 will wire `IConfigurationService` and `ConfigWatcherService` to this coordinator. Plan 02 depends on Plan 01 because the coordinator's state snapshots call `UserConfiguration.Copy()`; this explicit dependency addresses the cross-AI review concern that parallel Wave 1 execution could otherwise fail to compile.

Purpose: REF-03 (single serialized authority) and the bulk of TEST-03 (deterministic race coverage) close on this plan. Wave 2 after Plan 01 so model-owned `Copy()` is available before coordinator GREEN work begins.

Output: 5 production source files (`ConfigPersistenceCoordinator.cs`, `ConfigPersistenceOperation.cs`, `ConfigPersistenceStatus.cs`, `IUserConfigFileStore.cs`, `UserConfigFileStore.cs`) and 2 test files (`ConfigPersistenceCoordinatorTests.cs`, `Fakes/FakeUserConfigFileStore.cs`).
- [x] **T03: 10-configuration-persistence-hardening 03** `est:8 min`
  - Wire the Plan 02 coordinator and Plan 01 model copies into the public configuration surface. `IConfigurationService` continues to expose its existing persistence-slice methods (D-03 boundary preserved), but their bodies become thin delegations to `ConfigPersistenceCoordinator`. `ConfigWatcherService` becomes an event source only (D-08): the Rx throttle, hash echo, deferral logic, and invalid-YAML validation are removed and re-anchored inside the coordinator (already proven by Plan 02). Watcher event handling is expanded to `Changed | Created | Renamed | Deleted` (D-23). Existing service tests continue to pass — they now exercise the coordinator-backed implementation. Watcher tests are reduced to smoke coverage proving the watcher submits signals to the coordinator (D-33).

This plan completes the persistence-slice rewrite while leaving the rest of the public configuration surface (skip-lists, game queries, MO2 path overrides) untouched (D-03, D-15).

Purpose: REF-03 (one serialized flow visible at the public service boundary) and PERF-03 (clone callers in the service swap to model `Copy()`) are completed by this plan. TEST-03 watcher-smoke reduction (D-33) lands here.

Output: Updated `IConfigurationService.cs`, `ConfigurationService.cs`, `IConfigWatcherService.cs` (if needed), `ConfigWatcherService.cs`, `ServiceCollectionExtensions.cs`, plus targeted updates to `ConfigurationServiceTests.cs` (preserve behavior) and `ConfigWatcherServiceTests.cs` (smoke only).
- [x] **T04: 10-configuration-persistence-hardening 04** `est:9 min`
  - Surface persistence flush failure into the cleaning workflow so a pre-cleaning save failure prevents xEdit launch (SPEC requirement #5, decision D-26, success criterion #2). Today, `CleaningPreflight.PrepareAsync` calls `await configService.FlushPendingSavesAsync(ct)` and proceeds regardless of the outcome; after Plan 03, `FlushPendingSavesAsync` returns a typed `ConfigPersistenceResult`, and Plan 04 inspects it and aborts preflight on `Status=Failed`.

Purpose: closes the SPEC race-policy and acceptance gap "Pre-cleaning flush failure prevents xEdit launch and surfaces the persistence failure path." Wave 3 work; depends only on the public-boundary contracts already wired by Plans 02 and 03.

Output: a small change to `CleaningPreflight.cs` (one new branch + one new typed throw) plus a focused test addition to `CleaningPreflightTests.cs` covering the failure path and the no-regression happy path.
- [x] **T05: 10-configuration-persistence-hardening 05** `est:15 min active execution plus manual UAT checkpoint`
  - Surface the typed `IConfigurationService.Failures` stream as a concise banner in the existing `SettingsViewModel`, and clear the banner on the next successful save (D-27). The banner explicitly states that settings were restored to last saved values when an optimistic save rolled back (D-30). This is the public-UX completion of the typed-failure path established by Plans 02 and 03; the cleaning workflow path is closed by Plan 04.

Purpose: D-24/D-25/D-27/D-29/D-30/D-31 land here at the public UI surface. Wave 3 work parallel with Plan 04.

Output: `SettingsViewModel.cs` gains a `[ObservableProperty] PersistenceBannerText`, `[NotifyPropertyChangedFor(nameof(HasPersistenceBanner))]`, a `Failures` subscription guarded for design-time construction, a `PersistenceResults` clear-on-success subscription, `SaveAsync` flush-before-close behavior, and a Dispose hook for subscriptions. `SettingsWindow.axaml` gains a minimal visible banner bound to `PersistenceBannerText` / `HasPersistenceBanner`. Tests cover the banner state machine and XAML binding presence.
- [x] **T06: 10-configuration-persistence-hardening 06** `est:3 min`
  - Close two coordinator-level verification gaps: (1) inline Subject<T>.OnNext calls can throw from misbehaving observers before TrySetResult completes request TaskCompletionSources, hanging callers; (2) explicit reload requests bypass the pending-save guard that watcher reloads already respect.

Purpose: Make the single-reader coordinator loop resilient to observer exceptions and prevent explicit reloads from silently overwriting queued app saves.
Output: Hardened coordinator with safe publication helpers and pending-save reload guard, plus deterministic regression tests.
- [x] **T07: 10-configuration-persistence-hardening 07** `est:4 min`
  - Close the facade state synchronization gap: ConfigurationService._hasPendingUserSave and _loadedUserConfigFromDisk are read/written from async methods without synchronization, and ReloadFromDiskAsync clears the pending-save flag unconditionally even when the reload fails or a save is pending.

Purpose: Ensure facade bookkeeping flags cannot diverge from coordinator state, completing the "one serialized flow" contract.
Output: Synchronized facade flags with conditional clearing on reload success, plus regression tests.
- [x] **T08: 10-configuration-persistence-hardening 08** `est:3 min`
  - Close the remaining Phase 10 verification blocker: explicit reload currently flushes a pending app save first, but if that prerequisite flush fails it still reads disk and can return Success using old persisted content.

Purpose: Preserve app-save protected reload semantics under failure so queued user edits cannot be dropped while the reload caller receives a successful result.
Output: A failing regression test first, then coordinator logic that returns the failed/rejected flush result immediately without reading or applying disk content.
- [x] **T09: 10-configuration-persistence-hardening 09** `est:12 min`
  - Close the remaining Phase 10 verification blocker: `ConfigurationService.FlushPendingSavesAsync` currently returns a local facade-level NoOp when `_hasPendingUserSave` is false, bypassing the coordinator queue barrier required by D-02 and D-05.

Purpose: Make forced flush a reliable serialized barrier across both app saves and queued watcher reloads so cleaning/preflight callers cannot continue while external settings-file work remains queued behind the facade.
Output: A failing regression test first, then `ConfigurationService` flush logic that always awaits the coordinator barrier and only uses facade state to decide whether to clear `_hasPendingUserSave` after accepted Success/NoOp results.
- [x] **T10: 10-configuration-persistence-hardening 10** `est:8 min`
  - Close the remaining Phase 10 verification gap where watcher reloads can be silently dropped when hashing the settings file races an external writer lock.

Purpose: Phase 10 requires reliable save/reload behavior under race conditions through one serialized, typed persistence flow. A watcher hash exception must become an observable recoverable failure, not a top-level log-only operation drop.

Output: A TDD gap-closure regression and coordinator fix proving a transient `ComputeHashAsync` failure emits a typed `Watcher`/`ReadFailed` result and the coordinator still processes a later valid watcher reload.
- [x] **T11: 10-configuration-persistence-hardening 11** `est:3 min`
  - Close the Phase 10 verification regression where production DI can construct `ConfigurationService` through its public logger-only constructor, creating a private coordinator that is disconnected from `ConfigWatcherService`.

Purpose: Phase 10's serialized persistence authority only protects users if the running app uses one coordinator for facade saves/flushes/reloads and watcher signals. The production service graph must wire `IConfigurationService`, `IConfigWatcherService`, and `IConfigPersistenceCoordinator` to the same coordinator instance so external settings changes update the facade-observed result/failure streams.

Output: A TDD gap-closure integration regression and DI wiring fix proving watcher-originated notifications flow through the same `IConfigurationService` facade that Settings UI and cleaning preflight consume.

## Files Likely Touched

- `AutoQAC/Models/Configuration/UserConfiguration.cs`
- `AutoQAC/Models/Configuration/BackupSettings.cs`
- `AutoQAC/Models/Configuration/RetentionSettings.cs`
- `AutoQAC.Tests/Models/UserConfigurationCopyTests.cs`
- `AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs`
- `AutoQAC/Services/Configuration/ConfigPersistenceOperation.cs`
- `AutoQAC/Services/Configuration/ConfigPersistenceStatus.cs`
- `AutoQAC/Services/Configuration/IUserConfigFileStore.cs`
- `AutoQAC/Services/Configuration/UserConfigFileStore.cs`
- `AutoQAC/AutoQAC.csproj`
- `AutoQAC.Tests/Services/Configuration/ConfigPersistenceCoordinatorTests.cs`
- `AutoQAC.Tests/Services/Configuration/Fakes/FakeUserConfigFileStore.cs`
- `AutoQAC.Tests/Services/Configuration/UserConfigFileStoreTests.cs`
- `AutoQAC/Services/Configuration/IConfigurationService.cs`
- `AutoQAC/Services/Configuration/IConfigPersistenceCoordinator.cs`
- `AutoQAC/Services/Configuration/ConfigurationService.cs`
- `AutoQAC/Services/Configuration/IConfigWatcherService.cs`
- `AutoQAC/Services/Configuration/ConfigWatcherService.cs`
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`
- `AutoQAC.Tests/Services/ConfigurationServiceTests.cs`
- `AutoQAC.Tests/Services/ConfigWatcherServiceTests.cs`
- `AutoQAC/Services/Configuration/ConfigPersistenceStatus.cs`
- `AutoQAC/Services/Cleaning/CleaningPreflight.cs`
- `AutoQAC.Tests/Services/Cleaning/CleaningPreflightTests.cs`
- `AutoQAC/ViewModels/SettingsViewModel.cs`
- `AutoQAC/Views/SettingsWindow.axaml`
- `AutoQAC.Tests/ViewModels/SettingsViewModelTests.cs`
- `AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs`
- `AutoQAC.Tests/Services/Configuration/ConfigPersistenceCoordinatorTests.cs`
- `AutoQAC/Services/Configuration/ConfigurationService.cs`
- `AutoQAC.Tests/Services/ConfigurationServiceTests.cs`
- `AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs`
- `AutoQAC.Tests/Services/Configuration/ConfigPersistenceCoordinatorTests.cs`
- `AutoQAC/Services/Configuration/ConfigurationService.cs`
- `AutoQAC.Tests/Services/ConfigurationServiceTests.cs`
- `AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs`
- `AutoQAC.Tests/Services/Configuration/ConfigPersistenceCoordinatorTests.cs`
- `AutoQAC.Tests/Services/Configuration/Fakes/FakeUserConfigFileStore.cs`
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`
- `AutoQAC.Tests/Integration/DependencyInjectionTests.cs`
