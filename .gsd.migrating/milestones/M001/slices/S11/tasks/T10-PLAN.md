# T10: 10-configuration-persistence-hardening 10

**Slice:** S11 — **Milestone:** M001

## Description

Close the remaining Phase 10 verification gap where watcher reloads can be silently dropped when hashing the settings file races an external writer lock.

Purpose: Phase 10 requires reliable save/reload behavior under race conditions through one serialized, typed persistence flow. A watcher hash exception must become an observable recoverable failure, not a top-level log-only operation drop.

Output: A TDD gap-closure regression and coordinator fix proving a transient `ComputeHashAsync` failure emits a typed `Watcher`/`ReadFailed` result and the coordinator still processes a later valid watcher reload.

## Must-Haves

- [ ] "Watcher reload hash-read failures publish a typed Watcher/ReadFailed result instead of being logged and dropped."
- [ ] "A transient watcher hash failure does not stop the single-reader coordinator loop from processing a later valid watcher reload."
- [ ] "Maintainer can verify the hash-read race deterministically without FileSystemWatcher timing or production debounce waits."

## Files

- `AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs`
- `AutoQAC.Tests/Services/Configuration/ConfigPersistenceCoordinatorTests.cs`
- `AutoQAC.Tests/Services/Configuration/Fakes/FakeUserConfigFileStore.cs`
