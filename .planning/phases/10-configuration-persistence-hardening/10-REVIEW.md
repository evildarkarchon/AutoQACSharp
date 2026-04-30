---
phase: 10-configuration-persistence-hardening
reviewed: 2026-04-30T00:00:00Z
depth: standard
files_reviewed: 28
files_reviewed_list:
  - AutoQAC.Tests/Integration/DependencyInjectionTests.cs
  - AutoQAC.Tests/Models/UserConfigurationCopyTests.cs
  - AutoQAC.Tests/Services/Cleaning/CleaningPreflightTests.cs
  - AutoQAC.Tests/Services/CleaningOrchestratorTests.cs
  - AutoQAC.Tests/Services/Configuration/ConfigPersistenceCoordinatorTests.cs
  - AutoQAC.Tests/Services/Configuration/Fakes/FakeUserConfigFileStore.cs
  - AutoQAC.Tests/Services/Configuration/UserConfigFileStoreTests.cs
  - AutoQAC.Tests/Services/ConfigurationServiceTests.cs
  - AutoQAC.Tests/Services/ConfigWatcherServiceTests.cs
  - AutoQAC.Tests/Services/ProcessExecutionServiceTests.cs
  - AutoQAC.Tests/ViewModels/SettingsViewModelTests.cs
  - AutoQAC/Infrastructure/ServiceCollectionExtensions.cs
  - AutoQAC/Models/Configuration/BackupSettings.cs
  - AutoQAC/Models/Configuration/RetentionSettings.cs
  - AutoQAC/Models/Configuration/UserConfiguration.cs
  - AutoQAC/Services/Cleaning/CleaningPreflight.cs
  - AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs
  - AutoQAC/Services/Configuration/ConfigPersistenceOperation.cs
  - AutoQAC/Services/Configuration/ConfigPersistenceStatus.cs
  - AutoQAC/Services/Configuration/ConfigurationService.cs
  - AutoQAC/Services/Configuration/ConfigWatcherService.cs
  - AutoQAC/Services/Configuration/IConfigPersistenceCoordinator.cs
  - AutoQAC/Services/Configuration/IConfigurationService.cs
  - AutoQAC/Services/Configuration/IUserConfigFileStore.cs
  - AutoQAC/Services/Configuration/UserConfigFileStore.cs
  - AutoQAC/ViewModels/SettingsViewModel.cs
  - AutoQAC/Views/SettingsWindow.axaml
findings:
  critical: 2
  warning: 3
  info: 0
  total: 5
status: issues_found
---

# Phase 10: Code Review Report

**Reviewed:** 2026-04-30T00:00:00Z
**Depth:** standard
**Files Reviewed:** 28
**Status:** issues_found

## Summary

Reviewed the configuration persistence hardening changes, including the coordinator, file store, configuration facade, preflight flush gate, Settings ViewModel banner flow, AXAML binding, and related tests. The main risks are correctness failures in the coordinator/facade under exceptional or concurrent conditions: observer exceptions can wedge save/flush callers indefinitely, and manual reloads can overwrite or clear pending app saves. Additional validation and test reliability gaps should be fixed before relying on this hardening layer.

## Critical Issues

### CR-01: Observer exceptions can permanently hang save and flush callers

**File:** `AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs:240-242,249-250,281-283,431-438,453-454`

**Issue:** The coordinator calls `Subject<T>.OnNext(...)` inline from the single consumer loop before completing pending operation `TaskCompletionSource`s. If any subscriber throws, `Subject<T>` propagates the exception. `RunAsync` catches it at the outer operation boundary, but the relevant completion is never signaled. For example, a throwing `UserConfigurationChanged` subscriber at line 240 prevents `intent.Completion.TrySetResult()` at line 242, so `SaveUserConfigAsync` hangs forever. A throwing `PersistenceResults` subscriber at line 249 can likewise hang `FlushPendingSavesAsync`, including the pre-cleaning flush barrier. This makes UI/plugin cleaning workflows vulnerable to indefinite hangs caused by one bad observer.

**Fix:** Never let observer callbacks escape the coordinator operation path, and complete request barriers in a `finally` or before best-effort publishing. For example:

```csharp
private void SafeOnNext<T>(ISubject<T> subject, T value, string streamName)
{
    try
    {
        subject.OnNext(value);
    }
    catch (Exception ex)
    {
        _logger.Error(ex, "[ConfigPersistence] {Stream} observer failed", streamName);
    }
}

private Task ApplySaveAsync(SaveIntent intent)
{
    try
    {
        _appGeneration = Math.Max(_appGeneration, intent.Generation);
        _pendingApp = intent.Config.Copy();
        SetActive(intent.Config);
        SafeOnNext(_configurationAccepted, intent.Config.Copy(), nameof(ConfigurationAccepted));
        ScheduleAutoFlush();
        intent.Completion.TrySetResult();
    }
    catch (Exception ex)
    {
        intent.Completion.TrySetException(ex);
    }

    return Task.CompletedTask;
}
```

Apply the same safe-publish/guaranteed-completion pattern to flush, reload, deferred reload, and failure publication paths.

### CR-02: Explicit reload can overwrite pending app saves and clear the pending-save flag

**File:** `AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs:348-353`, `AutoQAC/Services/Configuration/ConfigurationService.cs:539-545`

**Issue:** Watcher reloads correctly reject external content while `_pendingApp` exists, but `ReloadRequest` does not. `ApplyReloadRequestAsync` reads and applies disk content unconditionally, even if an app save is pending in memory. The public `ConfigurationService.ReloadFromDiskAsync` then sets `_hasPendingUserSave = false` regardless of whether a pending save existed or whether reload succeeded. A caller that reloads while a debounced save is pending can lose the in-memory edit and incorrectly mark the service as having no pending save.

**Fix:** Make explicit reload obey the same pending-save protection as watcher reload, or force a flush first and abort reload if the flush fails. Also only clear `_hasPendingUserSave` when a reload is actually accepted.

```csharp
private async Task ApplyReloadRequestAsync(ReloadRequest reload, CancellationToken ct)
{
    if (_pendingApp != null)
    {
        var failure = CreateFailure(
            ConfigPersistenceOperationKind.Reload,
            ConfigPersistenceFailureKind.RaceRejected,
            "Reload rejected because an app save is pending (race_rejected)");
        PublishFailure(failure);
        var rejected = new ConfigPersistenceResult(
            ConfigPersistenceStatusKind.Rejected,
            ConfigPersistenceOperationKind.Reload,
            _appGeneration,
            failure);
        SafeOnNext(_persistenceResults, rejected, nameof(PersistenceResults));
        reload.Completion.TrySetResult(rejected);
        return;
    }

    var read = await ReadForReloadAsync(ConfigPersistenceOperationKind.Reload, ct).ConfigureAwait(false);
    var result = read.Result ?? ValidateAndApply(read.ReadResult, ConfigPersistenceOperationKind.Reload);
    SafeOnNext(_persistenceResults, result, nameof(PersistenceResults));
    reload.Completion.TrySetResult(result);
}
```

In `ConfigurationService.ReloadFromDiskAsync`, clear `_hasPendingUserSave` only for `Success` (and consider preserving it for `Rejected`/`Failed`).

## Warnings

### WR-01: MO2 mode path validation incorrectly accepts missing binary paths

**File:** `AutoQAC/Services/Configuration/ConfigurationService.cs:248-254`

**Issue:** `ValidatePathsAsync` only flags the MO2 binary when `Mo2Mode` is enabled **and** `ModOrganizer.Binary` is non-empty but missing. If MO2 mode is enabled with a null/empty binary, validation returns true. That contradicts the runtime requirement enforced later by `CleaningPreflight` and can let an invalid settings state pass validation paths that rely on `ValidatePathsAsync`.

**Fix:** Treat a blank MO2 binary as invalid whenever MO2 mode is enabled.

```csharp
if (config.Settings.Mo2Mode)
{
    if (string.IsNullOrWhiteSpace(config.ModOrganizer.Binary) || !File.Exists(config.ModOrganizer.Binary))
    {
        _logger.Warning("MO2 binary not found: {Path}", config.ModOrganizer.Binary ?? "<empty>");
        isValid = false;
    }
}
```

### WR-02: Pending-save bookkeeping is not synchronized across concurrent facade calls

**File:** `AutoQAC/Services/Configuration/ConfigurationService.cs:169-172,197-215`

**Issue:** `_hasPendingUserSave` and `_loadedUserConfigFromDisk` are read and written from async public methods without locking or volatile access. A concurrent `SaveUserConfigAsync` and `FlushPendingSavesAsync` can interleave so the flush succeeds and sets `_hasPendingUserSave = false`, then the save continuation sets it back to `true`. Later `LoadUserConfigAsync` will believe a save is pending when the coordinator has already flushed, which can suppress disk reload behavior and make external changes appear stale.

**Fix:** Protect facade state flags with a lock or move this bookkeeping fully into the coordinator. At minimum, update and read them under `_stateLock` after coordinator operations complete.

### WR-03: Empty-file behavior test has no assertion

**File:** `AutoQAC.Tests/Services/ConfigurationServiceTests.cs:459-473`

**Issue:** `LoadUserConfigAsync_ShouldHandleEmptyFile` only calls the method and then performs no assertion. The test can pass even if the service returns null-equivalent defaults, fails to set `LastFailure`, or silently treats an empty file as a missing-file failure. This weakens regression coverage for exactly the invalid/empty YAML path Phase 10 is hardening.

**Fix:** Assert the expected behavior explicitly, e.g. default configuration is returned and `LastFailure.Kind` is `MissingFile` or `InvalidExternalYaml` (whichever contract is intended):

```csharp
var config = await service.LoadUserConfigAsync();

config.Should().NotBeNull();
service.LastFailure.Should().NotBeNull();
service.LastFailure!.Kind.Should().Be(ConfigPersistenceFailureKind.MissingFile);
```

---

_Reviewed: 2026-04-30T00:00:00Z_
_Reviewer: the agent (gsd-code-reviewer)_
_Depth: standard_
