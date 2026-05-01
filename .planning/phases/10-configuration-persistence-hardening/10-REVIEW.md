---
phase: 10-configuration-persistence-hardening
reviewed: 2026-04-30T00:00:00Z
depth: deep
files_reviewed: 26
files_reviewed_list:
  - AutoQAC/AutoQAC.csproj
  - AutoQAC/Infrastructure/ServiceCollectionExtensions.cs
  - AutoQAC/Models/Configuration/BackupSettings.cs
  - AutoQAC/Models/Configuration/RetentionSettings.cs
  - AutoQAC/Models/Configuration/UserConfiguration.cs
  - AutoQAC/Services/Cleaning/CleaningPreflight.cs
  - AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs
  - AutoQAC/Services/Configuration/ConfigPersistenceOperation.cs
  - AutoQAC/Services/Configuration/ConfigPersistenceStatus.cs
  - AutoQAC/Services/Configuration/ConfigWatcherService.cs
  - AutoQAC/Services/Configuration/ConfigurationService.cs
  - AutoQAC/Services/Configuration/IConfigPersistenceCoordinator.cs
  - AutoQAC/Services/Configuration/IConfigWatcherService.cs
  - AutoQAC/Services/Configuration/IConfigurationService.cs
  - AutoQAC/Services/Configuration/IUserConfigFileStore.cs
  - AutoQAC/Services/Configuration/UserConfigFileStore.cs
  - AutoQAC.Tests/Models/UserConfigurationCopyTests.cs
  - AutoQAC.Tests/Services/Cleaning/CleaningPreflightTests.cs
  - AutoQAC.Tests/Services/ConfigWatcherServiceTests.cs
  - AutoQAC.Tests/Services/Configuration/ConfigPersistenceCoordinatorTests.cs
  - AutoQAC.Tests/Services/Configuration/Fakes/FakeUserConfigFileStore.cs
  - AutoQAC.Tests/Services/Configuration/UserConfigFileStoreTests.cs
  - AutoQAC.Tests/Services/ConfigurationServiceTests.cs
  - AutoQAC.Tests/ViewModels/SettingsViewModelTests.cs
  - AutoQAC/ViewModels/SettingsViewModel.cs
  - AutoQAC/Views/SettingsWindow.axaml
findings:
  critical: 1
  warning: 4
  info: 0
  total: 5
status: issues_found
---

# Phase 10: Code Review Report

**Reviewed:** 2026-04-30T00:00:00Z
**Depth:** deep
**Files Reviewed:** 26
**Status:** issues_found

## Summary

Reviewed the configuration persistence hardening changes, including the coordinator/file-store pipeline, watcher integration, pre-cleaning flush barrier, Settings dialog banner handling, DI wiring, and related tests. Build and test suite passed, but deep review found correctness gaps around watcher reload failure handling, user-facing failure mapping, and incomplete coverage of newly added settings in the public settings snapshot.

## Critical Issues

### CR-01: BLOCKER - Watcher reloads are silently dropped if hashing races a writer lock

**File:** `AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs:288-290`

**Issue:** `ApplyWatcherAsync` computes the settings-file hash before entering the existing read/reload failure handling path. `UserConfigFileStore.ComputeHashAsync` reads the file directly, so a normal `FileSystemWatcher` timing race where the external editor still has the file locked can throw `IOException`. That exception escapes to the top-level operation-loop catch (`RunAsync` lines 217-220), which only logs and drops the watcher operation. No `ReadFailed`/safe failure is published, no retry is scheduled, and if the OS does not emit another event after the writer releases the file, the external edit is permanently ignored while the app keeps stale active configuration.

**Fix:** Treat hash computation as part of the reload read operation: catch read/hash exceptions, publish a typed `ReadFailed` result, and avoid dropping the operation before observers are notified. For example:

```csharp
private async Task ApplyWatcherAsync(WatcherObserved op, CancellationToken ct)
{
    string? currentHash;
    try
    {
        currentHash = op.CurrentHash ?? await _fileStore.ComputeHashAsync(ct).ConfigureAwait(false);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        _logger.Error(ex, "[ConfigPersistence] Could not hash settings file for watcher reload");
        var failure = CreateFailure(
            ConfigPersistenceOperationKind.Watcher,
            ConfigPersistenceFailureKind.ReadFailed,
            "Could not read settings file (read_failed)");
        PublishFailure(failure);
        SafePublishResult(new ConfigPersistenceResult(
            ConfigPersistenceStatusKind.Failed,
            ConfigPersistenceOperationKind.Watcher,
            _appGeneration,
            failure));
        return;
    }

    // existing echo/pending/cleaning/read logic...
}
```

Add a coordinator test where `ComputeHashAsync` throws once and assert a `ReadFailed` failure/result is emitted and the consumer still processes a subsequent reload.

## Warnings

### WR-01: WARNING - Settings Save failures are reported as cleaning-blocked failures

**File:** `AutoQAC/ViewModels/SettingsViewModel.cs:222-223`

**Issue:** All write failures produced by the coordinator's forced disk barrier currently have `Operation = Flush` (`ConfigPersistenceCoordinator.cs:282`). `SettingsViewModel.SaveAsync` also uses `FlushPendingSavesAsync`, so a Settings-dialog save failure maps to “Could not save settings before cleaning. Cleaning was blocked...” even when the user was only saving Settings. The existing test only checks substrings and misses the incorrect context, so users get a misleading failure banner.

**Fix:** Use a Settings-specific banner in `SaveAsync` when the explicit Settings save flush fails, or add operation context so pre-cleaning flushes and Settings-save flushes map differently. Example local fix:

```csharp
if (flushResult.Status is ConfigPersistenceStatusKind.Failed or ConfigPersistenceStatusKind.Rejected)
{
    PersistenceBannerText = flushResult.Failure?.Kind == ConfigPersistenceFailureKind.WriteFailed
        ? "Could not save settings. Settings were restored to last saved values."
        : flushResult.Failure is not null
            ? MapFailureToBanner(flushResult.Failure)
            : "Could not save settings. Settings were restored to last saved values.";
    CloseRequested?.Invoke(false);
    return;
}
```

Strengthen `SaveAsync_FlushFailure_KeepsDialogOpenAndShowsBanner` to assert the banner does not contain “Cleaning was blocked”.

### WR-02: WARNING - Public settings snapshot omits the new Backup settings

**File:** `AutoQAC/Services/Configuration/ConfigurationService.cs:530-549`

**Issue:** `GetAllSettingsAsync` promises a flat dictionary of all user-facing settings, and Phase 10 adds user-facing backup controls (`Backup.Enabled`, `Backup.MaxSessions`). The method was not updated, so diagnostics/import/export-style callers that rely on this snapshot will silently miss the backup policy currently controlling whether plugin backups run.

**Fix:** Include backup fields in the returned dictionary and add a regression test.

```csharp
return new Dictionary<string, object?>
{
    // existing entries...
    ["Backup.Enabled"] = config.Backup.Enabled,
    ["Backup.MaxSessions"] = config.Backup.MaxSessions,
};
```

### WR-03: WARNING - FileSystemWatcher internal errors never reach the persistence failure pipeline

**File:** `AutoQAC/Services/Configuration/ConfigWatcherService.cs:82`

**Issue:** The watcher `Error` event only logs the exception. The phase adds typed persistence failures and a Settings banner specifically so recoverable persistence problems become visible, but an internal watcher buffer overflow or watcher failure leaves the coordinator and UI unaware that external YAML edits may no longer be observed. The `ConfigFileSignalKind.Error` enum exists but is never used.

**Fix:** Forward watcher errors to the coordinator and handle them as a safe `ReadFailed`/watcher failure result, or restart the watcher after logging. For example:

```csharp
watcher.Error += (_, e) =>
{
    _logger.Error(e.GetException(), "[ConfigWatcher] FSW error");
    _coordinator.NotifySettingsFileChanged(ConfigFileSignalKind.Error);
};
```

Then branch on `ConfigFileSignalKind.Error` in the coordinator to publish a safe recoverable failure instead of attempting a normal hash echo check.

### WR-04: WARNING - Canceled writes can leave temp settings files behind

**File:** `AutoQAC/Services/Configuration/UserConfigFileStore.cs:69-72`

**Issue:** The temp file is created before the `try` block that performs cleanup. If `File.WriteAllTextAsync(tempPath, yaml, ct)` throws after creating/truncating the temp file (for example due to cancellation), the method exits before the cleanup block and leaves `*.tmp` files in `AutoQAC Data`. This is not just cosmetic: stale same-directory temp files can confuse manual recovery and future support diagnostics for config persistence failures.

**Fix:** Enclose the temp write in the cleanup-protected region and preserve cancellation semantics.

```csharp
var tempPath = Path.Combine(directory, Path.GetRandomFileName() + ".tmp");
try
{
    await File.WriteAllTextAsync(tempPath, yaml, ct).ConfigureAwait(false);
    if (File.Exists(settingsPath))
    {
        _replace(tempPath, settingsPath, null);
    }
    else
    {
        _move(tempPath, settingsPath);
    }
}
catch
{
    try
    {
        File.Delete(tempPath);
    }
    catch (Exception cleanupEx)
    {
        _logger.Debug("[ConfigPersistence] Failed to delete temp settings file after write failure: {Message}", cleanupEx.Message);
    }

    throw;
}
```

Add a test with a pre-canceled token or injected writer failure during temp write that asserts no `*.tmp` file remains.

---

_Reviewed: 2026-04-30T00:00:00Z_
_Reviewer: the agent (gsd-code-reviewer)_
_Depth: deep_
