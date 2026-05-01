using System;
using System.IO;
using AutoQAC.Infrastructure.Logging;

namespace AutoQAC.Services.Configuration;

/// <summary>
/// Watches the user configuration YAML file for external changes and forwards file-system
/// signals to the persistence coordinator. Ordering, validation, echo skipping, and cleaning-time
/// deferral live in the coordinator after Phase 10 D-08/D-23.
/// </summary>
public sealed class ConfigWatcherService : IConfigWatcherService
{
    private readonly IConfigPersistenceCoordinator _coordinator;
    private readonly ILoggingService _logger;
    private readonly string _configDirectory;

    private FileSystemWatcher? _watcher;

    private const string UserConfigFile = "AutoQAC Settings.yaml";

    /// <summary>
    /// Creates a watcher service that forwards user settings file notifications to the shared persistence coordinator.
    /// </summary>
    public ConfigWatcherService(
        IConfigPersistenceCoordinator coordinator,
        ILoggingService logger,
        string? configDirectory = null)
    {
        _coordinator = coordinator;
        _logger = logger;
        _configDirectory = configDirectory ?? ResolveConfigDirectory();
    }

    private string ResolveConfigDirectory()
    {
        var baseDir = AppContext.BaseDirectory;

#if DEBUG
        var current = new DirectoryInfo(baseDir);
        for (int i = 0; i < 6 && current != null; i++)
        {
            var candidate = Path.Combine(current.FullName, "AutoQAC Data");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }
#endif

        return Path.Combine(baseDir, "AutoQAC Data");
    }

    public void StartWatching()
    {
        if (_watcher != null)
        {
            return;
        }

        if (!Directory.Exists(_configDirectory))
        {
            _logger.Warning("[ConfigWatcher] Config directory does not exist: {Path}", _configDirectory);
            return;
        }

        FileSystemWatcher? watcher = null;
        try
        {
            watcher = new FileSystemWatcher(_configDirectory, UserConfigFile)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
            };

            // D-23: all relevant FileSystemWatcher events are just signals; the coordinator re-reads content and decides.
            watcher.Changed += (_, _) => _coordinator.NotifySettingsFileChanged(ConfigFileSignalKind.Changed);
            watcher.Created += (_, _) => _coordinator.NotifySettingsFileChanged(ConfigFileSignalKind.Created);
            watcher.Renamed += (_, _) => _coordinator.NotifySettingsFileChanged(ConfigFileSignalKind.Renamed);
            watcher.Deleted += (_, _) => _coordinator.NotifySettingsFileChanged(ConfigFileSignalKind.Deleted);
            watcher.Error += (_, e) =>
            {
                _logger.Error(e.GetException(), "[ConfigWatcher] FSW error");
                _coordinator.NotifySettingsFileChanged(ConfigFileSignalKind.Error);
            };

            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
            _logger.Information("[ConfigWatcher] Started watching {File} in {Dir}", UserConfigFile, _configDirectory);
        }
        catch (Exception ex)
        {
            watcher?.Dispose();
            _watcher = null;
            _logger.Error(ex, "[ConfigWatcher] Failed to start file watcher");
        }
    }

    public void StopWatching()
    {
        if (_watcher == null)
        {
            return;
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _watcher = null;
        _logger.Information("[ConfigWatcher] Stopped watching config file");
    }

    public void Dispose()
    {
        StopWatching();
    }
}
