# T11: 10-configuration-persistence-hardening 11

**Slice:** S11 — **Milestone:** M001

## Description

Close the Phase 10 verification regression where production DI can construct `ConfigurationService` through its public logger-only constructor, creating a private coordinator that is disconnected from `ConfigWatcherService`.

Purpose: Phase 10's serialized persistence authority only protects users if the running app uses one coordinator for facade saves/flushes/reloads and watcher signals. The production service graph must wire `IConfigurationService`, `IConfigWatcherService`, and `IConfigPersistenceCoordinator` to the same coordinator instance so external settings changes update the facade-observed result/failure streams.

Output: A TDD gap-closure integration regression and DI wiring fix proving watcher-originated notifications flow through the same `IConfigurationService` facade that Settings UI and cleaning preflight consume.

## Must-Haves

- [ ] "Production DI constructs IConfigurationService and ConfigWatcherService with the same registered IConfigPersistenceCoordinator instance."
- [ ] "Watcher-originated coordinator notifications are observable through the IConfigurationService facade result/failure streams."
- [ ] "Maintainer can verify the shared coordinator wiring through an integration test that fails on constructor-selection regressions."

## Files

- `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs`
- `AutoQAC.Tests/Integration/DependencyInjectionTests.cs`
