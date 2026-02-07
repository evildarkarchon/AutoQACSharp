using System;
using System.IO;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Security.Cryptography;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.State;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AutoQAC.Services.Configuration;

/// <summary>
/// Watches the user configuration YAML file for external changes using FileSystemWatcher
/// combined with SHA256 content hashing.  Changes detected during an active cleaning
/// session are deferred until the session ends.
/// </summary>
public sealed class ConfigWatcherService : IConfigWatcherService
{
    private readonly IConfigurationService _configService;
    private readonly IStateService _stateService;
    private readonly ILoggingService _logger;
    private readonly string _configDirectory;
    private const string UserConfigFile = "AutoQAC Settings.yaml";

    private FileSystemWatcher? _watcher;
    private readonly CompositeDisposable _disposables = new();
    private readonly IDeserializer _yamlValidator;

    private string? _lastKnownExternalHash;
    private volatile bool _hasDeferredChanges;

    public ConfigWatcherService(
        IConfigurationService configService,
        IStateService stateService,
        ILoggingService logger,
        string? configDirectory = null)
    {
        _configService = configService;
        _stateService = stateService;
        _logger = logger;

        // Resolve config directory using same pattern as ConfigurationService
        _configDirectory = configDirectory ?? ResolveConfigDirectory();

        _yamlValidator = new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
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
                return candidate;
            current = current.Parent;
        }
#endif

        return Path.Combine(baseDir, "AutoQAC Data");
    }

    public void StartWatching()
    {
        if (_watcher != null) return; // Already watching

        if (!Directory.Exists(_configDirectory))
        {
            _logger.Warning("[ConfigWatcher] Config directory does not exist: {Path}", _configDirectory);
            return;
        }

        // Initialize the last known external hash from disk
        var filePath = Path.Combine(_configDirectory, UserConfigFile);
        if (File.Exists(filePath))
        {
            _lastKnownExternalHash = ComputeFileHash(filePath);
        }

        _watcher = new FileSystemWatcher(_configDirectory, UserConfigFile)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        // Pipe FSW Changed events through Rx: throttle to 500ms, then observe on thread pool
        var fswObservable = Observable.FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(
                h => _watcher.Changed += h,
                h => _watcher.Changed -= h)
            .Throttle(TimeSpan.FromMilliseconds(500))
            .ObserveOn(TaskPoolScheduler.Default)
            .Subscribe(
                _ => HandleFileChanged(),
                ex => _logger.Error(ex, "[ConfigWatcher] Error in FSW observable pipeline"));

        _disposables.Add(fswObservable);

        // Subscribe to cleaning state changes to apply deferred reloads when cleaning ends
        var cleaningEndSub = _stateService.StateChanged
            .Select(s => s.IsCleaning)
            .DistinctUntilChanged()
            .Where(cleaning => !cleaning && _hasDeferredChanges)
            .ObserveOn(TaskPoolScheduler.Default)
            .Subscribe(
                _ => ApplyDeferredChanges(),
                ex => _logger.Error(ex, "[ConfigWatcher] Error applying deferred config changes"));

        _disposables.Add(cleaningEndSub);

        _logger.Information("[ConfigWatcher] Started watching {File} in {Dir}", UserConfigFile, _configDirectory);
    }

    public void StopWatching()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
            _logger.Information("[ConfigWatcher] Stopped watching config file");
        }
    }

    private void HandleFileChanged()
    {
        try
        {
            var filePath = Path.Combine(_configDirectory, UserConfigFile);
            if (!File.Exists(filePath)) return;

            var currentHash = ComputeFileHash(filePath);

            // Check 1: Is this our own app-initiated save?
            var lastWrittenHash = _configService.GetLastWrittenHash();
            if (lastWrittenHash != null && string.Equals(currentHash, lastWrittenHash, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Debug("[ConfigWatcher] Detected app-initiated save, skipping reload");
                _lastKnownExternalHash = currentHash;
                return;
            }

            // Check 2: Has the file actually changed since the last external read?
            if (_lastKnownExternalHash != null && string.Equals(currentHash, _lastKnownExternalHash, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Debug("[ConfigWatcher] No actual content change (same hash), skipping reload");
                return;
            }

            // Check 3: Are we currently cleaning? If so, defer.
            if (_stateService.CurrentState.IsCleaning)
            {
                _hasDeferredChanges = true;
                _lastKnownExternalHash = currentHash;
                _logger.Warning("[ConfigWatcher] Config changed externally during cleaning session; deferring reload until cleaning ends");
                return;
            }

            // Check 4: Validate YAML before reloading
            if (!TryValidateYaml(filePath))
            {
                _logger.Warning("[ConfigWatcher] Invalid external config edit rejected, keeping previous config");
                return;
            }

            // All checks passed: reload from disk
            _lastKnownExternalHash = currentHash;
            _configService.ReloadFromDiskAsync().GetAwaiter().GetResult();
            _logger.Information("[ConfigWatcher] Reloaded config after detecting external change");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "[ConfigWatcher] Failed to process file change event");
        }
    }

    private void ApplyDeferredChanges()
    {
        _hasDeferredChanges = false;

        try
        {
            var filePath = Path.Combine(_configDirectory, UserConfigFile);
            if (!File.Exists(filePath)) return;

            if (!TryValidateYaml(filePath))
            {
                _logger.Warning("[ConfigWatcher] Deferred config change has invalid YAML, keeping previous config");
                return;
            }

            _configService.ReloadFromDiskAsync().GetAwaiter().GetResult();
            _logger.Information("[ConfigWatcher] Applied deferred config change after cleaning ended");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "[ConfigWatcher] Failed to apply deferred config change");
        }
    }

    /// <summary>
    /// Attempts to deserialize the YAML file to validate its structure.
    /// Returns true if the file is valid, false if deserialization fails.
    /// </summary>
    private bool TryValidateYaml(string filePath)
    {
        try
        {
            var content = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(content)) return false;
            _yamlValidator.Deserialize<UserConfiguration>(content);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Computes a SHA256 hex hash of the file at the given path.
    /// </summary>
    private static string ComputeFileHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hashBytes = SHA256.HashData(stream);
        return Convert.ToHexString(hashBytes);
    }

    public void Dispose()
    {
        StopWatching();
        _disposables.Dispose();
    }
}
