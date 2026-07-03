using System.Collections.Generic;
using AutoQAC.Models;

namespace AutoQAC.Services.GameCapability;

/// <summary>
/// Provides the deterministic Game capability matrix used by AutoQAC features.
/// </summary>
public interface IGameCapabilityProvider
{
    /// <summary>
    /// Gets the capability row for the specified game.
    /// </summary>
    /// <param name="gameType">Game to evaluate.</param>
    /// <returns>The capability row for the game.</returns>
    GameCapability Get(GameType gameType);

    /// <summary>
    /// Gets the games available for user selection.
    /// </summary>
    /// <returns>All supported selectable games, excluding <see cref="GameType.Unknown" />.</returns>
    IReadOnlyList<GameType> GetAvailableGames();
}
