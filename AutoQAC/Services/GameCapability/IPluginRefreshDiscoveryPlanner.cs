using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;
using AutoQAC.Services.Plugin;

namespace AutoQAC.Services.GameCapability;

/// <summary>
/// Creates resolved Plugin refresh discovery plans and exposes catalog facts without leaking discovery-mode decisions to callers.
/// </summary>
public interface IPluginRefreshDiscoveryPlanner
{
    /// <summary>
    /// Gets the games available for user selection in a stable display order.
    /// </summary>
    /// <returns>All selectable games, excluding <see cref="GameType.Unknown" />.</returns>
    IReadOnlyList<GameType> GetAvailableGames();

    /// <summary>
    /// Gets main-window affordances for the selected game and MO2 mode combination.
    /// </summary>
    /// <param name="gameType">Selected game.</param>
    /// <param name="mo2ModeEnabled">Whether MO2 mode is enabled.</param>
    /// <returns>Resolved UI affordance facts for the selected game.</returns>
    PluginRefreshGameAffordance GetAffordance(GameType gameType, bool mo2ModeEnabled);

    /// <summary>
    /// Creates a resolved plan for loading plugin rows in the requested game context.
    /// </summary>
    /// <param name="request">Selected game and optional direct-mode load-order path.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A structured planning outcome and configuration projection for Plugin refresh publication.</returns>
    Task<PluginRefreshDiscoveryPlanResult> CreatePlanAsync(
        PluginRefreshDiscoveryPlanRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Creates an opaque freshness token for the accepted Plugin refresh discovery plan.
    /// </summary>
    /// <param name="plan">Accepted discovery plan.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A token that callers store without inspecting.</returns>
    Task<PluginRefreshDiscoveryFreshnessToken> CreateFreshnessTokenAsync(
        PluginRefreshDiscoveryPlan plan,
        CancellationToken ct = default);

    /// <summary>
    /// Checks whether current Discovery-affecting settings still match an accepted freshness token.
    /// </summary>
    /// <param name="accepted">Opaque token stored with the accepted Plugin refresh publication.</param>
    /// <param name="current">Current AppState-owned context needed for freshness checking.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Freshness facts for the accepted Plugin refresh publication.</returns>
    Task<PluginRefreshFreshness> CheckFreshnessAsync(
        PluginRefreshDiscoveryFreshnessToken accepted,
        PluginRefreshDiscoveryFreshnessContext current,
        CancellationToken ct = default);

    /// <summary>
    /// Loads plugin rows using a ready discovery plan.
    /// </summary>
    /// <param name="plan">Ready discovery plan returned by <see cref="CreatePlanAsync" />.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Loaded plugins and any typed automatic-loading status.</returns>
    Task<PluginRefreshDiscoveredPlugins> LoadPluginsAsync(
        PluginRefreshDiscoveryPlan plan,
        CancellationToken ct = default);
}
