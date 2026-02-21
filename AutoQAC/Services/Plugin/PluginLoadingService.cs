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
using Mutagen.Bethesda.Environments;

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

    private static readonly Dictionary<GameType, string[]> RegistryInstallPathKeys = new()
    {
        {
            GameType.Oblivion,
            [
                @"SOFTWARE\Bethesda Softworks\Oblivion",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 22330"
            ]
        },
        {
            GameType.Fallout3,
            [
                @"SOFTWARE\Bethesda Softworks\Fallout3",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 22300"
            ]
        },
        {
            GameType.FalloutNewVegas,
            [
                @"SOFTWARE\Bethesda Softworks\FalloutNV",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 22380"
            ]
        },
        {
            GameType.SkyrimLe,
            [
                @"SOFTWARE\Bethesda Softworks\Skyrim",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 72850"
            ]
        },
        {
            GameType.SkyrimSe,
            [
                @"SOFTWARE\Bethesda Softworks\Skyrim Special Edition",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 489830"
            ]
        },
        {
            GameType.SkyrimVr,
            [
                @"SOFTWARE\Bethesda Softworks\Skyrim VR",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 611670"
            ]
        },
        {
            GameType.Fallout4,
            [
                @"SOFTWARE\Bethesda Softworks\Fallout4",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 377160"
            ]
        },
        {
            GameType.Fallout4Vr,
            [
                @"SOFTWARE\Bethesda Softworks\Fallout 4 VR",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 611660"
            ]
        }
    };

    private static readonly string[] RegistryInstallPathValueNames =
        ["Installed Path", "Install Path", "InstallLocation", "Path"];

    public PluginLoadingService(
        IPluginValidationService pluginValidation,
        ILoggingService logger,
        Func<GameType, string?>? registryDataFolderResolver = null)
    {
        _pluginValidation = pluginValidation;
        _logger = logger;
        _registryDataFolderResolver = registryDataFolderResolver ?? ResolveDataFolderFromRegistry;
    }

    /// <inheritdoc />
    public async Task<List<PluginInfo>> GetPluginsAsync(
        GameType gameType,
        string? customDataFolder = null,
        CancellationToken ct = default)
    {
        if (gameType == GameType.Unknown)
        {
            _logger.Warning("Cannot load plugins for Unknown game type");
            return new List<PluginInfo>();
        }

        if (IsGameSupportedByMutagen(gameType))
        {
            try
            {
                return await LoadFromMutagenAsync(gameType, customDataFolder, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to load plugins via Mutagen for {gameType}, will need file-based fallback: {ex.Message}");
                // Return empty list - caller should prompt for file-based loading
                return new List<PluginInfo>();
            }
        }

        // Non-Mutagen games require file path
        _logger.Information($"Game {gameType} is not supported by Mutagen, use GetPluginsFromFileAsync instead");
        return new List<PluginInfo>();
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
                using var env = GameEnvironment.Typical.Builder(release).Build();
                return env.DataFolderPath.Path;
            }
            catch (Exception ex)
            {
                _logger.Debug($"Could not detect data folder via Mutagen for {gameType}: {ex.Message}");
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
            _logger.Information($"Found default load order path for {gameType}: {path}");
            return path;
        }

        _logger.Debug($"Default load order path does not exist for {gameType}: {path}");
        return null;
    }

    /// <summary>
    /// Load plugins using Mutagen's GameEnvironment.
    /// </summary>
    private Task<List<PluginInfo>> LoadFromMutagenAsync(
        GameType gameType,
        string? customDataFolder,
        CancellationToken ct)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            var release = MapToGameRelease(gameType);

            _logger.Information($"Loading plugins via Mutagen for {gameType}");

            // Use custom data folder if provided, otherwise use typical (registry-detected) path
            using var env = string.IsNullOrEmpty(customDataFolder)
                ? GameEnvironment.Typical.Builder(release).Build()
                : GameEnvironment.Typical.Builder(release)
                    .WithTargetDataFolder(customDataFolder)
                    .Build();

            var dataFolder = env.DataFolderPath.Path;
            _logger.Debug($"Data folder: {dataFolder}");

            if (!string.IsNullOrEmpty(customDataFolder))
            {
                _logger.Information($"Using custom data folder override: {customDataFolder}");
            }

            var plugins = new List<PluginInfo>();

            foreach (var listing in env.LoadOrder.ListedOrder)
            {
                ct.ThrowIfCancellationRequested();

                var fileName = listing.ModKey.FileName;
                var fullPath = Path.Combine(dataFolder, fileName);

                plugins.Add(new PluginInfo
                {
                    FileName = fileName,
                    FullPath = fullPath,
                    IsInSkipList = false,
                    DetectedGameType = gameType
                });
            }

            _logger.Information($"Loaded {plugins.Count} plugins from {gameType} load order");
            return plugins;
        }, ct);
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
