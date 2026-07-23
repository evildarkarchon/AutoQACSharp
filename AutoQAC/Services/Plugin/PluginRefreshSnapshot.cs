using System.Collections.Generic;
using AutoQAC.Models;

namespace AutoQAC.Services.Plugin;

/// <summary>
/// Whole visible Plugin refresh publication snapshot ready for ViewModel binding.
/// </summary>
/// <param name="Generation">Active long-running refresh generation that produced the snapshot.</param>
/// <param name="GameType">Game associated with the snapshot.</param>
/// <param name="Rows">Visible plugin rows, excluding Skip list rows.</param>
/// <param name="Configuration">Resolved configuration display fields.</param>
/// <param name="Activity">Current running flags for Plugin refresh work.</param>
/// <param name="Commands">Command availability facts for the current snapshot.</param>
/// <param name="StatusText">User-facing status text for the snapshot.</param>
public sealed record PluginRefreshSnapshot(
    long Generation,
    GameType GameType,
    IReadOnlyList<PluginRefreshRow> Rows,
    PluginRefreshConfigurationProjection Configuration,
    PluginRefreshActivity Activity,
    PluginRefreshCommandAvailability Commands,
    string StatusText);

/// <summary>
/// Visible plugin row with selection and issue approximation display policy already applied.
/// </summary>
/// <param name="FileName">Plugin file name shown in the UI.</param>
/// <param name="FullPath">Resolved full path used for identity and Cleaning session compatibility.</param>
/// <param name="DetectedGameType">Game type detected for the plugin row.</param>
/// <param name="IsSelected">Whether the row is selected for cleaning.</param>
/// <param name="IsInSkipList">Whether the row was matched by the effective Skip list.</param>
/// <param name="Approximation">Best-effort issue approximation for the row.</param>
public sealed record PluginRefreshRow(
    string FileName,
    string FullPath,
    GameType DetectedGameType,
    bool IsSelected,
    bool IsInSkipList,
    PluginIssueApproximation Approximation)
{
    /// <summary>
    /// Gets the stable row key used for selection intents.
    /// </summary>
    public PluginRefreshRowKey Key => new(FileName, FullPath);

    /// <summary>
    /// Gets whether the row has an available issue approximation preview.
    /// </summary>
    public bool HasApproximationPreview => Approximation.Status == PluginIssueApproximationStatus.Available;

    /// <summary>
    /// Gets whether issue approximation analysis is pending for the row.
    /// </summary>
    public bool IsApproximationPending => Approximation.Status == PluginIssueApproximationStatus.Pending;

    /// <summary>
    /// Gets display text for the issue approximation preview.
    /// </summary>
    public string ApproximationDisplayText => Approximation.Status switch
    {
        PluginIssueApproximationStatus.Available =>
            $"Approx. ITM {Approximation.ItmCount} | UDR {Approximation.DeletedReferenceCount} | Nav {Approximation.DeletedNavmeshCount}",
        PluginIssueApproximationStatus.Pending => "Analyzing preview...",
        _ => "Preview unavailable"
    };
}

/// <summary>
/// Immutable identity for a visible Plugin refresh row.
/// </summary>
/// <param name="FileName">Plugin filename component of the row identity.</param>
/// <param name="FullPath">Resolved full-path component of the row identity.</param>
public sealed record PluginRefreshRowKey(string FileName, string FullPath);

/// <summary>
/// Running flags for Plugin refresh work.
/// </summary>
/// <param name="IsPluginRefreshRunning">Whether full plugin list refresh work is active.</param>
/// <param name="IsIssueApproximationRefreshRunning">Whether issue approximation refresh work is active.</param>
public sealed record PluginRefreshActivity(
    bool IsPluginRefreshRunning,
    bool IsIssueApproximationRefreshRunning);

/// <summary>
/// Command availability facts for the current Plugin refresh snapshot.
/// </summary>
/// <param name="CanSelectAll">Whether all visible rows can be selected.</param>
/// <param name="CanDeselectAll">Whether all visible rows can be deselected.</param>
/// <param name="CanRefreshSelectedIssueApproximations">Whether selected issue approximations can be refreshed.</param>
/// <param name="CanCancelRefresh">Whether active Plugin refresh work can be canceled.</param>
public sealed record PluginRefreshCommandAvailability(
    bool CanSelectAll,
    bool CanDeselectAll,
    bool CanRefreshSelectedIssueApproximations,
    bool CanCancelRefresh);

/// <summary>
/// Configuration display fields resolved during Plugin refresh context assembly.
/// </summary>
/// <param name="LoadOrderPath">Effective direct-mode load-order path, if one is being used.</param>
/// <param name="GameDataFolder">Effective game data folder used for loading or analysis, if resolved.</param>
/// <param name="HasGameDataFolderOverride">True when the game data folder came from a user override.</param>
/// <param name="XEditPath">Configured xEdit executable path.</param>
/// <param name="Mo2Path">Configured Mod Organizer executable path.</param>
/// <param name="Mo2ModeEnabled">Whether MO2 mode is enabled.</param>
/// <param name="Mo2InstancePath">Resolved MO2 instance path for display, if MO2 mode is active.</param>
/// <param name="IsMo2InstanceOverride">True when the MO2 instance path came from a per-game override.</param>
/// <param name="IsMo2InstanceValid">Null when no instance is selected; otherwise whether the instance path exists.</param>
/// <param name="AvailableProfiles">Resolved MO2 profiles that contain a load-order source.</param>
/// <param name="SelectedProfile">Resolved MO2 profile selected for the refresh, if any.</param>
/// <param name="CleaningTimeout">Configured cleaning timeout in seconds.</param>
public sealed record PluginRefreshConfigurationProjection(
    string? LoadOrderPath,
    string? GameDataFolder,
    bool HasGameDataFolderOverride,
    string? XEditPath,
    string? Mo2Path,
    bool Mo2ModeEnabled,
    string? Mo2InstancePath,
    bool IsMo2InstanceOverride,
    bool? IsMo2InstanceValid,
    IReadOnlyList<string> AvailableProfiles,
    string? SelectedProfile,
    int CleaningTimeout);
