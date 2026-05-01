---
phase: 10-configuration-persistence-hardening
reviewed: 2026-05-01T01:19:20Z
depth: deep
files_reviewed: 26
files_reviewed_list:
  - AutoQAC/Models/Configuration/UserConfiguration.cs
  - AutoQAC/Models/Configuration/BackupSettings.cs
  - AutoQAC/Models/Configuration/RetentionSettings.cs
  - AutoQAC.Tests/Models/UserConfigurationCopyTests.cs
  - AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs
  - AutoQAC/Services/Configuration/ConfigPersistenceOperation.cs
  - AutoQAC/Services/Configuration/ConfigPersistenceStatus.cs
  - AutoQAC/Services/Configuration/IUserConfigFileStore.cs
  - AutoQAC/Services/Configuration/UserConfigFileStore.cs
  - AutoQAC/AutoQAC.csproj
  - AutoQAC.Tests/Services/Configuration/ConfigPersistenceCoordinatorTests.cs
  - AutoQAC.Tests/Services/Configuration/Fakes/FakeUserConfigFileStore.cs
  - AutoQAC.Tests/Services/Configuration/UserConfigFileStoreTests.cs
  - AutoQAC/Services/Configuration/IConfigurationService.cs
  - AutoQAC/Services/Configuration/IConfigPersistenceCoordinator.cs
  - AutoQAC/Services/Configuration/ConfigurationService.cs
  - AutoQAC/Services/Configuration/IConfigWatcherService.cs
  - AutoQAC/Services/Configuration/ConfigWatcherService.cs
  - AutoQAC/Infrastructure/ServiceCollectionExtensions.cs
  - AutoQAC.Tests/Services/ConfigurationServiceTests.cs
  - AutoQAC.Tests/Services/ConfigWatcherServiceTests.cs
  - AutoQAC/Services/Cleaning/CleaningPreflight.cs
  - AutoQAC.Tests/Services/Cleaning/CleaningPreflightTests.cs
  - AutoQAC/ViewModels/SettingsViewModel.cs
  - AutoQAC/Views/SettingsWindow.axaml
  - AutoQAC.Tests/ViewModels/SettingsViewModelTests.cs
findings:
  critical: 1
  warning: 2
  info: 0
  total: 3
status: issues_found
---

# Phase 10: Code Review Report

**Reviewed:** 2026-05-01T01:19:20Z
**Depth:** deep
**Files Reviewed:** 26
**Status:** issues_found

## Summary

Deep review traced the configuration persistence flow across the watcher, coordinator, facade, cleaning preflight, and Settings UI. The central coordinator design is mostly coherent, but the facade now bypasses the coordinator barrier in a way that can let cleaning proceed before already-queued watcher reloads are processed. Two additional robustness gaps can cause external settings edits to be missed or transient reload failures to become sticky.

Focused Phase 10 tests were run as a sanity check and passed, but the findings below are edge cases not covered by the current tests.

## Critical Issues

### CR-01: BLOCKER — Facade flush short-circuit skips the coordinator barrier, so cleaning can launch with stale external config

**File:** `AutoQAC/Services/Configuration/ConfigurationService.cs:219-222`

**Issue:** `ConfigurationService.FlushPendingSavesAsync` returns `NoOp` immediately when `_hasPendingUserSave` is false. That bypasses the coordinator channel entirely, so it does not drain operations already queued ahead of the flush call. This breaks the barrier semantics relied on by `CleaningPreflight.PrepareAsync` (`CleaningPreflight.cs:34-36`): a `FileSystemWatcher` event can enqueue a watcher reload (`ConfigWatcherService.cs:78-81`), then cleaning can call `FlushPendingSavesAsync`, receive the facade-level `NoOp`, and continue using the old active configuration because the queued watcher reload has not been processed yet. This can launch xEdit with stale MO2/backup/skip-list settings after a manual YAML edit.

**Fix:** Always enqueue a coordinator flush barrier so previously queued operations drain in order. The facade can still clear its pending-save flag only when the coordinator reports success/no-op.

```csharp
public async Task<ConfigPersistenceResult> FlushPendingSavesAsync(CancellationToken ct = default)
{
    ThrowIfDisposed();
    await _consumerTask.ConfigureAwait(false);

    var result = await _coordinator.FlushPendingSavesAsync(ct).ConfigureAwait(false);
    if (result.Status is ConfigPersistenceStatusKind.Success or ConfigPersistenceStatusKind.NoOp)
    {
        lock (_stateLock)
        {
            _hasPendingUserSave = false;
        }
    }

    return result;
}
```

Add a regression test that queues a watcher notification, immediately calls the facade `FlushPendingSavesAsync` with no app save pending, and asserts the external config is applied before the flush returns.

## Warnings

### WR-01: WARNING — FileSystemWatcher error events are logged but never trigger recovery

**File:** `AutoQAC/Services/Configuration/ConfigWatcherService.cs:82`

**Issue:** The watcher `Error` handler only logs the exception. `FileSystemWatcher.Error` is raised for conditions such as internal buffer overflow, where one or more file changes may have been dropped. Because no signal reaches the coordinator, the app can permanently miss a manual settings edit until another file event happens. `ConfigFileSignalKind.Error` already exists (`ConfigPersistenceOperation.cs:26`) but is unused.

**Fix:** Forward an error signal to the coordinator after logging so it can re-hash/re-read the authoritative file content. Wrap the call defensively so shutdown/disposal races do not throw from the FSW callback.

```csharp
watcher.Error += (_, e) =>
{
    _logger.Error(e.GetException(), "[ConfigWatcher] FSW error");
    try
    {
        _coordinator.NotifySettingsFileChanged(ConfigFileSignalKind.Error);
    }
    catch (ObjectDisposedException)
    {
        // Shutdown race: the watcher is being torn down, so there is no coordinator to notify.
    }
};
```

### WR-02: WARNING — Failed initial user-config reload is marked as loaded, preventing automatic retry

**File:** `AutoQAC/Services/Configuration/ConfigurationService.cs:186-195`

**Issue:** `LoadUserConfigAsync` sets `_loadedUserConfigFromDisk = true` even when the initial coordinator reload returns `Failed`. After a transient read failure, temporary file lock, or invalid YAML that the user fixes, subsequent `LoadUserConfigAsync` calls will skip the disk reload path and keep returning the old in-memory default/snapshot unless a watcher event or explicit `ReloadFromDiskAsync` occurs. This makes a recoverable startup/read failure sticky through normal load calls.

**Fix:** Only mark the initial disk load complete after a successful reload. Leave `_loadedUserConfigFromDisk` false on failure so the next `LoadUserConfigAsync` retries the disk read.

```csharp
if (!loadedFromDisk && !hasPending)
{
    var result = await _coordinator.ReloadFromDiskAsync(ct).ConfigureAwait(false);
    if (result.Status == ConfigPersistenceStatusKind.Success)
    {
        lock (_stateLock)
        {
            _loadedUserConfigFromDisk = true;
        }
    }
    else
    {
        _logger.Warning("[Config] Initial user configuration reload failed: {Summary}",
            result.Failure?.SafeSummary ?? "unknown");
    }
}
```

---

_Reviewed: 2026-05-01T01:19:20Z_
_Reviewer: the agent (gsd-code-reviewer)_
_Depth: deep_
