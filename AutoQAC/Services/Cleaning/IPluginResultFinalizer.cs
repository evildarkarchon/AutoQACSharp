using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;

namespace AutoQAC.Services.Cleaning;

public interface IPluginResultFinalizer
{
    /// <summary>
    /// Builds the per-plugin <see cref="PluginCleaningResult" /> from the runner output, log file content,
    /// and the termination coordinator's view of whether the process may still be running.
    /// Honors the Phase 5 lock: skip log read when termination is unsafe or stop was requested.
    /// </summary>
    /// <param name="plugin">Plugin whose final cleaning result is being built.</param>
    /// <param name="gameType">Detected game type used for xEdit log names.</param>
    /// <param name="xEditDir">Directory containing the xEdit executable and logs.</param>
    /// <param name="runnerOutput">Output from the per-plugin runner.</param>
    /// <param name="terminationContext">Snapshot of termination state captured after process detach.</param>
    /// <param name="ct">Cancellation token for log reading.</param>
    /// <returns>The detailed per-plugin result to publish into session state.</returns>
    Task<PluginCleaningResult> FinalizeAsync(
        PluginInfo plugin,
        GameType gameType,
        string xEditDir,
        PluginRunnerOutput runnerOutput,
        TerminationFinalizeContext terminationContext,
        CancellationToken ct);
}
