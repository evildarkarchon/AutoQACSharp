using System.Collections.Generic;
using System.Linq;
using AutoQAC.Models;

namespace AutoQAC.Services.GameCapability;

/// <summary>
/// Deterministic Game capability matrix used behind the Plugin refresh discovery plan module.
/// </summary>
internal static class GameCapabilityCatalog
{
    private static readonly IReadOnlyDictionary<GameType, GameCapability> Capabilities =
        new Dictionary<GameType, GameCapability>
        {
            { GameType.Unknown, new GameCapability(GameType.Unknown, PluginDiscoveryMode.None, false) },
            { GameType.SkyrimLe, new GameCapability(GameType.SkyrimLe, PluginDiscoveryMode.Automatic, true) },
            { GameType.SkyrimSe, new GameCapability(GameType.SkyrimSe, PluginDiscoveryMode.Automatic, true) },
            { GameType.SkyrimVr, new GameCapability(GameType.SkyrimVr, PluginDiscoveryMode.Automatic, true) },
            { GameType.Fallout4, new GameCapability(GameType.Fallout4, PluginDiscoveryMode.Automatic, true) },
            { GameType.Fallout4Vr, new GameCapability(GameType.Fallout4Vr, PluginDiscoveryMode.Automatic, true) },
            { GameType.Oblivion, new GameCapability(GameType.Oblivion, PluginDiscoveryMode.LoadOrderFile, false) },
            { GameType.Fallout3, new GameCapability(GameType.Fallout3, PluginDiscoveryMode.LoadOrderFile, false) },
            {
                GameType.FalloutNewVegas,
                new GameCapability(GameType.FalloutNewVegas, PluginDiscoveryMode.LoadOrderFile, false)
            }
        };

    /// <summary>
    /// Gets the capability row for the specified game.
    /// </summary>
    /// <param name="gameType">Game to evaluate.</param>
    /// <returns>The capability row for the game.</returns>
    internal static GameCapability Get(GameType gameType) =>
        Capabilities.TryGetValue(gameType, out var capability)
            ? capability
            : new GameCapability(gameType, PluginDiscoveryMode.None, false);

    /// <summary>
    /// Gets the games available for user selection.
    /// </summary>
    /// <returns>All supported selectable games, excluding <see cref="GameType.Unknown" />.</returns>
    internal static IReadOnlyList<GameType> GetAvailableGames() =>
        Capabilities.Values
            .Where(capability => capability.SupportsPluginLoading)
            .Select(capability => capability.GameType)
            .OrderBy(gameType => gameType.ToString())
            .ToList();
}
