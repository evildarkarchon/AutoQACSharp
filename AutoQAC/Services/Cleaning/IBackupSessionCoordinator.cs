using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;

namespace AutoQAC.Services.Cleaning;

/// <summary>
/// Coordinates the backup-session lifecycle for one cleaning run while the facade preserves
/// overall session ordering and result publication.
/// </summary>
public interface IBackupSessionCoordinator
{
    /// <summary>
    /// Creates the backup session directory when the preflight plan enables backups and does not run through MO2.
    /// </summary>
    /// <param name="plan">Preflight plan containing backup policy and selected plugin paths for the cleaning session.</param>
    /// <param name="sessionToken">Cleaning-session cancellation token; the coordinator does not take ownership of it.</param>
    /// <returns>The created session directory, or <see langword="null"/> when backups are disabled or skipped by policy.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="sessionToken"/> is canceled before session creation.</exception>
    Task<string?> BeginSessionAsync(CleaningPreflightPlan plan, CancellationToken sessionToken);

    /// <summary>
    /// Runs a per-plugin backup using an operation-scoped CTS linked to the cleaning session token.
    /// </summary>
    /// <param name="plugin">Plugin that is about to be cleaned by xEdit.</param>
    /// <param name="sessionDir">Backup session directory returned by <see cref="BeginSessionAsync"/>.</param>
    /// <param name="onBackupFailure">Optional user-choice callback for expected backup failures.</param>
    /// <param name="sessionToken">Cleaning-session cancellation token; a linked operation CTS is created internally.</param>
    /// <returns>Structured outcome that tells the facade whether to launch xEdit, skip the plugin, or abort the session.</returns>
    /// <exception cref="OperationCanceledException">May be thrown for unexpected cancellation outside expected backup-result cancellation.</exception>
    Task<PluginBackupOutcome> RunPluginBackupAsync(
        PluginInfo plugin,
        string sessionDir,
        BackupFailureCallback? onBackupFailure,
        CancellationToken sessionToken);

    /// <summary>
    /// Writes final backup metadata and runs retention cleanup before the facade publishes the session result.
    /// </summary>
    /// <param name="sessionDir">Backup session directory for this cleaning run.</param>
    /// <param name="gameType">Detected game type written into the metadata sidecar.</param>
    /// <param name="entries">Successful backup entries to write to session metadata.</param>
    /// <param name="maxSessions">Maximum retained sessions to keep after cleanup.</param>
    /// <param name="sessionToken">Cleaning-session cancellation token used for metadata and retention work.</param>
    /// <returns>The retention cleanup result, or <see langword="null"/> when no backup entries exist.</returns>
    /// <exception cref="OperationCanceledException">Thrown when metadata or cleanup observes <paramref name="sessionToken"/>.</exception>
    Task<BackupRetentionCleanupResult?> FinalizeSessionAsync(
        string sessionDir,
        GameType gameType,
        IReadOnlyList<BackupPluginEntry> entries,
        int maxSessions,
        CancellationToken sessionToken);

    /// <summary>
    /// Best-effort partial metadata write used when cancellation or abort exits before normal finalization.
    /// </summary>
    /// <param name="sessionDir">Backup session directory for this cleaning run.</param>
    /// <param name="gameType">Detected game type written into the metadata sidecar.</param>
    /// <param name="entries">Successful backup entries gathered before the early exit.</param>
    /// <param name="ct">Cancellation token for the metadata write; callers may pass <see cref="CancellationToken.None"/> for best effort.</param>
    /// <returns>A task that completes after metadata is written or after a warning is logged for write failures.</returns>
    Task WritePartialMetadataAsync(
        string sessionDir,
        GameType gameType,
        IReadOnlyList<BackupPluginEntry> entries,
        CancellationToken ct);

    /// <summary>
    /// Cancels the active non-xEdit backup or retention file operation, if one is currently running.
    /// </summary>
    /// <returns>A completed task after the current operation CTS has been signaled, or immediately when none is active.</returns>
    Task CancelActiveOperationAsync();
}
