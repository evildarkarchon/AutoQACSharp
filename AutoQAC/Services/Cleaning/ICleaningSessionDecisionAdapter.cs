using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;

namespace AutoQAC.Services.Cleaning;

/// <summary>
/// Supplies user decisions needed inside a Cleaning session while keeping UI technology out of the session module.
/// </summary>
public interface ICleaningSessionDecisionAdapter
{
    /// <summary>
    /// Chooses whether a timed-out plugin should be retried.
    /// </summary>
    Task<bool> ShouldRetryTimedOutPluginAsync(
        string pluginName,
        int timeoutSeconds,
        int attemptNumber,
        int maxAttempts,
        CancellationToken ct);

    /// <summary>
    /// Chooses how to continue after a plugin backup failure.
    /// </summary>
    Task<BackupFailureChoice> ChooseBackupFailureAsync(
        string pluginName,
        string errorMessage,
        CancellationToken ct);

    /// <summary>
    /// Chooses how to resolve a graceful-stop grace period expiry.
    /// </summary>
    Task<CleaningSessionStopDecision> ChooseAfterGracePeriodExpiredAsync(
        TerminationResult terminationResult,
        CancellationToken ct);
}

/// <summary>
/// User choice after xEdit does not exit during the graceful stop grace period.
/// </summary>
public enum CleaningSessionStopDecision
{
    ForceTerminate,
    LeaveRunning
}
