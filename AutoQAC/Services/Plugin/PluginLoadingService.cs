using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using Microsoft.Win32;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Installs;
using Mutagen.Bethesda.Plugins.Order;
using Noggog;

namespace AutoQAC.Services.Plugin;

/// <summary>
/// Service for loading plugins from various sources.
/// Uses Mutagen for supported games, falls back to file-based loading.
/// </summary>
public sealed class PluginLoadingService : IPluginLoadingService
{
    private readonly IPluginValidationService _pluginValidation;
    private readonly ILoggingService _logger;
    private readonly Func<GameType, string?> _registryDataFolderResolver;
    private readonly Func<GameType, string?, CancellationToken, (string? DataFolder, IReadOnlyList<string> PluginFileNames)>
        _mutagenListingProvider;

    /// <summary>
    /// Games supported by Mutagen for load order detection.
    /// Note: Fallout 3, Fallout NV, and Oblivion are not supported by Mutagen.
    /// </summary>
    private static readonly HashSet<GameType> MutagenSupportedGames =
    [
        GameType.SkyrimLe,
        GameType.SkyrimSe,
        GameType.SkyrimVr,
        GameType.Fallout4,
        GameType.Fallout4Vr
    ];

    /// <summary>
    /// Maps GameType to My Games folder names for non-Mutagen games.
    /// These games require file-based load order detection.
    /// </summary>
    private static readonly Dictionary<GameType, string> MyGamesFolderNames = new()
    {
        { GameType.Fallout3, "Fallout3" },
        { GameType.FalloutNewVegas, "FalloutNV" },
        { GameType.Oblivion, "Oblivion" }
    };

    private static readonly Dictionary<GameType, string[]> RegistryInstallPathKeys =
        CreateRegistryInstallPathKeys();

    private static readonly string[] RegistryInstallPathValueNames =
        ["Installed Path", "Install Path", "InstallLocation", "Path"];

    public PluginLoadingService(
        IPluginValidationService pluginValidation,
        ILoggingService logger,
        Func<GameType, string?>? registryDataFolderResolver = null,
        Func<GameType, string?, CancellationToken, (string? DataFolder, IReadOnlyList<string> PluginFileNames)>?
            mutagenListingProvider = null)
    {
        _pluginValidation = pluginValidation;
        _logger = logger;
        _registryDataFolderResolver = registryDataFolderResolver ?? ResolveDataFolderFromRegistry;
        _mutagenListingProvider = mutagenListingProvider ?? LoadMutagenListings;
    }

    /// <inheritdoc />
    public async Task<List<PluginInfo>> GetPluginsAsync(
        GameType gameType,
        string? customDataFolder = null,
        CancellationToken ct = default)
    {
        var result = await TryGetPluginsAsync(gameType, customDataFolder, ct).ConfigureAwait(false);
        return result.Status == PluginLoadingStatus.Success
            ? result.Plugins.ToList()
            : new List<PluginInfo>();
    }

    /// <inheritdoc />
    public async Task<PluginLoadingResult> TryGetPluginsAsync(
        GameType gameType,
        string? customDataFolder = null,
        CancellationToken ct = default)
    {
        if (gameType == GameType.Unknown || !IsGameSupportedByMutagen(gameType))
        {
            _logger.Information(
                "Game {GameType} is not supported by Mutagen, use GetPluginsFromFileAsync instead",
                gameType);
            return new PluginLoadingResult
            {
                Status = PluginLoadingStatus.UnsupportedGame,
                Plugins = Array.Empty<PluginInfo>()
            };
        }

        try
        {
            ct.ThrowIfCancellationRequested();

            var (resolvedDataFolder, pluginFileNames) = await Task
                .Run(() => _mutagenListingProvider(gameType, customDataFolder, ct), ct)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(resolvedDataFolder))
            {
                _logger.Warning("Could not resolve data folder via Mutagen for {GameType}", gameType);
                return new PluginLoadingResult
                {
                    Status = PluginLoadingStatus.DataFolderNotFound,
                    Plugins = Array.Empty<PluginInfo>(),
                    DataFolder = customDataFolder
                };
            }

            var plugins = CreatePluginInfoList(gameType, resolvedDataFolder, pluginFileNames);
            if (plugins.Count == 0)
            {
                _logger.Information("Mutagen returned an empty load order for {GameType}", gameType);
                return new PluginLoadingResult
                {
                    Status = PluginLoadingStatus.NoPluginsDiscovered,
                    Plugins = Array.Empty<PluginInfo>(),
                    DataFolder = resolvedDataFolder
                };
            }

            _logger.Information("Loaded {Count} plugins from {GameType} load order", plugins.Count, gameType);
            return new PluginLoadingResult
            {
                Status = PluginLoadingStatus.Success,
                Plugins = plugins.AsReadOnly(),
                DataFolder = resolvedDataFolder
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(
                "Failed to load plugins via Mutagen for {GameType}; file-based loading can be used if configured: {Message}",
                gameType,
                ex.Message);
            return new PluginLoadingResult
            {
                Status = PluginLoadingStatus.Failed,
                Plugins = Array.Empty<PluginInfo>(),
                DataFolder = customDataFolder,
                FailureReason = ex.Message
            };
        }
    }

    /// <inheritdoc />
    public async Task<List<PluginInfo>> GetPluginsFromFileAsync(
        string loadOrderPath,
        string? dataFolderPath = null,
        CancellationToken ct = default)
    {
        return await _pluginValidation.GetPluginsFromLoadOrderAsync(loadOrderPath, dataFolderPath, ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public bool IsGameSupportedByMutagen(GameType gameType)
    {
        return MutagenSupportedGames.Contains(gameType);
    }

    /// <inheritdoc />
    public IReadOnlyList<GameType> GetAvailableGames()
    {
        return Enum.GetValues<GameType>()
            .Where(g => g != GameType.Unknown)
            .OrderBy(g => g.ToString())
            .ToList();
    }

    /// <inheritdoc />
    public string? GetGameDataFolder(GameType gameType, string? customDataFolderOverride = null)
    {
        // Return override if provided
        if (!string.IsNullOrEmpty(customDataFolderOverride))
        {
            return customDataFolderOverride;
        }

        if (IsGameSupportedByMutagen(gameType))
        {
            try
            {
                var release = MapToGameRelease(gameType);
                if (GameLocations.TryGetDataFolder(release, out var detectedDataFolder))
                {
                    return detectedDataFolder.Path;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug("Could not detect data folder via Mutagen for {GameType}: {Message}", gameType, ex.Message);
            }
        }

        var registryPath = _registryDataFolderResolver(gameType);
        if (!string.IsNullOrWhiteSpace(registryPath))
        {
            _logger.Information("Detected data folder via registry for {GameType}: {DataFolder}", gameType, registryPath);
            return registryPath;
        }

        return null;
    }

    /// <inheritdoc />
    public string? GetDefaultLoadOrderPath(GameType gameType)
    {
        if (!MyGamesFolderNames.TryGetValue(gameType, out var folderName))
        {
            return null;
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var path = Path.Combine(documents, "My Games", folderName, "plugins.txt");

        if (File.Exists(path))
        {
            _logger.Information("Found default load order path for {GameType}: {Path}", gameType, path);
            return path;
        }

        _logger.Debug("Default load order path does not exist for {GameType}: {Path}", gameType, path);
        return null;
    }

    private static List<PluginInfo> CreatePluginInfoList(
        GameType gameType,
        string dataFolder,
        IReadOnlyList<string> pluginFileNames)
    {
        var plugins = new List<PluginInfo>(pluginFileNames.Count);

        foreach (var fileName in pluginFileNames)
        {
            plugins.Add(new PluginInfo
            {
                FileName = fileName,
                FullPath = Path.Combine(dataFolder, fileName),
                IsInSkipList = false,
                DetectedGameType = gameType
            });
        }

        return plugins;
    }

    private static (string? DataFolder, IReadOnlyList<string> PluginFileNames) LoadMutagenListings(
        GameType gameType,
        string? customDataFolder,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var release = MapToGameRelease(gameType);
        var dataFolder = customDataFolder;

        if (string.IsNullOrWhiteSpace(dataFolder))
        {
            if (!GameLocations.TryGetDataFolder(release, out var detectedDataFolder))
            {
                return (null, Array.Empty<string>());
            }

            dataFolder = detectedDataFolder.Path;
        }

        if (string.IsNullOrWhiteSpace(dataFolder))
        {
            return (null, Array.Empty<string>());
        }

        var listings = LoadOrder.GetLoadOrderListings(
            release,
            new DirectoryPath(dataFolder),
            throwOnMissingMods: false);

        var pluginFileNames = new List<string>();
        foreach (var listing in listings)
        {
            ct.ThrowIfCancellationRequested();
            pluginFileNames.Add(listing.ModKey.FileName);
        }

        return (dataFolder, pluginFileNames);
    }

    /// <summary>
    /// Maps GameType to Mutagen's GameRelease.
    /// </summary>
    private static GameRelease MapToGameRelease(GameType gameType) => gameType switch
    {
        GameType.SkyrimLe => GameRelease.SkyrimLE,
        GameType.SkyrimSe => GameRelease.SkyrimSE,
        GameType.SkyrimVr => GameRelease.SkyrimVR,
        GameType.Fallout4 => GameRelease.Fallout4,
        GameType.Fallout4Vr => GameRelease.Fallout4VR,
        _ => throw new ArgumentException($"Game {gameType} is not supported by Mutagen")
    };

    private static Dictionary<GameType, string[]> CreateRegistryInstallPathKeys() => new()
    {
        { GameType.Oblivion, CreateBethesdaRegistryKeyCandidates("Oblivion", "22330") },
        { GameType.Fallout3, CreateBethesdaRegistryKeyCandidates("Fallout3", "22300") },
        { GameType.FalloutNewVegas, CreateBethesdaRegistryKeyCandidates("FalloutNV", "22380") },
        { GameType.SkyrimLe, CreateBethesdaRegistryKeyCandidates("Skyrim", "72850") },
        { GameType.SkyrimSe, CreateBethesdaRegistryKeyCandidates("Skyrim Special Edition", "489830") },
        { GameType.SkyrimVr, CreateBethesdaRegistryKeyCandidates("Skyrim VR", "611670") },
        { GameType.Fallout4, CreateBethesdaRegistryKeyCandidates("Fallout4", "377160") },
        { GameType.Fallout4Vr, CreateBethesdaRegistryKeyCandidates("Fallout 4 VR", "611660") }
    };

    private static string[] CreateBethesdaRegistryKeyCandidates(string bethesdaSubKey, string steamAppId) =>
    [
        $@"SOFTWARE\WOW6432Node\Bethesda Softworks\{bethesdaSubKey}",
        $@"SOFTWARE\Bethesda Softworks\{bethesdaSubKey}",
        $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App {steamAppId}"
    ];

    private string? ResolveDataFolderFromRegistry(GameType gameType)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        if (!RegistryInstallPathKeys.TryGetValue(gameType, out var subKeys))
        {
            return null;
        }

        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                foreach (var subKeyPath in subKeys)
                {
                    try
                    {
                        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                        using var key = baseKey.OpenSubKey(subKeyPath);

                        if (key == null)
                        {
                            continue;
                        }

                        foreach (var valueName in RegistryInstallPathValueNames)
                        {
                            if (key.GetValue(valueName) is string rawPath)
                            {
                                var normalized = NormalizeDataFolderPath(rawPath);
                                if (!string.IsNullOrWhiteSpace(normalized))
                                {
                                    return normalized;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(
                            "Registry probe failed for {GameType} key {SubKey} ({Hive}/{View}): {Message}",
                            gameType,
                            subKeyPath,
                            hive,
                            view,
                            ex.Message);
                    }
                }
            }
        }

        return null;
    }

    private static string? NormalizeDataFolderPath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        var trimmed = rawPath.Trim().Trim('"');
        if (!Path.IsPathRooted(trimmed))
        {
            return null;
        }

        if (Directory.Exists(trimmed))
        {
            var dirName = Path.GetFileName(trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.Equals(dirName, "Data", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }
        }

        var dataFolder = Path.Combine(trimmed, "Data");
        if (Directory.Exists(dataFolder))
        {
            return dataFolder;
        }

        return null;
    }
}
