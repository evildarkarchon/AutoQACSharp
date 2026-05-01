using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.State;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AutoQAC.Services.Configuration;

public sealed class ConfigurationService : IConfigurationService, IDisposable, IAsyncDisposable
{
    private readonly ILoggingService _logger;
    private readonly IConfigPersistenceCoordinator _coordinator;
    private readonly Task _consumerTask;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly Lock _stateLock = new();
    private readonly Subject<GameType> _skipListChanges = new();
    private readonly string _configDirectory;
    private readonly IDeserializer _deserializer;

    private MainConfiguration? _mainConfigCache;
    private bool _loadedUserConfigFromDisk;
    private bool _hasPendingUserSave;
    private int _disposeState;

    private const string MainConfigFile = "AutoQAC Main.yaml";
    private const string UserConfigFile = "AutoQAC Settings.yaml";

    public IObservable<UserConfiguration> UserConfigurationChanged => _coordinator.ConfigurationAccepted;
    public IObservable<GameType> SkipListChanged => _skipListChanges;
    public IObservable<ConfigPersistenceFailure> Failures => _coordinator.Failures;
    public IObservable<ConfigPersistenceResult> PersistenceResults => _coordinator.PersistenceResults;
    public ConfigPersistenceFailure? LastFailure => _coordinator.LastFailure;

    internal ConfigurationService(
        IConfigPersistenceCoordinator coordinator,
        ILoggingService logger,
        string? configDirectory = null)
    {
        _coordinator = coordinator;
        _logger = logger;
        _configDirectory = configDirectory ?? ResolveConfigDirectory(logger);
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        if (!Directory.Exists(_configDirectory))
        {
            Directory.CreateDirectory(_configDirectory);
        }

        _consumerTask = _coordinator.StartAsync(CancellationToken.None);
        _ = _consumerTask.ContinueWith(
            t => _logger.Error(t.Exception, "[Config] Persistence coordinator consumer crashed"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    public ConfigurationService(ILoggingService logger, string? configDirectory = null)
        : this(CreateDefaultCoordinator(logger, configDirectory), logger, configDirectory)
    {
    }

    private static IConfigPersistenceCoordinator CreateDefaultCoordinator(
        ILoggingService logger,
        string? configDirectory)
    {
        var stateService = new StateService();
        var fileStore = new UserConfigFileStore(logger, configDirectory);
        return new ConfigPersistenceCoordinator(fileStore, stateService, logger);
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
        await _consumerTask.ConfigureAwait(false);

        var (hasPending, loadedFromDisk) = GetUserConfigStateFlags();
        if (hasPending)
        {
            return await _coordinator.LoadCurrentAsync(ct).ConfigureAwait(false);
        }

        var path = Path.Combine(_configDirectory, UserConfigFile);
        if (!File.Exists(path))
        {
            _logger.Information("[Config] User config file not found at {Path}. Creating default.", path);
            var defaultConfig = new UserConfiguration();
            await SaveUserConfigAsync(defaultConfig, ct).ConfigureAwait(false);
            return defaultConfig.Copy();
        }

        if (!loadedFromDisk && !hasPending)
        {
            var result = await _coordinator.ReloadFromDiskAsync(ct).ConfigureAwait(false);
            if (result.Status == ConfigPersistenceStatusKind.Failed)
            {
                _logger.Warning("[Config] Initial user configuration reload failed: {Summary}", result.Failure?.SafeSummary ?? "unknown");
            }

            lock (_stateLock)
            {
                _loadedUserConfigFromDisk = true;
            }
        }

        return await _coordinator.LoadCurrentAsync(ct).ConfigureAwait(false);
    }

    public async Task SaveUserConfigAsync(UserConfiguration config, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        await _consumerTask.ConfigureAwait(false);
        await _coordinator.SaveUserConfigAsync(config, ct).ConfigureAwait(false);
        lock (_stateLock)
        {
            _loadedUserConfigFromDisk = true;
            _hasPendingUserSave = true;
        }
    }

    public async Task<ConfigPersistenceResult> FlushPendingSavesAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await _consumerTask.ConfigureAwait(false);

        // Forced flush is also the public queue barrier for watcher reload work; a missing
        // facade pending-save marker does not prove the coordinator queue is already drained.
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

        if (variant == GameVariant.Ttw)
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
            ["Backup.Enabled"] = config.Backup.Enabled,
            ["Backup.MaxSessions"] = config.Backup.MaxSessions,
            ["SelectedGame"] = config.SelectedGame
        };
    }

    public async Task ReloadFromDiskAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await _consumerTask.ConfigureAwait(false);
        var result = await _coordinator.ReloadFromDiskAsync(ct).ConfigureAwait(false);
        if (result.Status is ConfigPersistenceStatusKind.Success)
        {
            // Only a successful coordinator reload means pending facade edits were persisted
            // and accepted back from disk. Failed/rejected reloads must leave the pending
            // indicator intact so a later flush still protects the user's edits.
            lock (_stateLock)
            {
                _loadedUserConfigFromDisk = true;
                _hasPendingUserSave = false;
            }
        }

        lock (_stateLock)
        {
            _mainConfigCache = null;
        }
    }

    public string? GetLastWrittenHash()
    {
        return _coordinator.GetLastWrittenHash();
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

    /// <summary>
    /// Returns a consistent snapshot of facade user-config bookkeeping flags.
    /// </summary>
    private (bool HasPendingUserSave, bool LoadedUserConfigFromDisk) GetUserConfigStateFlags()
    {
        lock (_stateLock)
        {
            return (_hasPendingUserSave, _loadedUserConfigFromDisk);
        }
    }

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
            await _coordinator.StopAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning("[Config] Coordinator stop during disposal failed: {Message}", ex.Message);
        }

        _fileLock.Dispose();
        _skipListChanges.Dispose();
        Volatile.Write(ref _disposeState, 2);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposeState) >= 2)
        {
            throw new ObjectDisposedException(nameof(ConfigurationService));
        }
    }
}
