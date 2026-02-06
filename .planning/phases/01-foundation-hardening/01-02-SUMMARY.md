---
phase: 01-foundation-hardening
plan: 02
subsystem: state-configuration
tags: [deadlock-fix, debounced-saves, rx-throttle, config-flush, state-service]

dependency_graph:
  requires:
    - "01-01 (process termination hardening -- CleanOrphanedProcessesAsync call order)"
  provides:
    - "Deadlock-free StateService with lock-free emission pattern"
    - "Debounced config saves via Rx Throttle (500ms) with retry and revert-on-failure"
    - "FlushPendingSavesAsync for pre-clean and app-shutdown config persistence"
    - "IsTerminatingChanged observable for future UI termination spinner"
  affects:
    - "Future UI plan for Stopping... spinner (consumes IsTerminatingChanged)"
    - "Any future plan that tests cross-instance ConfigurationService persistence (must call FlushPendingSavesAsync)"

tech_stack:
  added: []
  patterns:
    - "Capture-inside-lock-emit-outside pattern for BehaviorSubject deadlock avoidance"
    - "Separate _currentState field as authoritative state (volatile reference) vs BehaviorSubject for notification only"
    - "Rx Throttle (500ms) debounce pipeline for config disk writes"
    - "Retry with revert-on-failure for config persistence (3 attempts)"
    - "Fire-and-forget async save from Rx Subscribe with internal error handling"

key_files:
  created: []
  modified:
    - "AutoQAC/Services/State/IStateService.cs"
    - "AutoQAC/Services/State/StateService.cs"
    - "AutoQAC/Services/Configuration/IConfigurationService.cs"
    - "AutoQAC/Services/Configuration/ConfigurationService.cs"
    - "AutoQAC/Services/Cleaning/CleaningOrchestrator.cs"
    - "AutoQAC/App.axaml.cs"
    - "AutoQAC.Tests/Services/ConfigurationServiceTests.cs"
    - "AutoQAC.Tests/Services/ConfigurationServiceSkipListTests.cs"
    - "AutoQAC.Tests/Integration/GameSelectionIntegrationTests.cs"

decisions:
  - id: "authoritative-state-field"
    description: "Added private _currentState field as authoritative state source, separate from BehaviorSubject"
    rationale: "BehaviorSubject.Value lags behind when OnNext is called outside the lock. Concurrent UpdateState calls must read-modify-write against the locked _currentState field, not the BehaviorSubject, to prevent lost updates."
  - id: "immediate-config-event-emission"
    description: "ConfigChanges.OnNext fires immediately in SaveUserConfigAsync, not deferred to debounce"
    rationale: "Subscribers need responsive updates for UI reactivity. Disk write is deferred but in-memory state and notifications are immediate."
  - id: "reference-equality-pending-clear"
    description: "Use ReferenceEquals to clear _pendingConfig only if it matches the config just written"
    rationale: "Prevents race where a newer save request comes in during the write, which would incorrectly clear the still-pending config."

metrics:
  duration: "8 minutes"
  completed: "2026-02-06"
  tasks: 3
  tests_affected: 4
  tests_total: 428
  tests_passing: 428
---

# Phase 1 Plan 2: State Deadlock Fix and Debounced Config Saves Summary

**One-liner:** Deadlock-free StateService emission with separate authoritative state field, plus Rx Throttle debounced config saves with retry/revert and pre-clean/shutdown flush.

## What Was Done

### Task 1: Fix StateService Deadlock

- Moved `_stateSubject.OnNext(newState)` OUTSIDE the lock in `UpdateState` to prevent subscriber deadlocks when callbacks read `CurrentState`
- Introduced `_currentState` volatile field as the authoritative state inside the lock -- concurrent `UpdateState` calls read-modify-write against this field, not `BehaviorSubject.Value` which lags behind
- Removed lock from `CurrentState` getter -- reads a volatile reference, no lock needed
- Added `IsTerminatingChanged` observable (`BehaviorSubject<bool>`) and `SetTerminating(bool)` method to `IStateService` and `StateService` as foundation for future UI termination spinner
- Added `_isTerminatingSubject` disposal in `Dispose` method
- All 428 tests pass including concurrent state update tests

### Task 2: Debounced Config Saves with Retry, Revert, and Flush

- Added `System.Reactive.Linq` import for `Throttle` operator
- Added `_saveRequests` Subject, `_debounceSubscription`, `_lastKnownGoodConfig`, and `_pendingConfig` fields
- Constructor sets up Rx Throttle pipeline: `_saveRequests.Throttle(500ms).Subscribe(SaveToDiskWithRetryAsync)`
- `SaveUserConfigAsync` now stores pending config in memory, emits `_configChanges.OnNext` immediately, and schedules debounced disk write via `_saveRequests.OnNext`
- `LoadUserConfigAsync` returns `_pendingConfig` if available (ensures read-after-write consistency within same instance)
- Sets `_lastKnownGoodConfig` on successful disk load
- `SaveToDiskWithRetryAsync`: 3 attempts (initial + 2 retries) with 100ms delay between retries. On all retries exhausted, reverts in-memory to `_lastKnownGoodConfig` and emits notification
- Uses `ReferenceEquals` to clear `_pendingConfig` only if it matches the config just written (prevents race with newer save requests)
- `FlushPendingSavesAsync`: forces immediate disk write of pending config, bypassing debounce
- Added `FlushPendingSavesAsync` to `IConfigurationService` interface
- Dispose cleans up `_debounceSubscription` and `_saveRequests`
- Updated 4 cross-instance persistence tests to call `FlushPendingSavesAsync` before verifying disk state

### Task 3: Pre-Clean and App-Shutdown Flush

- Added `FlushPendingSavesAsync(ct)` call in `CleaningOrchestrator.StartCleaningAsync` after orphan cleanup and before config validation -- guarantees xEdit always runs with up-to-date config on disk
- Added `ShutdownRequested` handler in `App.axaml.cs` that calls `FlushPendingSavesAsync().GetAwaiter().GetResult()` -- synchronous wait is acceptable during app shutdown since UI thread is winding down
- Handler catches exceptions to prevent blocking shutdown
- All 428 tests pass

## Decisions Made

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Authoritative state field | Separate `_currentState` volatile field | BehaviorSubject.Value lags when OnNext is outside lock; concurrent UpdateState needs locked source of truth |
| Config event timing | Immediate on SaveUserConfigAsync | UI subscribers need responsive updates; disk write is deferred but in-memory state is always current |
| Pending config clear | ReferenceEquals check | Prevents race where newer save during write incorrectly clears still-pending config |
| Debounce timing | 500ms (from CONTEXT.md) | Batches 2-3 rapid setting toggles; pre-clean flush ensures no stale config for xEdit |
| Shutdown flush pattern | GetAwaiter().GetResult() | ShutdownRequested is synchronous; safe during shutdown when UI thread is winding down |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed concurrent UpdateState lost-update race condition**

- **Found during:** Task 1
- **Issue:** Moving OnNext outside the lock caused concurrent `UpdateState` calls to read stale `_stateSubject.Value` (since OnNext hadn't fired yet), losing updates in the concurrent `ConcurrentAddCleaningResult_ShouldNotLoseUpdates` test
- **Fix:** Introduced `_currentState` volatile field as authoritative state. `UpdateState` reads and writes `_currentState` inside the lock, then emits `_stateSubject.OnNext` outside for notification only
- **Files modified:** `AutoQAC/Services/State/StateService.cs`
- **Commit:** 794ca2a

**2. [Rule 3 - Blocking] Updated 4 cross-instance persistence tests for debounced saves**

- **Found during:** Task 2
- **Issue:** Tests that create a second `ConfigurationService` instance to verify disk persistence failed because debounced writes hadn't flushed yet
- **Fix:** Added `await service.FlushPendingSavesAsync()` before creating the second instance in 4 tests
- **Files modified:** `AutoQAC.Tests/Services/ConfigurationServiceTests.cs`, `AutoQAC.Tests/Services/ConfigurationServiceSkipListTests.cs`, `AutoQAC.Tests/Integration/GameSelectionIntegrationTests.cs`
- **Commit:** a3a6266

## Task Commits

| Task | Name | Commit | Key Files |
|------|------|--------|-----------|
| 1 | Fix StateService deadlock + termination observable | 794ca2a | IStateService.cs, StateService.cs |
| 2 | Debounced config saves with retry/revert/flush | a3a6266 | IConfigurationService.cs, ConfigurationService.cs, 3 test files |
| 3 | Pre-clean flush + app-shutdown flush | 4b9648e | CleaningOrchestrator.cs, App.axaml.cs |

## Verification Results

1. `dotnet build` -- 0 errors, 0 warnings
2. `dotnet test` -- 428 passed, 0 failed, 0 skipped
3. `StateService.UpdateState` emits `OnNext` OUTSIDE the lock (line 63, lock ends line 61)
4. `StateService.CurrentState` getter does NOT acquire the lock (reads `_currentState` directly)
5. `ConfigurationService.SaveUserConfigAsync` does NOT write to disk immediately (stores pending + schedules debounce)
6. `ConfigurationService.FlushPendingSavesAsync` forces immediate write via `SaveToDiskWithRetryAsync`
7. `ConfigurationService` retries 3 times (initial + 2), reverts to `_lastKnownGoodConfig` on failure
8. `CleaningOrchestrator.StartCleaningAsync` calls `FlushPendingSavesAsync` before validation (line 80)
9. `App.axaml.cs` calls `FlushPendingSavesAsync` on `ShutdownRequested` event (line 60)
10. Concurrent `UpdateState` calls do not lose updates (verified by `ConcurrentAddCleaningResult_ShouldNotLoseUpdates` test)

## Next Phase Readiness

**Blockers:** None

**Notes for future plans:**
- `IsTerminatingChanged` observable is available but not yet consumed by any ViewModel -- wire it in the Stopping... spinner UI plan
- Tests that verify cross-instance ConfigurationService persistence must call `FlushPendingSavesAsync` before creating the second instance
- The debounce subscription uses fire-and-forget (`_ = SaveToDiskWithRetryAsync(config)`); errors are handled internally with retry+revert

## Self-Check: PASSED
