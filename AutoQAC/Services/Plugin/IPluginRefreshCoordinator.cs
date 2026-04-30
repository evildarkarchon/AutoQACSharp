using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
    /// Refreshes the plugin list for the requested game or load-order input, then starts background approximation work for the chosen target scope.
    /// A newer refresh request must cancel and replace older work so stale callbacks cannot mutate current state.
    /// </summary>
    /// <param name="request">The game, data-folder, and optional load-order context for the refresh.</param>
    /// <param name="ct">Cancellation token that cancels the requested refresh.</param>
    Task RefreshForGameAsync(PluginRefreshRequest request, CancellationToken ct = default);

    /// <summary>
    /// Refreshes issue approximations only for the supplied selected target snapshot.
    /// Implementations must not expand this list with deselected rows after the snapshot is captured.
    /// </summary>
    /// <param name="request">The game and data-folder context for the approximation refresh.</param>
    /// <param name="selectedTargets">Immutable snapshot of selected visible plugin rows.</param>
    /// <param name="ct">Cancellation token that cancels the requested refresh.</param>
    Task RefreshSelectedApproximationsAsync(
        PluginRefreshRequest request,
        IReadOnlyList<PluginRefreshTarget> selectedTargets,
        CancellationToken ct = default);

    /// <summary>
    /// Cancels the active refresh generation, if any, with the supplied reason.
    /// Manual cancellation should publish a canceled status while superseded generations should normally stay silent.
    /// </summary>
    /// <param name="reason">Reason the active refresh is being canceled.</param>
    void CancelActiveRefresh(PluginRefreshCancelReason reason);
}
