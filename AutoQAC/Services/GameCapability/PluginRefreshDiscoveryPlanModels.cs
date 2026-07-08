using System.Collections.Generic;
using AutoQAC.Models;
using AutoQAC.Services.Plugin;

namespace AutoQAC.Services.GameCapability;

/// <summary>
/// Selected game context requested by Plugin refresh discovery planning.
/// </summary>
/// <param name="GameType">Game to refresh.</param>
/// <param name="SelectedLoadOrderPath">Optional load-order path selected during this refresh intent.</param>
public sealed record PluginRefreshDiscoveryPlanRequest(
    GameType GameType,
    string? SelectedLoadOrderPath);

/// <summary>
/// Structured outcome from creating a Plugin refresh discovery plan.
/// </summary>
/// <param name="Status">Planning outcome kind.</param>
/// <param name="Plan">Ready plan when <paramref name="Status" /> is <see cref="PluginRefreshDiscoveryPlanStatus.Ready" />.</param>
/// <param name="Configuration">Resolved configuration display fields for publication.</param>
public sealed record PluginRefreshDiscoveryPlanResult(
    PluginRefreshDiscoveryPlanStatus Status,
    PluginRefreshDiscoveryPlan? Plan,
    PluginRefreshConfigurationProjection Configuration)
{
    /// <summary>
    /// Gets whether the planner produced a loadable plan.
    /// </summary>
    public bool IsReady => Status == PluginRefreshDiscoveryPlanStatus.Ready && Plan is not null;
}

/// <summary>
/// Resolved plan for loading plugin rows and later refreshing issue approximations in the same game context.
/// </summary>
/// <param name="GameType">Game to refresh.</param>
/// <param name="Mode">Discovery source to use.</param>
/// <param name="Configuration">Resolved configuration display fields for publication.</param>
/// <param name="DisableSkipLists">Whether skip-list policy is disabled for this refresh.</param>
/// <param name="CanAttemptIssueApproximation">Whether issue approximation can be attempted for this game.</param>
/// <param name="DataFolderPath">Resolved direct game data folder, if available.</param>
/// <param name="LoadOrderPath">Direct-mode load-order path, if direct file loading is used.</param>
/// <param name="Mo2LoadOrderPath">MO2 profile load-order path, if MO2 file loading is used.</param>
/// <param name="Mo2PathMap">MO2 virtualized plugin path map keyed by plugin file name.</param>
/// <param name="Mo2BaseDataFolder">Base game data folder used by MO2 issue approximation.</param>
public sealed record PluginRefreshDiscoveryPlan(
    GameType GameType,
    PluginRefreshDiscoveryMode Mode,
    PluginRefreshConfigurationProjection Configuration,
    bool DisableSkipLists,
    bool CanAttemptIssueApproximation,
    string? DataFolderPath,
    string? LoadOrderPath,
    string? Mo2LoadOrderPath,
    IReadOnlyDictionary<string, string> Mo2PathMap,
    string? Mo2BaseDataFolder);

/// <summary>
/// Plugin rows loaded from a ready discovery plan.
/// </summary>
/// <param name="Plan">Discovery plan used for loading.</param>
/// <param name="Plugins">Loaded plugin rows.</param>
/// <param name="LoadingStatus">Typed status when loading used automatic discovery; otherwise null.</param>
public sealed record PluginRefreshDiscoveredPlugins(
    PluginRefreshDiscoveryPlan Plan,
    IReadOnlyList<PluginInfo> Plugins,
    PluginLoadingStatus? LoadingStatus);

/// <summary>
/// Main-window affordances derived from Game capability and current MO2 mode.
/// </summary>
/// <param name="GameType">Selected game.</param>
/// <param name="IsMutagenSupported">Whether automatic Mutagen-backed discovery is available.</param>
/// <param name="RequiresLoadOrderFile">Whether direct mode requires a load-order file.</param>
/// <param name="CanAttemptIssueApproximation">Whether issue approximation can be attempted for this game.</param>
public sealed record PluginRefreshGameAffordance(
    GameType GameType,
    bool IsMutagenSupported,
    bool RequiresLoadOrderFile,
    bool CanAttemptIssueApproximation);

/// <summary>
/// Outcome kinds returned while resolving a Plugin refresh discovery plan.
/// </summary>
public enum PluginRefreshDiscoveryPlanStatus
{
    Ready,
    NoGameSelected,
    MissingLoadOrderFile,
    MissingMo2Instance,
    MissingMo2Profile,
    MissingMo2ProfileLoadOrder,
    UnsupportedGame
}

/// <summary>
/// Concrete plugin row discovery source chosen by a ready Plugin refresh discovery plan.
/// </summary>
public enum PluginRefreshDiscoveryMode
{
    None,
    DirectAutomatic,
    DirectLoadOrderFile,
    Mo2LoadOrderFile
}
