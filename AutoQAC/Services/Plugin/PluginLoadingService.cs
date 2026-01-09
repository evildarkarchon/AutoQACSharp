using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
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

    public PluginLoadingService(
        IPluginValidationService pluginValidation,
        ILoggingService logger)
    {
        _pluginValidation = pluginValidation;
        _logger = logger;
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
        CancellationToken ct = default)
    {
        return await _pluginValidation.GetPluginsFromLoadOrderAsync(loadOrderPath, ct)
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

        if (!IsGameSupportedByMutagen(gameType))
        {
            return null;
        }

        try
        {
            var release = MapToGameRelease(gameType);
            using var env = GameEnvironment.Typical.Builder(release).Build();
            return env.DataFolderPath.Path;
        }
        catch (Exception ex)
        {
            _logger.Debug($"Could not detect data folder for {gameType}: {ex.Message}");
            return null;
        }
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
}
