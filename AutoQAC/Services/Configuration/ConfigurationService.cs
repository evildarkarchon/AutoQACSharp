using System;
using System.Collections.Generic;
using System.IO;
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

    private const string ConfigDirectory = "AutoQAC Data";
    private const string MainConfigFile = "AutoQAC Main.yaml";
    private const string UserConfigFile = "AutoQAC Config.yaml";

    private MainConfiguration? _mainConfigCache;
    private readonly ISerializer _serializer;
    private readonly IDeserializer _deserializer;

    public IObservable<UserConfiguration> UserConfigurationChanged => _configChanges;

    public ConfigurationService(ILoggingService logger)
    {
        _logger = logger;
        _serializer = new SerializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .Build();
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
            
        if (!Directory.Exists(ConfigDirectory))
        {
            Directory.CreateDirectory(ConfigDirectory);
        }
    }

    public async Task<MainConfiguration> LoadMainConfigAsync(CancellationToken ct = default)
    {
        if (_mainConfigCache != null) return _mainConfigCache;

        var path = Path.Combine(ConfigDirectory, MainConfigFile);
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
            await _fileLock.WaitAsync(ct);
            var content = await File.ReadAllTextAsync(path, ct);
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
        var path = Path.Combine(ConfigDirectory, UserConfigFile);
        if (!File.Exists(path))
        {
            _logger.Information($"User config file not found at {path}. Creating default.");
            var config = new UserConfiguration();
            await SaveUserConfigAsync(config, ct);
            return config;
        }

        try
        {
            await _fileLock.WaitAsync(ct);
            var content = await File.ReadAllTextAsync(path, ct);
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

    public async Task SaveUserConfigAsync(UserConfiguration config, CancellationToken ct = default)
    {
        var path = Path.Combine(ConfigDirectory, UserConfigFile);
        try
        {
            await _fileLock.WaitAsync(ct);
            var content = _serializer.Serialize(config);
            await File.WriteAllTextAsync(path, content, ct);
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

    public List<string> GetSkipList(GameType gameType)
    {
        // Ensure main config is loaded. Synchronous wait is not ideal but this method is sync.
        // Ideally should be async or cache should be preloaded.
        // For now, if cache is null, try to load (will block).
        if (_mainConfigCache == null)
        {
             // Warning: Blocking async code
             _mainConfigCache = LoadMainConfigAsync().GetAwaiter().GetResult();
        }

        var key = GetGameKey(gameType);
        if (_mainConfigCache.Data.SkipLists.TryGetValue(key, out var list))
        {
            return list;
        }
        return new List<string>();
    }

    public List<string> GetXEditExecutableNames(GameType gameType)
    {
        if (_mainConfigCache == null)
        {
             _mainConfigCache = LoadMainConfigAsync().GetAwaiter().GetResult();
        }

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

    private string GetGameKey(GameType gameType) => gameType switch
    {
        GameType.Fallout3 => "FO3",
        GameType.FalloutNewVegas => "FNV",
        GameType.Fallout4 => "FO4",
        GameType.SkyrimSpecialEdition => "SSE",
        GameType.Fallout4VR => "FO4VR",
        GameType.SkyrimVR => "SkyrimVR",
        _ => "Unknown"
    };

    public void Dispose()
    {
        _fileLock.Dispose();
        _configChanges.Dispose();
    }
}
