using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;

namespace AutoQAC.Services.Cleaning;

/// <summary>
/// Callback delegate for handling plugin timeout events.
/// </summary>
/// <param name="pluginName">Name of the plugin that timed out.</param>
/// <param name="timeoutSeconds">Timeout value in seconds.</param>
/// <param name="attemptNumber">Current attempt number (1-based).</param>
/// <returns>True to retry, false to skip the plugin.</returns>
public delegate Task<bool> TimeoutRetryCallback(string pluginName, int timeoutSeconds, int attemptNumber);

public interface ICleaningOrchestrator
{
    /// <summary>
    /// Start cleaning workflow.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task StartCleaningAsync(CancellationToken ct = default);

    /// <summary>
    /// Start cleaning workflow with timeout retry callback.
    /// </summary>
    /// <param name="onTimeout">Callback invoked when a plugin times out. Return true to retry.</param>
    /// <param name="ct">Cancellation token.</param>
    Task StartCleaningAsync(TimeoutRetryCallback? onTimeout, CancellationToken ct = default);

    /// <summary>
    /// Graceful stop: cancels the CTS, attempts graceful process termination.
    /// If called a second time during the grace period, escalates to immediate force kill.
    /// </summary>
    Task StopCleaningAsync();

    /// <summary>
    /// Immediate force kill of the process tree. No grace period, no prompt.
    /// </summary>
    Task ForceStopCleaningAsync();

    /// <summary>
    /// Result of the last termination attempt. When GracePeriodExpired, the ViewModel
    /// should prompt the user to confirm force kill (Path A: patient user).
    /// Reset at the start and end of each cleaning session.
    /// </summary>
    TerminationResult? LastTerminationResult { get; }
}
