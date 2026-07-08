using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;

namespace AutoQAC.Services.Cleaning;

/// <summary>
/// Owns the per-plugin Plugin cleaning attempt inside a Cleaning session.
/// </summary>
public sealed class PluginCleaning(
    IPluginCleaningRunner runner,
    IPluginResultFinalizer finalizer,
    ICleaningSessionDecisionAdapter decisions,
    ICleaningTerminationCoordinator terminationCoordinator)
    : IPluginCleaning
{
    /// <inheritdoc />
    public async Task<PluginCleaningResult> CleanAsync(PluginCleaningContext context, CancellationToken ct)
    {
        var runnerOutput = await runner.RunAsync(
                context.Plugin,
                context.GameType,
                context.XEditDirectory,
                decisions,
                context.TimeoutSeconds,
                context.MaxRetryAttempts,
                terminationCoordinator.AttachProcess,
                terminationCoordinator.DetachProcess,
                ct)
            .ConfigureAwait(false);

        // Snapshot termination state after runner detach so log finalization can avoid unsafe reads.
        var terminationContext = new TerminationFinalizeContext(
            terminationCoordinator.ProcessMayStillBeRunning,
            terminationCoordinator.IsStopRequested);
        return await finalizer.FinalizeAsync(
                context.Plugin,
                context.GameType,
                context.XEditDirectory,
                runnerOutput,
                terminationContext,
                ct)
            .ConfigureAwait(false);
    }
}
