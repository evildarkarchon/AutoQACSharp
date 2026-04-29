using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;

namespace AutoQAC.Services.Backup;

/// <summary>
/// Service for backing up and restoring plugin files before/after cleaning.
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// Creates a new session directory and returns its absolute path.
    /// Format: yyyy-MM-dd_HH-mm-ss
    /// </summary>
    string CreateSessionDirectory(string backupRoot);

    /// <summary>
    /// Copies the plugin file to the session directory.
    /// </summary>
    BackupResult BackupPlugin(PluginInfo plugin, string sessionDir);

    /// <summary>
    /// Copies the plugin file to the session directory with structured status, byte progress, and cancellation support.
    /// </summary>
    Task<BackupCreateResult> BackupPluginAsync(
        PluginInfo plugin,
        string sessionDir,
        IProgress<BackupCopyProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Writes session.json metadata sidecar to the session directory.
    /// </summary>
    Task WriteSessionMetadataAsync(string sessionDir, BackupSession session, CancellationToken ct = default);

    /// <summary>
    /// Enumerates all backup sessions from the backup root, newest first.
    /// </summary>
    Task<List<BackupSession>> GetBackupSessionsAsync(string backupRoot, CancellationToken ct = default);

    /// <summary>
    /// Restores a single plugin from backup to its original path.
    /// </summary>
    void RestorePlugin(BackupPluginEntry entry, string sessionDir);

    /// <summary>
    /// Restores a single plugin from backup to its original path with structured row-level outcome details.
    /// </summary>
    Task<BackupRestoreResult> RestorePluginAsync(
        BackupPluginEntry entry,
        string sessionDir,
        IProgress<BackupCopyProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Restores all plugins from a session.
    /// </summary>
    void RestoreSession(BackupSession session);

    /// <summary>
    /// Restores all plugins from a session while preserving partial, failed, and canceled aggregate outcomes.
    /// </summary>
    Task<BackupRestoreResult> RestoreSessionAsync(
        BackupSession session,
        IProgress<BackupCopyProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes oldest sessions beyond maxSessionCount. Never deletes currentSessionDir.
    /// </summary>
    void CleanupOldSessions(string backupRoot, int maxSessionCount, string? currentSessionDir = null);

    /// <summary>
    /// Deletes oldest sessions beyond maxSessionCount with structured cleanup status for warning/cancel reporting.
    /// </summary>
    Task<BackupRetentionCleanupResult> CleanupOldSessionsAsync(
        string backupRoot,
        int maxSessionCount,
        string? currentSessionDir = null,
        IProgress<BackupCopyProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves backup root path from game Data folder path.
    /// Returns the sibling "AutoQAC Backups" directory next to the Data folder.
    /// </summary>
    string GetBackupRoot(string dataFolderPath);
}
