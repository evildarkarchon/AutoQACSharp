using AutoQAC.Models;

namespace AutoQAC.Services.Cleaning;

/// <summary>
/// Control intents that can be applied to a live Cleaning session.
/// </summary>
public enum CleaningSessionControl
{
    RequestStop,
    ForceStop,
    CancelBackupOperation
}

/// <summary>
/// Result of applying a Cleaning session control intent.
/// </summary>
/// <param name="Control">The requested control action.</param>
/// <param name="Status">High-level outcome for caller projection.</param>
/// <param name="TerminationResult">Low-level process termination result when one was observed.</param>
public sealed record CleaningSessionControlResult(
    CleaningSessionControl Control,
    CleaningSessionControlStatus Status,
    TerminationResult? TerminationResult = null);

/// <summary>
/// High-level outcomes returned by <see cref="ICleaningSession.ControlAsync" />.
/// </summary>
public enum CleaningSessionControlStatus
{
    NoActiveSession,
    StopRequested,
    GracefullyStopped,
    ForceStopped,
    LeftRunningByUser,
    ForceKillFailed,
    BackupCancellationRequested,
    NoActiveBackupOperation
}
