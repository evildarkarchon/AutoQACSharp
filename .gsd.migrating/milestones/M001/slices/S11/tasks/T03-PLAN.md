# T03: 10-configuration-persistence-hardening 03

**Slice:** S11 — **Milestone:** M001

## Description

Wire the Plan 02 coordinator and Plan 01 model copies into the public configuration surface. `IConfigurationService` continues to expose its existing persistence-slice methods (D-03 boundary preserved), but their bodies become thin delegations to `ConfigPersistenceCoordinator`. `ConfigWatcherService` becomes an event source only (D-08): the Rx throttle, hash echo, deferral logic, and invalid-YAML validation are removed and re-anchored inside the coordinator (already proven by Plan 02). Watcher event handling is expanded to `Changed | Created | Renamed | Deleted` (D-23). Existing service tests continue to pass — they now exercise the coordinator-backed implementation. Watcher tests are reduced to smoke coverage proving the watcher submits signals to the coordinator (D-33).

This plan completes the persistence-slice rewrite while leaving the rest of the public configuration surface (skip-lists, game queries, MO2 path overrides) untouched (D-03, D-15).

Purpose: REF-03 (one serialized flow visible at the public service boundary) and PERF-03 (clone callers in the service swap to model `Copy()`) are completed by this plan. TEST-03 watcher-smoke reduction (D-33) lands here.

Output: Updated `IConfigurationService.cs`, `ConfigurationService.cs`, `IConfigWatcherService.cs` (if needed), `ConfigWatcherService.cs`, `ServiceCollectionExtensions.cs`, plus targeted updates to `ConfigurationServiceTests.cs` (preserve behavior) and `ConfigWatcherServiceTests.cs` (smoke only).

## Must-Haves

- [ ] "IConfigurationService.SaveUserConfigAsync, FlushPendingSavesAsync, LoadUserConfigAsync, ReloadFromDiskAsync, GetLastWrittenHash, UserConfigurationChanged, Failures, and PersistenceResults delegate to ConfigPersistenceCoordinator (D-01, D-02, D-03 — persistence-slice rewrite only)."
- [ ] "FlushPendingSavesAsync now returns a typed Task<ConfigPersistenceResult> (D-05); legacy callers continue to compile because the result is observable via the new return value rather than an out-of-band callback."
- [ ] "IConfigurationService exposes IObservable<ConfigPersistenceFailure> Failures and ConfigPersistenceFailure? LastFailure (D-24, D-27)."
- [ ] "ConfigWatcherService is an event source only: Changed/Created/Renamed/Deleted handlers call the coordinator's NotifySettingsFileChanged with current-file content/hash; the watcher does not own ordering, hash echo skip policy, deferral, or invalid-YAML rejection (D-08, D-23)."
- [ ] "ConfigurationService no longer uses YamlDotNet for cloning: every clone path inside the service uses UserConfiguration.Copy() (D-43, PERF-03)."
- [ ] "AutoQAC/Services/Configuration/ConfigurationService.cs no longer contains the private CloneConfig YAML round-trip method, OR if it remains as a private wrapper it must call .Copy() and not call ISerializer/IDeserializer."
- [ ] "DI registers IConfigPersistenceCoordinator (or the concrete coordinator as a singleton) and IUserConfigFileStore through ServiceCollectionExtensions.AddConfiguration (D-15)."
- [ ] "Existing skip-list/game-query helpers (GetSkipListAsync, GetSelectedGameAsync, etc.) on ConfigurationService remain behaviorally unchanged (D-03 boundary preserved)."
- [ ] "Existing ConfigurationServiceTests (clone independence, concurrent save/load, debounced behavior) continue to pass against the new service implementation."
- [ ] "ConfigWatcherServiceTests is reduced to smoke coverage: it asserts that real watcher events trigger NotifySettingsFileChanged on the coordinator (or a coordinator substitute), not the previous race assertions (D-33)."

## Files

- `AutoQAC/Services/Configuration/IConfigurationService.cs`
- `AutoQAC/Services/Configuration/IConfigPersistenceCoordinator.cs`
- `AutoQAC/Services/Configuration/ConfigurationService.cs`
- `AutoQAC/Services/Configuration/IConfigWatcherService.cs`
- `AutoQAC/Services/Configuration/ConfigWatcherService.cs`
- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`
- `AutoQAC.Tests/Services/ConfigurationServiceTests.cs`
- `AutoQAC.Tests/Services/ConfigWatcherServiceTests.cs`
