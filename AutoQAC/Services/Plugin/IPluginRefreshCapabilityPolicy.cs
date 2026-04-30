using AutoQAC.Models;

namespace AutoQAC.Services.Plugin;

/// <summary>
/// Describes the refresh-scoped game capability surface needed by plugin loading and issue approximation refresh.
/// This is intentionally narrower than a full app-wide game capability registry.
/// </summary>
public interface IPluginRefreshCapabilityPolicy
{
    /// <summary>
    /// Returns whether AutoQAC can load plugin rows for the specified game through the refresh coordinator.
    /// </summary>
    /// <param name="gameType">Game to evaluate.</param>
    /// <returns>True when plugin rows can be loaded for refresh.</returns>
    bool SupportsPluginLoading(GameType gameType);

    /// <summary>
    /// Returns whether AutoQAC should run issue approximation analysis for the specified game.
    /// </summary>
    /// <param name="gameType">Game to evaluate.</param>
    /// <returns>True when approximation refresh is available for this game in the app UI.</returns>
    bool SupportsIssueApproximation(GameType gameType);

    /// <summary>
    /// Returns whether the game requires an explicit load-order file instead of Mutagen load-order discovery.
    /// </summary>
    /// <param name="gameType">Game to evaluate.</param>
    /// <returns>True when refresh requires a load-order file path.</returns>
    bool RequiresLoadOrderFile(GameType gameType);
}
