using System;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;

namespace AutoQAC.Services.Cleaning;

public interface IPluginCleaningRunner
{
    /// <summary>
    /// Runs xEdit for one plugin with attempt/retry, log-offset capture before each attempt,
    /// and process-attach delegated to the termination coordinator. Per D-20: log offset capture
    /// occurs inside the retry loop, before each xEdit launch — this ordering is protected behavior.
    /// detachProcess is called exactly once per plugin in a finally block after the retry loop
    /// (matches CleaningOrchestrator once-per-plugin semantics; per R-02 do not detach per attempt).
    /// </summary>
    /// <param name="plugin">Plugin to clean.</param>
    /// <param name="gameType">Detected game type used for xEdit log names.</param>
    /// <param name="xEditDir">Directory containing the xEdit executable and logs.</param>
    /// <param name="onTimeout">Optional callback that decides whether to retry timed-out attempts.</param>
    /// <param name="timeoutSeconds">Configured timeout shown to the timeout retry callback.</param>
    /// <param name="maxRetryAttempts">Maximum number of cleaning attempts for this plugin.</param>
    /// <param name="attachProcess">Delegate invoked from the process-start callback for each xEdit launch.</param>
    /// <param name="detachProcess">Delegate invoked once in a trailing finally block after all attempts.</param>
    /// <param name="ct">Cancellation token for the cleaning attempt and timeout callback.</param>
    /// <returns>Structured runner output consumed by <see cref="IPluginResultFinalizer" />.</returns>
    Task<PluginRunnerOutput> RunAsync(
        PluginInfo plugin,
        GameType gameType,
        string xEditDir,
        TimeoutRetryCallback? onTimeout,
        int timeoutSeconds,
        int maxRetryAttempts,
        Action<System.Diagnostics.Process> attachProcess,
        Action detachProcess,
        CancellationToken ct);
}
