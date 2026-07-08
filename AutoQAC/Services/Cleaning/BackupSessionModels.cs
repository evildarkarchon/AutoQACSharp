using AutoQAC.Models;

namespace AutoQAC.Services.Cleaning;

/// <summary>
/// Structured result for a per-plugin backup attempt in the cleaning session loop.
/// </summary>
public sealed record PluginBackupOutcome
{
    /// <summary>Outcome kind used by the facade dispatch switch.</summary>
    public required PluginBackupOutcomeKind Kind { get; init; }

    /// <summary>Successful metadata entry to add to the session; only populated for <see cref="PluginBackupOutcomeKind.Succeeded"/>.</summary>
    public BackupPluginEntry? Entry { get; init; }

    /// <summary>Skipped cleaning result to publish; populated for canceled backups and user skip choices.</summary>
    public PluginCleaningResult? SkippedResult { get; init; }

    /// <summary>Concise backup failure reason passed to facade-level abort or logging paths.</summary>
    public string? FailureReasonText { get; init; }
}

/// <summary>
/// Per-plugin backup outcomes that preserve the existing backup-failure choice semantics.
/// </summary>
public enum PluginBackupOutcomeKind
{
    /// <summary>Backup completed and the facade should add the returned metadata entry before launching xEdit.</summary>
    Succeeded,

    /// <summary>Backup was canceled; the facade should publish the skipped result and skip xEdit for this plugin.</summary>
    Canceled,

    /// <summary><see cref="BackupFailureChoice.SkipPlugin"/> was selected; the facade should publish the skipped result.</summary>
    UserSkipped,

    /// <summary><see cref="BackupFailureChoice.AbortSession"/> was selected; the facade owns partial metadata and final session publication.</summary>
    AbortSession,

    /// <summary><see cref="BackupFailureChoice.ContinueWithoutBackup"/> was selected; the facade should launch xEdit without adding an entry.</summary>
    ContinueWithoutBackup
}
