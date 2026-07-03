using System;
using System.Collections.Generic;
using System.Threading;
using AutoQAC.Models;

namespace AutoQAC.Services.Plugin;

/// <summary>
/// Publishes visible Plugin refresh effects while hiding state-service mutation details from refresh orchestration.
/// </summary>
public interface IPluginRefreshPublication : IDisposable
{
    /// <summary>
    /// Emits typed Plugin refresh progress and terminal statuses for ViewModel subscribers.
    /// </summary>
    IObservable<PluginRefreshStatus> StatusChanged { get; }

    /// <summary>
    /// Starts a new visible refresh generation. Older scopes become stale and stop publishing effects.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the refresh work generation.</param>
    /// <returns>A generation-scoped publication handle.</returns>
    IPluginRefreshPublicationScope BeginRefresh(CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes the terminal status used when a selected approximation refresh has no targets.
    /// </summary>
    void PublishSelectPlugins();

    /// <summary>
    /// Publishes a manual cancellation status even after the active refresh token has been canceled.
    /// </summary>
    void PublishManualCancellation();
}

/// <summary>
/// Publishes visible effects for one Plugin refresh generation.
/// </summary>
public interface IPluginRefreshPublicationScope : IDisposable
{
    /// <summary>
    /// Gets whether this scope still represents the active, non-canceled refresh generation.
    /// </summary>
    bool IsVisible { get; }

    /// <summary>
    /// Clears Plugin refresh state when no game is selected.
    /// </summary>
    void PublishNoGameSelected();

    /// <summary>
    /// Publishes the configuration paths and runtime settings resolved for the refresh.
    /// </summary>
    /// <param name="projection">Display projection produced by refresh context assembly.</param>
    /// <param name="snapshot">Configuration fields to publish to runtime state.</param>
    void PublishConfiguration(
        PluginRefreshProjection projection,
        PluginRefreshPublicationSnapshot snapshot);

    /// <summary>
    /// Clears rows and publishes the supplied no-context terminal status.
    /// </summary>
    /// <param name="status">Terminal status explaining why no refresh context is available.</param>
    void PublishNoRefreshContext(PluginRefreshStatus status);

    /// <summary>
    /// Publishes the loading status before plugin rows are loaded.
    /// </summary>
    void PublishLoadingPlugins();

    /// <summary>
    /// Publishes the visible plugin rows for the active refresh.
    /// </summary>
    /// <param name="rows">Rows to publish to application state.</param>
    void PublishPluginRows(IReadOnlyList<PluginInfo> rows);

    /// <summary>
    /// Publishes the status used when issue approximation is unavailable for the active game.
    /// </summary>
    void PublishApproximationUnavailable();

    /// <summary>
    /// Starts result publication for a full-list approximation refresh.
    /// </summary>
    /// <param name="targets">Approximation targets eligible for analysis.</param>
    /// <returns>A scoped approximation publication handle.</returns>
    IPluginRefreshApproximationPublication BeginFullApproximationRefresh(
        IReadOnlyList<PluginRefreshTarget> targets);

    /// <summary>
    /// Starts result publication for a selected approximation refresh and marks target rows pending.
    /// </summary>
    /// <param name="gameType">Game associated with materialized selected rows.</param>
    /// <param name="targets">Selected approximation targets.</param>
    /// <returns>A scoped approximation publication handle.</returns>
    IPluginRefreshApproximationPublication BeginSelectedApproximationRefresh(
        GameType gameType,
        IReadOnlyList<PluginRefreshTarget> targets);

    /// <summary>
    /// Marks target rows unavailable and publishes a terminal failure status.
    /// </summary>
    /// <param name="targets">Targets whose approximations failed.</param>
    /// <param name="message">Failure message for the status stream.</param>
    void PublishApproximationFailure(
        IReadOnlyList<PluginRefreshTarget> targets,
        string message = "Approximation refresh failed.");
}

/// <summary>
/// Publishes per-target issue approximation results for an active Plugin refresh generation.
/// </summary>
public interface IPluginRefreshApproximationPublication
{
    /// <summary>
    /// Publishes one issue approximation result if it matches a visible target row.
    /// </summary>
    /// <param name="result">Approximation result produced by analysis.</param>
    void PublishResult(PluginIssueApproximationResult result);

    /// <summary>
    /// Publishes the refresh completion status with the number of visible rows updated.
    /// </summary>
    void PublishCompleted();
}
