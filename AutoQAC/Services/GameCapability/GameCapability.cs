using AutoQAC.Models;

namespace AutoQAC.Services.GameCapability;

/// <summary>
/// Describes the AutoQAC behaviors available for a selected game.
/// </summary>
/// <param name="GameType">Game this capability row describes.</param>
/// <param name="PluginDiscoveryMode">How AutoQAC can discover plugin rows for this game.</param>
/// <param name="SupportsIssueApproximation">Whether AutoQAC can show pre-cleaning issue approximations for this game.</param>
public sealed record GameCapability(
    GameType GameType,
    PluginDiscoveryMode PluginDiscoveryMode,
    bool SupportsIssueApproximation)
{
    /// <summary>
    /// Gets whether AutoQAC can load plugin rows for this game.
    /// </summary>
    public bool SupportsPluginLoading => PluginDiscoveryMode != PluginDiscoveryMode.None;

    /// <summary>
    /// Gets whether direct-mode plugin refresh requires an explicit load-order file.
    /// </summary>
    public bool RequiresLoadOrderFile => PluginDiscoveryMode == PluginDiscoveryMode.LoadOrderFile;

    /// <summary>
    /// Gets whether AutoQAC can discover plugin rows automatically from the game installation.
    /// </summary>
    public bool SupportsAutomaticPluginDiscovery => PluginDiscoveryMode == PluginDiscoveryMode.Automatic;
}
