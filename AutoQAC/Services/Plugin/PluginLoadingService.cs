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
    private static readonly HashSet<GameType> MutagenSupportedGames = new()
    {
        GameType.SkyrimLe,
        GameType.SkyrimSe,
        GameType.SkyrimVr,
        GameType.Fallout4,
        GameType.Fallout4Vr
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
                return await LoadFromMutagenAsync(gameType, ct).ConfigureAwait(false);
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
    public string? GetGameDataFolder(GameType gameType)
    {
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

    /// <summary>
    /// Load plugins using Mutagen's GameEnvironment.
    /// </summary>
    private Task<List<PluginInfo>> LoadFromMutagenAsync(
        GameType gameType,
        CancellationToken ct)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            var release = MapToGameRelease(gameType);

            _logger.Information($"Loading plugins via Mutagen for {gameType}");

            using var env = GameEnvironment.Typical.Builder(release).Build();

            var dataFolder = env.DataFolderPath.Path;
            _logger.Debug($"Data folder: {dataFolder}");

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
