using System;
using System.Collections.Generic;
using System.Linq;
using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.GameCapability;

namespace AutoQAC.Services.Plugin;

/// <summary>
///     Computes command availability from publication facts, game affordances, and Cleaning admission.
/// </summary>
internal sealed class PluginRefreshCommandAvailabilityPolicy(CleaningAdmission admission)
{
    private readonly CleaningAdmission _admission = admission;

    /// <summary>
    ///     Creates command availability for the current visible rows and activity state.
    /// </summary>
    /// <param name="gameType">Game associated with the publication.</param>
    /// <param name="rows">Visible rows exposed by the current snapshot.</param>
    /// <param name="activity">Current refresh activity flags.</param>
    /// <param name="configuration">Configuration projection used to resolve the affordance.</param>
    /// <param name="affordance">Game-specific refresh affordance facts supplied by the caller.</param>
    /// <returns>Command availability for the publication snapshot, including the admission state it was computed under.</returns>
    internal PluginRefreshCommandAvailability Create(
        GameType gameType,
        IReadOnlyList<PluginRefreshRow> rows,
        PluginRefreshActivity activity,
        PluginRefreshConfigurationProjection configuration,
        PluginRefreshGameAffordance affordance)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(affordance);

        // Admission is reserved before AppState publishes active cleaning and released after it clears, so it
        // alone covers the whole Cleaning session window for Plugin mutation commands.
        var isCleaningReserved = _admission.IsCleaning;
        var hasRows = rows.Count > 0;
        var isRunning = activity.IsPluginRefreshRunning || activity.IsIssueApproximationRefreshRunning;
        var canUseRows = hasRows && !isCleaningReserved;
        var canRefreshApproximations = canUseRows &&
                                       !isRunning &&
                                       rows.Any(row => row.IsSelected) &&
                                       gameType != GameType.Unknown &&
                                       affordance.CanAttemptIssueApproximation;

        return new PluginRefreshCommandAvailability(
            canUseRows,
            canUseRows,
            canRefreshApproximations,
            isRunning,
            isCleaningReserved);
    }
}
