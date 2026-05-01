---
phase: 10-configuration-persistence-hardening
reviewed: 2026-05-01T00:00:00Z
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
  warning: 1
  info: 0
  total: 2
status: issues_found
---

# Phase 10: Code Review Report

**Reviewed:** 2026-05-01T00:00:00Z
**Depth:** deep
**Files Reviewed:** 26
**Status:** issues_found

## Summary

Deep review traced the configuration persistence coordinator, watcher, facade, DI wiring, Settings banner, and tests. The prior hash-read CR-01 is closed in the coordinator itself: `ApplyWatcherAsync` now catches `ComputeHashAsync` failures and publishes typed `Watcher/ReadFailed` results. However, the production DI graph does not actually connect `ConfigurationService` to the same coordinator instance as `ConfigWatcherService`, so watcher reloads still do not update the configuration facade in the running app.

## Critical Issues

### CR-01: BLOCKER - ConfigurationService ignores the DI coordinator, so watcher reloads go to an unstarted coordinator

**File:** `AutoQAC/Infrastructure/ServiceCollectionExtensions.cs:30-33`, `AutoQAC/Services/Configuration/ConfigurationService.cs:42-69`

**Issue:** The DI container registers a singleton `ConfigPersistenceCoordinator` and gives that shared instance to `ConfigWatcherService`, but `ConfigurationService` is registered as `AddSingleton<IConfigurationService, ConfigurationService>()`. The injectable coordinator constructor on `ConfigurationService` is `internal` (`ConfigurationService.cs:42`), while the public constructor (`ConfigurationService.cs:68-69`) creates a separate coordinator via `CreateDefaultCoordinator` and a separate `StateService`. Microsoft.Extensions.DependencyInjection selects public constructors, so production `IConfigurationService` uses a private coordinator that the watcher never notifies. The registered coordinator used by `ConfigWatcherService` is never started by `ConfigurationService`, so external YAML changes are queued to the wrong coordinator and do not update `ConfigurationService.UserConfigurationChanged`, `LoadUserConfigAsync`, `LastFailure`, or the Settings banner. This breaks the phase's primary watcher-reload behavior despite the local hash-read fix.

**Fix:** Wire `ConfigurationService` to the registered coordinator explicitly, or make the coordinator-taking constructor public and remove the self-constructed default path from DI. For example:

```csharp
public static IServiceCollection AddConfiguration(this IServiceCollection services)
{
    services.AddSingleton<IUserConfigFileStore, UserConfigFileStore>();
    services.AddSingleton<ConfigPersistenceCoordinator>();
    services.AddSingleton<IConfigPersistenceCoordinator>(sp => sp.GetRequiredService<ConfigPersistenceCoordinator>());
    services.AddSingleton<IConfigurationService>(sp => new ConfigurationService(
        sp.GetRequiredService<IConfigPersistenceCoordinator>(),
        sp.GetRequiredService<ILoggingService>()));
    services.AddSingleton<IConfigWatcherService, ConfigWatcherService>();
    // ...
    return services;
}
```

Add an integration test that builds the real `ServiceCollection`, resolves `IConfigurationService`, `IConfigWatcherService`/`IConfigPersistenceCoordinator`, sends `NotifySettingsFileChanged`, and asserts the same facade observes the accepted external configuration/failure.

## Warnings

### WR-01: WARNING - FileSystemWatcher callbacks can throw during shutdown races

**File:** `AutoQAC/Services/Configuration/ConfigWatcherService.cs:78-85`, `AutoQAC/Services/Configuration/ConfigPersistenceCoordinator.cs:197-200`

**Issue:** File watcher event handlers call `_coordinator.NotifySettingsFileChanged(...)` directly. That method calls `ThrowIfDisposed()`, so any queued `Changed`/`Renamed`/`Error` callback that runs while the app is disposing the coordinator can throw `ObjectDisposedException` out of a `FileSystemWatcher` thread-pool callback. Shutdown normally disposes the watcher first, but Windows file-system events can already be queued; this is a robustness gap in a failure path.

**Fix:** Treat watcher callbacks as best-effort signals and do not let disposal races escape the event handler.

```csharp
private void Forward(ConfigFileSignalKind kind)
{
    try
    {
        _coordinator.NotifySettingsFileChanged(kind);
    }
    catch (ObjectDisposedException)
    {
        // Shutdown can race already-queued FileSystemWatcher callbacks; the signal is no longer actionable.
    }
}
```

Then wire `Changed/Created/Renamed/Deleted/Error` through `Forward(...)` and add a unit test with a coordinator substitute that throws `ObjectDisposedException` from `NotifySettingsFileChanged`.

---

_Reviewed: 2026-05-01T00:00:00Z_
_Reviewer: the agent (gsd-code-reviewer)_
_Depth: deep_
