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
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AutoQAC.Services.Configuration;

public sealed class ConfigurationService : IConfigurationService, IDisposable
{
    private readonly ILoggingService _logger;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly Subject<UserConfiguration> _configChanges = new();
    private readonly Subject<GameType> _skipListChanges = new();

    private readonly string _configDirectory;
    private const string MainConfigFile = "AutoQAC Main.yaml";
    private const string UserConfigFile = "AutoQAC Settings.yaml";
    private const string LegacyUserConfigFile = "AutoQAC Config.yaml";

    private MainConfiguration? _mainConfigCache;
    private bool _migrationCompleted;
    private readonly ISerializer _serializer;
    private readonly IDeserializer _deserializer;

    public IObservable<UserConfiguration> UserConfigurationChanged => _configChanges;
    public IObservable<GameType> SkipListChanged => _skipListChanges;

    public ConfigurationService(ILoggingService logger, string? configDirectory = null)
    {
        _logger = logger;
        _configDirectory = configDirectory ?? "AutoQAC Data";
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
        // Run migration once per session if needed
        if (!_migrationCompleted)
        {
            await MigrateLegacyConfigAsync(ct).ConfigureAwait(false);
            _migrationCompleted = true;
        }

        var path = Path.Combine(_configDirectory, UserConfigFile);
        if (!File.Exists(path))
        {
            _logger.Information($"User config file not found at {path}. Creating default.");
            var config = new UserConfiguration();
            await SaveUserConfigAsync(config, ct).ConfigureAwait(false);
            return config;
        }

        try
        {
            await _fileLock.WaitAsync(ct).ConfigureAwait(false);
            var content = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            return _deserializer.Deserialize<UserConfiguration>(content);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load user configuration");
            throw;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task MigrateLegacyConfigAsync(CancellationToken ct = default)
    {
        var legacyPath = Path.Combine(_configDirectory, LegacyUserConfigFile);
        var newPath = Path.Combine(_configDirectory, UserConfigFile);

        // Only migrate if legacy file exists and new file doesn't
        if (!File.Exists(legacyPath))
        {
            return;
        }

        _logger.Information("Found legacy config file {LegacyPath}, migrating to {NewPath}", legacyPath, newPath);

        try
        {
            await _fileLock.WaitAsync(ct).ConfigureAwait(false);

            // Load legacy config
            var legacyContent = await File.ReadAllTextAsync(legacyPath, ct).ConfigureAwait(false);
            var legacyConfig = _deserializer.Deserialize<UserConfiguration>(legacyContent);

            // If new file exists, merge skip lists from it (preserve user skip lists)
            if (File.Exists(newPath))
            {
                var existingContent = await File.ReadAllTextAsync(newPath, ct).ConfigureAwait(false);
                var existingConfig = _deserializer.Deserialize<UserConfiguration>(existingContent);

                // Merge skip lists from existing Settings.yaml into the migrated config
                if (existingConfig?.SkipLists != null)
                {
                    foreach (var kvp in existingConfig.SkipLists)
                    {
                        legacyConfig.SkipLists[kvp.Key] = kvp.Value;
                    }
                }
            }

            // Save merged config to new location
            var content = _serializer.Serialize(legacyConfig);
            await File.WriteAllTextAsync(newPath, content, ct).ConfigureAwait(false);

            _logger.Information("Migration complete, deleting legacy config file");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to migrate legacy config");
            // Don't throw - we can continue with whatever config exists
            return;
        }
        finally
        {
            _fileLock.Release();
        }

        // Delete legacy file outside of lock
        try
        {
            File.Delete(legacyPath);
            _logger.Information("Deleted legacy config file {LegacyPath}", legacyPath);
        }
        catch (Exception ex)
        {
            _logger.Warning("Failed to delete legacy config file {LegacyPath}: {Message}", legacyPath, ex.Message);
        }
    }

    public async Task SaveUserConfigAsync(UserConfiguration config, CancellationToken ct = default)
    {
        var path = Path.Combine(_configDirectory, UserConfigFile);
        try
        {
            await _fileLock.WaitAsync(ct).ConfigureAwait(false);
            var content = _serializer.Serialize(config);
            await File.WriteAllTextAsync(path, content, ct).ConfigureAwait(false);
            _configChanges.OnNext(config);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save user configuration");
            throw;
        }
        finally
        {
            _fileLock.Release();
        }
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

        if (config.Settings.MO2Mode)
        {
             if (!string.IsNullOrEmpty(config.ModOrganizer.Binary) && !File.Exists(config.ModOrganizer.Binary))
             {
                 _logger.Warning($"MO2 binary not found: {config.ModOrganizer.Binary}");
                 isValid = false;
             }
        }

        return isValid;
    }

    public async Task<List<string>> GetSkipListAsync(GameType gameType)
    {
        // Load both configs
        _mainConfigCache ??= await LoadMainConfigAsync().ConfigureAwait(false);
        var userConfig = await LoadUserConfigAsync().ConfigureAwait(false);

        var result = new List<string>();
        var key = GetGameKey(gameType);

        // 1. User's game-specific skip list
        if (userConfig.SkipLists.TryGetValue(key, out var userList))
        {
            result.AddRange(userList);
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
        var toRemove = currentList.FirstOrDefault(p => string.Equals(p, pluginName, StringComparison.OrdinalIgnoreCase));
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
        
        if (config.GameDataFolderOverrides.TryGetValue(key, out var folderPath))
        {
            return folderPath;
        }
        
        return null;
    }

    public async Task SetGameDataFolderOverrideAsync(GameType gameType, string? folderPath, CancellationToken ct = default)
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
        _fileLock.Dispose();
        _configChanges.Dispose();
        _skipListChanges.Dispose();
    }
}
