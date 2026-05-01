---
phase: 10-configuration-persistence-hardening
reviewed: 2026-05-01T00:57:23Z
depth: deep
files_reviewed: 27
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
  - AutoQAC/ViewModels/SettingsViewModel.cs
  - AutoQAC/Views/SettingsWindow.axaml
  - AutoQAC.Tests/Models/UserConfigurationCopyTests.cs
  - AutoQAC.Tests/Services/Cleaning/CleaningPreflightTests.cs
  - AutoQAC.Tests/Services/ConfigWatcherServiceTests.cs
  - AutoQAC.Tests/Services/Configuration/Fakes/FakeUserConfigFileStore.cs
  - AutoQAC.Tests/Services/Configuration/ConfigPersistenceCoordinatorTests.cs
  - AutoQAC.Tests/Services/Configuration/UserConfigFileStoreTests.cs
  - AutoQAC.Tests/Services/ConfigurationServiceTests.cs
  - AutoQAC.Tests/ViewModels/SettingsViewModelTests.cs
findings:
  critical: 1
  warning: 3
  info: 0
  total: 4
status: issues_found
---

# Phase 10: Code Review Report

**Reviewed:** 2026-05-01T00:57:23Z
**Depth:** deep
**Files Reviewed:** 27
**Status:** issues_found

## Summary

Deep review traced the new configuration persistence flow across DI registration, coordinator operation handling, configuration facade state, Settings UI subscriptions, file watching, and related tests. The main correctness risk is that an explicit reload can proceed after a failed pending-save flush and then report success, which can clear the facade's pending-save marker and drop unsaved user edits. Additional robustness issues make transient read failures sticky, allow unexpected coordinator exceptions to hang callers, and leave watcher smoke coverage timing-dependent.

## Critical Issues

### CR-01: BLOCKER - Explicit reload masks a failed pending-save flush and can drop user edits

**File:** `AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs:350-362`

**Issue:** `ApplyReloadRequestAsync` correctly tries to flush a pending app save before reading disk, but it ignores a failed/rejected flush and continues to `ReadForReloadAsync`/`ValidateAndApply`. If the reload then succeeds with the old disk content, the public `ConfigurationService.ReloadFromDiskAsync` sees a successful reload and clears `_hasPendingUserSave` (`ConfigurationService.cs:560-569`). That sequence loses the user's pending in-memory edit after the prerequisite write failed.

**Fix:** Treat a failed prerequisite flush as the reload result and do not continue to disk reload.

```csharp
private async Task ApplyReloadRequestAsync(ReloadRequest reload, CancellationToken ct)
{
    if (_pendingApp != null)
    {
        _logger.Information("[ConfigPersistence] Flushing pending app save before explicit reload");
        var flushResult = await FlushPendingInsideConsumerAsync(ct).ConfigureAwait(false);
        SafePublishResult(flushResult);

        if (flushResult.Status is ConfigPersistenceStatusKind.Failed or ConfigPersistenceStatusKind.Rejected)
        {
            reload.Completion.TrySetResult(flushResult);
            return;
        }
    }

    var read = await ReadForReloadAsync(ConfigPersistenceOperationKind.Reload, ct).ConfigureAwait(false);
    var result = read.Result ?? ValidateAndApply(read.ReadResult, ConfigPersistenceOperationKind.Reload);
    SafePublishResult(result);
    reload.Completion.TrySetResult(result);
}
```

## Warnings

### WR-01: WARNING - Failed initial user-config reload is marked as loaded, preventing automatic retry

**File:** `AutoQAC/Services/Configuration/ConfigurationService.cs:184-195`

**Issue:** `LoadUserConfigAsync` sets `_loadedUserConfigFromDisk = true` even when `ReloadFromDiskAsync` returns `Failed`. A transient read failure or temporarily invalid YAML therefore poisons the facade state: later `LoadUserConfigAsync` calls skip reload and keep returning the coordinator's default/last active config until some separate explicit reload or watcher event occurs.

**Fix:** Only mark the initial disk load complete after a successful reload; leave it false on failure so the next load retries.

```csharp
var result = await _coordinator.ReloadFromDiskAsync(ct).ConfigureAwait(false);
if (result.Status == ConfigPersistenceStatusKind.Failed)
{
    _logger.Warning("[Config] Initial user configuration reload failed: {Summary}", result.Failure?.SafeSummary ?? "unknown");
}
else
{
    lock (_stateLock)
    {
        _loadedUserConfigFromDisk = true;
    }
}
```

### WR-02: WARNING - Unexpected coordinator operation exceptions can leave public callers waiting forever

**File:** `AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs:217-220`

**Issue:** The single-reader loop catches and logs all unexpected operation exceptions but does not complete the operation's `TaskCompletionSource`. If an unexpected exception occurs inside an operation that has a caller-visible completion (for example a malformed config graph causing `ApplySaveAsync` to throw before `intent.Completion.TrySetResult()` at `ConfigPersistenceCoordinator.cs:242`), `SaveUserConfigAsync`, `FlushPendingSavesAsync`, or `ReloadFromDiskAsync` can hang indefinitely.

**Fix:** Complete operation-specific TCS objects with an exception or typed failure when the dispatcher catches an unexpected exception.

```csharp
catch (Exception ex)
{
    _logger.Error(ex, "[ConfigPersistence] Operation handler threw");
    CompleteOperationAsFailed(op, ex);
}

private void CompleteOperationAsFailed(ConfigPersistenceOperation op, Exception ex)
{
    var failure = CreateFailure(ConfigPersistenceOperationKind.Flush,
        ConfigPersistenceFailureKind.Unknown,
        "Configuration persistence operation failed (unknown)");

    switch (op)
    {
        case SaveIntent save:
            save.Completion.TrySetException(ex);
            break;
        case FlushBarrier flush:
            flush.Completion.TrySetResult(new ConfigPersistenceResult(ConfigPersistenceStatusKind.Failed, ConfigPersistenceOperationKind.Flush, _appGeneration, failure));
            break;
        case ReloadRequest reload:
            reload.Completion.TrySetResult(new ConfigPersistenceResult(ConfigPersistenceStatusKind.Failed, ConfigPersistenceOperationKind.Reload, _appGeneration, failure));
            break;
    }
}
```

### WR-03: WARNING - File-system watcher smoke test is timing-dependent and flaky

**File:** `AutoQAC.Tests/Services/ConfigWatcherServiceTests.cs:45-51`

**Issue:** The test writes the settings file, sleeps for two seconds, and then asserts that at least one watcher notification arrived. `FileSystemWatcher` delivery varies under CI load and Windows file-system timing, so this can fail intermittently even when production behavior is correct.

**Fix:** Use a bounded signal from the substitute callback instead of a fixed delay.

```csharp
var signaled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
coordinator
    .When(c => c.NotifySettingsFileChanged(Arg.Any<ConfigFileSignalKind>()))
    .Do(_ => signaled.TrySetResult());

await File.WriteAllTextAsync(path, "Selected_Game: Unknown\n");
await signaled.Task.WaitAsync(TimeSpan.FromSeconds(5));

coordinator.Received().NotifySettingsFileChanged(Arg.Any<ConfigFileSignalKind>());
```

### WR-04: WARNING - Public path validation assumes all nested config objects are non-null

**File:** `AutoQAC/Services/Configuration/ConfigurationService.cs:243-266`

**Issue:** `ValidatePathsAsync` dereferences `config.LoadOrder`, `config.LoadOrderFileOverrides`, `config.XEdit`, `config.Settings`, and `config.ModOrganizer` directly. Other Phase 10 code explicitly normalizes null nested configuration objects in `UserConfiguration.Copy()`, so this public method is now inconsistent: callers passing a deserialized or partially constructed `UserConfiguration` with null nested objects can crash validation instead of receiving a safe `false` result.

**Fix:** Normalize or null-check at the method boundary.

```csharp
public Task<bool> ValidatePathsAsync(UserConfiguration config, CancellationToken ct = default)
{
    ThrowIfDisposed();
    ct.ThrowIfCancellationRequested();

    config = (config ?? new UserConfiguration()).Copy();
    var isValid = true;
    // existing validation follows using normalized nested objects/collections
}
```

---

_Reviewed: 2026-05-01T00:57:23Z_
_Reviewer: the agent (gsd-code-reviewer)_
_Depth: deep_
