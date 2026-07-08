using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using AutoQAC.Models;
using AutoQAC.Services.State;

namespace AutoQAC.Services.Cleaning;

/// <summary>
/// Maps Cleaning session state publication to the existing application state service.
/// </summary>
public sealed class StateServiceCleaningSessionStatePublisher(IStateService stateService)
    : ICleaningSessionStatePublisher
{
    /// <inheritdoc />
    public void PublishDetectedGame(GameType gameType) =>
        stateService.UpdateState(s => s with { CurrentGameType = gameType });

    /// <inheritdoc />
    public void PublishStarted(IReadOnlyList<PluginInfo> pluginsToClean) =>
        stateService.StartCleaning(pluginsToClean.ToList());

    /// <inheritdoc />
    public void PublishCurrentPlugin(string pluginName) =>
        stateService.UpdateState(s => s with { CurrentPlugin = pluginName });

    /// <inheritdoc />
    public void PublishPluginResult(PluginCleaningResult result) =>
        stateService.AddDetailedCleaningResult(result);

    /// <inheritdoc />
    public void PublishSkippedPlugin(string pluginName) =>
        stateService.UpdateState(s => s with
        {
            SkippedPlugins = new HashSet<string>(s.SkippedPlugins)
            {
                pluginName
            }.ToFrozenSet(System.StringComparer.Ordinal)
        });

    /// <inheritdoc />
    public void PublishTerminating(bool isTerminating) =>
        stateService.SetTerminating(isTerminating);

    /// <inheritdoc />
    public void PublishCompleted(CleaningSessionResult sessionResult) =>
        stateService.FinishCleaningWithResults(sessionResult);
}
