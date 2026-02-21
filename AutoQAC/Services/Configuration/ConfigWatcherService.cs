using System;
using System.IO;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.State;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AutoQAC.Services.Configuration;

/// <summary>
/// Watches the user configuration YAML file for external changes using FileSystemWatcher
/// combined with SHA256 content hashing. Changes detected during an active cleaning
/// session are deferred until the session ends.
/// </summary>
public sealed class ConfigWatcherService : IConfigWatcherService
{
    private readonly IConfigurationService _configService;
    private readonly IStateService _stateService;
    private readonly ILoggingService _logger;
    private readonly string _configDirectory;
    private readonly CompositeDisposable _disposables = new();
    private readonly IDeserializer _yamlValidator;

    private FileSystemWatcher? _watcher;
    private string? _lastKnownExternalHash;
    private volatile bool _hasDeferredChanges;

    private const string UserConfigFile = "AutoQAC Settings.yaml";

    public ConfigWatcherService(
        IConfigurationService configService,
        IStateService stateService,
        ILoggingService logger,
        string? configDirectory = null)
    {
        _configService = configService;
        _stateService = stateService;
        _logger = logger;
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

        var filePath = Path.Combine(_configDirectory, UserConfigFile);
        if (File.Exists(filePath))
        {
            _lastKnownExternalHash = ComputeFileHash(filePath);
        }

        FileSystemWatcher? watcher = null;
        try
        {
            watcher = new FileSystemWatcher(_configDirectory, UserConfigFile)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
            };

            var fswObservable = Observable.FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(
                    h => watcher.Changed += h,
                    h => watcher.Changed -= h)
                .Throttle(TimeSpan.FromMilliseconds(500))
                .ObserveOn(TaskPoolScheduler.Default)
                .SelectMany(_ => Observable.FromAsync(HandleFileChangedAsync))
                .Subscribe(
                    _ => { },
                    ex => _logger.Error(ex, "[ConfigWatcher] Error in FSW observable pipeline"));

            _disposables.Add(fswObservable);

            var cleaningEndSub = _stateService.StateChanged
                .Select(s => s.IsCleaning)
                .DistinctUntilChanged()
                .Where(cleaning => !cleaning && _hasDeferredChanges)
                .ObserveOn(TaskPoolScheduler.Default)
                .SelectMany(_ => Observable.FromAsync(ApplyDeferredChangesAsync))
                .Subscribe(
                    _ => { },
                    ex => _logger.Error(ex, "[ConfigWatcher] Error applying deferred config changes"));

            _disposables.Add(cleaningEndSub);

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

    private async Task HandleFileChangedAsync(CancellationToken ct)
    {
        try
        {
            var filePath = Path.Combine(_configDirectory, UserConfigFile);
            if (!File.Exists(filePath))
            {
                return;
            }

            var currentHash = ComputeFileHash(filePath);
            var lastWrittenHash = _configService.GetLastWrittenHash();
            if (lastWrittenHash != null &&
                string.Equals(currentHash, lastWrittenHash, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Debug("[ConfigWatcher] Detected app-initiated save, skipping reload");
                _lastKnownExternalHash = currentHash;
                return;
            }

            if (_lastKnownExternalHash != null &&
                string.Equals(currentHash, _lastKnownExternalHash, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Debug("[ConfigWatcher] No actual content change (same hash), skipping reload");
                return;
            }

            if (_stateService.CurrentState.IsCleaning)
            {
                _hasDeferredChanges = true;
                _lastKnownExternalHash = currentHash;
                _logger.Warning("[ConfigWatcher] Config changed externally during cleaning session; deferring reload until cleaning ends");
                return;
            }

            if (!TryValidateYaml(filePath))
            {
                _logger.Warning("[ConfigWatcher] Invalid external config edit rejected, keeping previous config");
                return;
            }

            _lastKnownExternalHash = currentHash;
            await _configService.ReloadFromDiskAsync(ct).ConfigureAwait(false);
            _logger.Information("[ConfigWatcher] Reloaded config after detecting external change");
        }
        catch (OperationCanceledException)
        {
            // No-op; canceled by pipeline shutdown/switch.
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "[ConfigWatcher] Failed to process file change event");
        }
    }

    private async Task ApplyDeferredChangesAsync(CancellationToken ct)
    {
        _hasDeferredChanges = false;

        try
        {
            var filePath = Path.Combine(_configDirectory, UserConfigFile);
            if (!File.Exists(filePath))
            {
                return;
            }

            if (!TryValidateYaml(filePath))
            {
                _logger.Warning("[ConfigWatcher] Deferred config change has invalid YAML, keeping previous config");
                return;
            }

            await _configService.ReloadFromDiskAsync(ct).ConfigureAwait(false);
            _logger.Information("[ConfigWatcher] Applied deferred config change after cleaning ended");
        }
        catch (OperationCanceledException)
        {
            // No-op; canceled by pipeline shutdown/switch.
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
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();
            if (string.IsNullOrWhiteSpace(content))
            {
                return false;
            }

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
