# T02: 10-configuration-persistence-hardening 02

**Slice:** S11 — **Milestone:** M001

## Description

Build the internal serialized persistence authority for `AutoQAC Settings.yaml`. Introduce a `ConfigPersistenceCoordinator` driven by a single-reader `System.Threading.Channels.Channel<ConfigPersistenceOperation>` that owns every save/flush/reload/watcher/defer ordering decision (D-01, D-02), an injectable file-store seam (`IUserConfigFileStore` + production `UserConfigFileStore` with temp+`File.Replace`/`File.Move` atomicity per D-10), typed result and failure records (D-06, D-24, D-28), and a deterministic test suite that proves the SPEC race matrix without real watcher timing (D-32, D-36, D-39).

Plan 03 will wire `IConfigurationService` and `ConfigWatcherService` to this coordinator. Plan 02 depends on Plan 01 because the coordinator's state snapshots call `UserConfiguration.Copy()`; this explicit dependency addresses the cross-AI review concern that parallel Wave 1 execution could otherwise fail to compile.

Purpose: REF-03 (single serialized authority) and the bulk of TEST-03 (deterministic race coverage) close on this plan. Wave 2 after Plan 01 so model-owned `Copy()` is available before coordinator GREEN work begins.

Output: 5 production source files (`ConfigPersistenceCoordinator.cs`, `ConfigPersistenceOperation.cs`, `ConfigPersistenceStatus.cs`, `IUserConfigFileStore.cs`, `UserConfigFileStore.cs`) and 2 test files (`ConfigPersistenceCoordinatorTests.cs`, `Fakes/FakeUserConfigFileStore.cs`).

## Must-Haves

- [ ] "All persistence intents (debounced save, forced flush barrier, watcher signal, deferred reload, cleaning state transition) flow through one Channel<ConfigPersistenceOperation> with a single reader (D-01, D-02, D-08)."
- [ ] "D-04: Normal app save requests coalesce to the latest pending UserConfiguration; tests prove intermediate settings UI states are not written."
- [ ] "D-07: Shutdown/disposal attempts one final barrier flush of the latest app config and logs failure without hanging indefinitely."
- [ ] "D-13: Normal user-edit save debounce remains in production; forced flush bypasses debounce through a queue barrier, and tests disable timers with TimeSpan.Zero."
- [ ] "D-14: Queued reloads are re-evaluated by version/hash after newer app saves and rejected when stale or racing app intent."
- [ ] "Forced flush returns a typed ConfigPersistenceResult that callers can branch on (success/failure) before continuing (D-05)."
- [ ] "Save failure returns a typed result and keeps last known good config active; the failure is observable via a typed status stream (D-06, D-12, D-24, D-28)."
- [ ] "Watcher signals re-read current file content/hash inside the coordinator and reject hash echoes silently (D-08, D-09, D-20)."
- [ ] "App-save wins close-timing race: external content observed while an app save is pending or just-written is rejected by version/hash policy (D-16, D-17, D-21)."
- [ ] "External edits observed while IsCleaning=true are deferred; only the latest valid deferred snapshot is applied after cleaning ends; invalid deferred YAML is rejected and current config is kept (D-18, D-19)."
- [ ] "Settings file deleted/missing during a watcher reload keeps current active config and records a recoverable reload failure (D-22)."
- [ ] "Save uses the file store seam writing to a temp file then File.Replace/File.Move so partial settings files are unlikely (D-10)."
- [ ] "Reload validates external YAML into a candidate before replacing in-memory config; invalid YAML is rejected without state mutation (D-11)."
- [ ] "Race tests do not wait on the production 500 ms watcher throttle or arbitrary sleeps; they submit operations directly and use the fake file store (D-32, D-36, D-39)."
- [ ] "D-34: Coordinator tests are outcome-first: final active config, on-disk config, failure signal, and protected ordering are asserted without over-specifying every queue step."
- [ ] "D-35: Targeted guards prove YAML-free clone paths and deterministic race tests avoid production throttle waits."
- [ ] "D-38: Race matrix covers save/flush/reload overlap, app-save external edit, cleaning deferral, invalid/missing YAML, and recovery."

## Files

- `AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs`
- `AutoQAC/Services/Configuration/ConfigPersistenceOperation.cs`
- `AutoQAC/Services/Configuration/ConfigPersistenceStatus.cs`
- `AutoQAC/Services/Configuration/IUserConfigFileStore.cs`
- `AutoQAC/Services/Configuration/UserConfigFileStore.cs`
- `AutoQAC/AutoQAC.csproj`
- `AutoQAC.Tests/Services/Configuration/ConfigPersistenceCoordinatorTests.cs`
- `AutoQAC.Tests/Services/Configuration/Fakes/FakeUserConfigFileStore.cs`
- `AutoQAC.Tests/Services/Configuration/UserConfigFileStoreTests.cs`
