using System.Collections.Generic;
using AutoQAC.Models;
using AutoQAC.Services.GameCapability;

namespace AutoQAC.Services.Plugin;

/// <summary>
/// Authoritative Plugin refresh publication consumed by later Cleaning session preflight.
/// </summary>
/// <param name="Generation">Plugin refresh generation that accepted this publication.</param>
/// <param name="GameType">Selected game context for this publication.</param>
/// <param name="DiscoveryPlan">Accepted discovery plan, when plugin discovery reached a ready plan.</param>
/// <param name="Configuration">Configuration facts resolved by Plugin refresh discovery planning.</param>
/// <param name="Freshness">Whether the accepted plan still matches Discovery-affecting settings.</param>
/// <param name="Rows">Full published rows, including hidden Skip list rows.</param>
/// <param name="VisibleRows">Visible rows projected for UI compatibility.</param>
/// <param name="Activity">Current Plugin refresh activity flags.</param>
/// <param name="Commands">Current Plugin refresh command availability facts.</param>
/// <param name="StatusText">User-facing publication status text.</param>
public sealed record PluginRefreshPublication(
    long Generation,
    GameType GameType,
    PluginRefreshDiscoveryPlan? DiscoveryPlan,
    PluginRefreshConfigurationProjection Configuration,
    PluginRefreshFreshness Freshness,
    IReadOnlyList<PluginRefreshPublishedRow> Rows,
    IReadOnlyList<PluginRefreshRow> VisibleRows,
    PluginRefreshActivity Activity,
    PluginRefreshCommandAvailability Commands,
    string StatusText);

/// <summary>
/// Freshness facts for an accepted Plugin refresh publication.
/// </summary>
/// <param name="IsFresh">True when Discovery-affecting settings still match the accepted publication.</param>
/// <param name="StalenessReason">The first detected freshness mismatch, or null when fresh.</param>
public sealed record PluginRefreshFreshness(
    bool IsFresh,
    PluginRefreshStalenessReason? StalenessReason)
{
    /// <summary>Fresh publication marker.</summary>
    public static PluginRefreshFreshness Fresh { get; } = new(true, null);

    /// <summary>Missing publication marker.</summary>
    public static PluginRefreshFreshness Missing { get; } = new(false, PluginRefreshStalenessReason.MissingPublication);
}

/// <summary>
/// Reasons a Plugin refresh publication is unavailable or no longer matches Discovery-affecting settings.
/// </summary>
public enum PluginRefreshStalenessReason
{
    MissingPublication,
    SelectedGameChanged,
    Mo2ModeChanged,
    Mo2ExecutablePathChanged,
    LoadOrderPathChanged,
    GameDataFolderOverrideChanged,
    Mo2InstanceChanged,
    Mo2ProfileChanged,
    SkipListSettingsChanged
}

/// <summary>
/// Full Plugin refresh row facts used by Cleaning session preflight.
/// </summary>
/// <param name="Plugin">Full plugin row published by Plugin refresh.</param>
/// <param name="IsVisible">Whether the row is visible in the UI.</param>
/// <param name="IsSelected">Whether Plugin selection includes this row for Cleaning session consideration.</param>
/// <param name="IsSkippedByPolicy">Whether Skip list policy excludes this row.</param>
/// <param name="Key">Stable row identity used for Plugin selection and issue approximation matching.</param>
public sealed record PluginRefreshPublishedRow(
    PluginInfo Plugin,
    bool IsVisible,
    bool IsSelected,
    bool IsSkippedByPolicy,
    PluginRefreshRowKey Key);
