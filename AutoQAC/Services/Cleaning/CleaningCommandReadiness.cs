using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;

namespace AutoQAC.Services.Cleaning;

/// <summary>
///     Default Cleaning command readiness projection backed by Plugin refresh publication facts.
/// </summary>
/// <param name="cleaningAdmission">Reports Busy for the whole reservation, not just while AppState is cleaning.</param>
internal sealed class CleaningCommandReadiness(
    IPluginRefreshModule pluginRefreshModule,
    IStateService stateService,
    CleaningAdmission cleaningAdmission)
    : ICleaningCommandReadiness
{
    /// <inheritdoc />
    public async Task<CleaningCommandReadinessResult> EvaluateAsync(CancellationToken ct = default)
    {
        var publication = await pluginRefreshModule.GetCurrentPublicationAsync(ct).ConfigureAwait(false);
        if (cleaningAdmission.IsCleaning) return CleaningCommandReadinessResult.Busy;

        var state = stateService.CurrentState;

        if (CleaningLaunchBlockerValidator.TryValidateSharedBlockers(publication, state, out var failure))
            return CleaningCommandReadinessResult.Blocked(failure);

        return CleaningCommandReadinessResult.Ready;
    }
}