using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;

namespace AutoQAC.Services.Plugin;

/// <summary>
/// Coordinates plugin list refreshes and targeted plugin issue approximation refreshes.
/// Implementations own refresh generation, cancellation, and stale-result filtering before publishing through the shared state hub.
/// </summary>
public interface IPluginRefreshCoordinator
{
    /// <summary>
    /// Emits typed refresh progress and terminal outcomes for ViewModels to map into user-facing status text.
    /// </summary>
    IObservable<PluginRefreshStatus> StatusChanged { get; }

    /// <summary>
    /// Refreshes the plugin list for the requested game intent, then starts background approximation work for the chosen target scope.
    /// The implementation owns path/profile/load-order resolution, skip-list status, row publication, and stale-result filtering.
    /// </summary>
    /// <param name="gameType">Game selected by the user.</param>
    /// <param name="selectedLoadOrderPath">Optional load-order file explicitly chosen by the user.</param>
    /// <param name="ct">Cancellation token that cancels the requested refresh.</param>
    /// <returns>A small UI projection for display-only fields not modeled in application state.</returns>
    Task<PluginRefreshProjection> RefreshForGameAsync(
        GameType gameType,
        string? selectedLoadOrderPath = null,
        CancellationToken ct = default);

    /// <summary>
    /// Refreshes issue approximations only for the supplied selected target snapshot.
    /// Implementations must not expand this list with deselected rows after the snapshot is captured.
    /// </summary>
    /// <param name="selectedTargets">Immutable snapshot of selected visible plugin rows.</param>
    /// <param name="ct">Cancellation token that cancels the requested refresh.</param>
    Task RefreshSelectedApproximationsAsync(
        IReadOnlyList<PluginRefreshTarget> selectedTargets,
        CancellationToken ct = default);

    /// <summary>
    /// Cancels the active refresh generation, if any, with the supplied reason.
    /// Manual cancellation should publish a canceled status while superseded generations should normally stay silent.
    /// </summary>
    /// <param name="reason">Reason the active refresh is being canceled.</param>
    void CancelActiveRefresh(PluginRefreshCancelReason reason);
}
