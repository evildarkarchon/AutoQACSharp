using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;

namespace AutoQAC.Services.Cleaning;

/// <summary>
/// Default Cleaning command readiness projection backed by Plugin refresh publication facts.
/// </summary>
internal sealed class CleaningCommandReadiness(
    IPluginRefreshModule pluginRefreshModule,
    IStateService stateService)
    : ICleaningCommandReadiness
{
    /// <inheritdoc />
    public async Task<CleaningCommandReadinessResult> EvaluateAsync(CancellationToken ct = default)
    {
        var publication = await pluginRefreshModule.GetCurrentPublicationAsync(ct).ConfigureAwait(false);
        var state = stateService.CurrentState;
        if (state.IsCleaning)
        {
            return CleaningCommandReadinessResult.Busy;
        }

        if (CleaningLaunchBlockerValidator.TryValidateSharedBlockers(publication, state, out var failure))
        {
            return CleaningCommandReadinessResult.Blocked(failure);
        }

        return CleaningCommandReadinessResult.Ready;
    }
}
