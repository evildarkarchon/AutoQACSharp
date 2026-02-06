using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;

namespace AutoQAC.Services.Plugin;

/// <summary>
/// Service for loading plugins from various sources.
/// Uses Mutagen for supported games, falls back to file-based loading.
/// </summary>
public interface IPluginLoadingService
{
    /// <summary>
    /// Gets plugins for the specified game.
    /// Uses Mutagen if supported, falls back to file-based loading.
    /// </summary>
    /// <param name="gameType">The game type to load plugins for.</param>
    /// <param name="customDataFolder">Optional custom data folder path to override registry detection.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of plugins from the game's load order.</returns>
    Task<List<PluginInfo>> GetPluginsAsync(
        GameType gameType,
        string? customDataFolder = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets plugins from a specific load order file path (fallback mode).
    /// </summary>
    /// <param name="loadOrderPath">Path to the load order file.</param>
    /// <param name="dataFolderPath">Optional game data folder path for resolving FullPath.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of plugins from the file.</returns>
    Task<List<PluginInfo>> GetPluginsFromFileAsync(
        string loadOrderPath,
        string? dataFolderPath = null,
        CancellationToken ct = default);

    /// <summary>
    /// Returns true if the game is supported by Mutagen for load order detection.
    /// </summary>
    /// <param name="gameType">The game type to check.</param>
    /// <returns>True if Mutagen can load the game's load order.</returns>
    bool IsGameSupportedByMutagen(GameType gameType);

    /// <summary>
    /// Gets list of games available for selection.
    /// </summary>
    /// <returns>All game types except Unknown.</returns>
    IReadOnlyList<GameType> GetAvailableGames();

    /// <summary>
    /// Gets the data folder path for a game.
    /// Returns the custom override if provided, otherwise queries registry via Mutagen.
    /// </summary>
    /// <param name="gameType">The game type.</param>
    /// <param name="customDataFolderOverride">Optional custom data folder path to use instead of registry detection.</param>
    /// <returns>The data folder path, or null if not found.</returns>
    string? GetGameDataFolder(GameType gameType, string? customDataFolderOverride = null);

    /// <summary>
    /// Gets the default load order file path for a game from My Games folder.
    /// Returns null if the file doesn't exist.
    /// </summary>
    /// <param name="gameType">The game type to get the load order path for.</param>
    /// <returns>The path to plugins.txt if found, otherwise null.</returns>
    string? GetDefaultLoadOrderPath(GameType gameType);
}
