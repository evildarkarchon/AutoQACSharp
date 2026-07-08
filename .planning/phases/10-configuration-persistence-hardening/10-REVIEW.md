---
phase: 10-configuration-persistence-hardening
reviewed: 2026-04-30T00:00:00Z
depth: deep
files_reviewed: 27
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
  critical: 0
  warning: 3
  info: 0
  total: 3
status: issues_found
---

# Phase 10: Code Review Report

**Reviewed:** 2026-04-30T00:00:00Z
**Depth:** deep
**Files Reviewed:** 27
**Status:** issues_found

## Summary

Reviewed the configuration persistence hardening implementation, including the new coordinator/file-store flow, Settings banner subscriptions, pre-cleaning flush gate, watcher forwarding, and the changed tests. No blocker-level issue was proven, but there are correctness and reliability risks around validation, watcher disposal, and a flaky FileSystemWatcher smoke test.

## Warnings

### WR-01: MO2 mode with an empty executable path is reported as valid

**File:** `AutoQAC/Services/Configuration/ConfigurationService.cs:261-267`
**Issue:** `ValidatePathsAsync` only rejects an MO2 executable when `Mo2Mode` is enabled *and* `ModOrganizer.Binary` is non-empty but missing. If MO2 mode is enabled with `null`, empty, or whitespace binary, the method returns `true` as long as other paths are valid, even though `CleaningPreflight` later blocks the same configuration as invalid. This inconsistent public validation result can let UI/future callers accept a configuration that cannot launch cleaning.
**Fix:** Treat missing MO2 binary as invalid whenever MO2 mode is enabled, then separately validate existence.

```csharp
if (config.Settings.Mo2Mode)
{
    if (string.IsNullOrWhiteSpace(config.ModOrganizer.Binary))
    {
        _logger.Warning("MO2 mode is enabled but no MO2 binary is configured");
        isValid = false;
    }
    else if (!File.Exists(config.ModOrganizer.Binary))
    {
        _logger.Warning("MO2 binary not found: {Path}", config.ModOrganizer.Binary);
        isValid = false;
    }
}
```

### WR-02: Watcher event callbacks can throw after the coordinator is disposed

**File:** `AutoQAC/Services/Configuration/ConfigWatcherService.cs:78-85`
**Issue:** The `FileSystemWatcher` event handlers call `_coordinator.NotifySettingsFileChanged(...)` directly. That method throws `ObjectDisposedException` once the coordinator is stopped/disposed. Shutdown normally disposes the watcher before the service provider, but queued watcher events, alternate disposal order in tests/tools, or future lifecycle changes can surface an unhandled callback exception from a background filesystem event instead of safely dropping the late signal.
**Fix:** Route notifications through a small guarded helper that catches `ObjectDisposedException` and logs/drops the event during shutdown.

```csharp
private void Notify(ConfigFileSignalKind kind)
{
    try
    {
        _coordinator.NotifySettingsFileChanged(kind);
    }
    catch (ObjectDisposedException)
    {
        _logger.Debug("[ConfigWatcher] Dropped {Kind} signal after coordinator disposal", kind);
    }
}

// usage
watcher.Changed += (_, _) => Notify(ConfigFileSignalKind.Changed);
watcher.Created += (_, _) => Notify(ConfigFileSignalKind.Created);
watcher.Renamed += (_, _) => Notify(ConfigFileSignalKind.Renamed);
watcher.Deleted += (_, _) => Notify(ConfigFileSignalKind.Deleted);
```

### WR-03: Config watcher smoke test relies on a fixed two-second sleep

**File:** `AutoQAC.Tests/Services/ConfigWatcherServiceTests.cs:47-51`
**Issue:** `FileSystemEvent_TriggersCoordinatorNotification_Smoke` waits with `Task.Delay(2000)` and then asserts the substitute received a notification. This makes the test suite timing-dependent: a slow filesystem/CI run can fail even if the watcher is correct, while fast failures still cost two seconds. This affects test reliability and will make regressions harder to diagnose.
**Fix:** Use a `TaskCompletionSource` in the substitute callback and wait for that signal with a bounded timeout instead of sleeping unconditionally.

```csharp
var notified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
coordinator
    .When(c => c.NotifySettingsFileChanged(Arg.Any<ConfigFileSignalKind>()))
    .Do(_ => notified.TrySetResult());

await File.WriteAllTextAsync(path, "Selected_Game: Unknown\n");
await notified.Task.WaitAsync(TimeSpan.FromSeconds(2));

coordinator.Received().NotifySettingsFileChanged(Arg.Any<ConfigFileSignalKind>());
```

---

_Reviewed: 2026-04-30T00:00:00Z_
_Reviewer: the agent (gsd-code-reviewer)_
_Depth: deep_
