using AutoQAC.Models;

namespace AutoQAC.Services.Plugin;

/// <summary>
///     Describes user or domain intent accepted by the Plugin refresh module.
/// </summary>
public abstract record PluginRefreshIntent
{
    /// <summary>
    ///     Refreshes the plugin list and issue approximations for a game.
    /// </summary>
    /// <param name="GameType">Selected game for the refresh.</param>
    /// <param name="SelectedLoadOrderPath">Optional user-selected load-order path for file-based refreshes.</param>
    public sealed record RefreshGame(
        GameType GameType,
        string? SelectedLoadOrderPath = null) : PluginRefreshIntent;

    /// <summary>
    ///     Refreshes issue approximations for the currently selected visible plugin rows.
    /// </summary>
    public sealed record RefreshSelectedIssueApproximations : PluginRefreshIntent;

    /// <summary>
    ///     Applies a visible plugin row selection change.
    /// </summary>
    /// <param name="Change">Selection mutation requested by the user.</param>
    public sealed record ChangeSelection(
        PluginSelectionChange Change) : PluginRefreshIntent;

    /// <summary>
    ///     Cancels active Plugin refresh work.
    /// </summary>
    /// <param name="Reason">Reason the active generation is being canceled.</param>
    public sealed record Cancel(
        PluginRefreshCancelReason Reason) : PluginRefreshIntent;
}

/// <summary>
///     Explains why active Plugin refresh work was canceled.
/// </summary>
/// <remarks>
///     Cancellation for a starting Cleaning session is not a caller intent: reserving Cleaning admission cancels and
///     drains Plugin refresh work itself.
/// </remarks>
public enum PluginRefreshCancelReason
{
    /// <summary>User explicitly canceled the visible refresh.</summary>
    Manual,

    /// <summary>The owning ViewModel or service is being disposed.</summary>
    Disposed
}