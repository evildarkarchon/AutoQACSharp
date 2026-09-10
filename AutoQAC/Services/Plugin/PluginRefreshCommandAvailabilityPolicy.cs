using System;
using System.Collections.Generic;
using System.Linq;
using AutoQAC.Models;
using AutoQAC.Services.GameCapability;

namespace AutoQAC.Services.Plugin;

/// <summary>
/// Computes command availability from publication facts and game affordances.
/// </summary>
internal sealed class PluginRefreshCommandAvailabilityPolicy
{
    /// <summary>
    /// Creates command availability for the current visible rows and activity state.
    /// </summary>
    /// <param name="gameType">Game associated with the publication.</param>
    /// <param name="rows">Visible rows exposed by the current snapshot.</param>
    /// <param name="activity">Current refresh activity flags.</param>
    /// <param name="configuration">Configuration projection used to resolve the affordance.</param>
    /// <param name="isCleaning">Whether a cleaning session is currently active.</param>
    /// <param name="affordance">Game-specific refresh affordance facts supplied by the caller.</param>
    /// <returns>Command availability for the publication snapshot.</returns>
    internal PluginRefreshCommandAvailability Create(
        GameType gameType,
        IReadOnlyList<PluginRefreshRow> rows,
        PluginRefreshActivity activity,
        PluginRefreshConfigurationProjection configuration,
        bool isCleaning,
        PluginRefreshGameAffordance affordance)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(affordance);

        var hasRows = rows.Count > 0;
        var isRunning = activity.IsPluginRefreshRunning || activity.IsIssueApproximationRefreshRunning;
        var canUseRows = hasRows && !isCleaning;
        var canRefreshApproximations = canUseRows &&
                                       !isRunning &&
                                       rows.Any(row => row.IsSelected) &&
                                       gameType != GameType.Unknown &&
                                       affordance.CanAttemptIssueApproximation;

        return new PluginRefreshCommandAvailability(
            CanSelectAll: canUseRows,
            CanDeselectAll: canUseRows,
            CanRefreshSelectedIssueApproximations: canRefreshApproximations,
            CanCancelRefresh: isRunning);
    }
}
