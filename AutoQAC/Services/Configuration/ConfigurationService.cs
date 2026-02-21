using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Models.Configuration;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AutoQAC.Services.Configuration;

public sealed class ConfigurationService : IConfigurationService, IDisposable, IAsyncDisposable
{
    private readonly ILoggingService _logger;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly Lock _stateLock = new();
    private readonly Subject<UserConfiguration> _configChanges = new();
    private readonly Subject<GameType> _skipListChanges = new();
    private readonly Subject<UserConfiguration> _saveRequests = new();
    private readonly IDisposable _debounceSubscription;
    private readonly string _configDirectory;
    private readonly ISerializer _serializer;
    private readonly IDeserializer _deserializer;

    private UserConfiguration? _lastKnownGoodConfig;
    private UserConfiguration? _pendingConfig;
    private MainConfiguration? _mainConfigCache;
    private string? _lastWrittenHash;
    private int _disposeState;

    private const string MainConfigFile = "AutoQAC Main.yaml";
    private const string UserConfigFile = "AutoQAC Settings.yaml";

    public IObservable<UserConfiguration> UserConfigurationChanged => _configChanges;
    public IObservable<GameType> SkipListChanged => _skipListChanges;

    public ConfigurationService(ILoggingService logger, string? configDirectory = null)
    {
        _logger = logger;
        _configDirectory = configDirectory ?? ResolveConfigDirectory(logger);
        _serializer = new SerializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .Build();
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        if (!Directory.Exists(_configDirectory))
        {
            Directory.CreateDirectory(_configDirectory);
        }

        // Debounced async save pipeline. Switch cancels superseded in-flight saves.
        _debounceSubscription = _saveRequests
            .Throttle(TimeSpan.FromMilliseconds(500))
            .Select(config => Observable.FromAsync(async saveCt =>
            {
                try
                {
                    await SaveToDiskWithRetryAsync(config, saveCt).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (saveCt.IsCancellationRequested)
                {
                    _logger.Debug("[Config] Superseded debounced save canceled");
                }
            }))
            .Switch()
            .Subscribe(
                _ => { },
                ex => _logger.Error(ex, "[Config] Debounced save pipeline failed"));
    }

    private string ResolveConfigDirectory(ILoggingService logger)
    {
        var baseDir = AppContext.BaseDirectory;

#if DEBUG
        var current = new DirectoryInfo(baseDir);
        for (int i = 0; i < 6 && current != null; i++)
        {
            var candidate = Path.Combine(current.FullName, "AutoQAC Data");
            if (Directory.Exists(candidate))
            {
                logger.Information("[Debug] Resolved configuration directory to source: {Candidate}", candidate);
                return candidate;
            }

            current = current.Parent;
        }
#endif

        return Path.Combine(baseDir, "AutoQAC Data");
    }

    public async Task<MainConfiguration> LoadMainConfigAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        lock (_stateLock)
        {
            if (_mainConfigCache != null)
            {
                return _mainConfigCache;
            }
        }

        var path = Path.Combine(_configDirectory, MainConfigFile);
        if (!File.Exists(path))
        {
            _logger.Warning("Main config file not found at {Path}. Creating default.", path);
            var defaultConfig = new MainConfiguration();
            lock (_stateLock)
            {
                _mainConfigCache ??= defaultConfig;
                return _mainConfigCache;
            }
        }

        await _fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            lock (_stateLock)
            {
                if (_mainConfigCache != null)
                {
                    return _mainConfigCache;
                }
            }

            var content = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            var loaded = _deserializer.Deserialize<MainConfiguration>(content);
            if (loaded == null)
            {
                throw new InvalidOperationException("Main configuration deserialized to null.");
            }

            lock (_stateLock)
            {
                _mainConfigCache = loaded;
                _logger.Information("Loaded Main Configuration (Version: {Version})", _mainConfigCache.Data.Version);
                return _mainConfigCache;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load main configuration");
            throw;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<UserConfiguration> LoadUserConfigAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        UserConfiguration? pendingSnapshot;
        lock (_stateLock)
        {
            pendingSnapshot = _pendingConfig;
        }

        if (pendingSnapshot != null)
        {
            return CloneConfig(pendingSnapshot);
        }

        var path = Path.Combine(_configDirectory, UserConfigFile);
        if (!File.Exists(path))
        {
            _logger.Information("[Config] User config file not found at {Path}. Creating default.", path);
            var defaultConfig = new UserConfiguration();
            await SaveUserConfigAsync(defaultConfig, ct).ConfigureAwait(false);
            return CloneConfig(defaultConfig);
        }

        await _fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var content = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            var loaded = _deserializer.Deserialize<UserConfiguration>(content);
            if (loaded == null)
            {
                _logger.Warning("[Config] User configuration file was empty. Using default configuration.");
                loaded = new UserConfiguration();
            }

            lock (_stateLock)
            {
                _lastKnownGoodConfig = CloneConfig(loaded);
                if (_pendingConfig != null)
                {
                    return CloneConfig(_pendingConfig);
                }
            }

            return CloneConfig(loaded);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "[Config] Failed to load user configuration");
            throw;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public Task SaveUserConfigAsync(UserConfiguration config, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();

        var configCopy = CloneConfig(config);
        lock (_stateLock)
        {
            _pendingConfig = configCopy;
        }

        _configChanges.OnNext(CloneConfig(configCopy));
        _saveRequests.OnNext(configCopy);
        return Task.CompletedTask;
    }

    private async Task SaveToDiskWithRetryAsync(UserConfiguration config, CancellationToken ct = default)
    {
        const int maxRetries = 2;
        var path = Path.Combine(_configDirectory, UserConfigFile);

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await _fileLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    Directory.CreateDirectory(_configDirectory);

                    var content = _serializer.Serialize(config);
                    await File.WriteAllTextAsync(path, content, ct).ConfigureAwait(false);

                    var successfulConfig = CloneConfig(config);
                    lock (_stateLock)
                    {
                        _lastKnownGoodConfig = successfulConfig;
                    }

                    Interlocked.CompareExchange(ref _pendingConfig, null, config);

                    lock (_stateLock)
                    {
                        _lastWrittenHash = ComputeFileHash(path);
                    }

                    _logger.Information("[Config] Debounced save completed successfully");
                    return;
                }
                finally
                {
                    _fileLock.Release();
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                _logger.Warning(
                    "[Config] Save failed (attempt {Attempt}/{MaxAttempts}): {Message}",
                    attempt + 1,
                    maxRetries + 1,
                    ex.Message);
                await Task.Delay(100, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "[Config] Save failed after {MaxRetries} retries. Reverting to last known good config.",
                    maxRetries + 1);

                UserConfiguration? fallback = null;
                lock (_stateLock)
                {
                    if (_lastKnownGoodConfig != null)
                    {
                        fallback = CloneConfig(_lastKnownGoodConfig);
                        Interlocked.Exchange(ref _pendingConfig, null);
                    }
                }

                if (fallback != null)
                {
                    _configChanges.OnNext(fallback);
                    _logger.Warning("[Config] Reverted to last known good configuration");
                }
            }
        }
    }

    public async Task FlushPendingSavesAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        UserConfiguration? config;
        lock (_stateLock)
        {
            config = _pendingConfig;
        }

        if (config == null)
        {
            _logger.Debug("[Config] No pending config changes to flush");
            return;
        }

        _logger.Information("[Config] Flushing pending config saves to disk");
        await SaveToDiskWithRetryAsync(config, ct).ConfigureAwait(false);
    }

    public Task<bool> ValidatePathsAsync(UserConfiguration config, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();

        var isValid = true;

        if (!string.IsNullOrEmpty(config.LoadOrder.File) && !File.Exists(config.LoadOrder.File))
        {
            _logger.Warning("Load Order file not found: {Path}", config.LoadOrder.File);
            isValid = false;
        }

        foreach (var (gameKey, loadOrderPath) in config.LoadOrderFileOverrides)
        {
            if (!string.IsNullOrWhiteSpace(loadOrderPath) && !File.Exists(loadOrderPath))
            {
                _logger.Warning("Load Order file not found for {GameKey}: {Path}", gameKey, loadOrderPath);
                isValid = false;
            }
        }

        if (!string.IsNullOrEmpty(config.XEdit.Binary) && !File.Exists(config.XEdit.Binary))
        {
            _logger.Warning("xEdit binary not found: {Path}", config.XEdit.Binary);
            isValid = false;
        }

        if (config.Settings.Mo2Mode &&
            !string.IsNullOrEmpty(config.ModOrganizer.Binary) &&
            !File.Exists(config.ModOrganizer.Binary))
        {
            _logger.Warning("MO2 binary not found: {Path}", config.ModOrganizer.Binary);
            isValid = false;
        }

        return Task.FromResult(isValid);
    }

    public async Task<List<string>> GetSkipListAsync(
        GameType gameType,
        GameVariant variant = GameVariant.None,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var mainConfig = await LoadMainConfigAsync(ct).ConfigureAwait(false);
        var userConfig = await LoadUserConfigAsync(ct).ConfigureAwait(false);

        var result = new List<string>();
        var key = variant == GameVariant.Enderal ? "Enderal" : GetGameKey(gameType);

        if (userConfig.SkipLists.TryGetValue(key, out var userList))
        {
            result.AddRange(userList);
        }

        if (mainConfig.Data.SkipLists.TryGetValue(key, out var mainGameList))
        {
            result.AddRange(mainGameList);
        }

        if (variant == GameVariant.TTW)
        {
            var fo3Key = GetGameKey(GameType.Fallout3);
            if (userConfig.SkipLists.TryGetValue(fo3Key, out var userFo3List))
            {
                result.AddRange(userFo3List);
            }

            if (mainConfig.Data.SkipLists.TryGetValue(fo3Key, out var mainFo3List))
            {
                result.AddRange(mainFo3List);
            }
        }

        if (mainConfig.Data.SkipLists.TryGetValue("Universal", out var universalList))
        {
            result.AddRange(universalList);
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<List<string>> GetDefaultSkipListAsync(GameType gameType, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var mainConfig = await LoadMainConfigAsync(ct).ConfigureAwait(false);

        var result = new List<string>();
        var key = GetGameKey(gameType);

        if (mainConfig.Data.SkipLists.TryGetValue(key, out var mainGameList))
        {
            result.AddRange(mainGameList);
        }

        if (mainConfig.Data.SkipLists.TryGetValue("Universal", out var universalList))
        {
            result.AddRange(universalList);
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<List<string>> GetXEditExecutableNamesAsync(GameType gameType, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var mainConfig = await LoadMainConfigAsync(ct).ConfigureAwait(false);
        var key = GetGameKey(gameType);

        if (mainConfig.Data.XEditLists.TryGetValue(key, out var list))
        {
            return list;
        }

        if (mainConfig.Data.XEditLists.TryGetValue("Universal", out var universalList))
        {
            return universalList;
        }

        return [];
    }

    public async Task<List<string>> GetGameSpecificSkipListAsync(GameType gameType, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var userConfig = await LoadUserConfigAsync(ct).ConfigureAwait(false);
        var key = GetGameKey(gameType);

        if (userConfig.SkipLists.TryGetValue(key, out var list))
        {
            return list.ToList();
        }

        return [];
    }

    public async Task UpdateSkipListAsync(GameType gameType, List<string> skipList, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var userConfig = await LoadUserConfigAsync(ct).ConfigureAwait(false);
        var key = GetGameKey(gameType);

        userConfig.SkipLists[key] = skipList.ToList();
        await SaveUserConfigAsync(userConfig, ct).ConfigureAwait(false);
        _skipListChanges.OnNext(gameType);
        _logger.Information("Skip list updated for {GameType} with {Count} entries", gameType, skipList.Count);
    }

    public async Task AddToSkipListAsync(GameType gameType, string pluginName, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(pluginName))
        {
            throw new ArgumentException("Plugin name cannot be empty", nameof(pluginName));
        }

        var currentList = await GetGameSpecificSkipListAsync(gameType, ct).ConfigureAwait(false);
        if (currentList.Any(p => string.Equals(p, pluginName, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.Debug("Plugin {PluginName} already in skip list for {GameType}", pluginName, gameType);
            return;
        }

        currentList.Add(pluginName);
        await UpdateSkipListAsync(gameType, currentList, ct).ConfigureAwait(false);
    }

    public async Task RemoveFromSkipListAsync(GameType gameType, string pluginName, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(pluginName))
        {
            return;
        }

        var currentList = await GetGameSpecificSkipListAsync(gameType, ct).ConfigureAwait(false);
        var toRemove = currentList.FirstOrDefault(
            p => string.Equals(p, pluginName, StringComparison.OrdinalIgnoreCase));

        if (toRemove != null)
        {
            currentList.Remove(toRemove);
            await UpdateSkipListAsync(gameType, currentList, ct).ConfigureAwait(false);
        }
    }

    public async Task<GameType> GetSelectedGameAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var config = await LoadUserConfigAsync(ct).ConfigureAwait(false);
        return Enum.TryParse<GameType>(config.SelectedGame, true, out var gameType)
            ? gameType
            : GameType.Unknown;
    }

    public async Task SetSelectedGameAsync(GameType gameType, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var config = await LoadUserConfigAsync(ct).ConfigureAwait(false);
        config.SelectedGame = gameType.ToString();
        await SaveUserConfigAsync(config, ct).ConfigureAwait(false);
    }

    public async Task<string?> GetGameDataFolderOverrideAsync(GameType gameType, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var config = await LoadUserConfigAsync(ct).ConfigureAwait(false);
        var key = GetGameKey(gameType);
        return config.GameDataFolderOverrides.GetValueOrDefault(key);
    }

    public async Task<string?> GetGameLoadOrderOverrideAsync(GameType gameType, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var config = await LoadUserConfigAsync(ct).ConfigureAwait(false);

        if (gameType != GameType.Unknown)
        {
            var key = GetGameKey(gameType);
            if (config.LoadOrderFileOverrides.TryGetValue(key, out var loadOrderPath) &&
                !string.IsNullOrWhiteSpace(loadOrderPath))
            {
                return loadOrderPath;
            }
        }

        return config.LoadOrder.File;
    }

    public async Task SetGameLoadOrderOverrideAsync(
        GameType gameType,
        string? loadOrderPath,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var config = await LoadUserConfigAsync(ct).ConfigureAwait(false);

        if (gameType == GameType.Unknown)
        {
            config.LoadOrder.File = string.IsNullOrWhiteSpace(loadOrderPath) ? null : loadOrderPath;
            await SaveUserConfigAsync(config, ct).ConfigureAwait(false);
            return;
        }

        var key = GetGameKey(gameType);
        if (string.IsNullOrWhiteSpace(loadOrderPath))
        {
            config.LoadOrderFileOverrides.Remove(key);
            _logger.Information("Removed load order override for {GameType}", gameType);
        }
        else
        {
            config.LoadOrderFileOverrides[key] = loadOrderPath;
            _logger.Information(
                "Set load order override for {GameType} to {LoadOrderPath}",
                gameType,
                loadOrderPath);
        }

        config.LoadOrder.File = string.IsNullOrWhiteSpace(loadOrderPath) ? null : loadOrderPath;
        await SaveUserConfigAsync(config, ct).ConfigureAwait(false);
    }

    public async Task SetGameDataFolderOverrideAsync(
        GameType gameType,
        string? folderPath,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var config = await LoadUserConfigAsync(ct).ConfigureAwait(false);
        var key = GetGameKey(gameType);

        if (string.IsNullOrWhiteSpace(folderPath))
        {
            config.GameDataFolderOverrides.Remove(key);
            _logger.Information("Removed data folder override for {GameType}", gameType);
        }
        else
        {
            config.GameDataFolderOverrides[key] = folderPath;
            _logger.Information("Set data folder override for {GameType} to {FolderPath}", gameType, folderPath);
        }

        await SaveUserConfigAsync(config, ct).ConfigureAwait(false);
    }

    public async Task ResetToDefaultsAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        _logger.Information("Resetting user configuration to defaults");
        var defaultConfig = new UserConfiguration();
        await SaveUserConfigAsync(defaultConfig, ct).ConfigureAwait(false);
    }

    public async Task<Dictionary<string, object?>> GetAllSettingsAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var config = await LoadUserConfigAsync(ct).ConfigureAwait(false);
        return new Dictionary<string, object?>
        {
            ["XEditPath"] = config.XEdit.Binary,
            ["LoadOrderPath"] = config.LoadOrder.File,
            ["LoadOrderOverrides"] = config.LoadOrderFileOverrides,
            ["Mo2Binary"] = config.ModOrganizer.Binary,
            ["Mo2Mode"] = config.Settings.Mo2Mode,
            ["CleaningTimeout"] = config.Settings.CleaningTimeout,
            ["JournalExpiration"] = config.Settings.JournalExpiration,
            ["CpuThreshold"] = config.Settings.CpuThreshold,
            ["DisableSkipLists"] = config.Settings.DisableSkipLists,
            ["LogRetention.Mode"] = config.LogRetention.Mode,
            ["LogRetention.MaxAgeDays"] = config.LogRetention.MaxAgeDays,
            ["LogRetention.MaxFileCount"] = config.LogRetention.MaxFileCount,
            ["SelectedGame"] = config.SelectedGame
        };
    }

    public async Task ReloadFromDiskAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var path = Path.Combine(_configDirectory, UserConfigFile);
        if (!File.Exists(path))
        {
            _logger.Warning("[Config] Config file not found during reload: {Path}", path);
            return;
        }

        await _fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var content = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            var loaded = _deserializer.Deserialize<UserConfiguration>(content);
            if (loaded == null)
            {
                throw new InvalidOperationException("Reloaded user configuration deserialized to null.");
            }

            var loadedCopy = CloneConfig(loaded);
            lock (_stateLock)
            {
                Interlocked.Exchange(ref _pendingConfig, null);
                _mainConfigCache = null;
                _lastKnownGoodConfig = loadedCopy;
            }

            _configChanges.OnNext(CloneConfig(loadedCopy));
            _logger.Information("[Config] Reloaded configuration from disk");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "[Config] Failed to reload configuration from disk");
            throw;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public string? GetLastWrittenHash()
    {
        lock (_stateLock)
        {
            return _lastWrittenHash;
        }
    }

    private UserConfiguration CloneConfig(UserConfiguration source)
    {
        var yaml = _serializer.Serialize(source);
        var clone = _deserializer.Deserialize<UserConfiguration>(yaml);
        if (clone == null)
        {
            throw new InvalidOperationException("Configuration clone failed (deserialized to null).");
        }

        return clone;
    }

    private static string ComputeFileHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hashBytes = SHA256.HashData(stream);
        return Convert.ToHexString(hashBytes);
    }

    private string GetGameKey(GameType gameType) => gameType switch
    {
        GameType.Fallout3 => "FO3",
        GameType.FalloutNewVegas => "FNV",
        GameType.Fallout4 => "FO4",
        GameType.SkyrimLe => "Skyrim",
        GameType.SkyrimSe => "SSE",
        GameType.Fallout4Vr => "FO4VR",
        GameType.SkyrimVr => "SkyrimVR",
        GameType.Oblivion => "Oblivion",
        _ => "Unknown"
    };

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0)
        {
            return;
        }

        try
        {
            await FlushPendingSavesAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning("[Config] Flush during disposal failed: {Message}", ex.Message);
        }

        _debounceSubscription.Dispose();
        _saveRequests.Dispose();
        _fileLock.Dispose();
        _configChanges.Dispose();
        _skipListChanges.Dispose();
        Volatile.Write(ref _disposeState, 2);
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposeState) >= 2)
        {
            throw new ObjectDisposedException(nameof(ConfigurationService));
        }
    }
}
