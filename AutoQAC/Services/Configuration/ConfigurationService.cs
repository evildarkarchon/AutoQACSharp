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

public sealed class ConfigurationService : IConfigurationService, IDisposable
{
    private readonly ILoggingService _logger;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly Subject<UserConfiguration> _configChanges = new();
    private readonly Subject<GameType> _skipListChanges = new();
    private readonly Subject<UserConfiguration> _saveRequests = new();
    private readonly IDisposable _debounceSubscription;
    private UserConfiguration? _lastKnownGoodConfig;
    private volatile UserConfiguration? _pendingConfig;

    private readonly string _configDirectory;
    private const string MainConfigFile = "AutoQAC Main.yaml";
    private const string UserConfigFile = "AutoQAC Settings.yaml";

    private MainConfiguration? _mainConfigCache;
    private string? _lastWrittenHash;
    private readonly ISerializer _serializer;
    private readonly IDeserializer _deserializer;

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

        // Set up Rx Throttle pipeline for debounced config saves (500ms)
        _debounceSubscription = _saveRequests
            .Throttle(TimeSpan.FromMilliseconds(500))
            .Subscribe(config =>
            {
                // Fire-and-forget the save -- errors are handled inside
                _ = SaveToDiskWithRetryAsync(config);
            });
    }

    private string ResolveConfigDirectory(ILoggingService logger)
    {
        var baseDir = AppContext.BaseDirectory;

#if DEBUG
        // In DEBUG mode, prioritize finding the directory in the source tree (parent directories)
        var current = new DirectoryInfo(baseDir);
        // Limit traversal to prevent scanning entire drive (e.g. 6 levels up)
        for (int i = 0; i < 6 && current != null; i++)
        {
            var candidate = Path.Combine(current.FullName, "AutoQAC Data");
            if (Directory.Exists(candidate))
            {
                logger.Information($"[Debug] Resolved configuration directory to source: {candidate}");
                return candidate;
            }

            current = current.Parent;
        }
#endif

        return Path.Combine(baseDir, "AutoQAC Data");
    }

    public async Task<MainConfiguration> LoadMainConfigAsync(CancellationToken ct = default)
    {
        if (_mainConfigCache != null) return _mainConfigCache;

        var path = Path.Combine(_configDirectory, MainConfigFile);
        if (!File.Exists(path))
        {
            _logger.Warning($"Main config file not found at {path}. Creating default.");
            var config = new MainConfiguration();
            // Ideally we would write it, but MainConfiguration is usually static/read-only provided by the app
            // For now, return empty or default
            _mainConfigCache = config;
            return config;
        }

        try
        {
            await _fileLock.WaitAsync(ct).ConfigureAwait(false);
            var content = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            _mainConfigCache = _deserializer.Deserialize<MainConfiguration>(content);
            _logger.Information("Loaded Main Configuration (Version: {Version})", _mainConfigCache.Data.Version);
            return _mainConfigCache;
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
        // Return pending in-memory config if available (debounced save may not have fired yet)
        if (_pendingConfig != null) return _pendingConfig;

        var path = Path.Combine(_configDirectory, UserConfigFile);
        if (!File.Exists(path))
        {
            _logger.Information($"[Config] User config file not found at {path}. Creating default.");
            var config = new UserConfiguration();
            await SaveUserConfigAsync(config, ct).ConfigureAwait(false);
            return config;
        }

        try
        {
            await _fileLock.WaitAsync(ct).ConfigureAwait(false);
            var content = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            var loaded = _deserializer.Deserialize<UserConfiguration>(content);
            _lastKnownGoodConfig = loaded;
            return loaded;
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
        // Store the pending config in memory (always up-to-date)
        _pendingConfig = config;
        // Notify subscribers immediately (in-memory state is current)
        _configChanges.OnNext(config);
        // Schedule debounced write to disk
        _saveRequests.OnNext(config);
        return Task.CompletedTask;
    }

    private async Task SaveToDiskWithRetryAsync(UserConfiguration config, CancellationToken ct = default)
    {
        const int maxRetries = 2;
        var path = Path.Combine(_configDirectory, UserConfigFile);

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                await _fileLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    // Temp/test directories can be removed between queued save and flush.
                    // Recreate before each write attempt to keep save resilient.
                    Directory.CreateDirectory(_configDirectory);

                    var content = _serializer.Serialize(config);
                    await File.WriteAllTextAsync(path, content, ct).ConfigureAwait(false);
                    _lastKnownGoodConfig = config;
                    // Clear pending if this was the latest pending config
                    if (ReferenceEquals(_pendingConfig, config))
                    {
                        _pendingConfig = null;
                    }

                    // Compute SHA256 hash of the written file so ConfigWatcherService
                    // can distinguish app-initiated saves from external edits
                    _lastWrittenHash = ComputeFileHash(path);
                    _logger.Information("[Config] Debounced save completed successfully");
                    return;
                }
                finally
                {
                    _fileLock.Release();
                }
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                _logger.Warning("[Config] Save failed (attempt {Attempt}/{MaxAttempts}): {Message}",
                    attempt + 1, maxRetries + 1, ex.Message);
                await Task.Delay(100, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[Config] Save failed after {MaxRetries} retries. Reverting to last known good config.",
                    maxRetries + 1);

                // Revert in-memory to last known good (per user decision: "revert to last known-good config on disk")
                if (_lastKnownGoodConfig != null)
                {
                    _pendingConfig = null;
                    _configChanges.OnNext(_lastKnownGoodConfig);
                    _logger.Warning("[Config] Reverted to last known good configuration");
                }
            }
        }
    }

    public async Task FlushPendingSavesAsync(CancellationToken ct = default)
    {
        var config = _pendingConfig;
        if (config == null)
        {
            _logger.Debug("[Config] No pending config changes to flush");
            return;
        }

        _logger.Information("[Config] Flushing pending config saves to disk");
        await SaveToDiskWithRetryAsync(config, ct).ConfigureAwait(false);
    }

    public async Task<bool> ValidatePathsAsync(UserConfiguration config, CancellationToken ct = default)
    {
        await Task.Yield(); // Ensure async context

        bool isValid = true;

        if (!string.IsNullOrEmpty(config.LoadOrder.File) && !File.Exists(config.LoadOrder.File))
        {
            _logger.Warning($"Load Order file not found: {config.LoadOrder.File}");
            isValid = false;
        }

        if (!string.IsNullOrEmpty(config.XEdit.Binary) && !File.Exists(config.XEdit.Binary))
        {
            _logger.Warning($"xEdit binary not found: {config.XEdit.Binary}");
            isValid = false;
        }

        if (config.Settings.Mo2Mode)
        {
            if (!string.IsNullOrEmpty(config.ModOrganizer.Binary) && !File.Exists(config.ModOrganizer.Binary))
            {
                _logger.Warning($"MO2 binary not found: {config.ModOrganizer.Binary}");
                isValid = false;
            }
        }

        return isValid;
    }

    public async Task<List<string>> GetSkipListAsync(GameType gameType, GameVariant variant = GameVariant.None)
    {
        // Load both configs
        _mainConfigCache ??= await LoadMainConfigAsync().ConfigureAwait(false);
        var userConfig = await LoadUserConfigAsync().ConfigureAwait(false);

        var result = new List<string>();

        // For Enderal, use Enderal-specific key instead of SSE
        var key = variant == GameVariant.Enderal ? "Enderal" : GetGameKey(gameType);

        // 1. User's game-specific skip list (highest priority - user overrides)
        if (userConfig.SkipLists.TryGetValue(key, out var userList))
        {
            result.AddRange(userList);
        }

        // 2. Game-specific skip list from Main.yaml (default DLC/base game protections)
        if (_mainConfigCache.Data.SkipLists.TryGetValue(key, out var mainGameList))
        {
            result.AddRange(mainGameList);
        }

        // 3. TTW: auto-merge FO3 skip list entries into FNV list (silently, per user decision)
        if (variant == GameVariant.TTW)
        {
            var fo3Key = GetGameKey(GameType.Fallout3);
            if (userConfig.SkipLists.TryGetValue(fo3Key, out var userFo3List))
                result.AddRange(userFo3List);
            if (_mainConfigCache.Data.SkipLists.TryGetValue(fo3Key, out var mainFo3List))
                result.AddRange(mainFo3List);
        }

        // 4. Universal from Main.yaml (always applied for safety)
        if (_mainConfigCache.Data.SkipLists.TryGetValue("Universal", out var universalList))
        {
            result.AddRange(universalList);
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<List<string>> GetDefaultSkipListAsync(GameType gameType)
    {
        // Load Main.yaml config only - this contains the default skip lists
        _mainConfigCache ??= await LoadMainConfigAsync().ConfigureAwait(false);

        var result = new List<string>();
        var key = GetGameKey(gameType);

        // 1. Game-specific skip list from Main.yaml (default DLC/base game protections)
        if (_mainConfigCache.Data.SkipLists.TryGetValue(key, out var mainGameList))
        {
            result.AddRange(mainGameList);
        }

        // 2. Universal from Main.yaml (always applied for safety)
        if (_mainConfigCache.Data.SkipLists.TryGetValue("Universal", out var universalList))
        {
            result.AddRange(universalList);
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<List<string>> GetXEditExecutableNamesAsync(GameType gameType)
    {
        _mainConfigCache ??= await LoadMainConfigAsync().ConfigureAwait(false);

        var key = GetGameKey(gameType);
        if (_mainConfigCache.Data.XEditLists.TryGetValue(key, out var list))
        {
            return list;
        }

        // Fallback to Universal if specific not found? Or maybe Universal is always useful?
        if (_mainConfigCache.Data.XEditLists.TryGetValue("Universal", out var universalList))
        {
            return universalList;
        }

        return new List<string>();
    }

    public async Task<List<string>> GetGameSpecificSkipListAsync(GameType gameType, CancellationToken ct = default)
    {
        var userConfig = await LoadUserConfigAsync(ct).ConfigureAwait(false);

        var key = GetGameKey(gameType);
        if (userConfig.SkipLists.TryGetValue(key, out var list))
        {
            return list.ToList(); // Return a copy to prevent external modification
        }

        return new List<string>();
    }

    public async Task UpdateSkipListAsync(GameType gameType, List<string> skipList, CancellationToken ct = default)
    {
        var userConfig = await LoadUserConfigAsync(ct).ConfigureAwait(false);

        var key = GetGameKey(gameType);
        userConfig.SkipLists[key] = skipList.ToList(); // Store a copy

        await SaveUserConfigAsync(userConfig, ct).ConfigureAwait(false);
        _skipListChanges.OnNext(gameType);
        _logger.Information("Skip list updated for {GameType} with {Count} entries", gameType, skipList.Count);
    }

    public async Task AddToSkipListAsync(GameType gameType, string pluginName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pluginName))
        {
            throw new ArgumentException("Plugin name cannot be empty", nameof(pluginName));
        }

        var currentList = await GetGameSpecificSkipListAsync(gameType, ct).ConfigureAwait(false);

        // Check for duplicates (case-insensitive)
        if (currentList.Any(p => string.Equals(p, pluginName, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.Debug("Plugin {PluginName} already in skip list for {GameType}", pluginName, gameType);
            return; // Already exists, no-op
        }

        currentList.Add(pluginName);
        await UpdateSkipListAsync(gameType, currentList, ct).ConfigureAwait(false);
    }

    public async Task RemoveFromSkipListAsync(GameType gameType, string pluginName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pluginName))
        {
            return; // Nothing to remove
        }

        var currentList = await GetGameSpecificSkipListAsync(gameType, ct).ConfigureAwait(false);

        // Find and remove (case-insensitive)
        var toRemove =
            currentList.FirstOrDefault(p => string.Equals(p, pluginName, StringComparison.OrdinalIgnoreCase));
        if (toRemove != null)
        {
            currentList.Remove(toRemove);
            await UpdateSkipListAsync(gameType, currentList, ct).ConfigureAwait(false);
        }
    }

    public async Task<GameType> GetSelectedGameAsync(CancellationToken ct = default)
    {
        var config = await LoadUserConfigAsync(ct).ConfigureAwait(false);
        if (Enum.TryParse<GameType>(config.SelectedGame, true, out var gameType))
        {
            return gameType;
        }

        return GameType.Unknown;
    }

    public async Task SetSelectedGameAsync(GameType gameType, CancellationToken ct = default)
    {
        var config = await LoadUserConfigAsync(ct).ConfigureAwait(false);
        config.SelectedGame = gameType.ToString();
        await SaveUserConfigAsync(config, ct).ConfigureAwait(false);
    }

    public async Task<string?> GetGameDataFolderOverrideAsync(GameType gameType, CancellationToken ct = default)
    {
        var config = await LoadUserConfigAsync(ct).ConfigureAwait(false);
        var key = GetGameKey(gameType);

        return config.GameDataFolderOverrides.GetValueOrDefault(key);
    }

    public async Task SetGameDataFolderOverrideAsync(GameType gameType, string? folderPath,
        CancellationToken ct = default)
    {
        var config = await LoadUserConfigAsync(ct).ConfigureAwait(false);
        var key = GetGameKey(gameType);

        if (string.IsNullOrWhiteSpace(folderPath))
        {
            // Remove the override if null or empty
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
        _logger.Information("Resetting user configuration to defaults");
        var defaultConfig = new UserConfiguration();
        await SaveUserConfigAsync(defaultConfig, ct).ConfigureAwait(false);
    }

    public async Task<Dictionary<string, object?>> GetAllSettingsAsync(CancellationToken ct = default)
    {
        var config = await LoadUserConfigAsync(ct).ConfigureAwait(false);
        return new Dictionary<string, object?>
        {
            ["XEditPath"] = config.XEdit.Binary,
            ["LoadOrderPath"] = config.LoadOrder.File,
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

    public async Task UpdateMultipleAsync(Action<UserConfiguration> updateAction, CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var config = await LoadUserConfigAsync(ct).ConfigureAwait(false);
            updateAction(config);
            await SaveUserConfigAsync(config, ct).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task ReloadFromDiskAsync(CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Clear in-memory caches to force fresh read from disk
            _pendingConfig = null;
            _mainConfigCache = null;

            var path = Path.Combine(_configDirectory, UserConfigFile);
            if (!File.Exists(path))
            {
                _logger.Warning("[Config] Config file not found during reload: {Path}", path);
                return;
            }

            var content = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            var loaded = _deserializer.Deserialize<UserConfiguration>(content);
            _lastKnownGoodConfig = loaded;
            _configChanges.OnNext(loaded);
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

    public string? GetLastWrittenHash() => _lastWrittenHash;

    /// <summary>
    /// Computes a SHA256 hex hash of the file at the given path.
    /// </summary>
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
        _debounceSubscription.Dispose();
        _saveRequests.Dispose();
        _fileLock.Dispose();
        _configChanges.Dispose();
        _skipListChanges.Dispose();
    }
}
