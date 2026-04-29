using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;

namespace AutoQAC.Services.Backup;

/// <summary>
/// Manages plugin file backup, restore, and session retention.
/// </summary>
public sealed class BackupService : IBackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly IBackupFileCopier _fileCopier;
    private readonly IBackupSessionDeleter _sessionDeleter;
    private readonly ILoggingService _logger;

    /// <summary>
    /// Creates the backup service with injectable file-copy behavior for async backup and restore operations.
    /// </summary>
    /// <param name="fileCopier">Copy service used for cancellable backup and atomic restore work.</param>
    /// <param name="logger">Logger for technical diagnostics that should not be exposed in user-facing result rows.</param>
    public BackupService(IBackupFileCopier fileCopier, ILoggingService logger, IBackupSessionDeleter? sessionDeleter = null)
    {
        _fileCopier = fileCopier;
        _sessionDeleter = sessionDeleter ?? new DirectoryBackupSessionDeleter();
        _logger = logger;
    }

    /// <summary>
    /// Creates the backup service with the default managed-stream file copier.
    /// </summary>
    /// <param name="logger">Logger for technical diagnostics that should not be exposed in user-facing result rows.</param>
    public BackupService(ILoggingService logger)
        : this(new BackupFileCopier(logger), logger)
    {
    }

    public string CreateSessionDirectory(string backupRoot)
    {
        var sessionName = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var sessionDir = Path.Combine(backupRoot, sessionName);
        Directory.CreateDirectory(sessionDir);
        _logger.Information("Created backup session directory: {SessionDir}", sessionDir);
        return sessionDir;
    }

    public BackupResult BackupPlugin(PluginInfo plugin, string sessionDir)
    {
        try
        {
            if (string.IsNullOrEmpty(plugin.FullPath) || !Path.IsPathRooted(plugin.FullPath))
            {
                return BackupResult.Failure($"Plugin path is not a valid rooted path: '{plugin.FullPath}'");
            }

            if (!File.Exists(plugin.FullPath))
            {
                return BackupResult.Failure($"Source file does not exist: '{plugin.FullPath}'");
            }

            // Ensure session directory exists (idempotent)
            Directory.CreateDirectory(sessionDir);

            var destPath = Path.Combine(sessionDir, plugin.FileName);
            File.Copy(plugin.FullPath, destPath, overwrite: false);

            var fileSize = new FileInfo(destPath).Length;
            _logger.Debug("Backed up {Plugin} ({Size} bytes) to {Dest}", plugin.FileName, fileSize, destPath);

            return BackupResult.Ok(fileSize);
        }
        catch (IOException ex)
        {
            _logger.Warning("Backup failed for {Plugin}: {Error}", plugin.FileName, ex.Message);
            return BackupResult.Failure($"I/O error backing up '{plugin.FileName}': {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Warning("Backup failed for {Plugin}: {Error}", plugin.FileName, ex.Message);
            return BackupResult.Failure($"Access denied backing up '{plugin.FileName}': {ex.Message}");
        }
    }

    public async Task<BackupCreateResult> BackupPluginAsync(
        PluginInfo plugin,
        string sessionDir,
        IProgress<BackupCopyProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(plugin.FullPath) || !Path.IsPathRooted(plugin.FullPath))
        {
            _logger.Warning("Backup source path for {Plugin} is not rooted: {Path}", plugin.FileName, plugin.FullPath);
            return new BackupCreateResult(
                BackupOperationStatus.Failed,
                plugin.FileName,
                BytesCopied: 0,
                TotalBytes: null,
                BackupFailureReason.SourceMissing);
        }

        Directory.CreateDirectory(sessionDir);
        var destinationPath = Path.Combine(sessionDir, plugin.FileName);
        var copyResult = await _fileCopier.CopyAsync(
            plugin.FullPath,
            destinationPath,
            BackupCopyOptions.CreateNewBackup,
            progress,
            ct).ConfigureAwait(false);

        return new BackupCreateResult(
            copyResult.Status,
            plugin.FileName,
            copyResult.BytesCopied,
            copyResult.TotalBytes,
            copyResult.FailureReason);
    }

    public async Task WriteSessionMetadataAsync(string sessionDir, BackupSession session, CancellationToken ct = default)
    {
        var metadataPath = Path.Combine(sessionDir, "session.json");

        await using var stream = new FileStream(metadataPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, session, JsonOptions, ct).ConfigureAwait(false);

        _logger.Debug("Wrote session metadata to {Path}", metadataPath);
    }

    public async Task<List<BackupSession>> GetBackupSessionsAsync(string backupRoot, CancellationToken ct = default)
    {
        var sessions = new List<BackupSession>();

        if (!Directory.Exists(backupRoot))
        {
            return sessions;
        }

        var directories = Directory.GetDirectories(backupRoot)
            .OrderByDescending(d => Path.GetFileName(d))
            .ToArray();

        foreach (var dir in directories)
        {
            ct.ThrowIfCancellationRequested();

            var metadataPath = Path.Combine(dir, "session.json");
            if (!File.Exists(metadataPath))
            {
                _logger.Debug("Skipping directory without session.json: {Dir}", dir);
                continue;
            }

            try
            {
                await using var stream = new FileStream(metadataPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var session = await JsonSerializer.DeserializeAsync<BackupSession>(stream, cancellationToken: ct)
                    .ConfigureAwait(false);

                if (session != null)
                {
                    // Populate SessionDirectory from filesystem path (not stored in JSON)
                    sessions.Add(session with { SessionDirectory = dir });
                }
            }
            catch (JsonException ex)
            {
                _logger.Warning("Corrupt session.json in {Dir}: {Error}", dir, ex.Message);
            }
            catch (IOException ex)
            {
                _logger.Warning("Failed to read session.json in {Dir}: {Error}", dir, ex.Message);
            }
        }

        return sessions;
    }

    public void RestorePlugin(BackupPluginEntry entry, string sessionDir)
    {
        var backupPath = Path.Combine(sessionDir, entry.FileName);
        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException($"Backup file not found: '{backupPath}'");
        }

        // Ensure the target directory exists
        var targetDir = Path.GetDirectoryName(entry.OriginalPath);
        if (!string.IsNullOrEmpty(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        File.Copy(backupPath, entry.OriginalPath, overwrite: true);
        _logger.Information("Restored {Plugin} to {Path}", entry.FileName, entry.OriginalPath);
    }

    public async Task<BackupRestoreResult> RestorePluginAsync(
        BackupPluginEntry entry,
        string sessionDir,
        IProgress<BackupCopyProgress>? progress = null,
        CancellationToken ct = default)
    {
        var row = await RestorePluginRowAsync(entry, sessionDir, progress, ct).ConfigureAwait(false);
        var status = row.Status switch
        {
            BackupRestoreRowStatus.Restored => BackupOperationStatus.Complete,
            BackupRestoreRowStatus.Canceled => BackupOperationStatus.Canceled,
            _ => BackupOperationStatus.Failed
        };

        return new BackupRestoreResult(status, [row]);
    }

    public void RestoreSession(BackupSession session)
    {
        foreach (var entry in session.Plugins)
        {
            RestorePlugin(entry, session.SessionDirectory);
        }
    }

    public async Task<BackupRestoreResult> RestoreSessionAsync(
        BackupSession session,
        IProgress<BackupCopyProgress>? progress = null,
        CancellationToken ct = default)
    {
        var rows = new List<BackupRestoreRowResult>();

        foreach (var entry in session.Plugins)
        {
            if (ct.IsCancellationRequested)
            {
                rows.Add(new BackupRestoreRowResult(entry.FileName, BackupRestoreRowStatus.Canceled, BackupFailureReason.Canceled, 0, entry.FileSizeBytes));
                continue;
            }

            rows.Add(await RestorePluginRowAsync(entry, session.SessionDirectory, progress, ct).ConfigureAwait(false));
        }

        return new BackupRestoreResult(GetRestoreStatus(rows), rows);
    }

    public void CleanupOldSessions(string backupRoot, int maxSessionCount, string? currentSessionDir = null)
    {
        if (!Directory.Exists(backupRoot))
        {
            return;
        }

        var directories = Directory.GetDirectories(backupRoot)
            .OrderByDescending(d => Path.GetFileName(d))
            .ToList();

        // Keep the newest maxSessionCount entries and always keep currentSessionDir
        var toDelete = new List<string>();
        var keepCount = 0;

        foreach (var dir in directories)
        {
            // Never delete the current session directory
            if (currentSessionDir != null &&
                string.Equals(Path.GetFullPath(dir), Path.GetFullPath(currentSessionDir), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            keepCount++;
            if (keepCount > maxSessionCount)
            {
                toDelete.Add(dir);
            }
        }

        foreach (var dir in toDelete)
        {
            try
            {
                Directory.Delete(dir, recursive: true);
                _logger.Information("Deleted old backup session: {Dir}", dir);
            }
            catch (Exception ex)
            {
                _logger.Warning("Failed to delete old backup session {Dir}: {Error}", dir, ex.Message);
            }
        }
    }

    public async Task<BackupRetentionCleanupResult> CleanupOldSessionsAsync(
        string backupRoot,
        int maxSessionCount,
        string? currentSessionDir = null,
        IProgress<BackupCopyProgress>? progress = null,
        CancellationToken ct = default)
    {
        var rows = new List<BackupRetentionRowResult>();
        if (!Directory.Exists(backupRoot))
        {
            return new BackupRetentionCleanupResult(BackupOperationStatus.Complete, rows);
        }

        if (ct.IsCancellationRequested)
        {
            AddDirectoryRowsAsRemaining(Directory.GetDirectories(backupRoot), rows);
            return new BackupRetentionCleanupResult(BackupOperationStatus.Canceled, rows);
        }

        var currentSessionFullPath = GetComparableFullPath(currentSessionDir);
        var validSessions = await ClassifyRetentionDirectoriesAsync(backupRoot, rows, ct).ConfigureAwait(false);
        if (ct.IsCancellationRequested)
        {
            AddRemainingRetentionRows(validSessions, rows, currentSessionFullPath);
            return new BackupRetentionCleanupResult(BackupOperationStatus.Canceled, rows);
        }

        var validNonCurrent = validSessions
            .Where(session => !IsSamePath(session.Directory, currentSessionFullPath))
            .OrderByDescending(session => session.Timestamp)
            .ThenByDescending(session => Path.GetFileName(session.Directory), StringComparer.OrdinalIgnoreCase)
            .ToList();
        var keepNonCurrent = Math.Max(0, maxSessionCount);
        var kept = validNonCurrent.Take(keepNonCurrent).ToList();
        foreach (var session in kept)
        {
            rows.Add(new BackupRetentionRowResult(session.Directory, BackupRetentionRowStatus.Kept, null));
        }

        var currentSession = validSessions.FirstOrDefault(session => IsSamePath(session.Directory, currentSessionFullPath));
        if (currentSession is not null)
        {
            rows.Add(new BackupRetentionRowResult(currentSession.Directory, BackupRetentionRowStatus.Kept, null));
        }

        foreach (var session in validNonCurrent.Skip(keepNonCurrent))
        {
            ct.ThrowIfCancellationRequested();
            var deleteResult = await DeleteRetentionCandidateAsync(session.Directory, ct).ConfigureAwait(false);
            rows.Add(deleteResult.Row);
            if (deleteResult.Canceled)
            {
                AddRemainingRetentionRows(validNonCurrent.Skip(keepNonCurrent).Where(candidate => candidate.Directory != session.Directory), rows, currentSessionFullPath);
                return new BackupRetentionCleanupResult(BackupOperationStatus.Canceled, rows);
            }
        }

        var status = rows.Any(row => row.Status == BackupRetentionRowStatus.Failed)
            ? BackupOperationStatus.Warning
            : BackupOperationStatus.Complete;
        return new BackupRetentionCleanupResult(status, rows);
    }

    public string GetBackupRoot(string dataFolderPath)
    {
        var parentDir = Path.GetDirectoryName(dataFolderPath);
        if (string.IsNullOrEmpty(parentDir))
        {
            // Fallback: use the data folder itself as parent
            parentDir = dataFolderPath;
        }

        return Path.Combine(parentDir, "AutoQAC Backups");
    }

    private async Task<BackupRestoreRowResult> RestorePluginRowAsync(
        BackupPluginEntry entry,
        string sessionDir,
        IProgress<BackupCopyProgress>? progress,
        CancellationToken ct)
    {
        var backupPath = Path.Combine(sessionDir, entry.FileName);
        if (!File.Exists(backupPath))
        {
            return new BackupRestoreRowResult(entry.FileName, BackupRestoreRowStatus.Failed, BackupFailureReason.MissingBackupFile, 0, entry.FileSizeBytes);
        }

        var targetDir = Path.GetDirectoryName(entry.OriginalPath);
        try
        {
            if (!string.IsNullOrEmpty(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _logger.Warning("Failed to create restore target directory for {Plugin}: {Error}", entry.FileName, ex.Message);
            return new BackupRestoreRowResult(entry.FileName, BackupRestoreRowStatus.Failed, BackupFailureReason.TargetFolderCreationFailed, 0, entry.FileSizeBytes);
        }

        var copyResult = await _fileCopier.CopyAsync(
            backupPath,
            entry.OriginalPath,
            BackupCopyOptions.AtomicReplace,
            progress,
            ct).ConfigureAwait(false);

        return copyResult.Status switch
        {
            BackupOperationStatus.Complete => new BackupRestoreRowResult(entry.FileName, BackupRestoreRowStatus.Restored, null, copyResult.BytesCopied, copyResult.TotalBytes),
            BackupOperationStatus.Canceled => new BackupRestoreRowResult(entry.FileName, BackupRestoreRowStatus.Canceled, BackupFailureReason.Canceled, copyResult.BytesCopied, copyResult.TotalBytes),
            _ => new BackupRestoreRowResult(entry.FileName, BackupRestoreRowStatus.Failed, MapRestoreFailure(copyResult.FailureReason), copyResult.BytesCopied, copyResult.TotalBytes)
        };
    }

    private static BackupFailureReason MapRestoreFailure(BackupFailureReason? reason) => reason switch
    {
        BackupFailureReason.SourceMissing => BackupFailureReason.MissingBackupFile,
        BackupFailureReason.AccessDenied => BackupFailureReason.AccessDenied,
        BackupFailureReason.Canceled => BackupFailureReason.Canceled,
        BackupFailureReason.TargetFolderCreationFailed => BackupFailureReason.TargetFolderCreationFailed,
        _ => BackupFailureReason.TargetWriteFailed
    };

    private static BackupOperationStatus GetRestoreStatus(IReadOnlyCollection<BackupRestoreRowResult> rows)
    {
        if (rows.Count == 0 || rows.All(row => row.Status == BackupRestoreRowStatus.Restored))
        {
            return BackupOperationStatus.Complete;
        }

        if (rows.All(row => row.Status == BackupRestoreRowStatus.Canceled))
        {
            return BackupOperationStatus.Canceled;
        }

        return rows.Any(row => row.Status == BackupRestoreRowStatus.Restored)
            ? BackupOperationStatus.Partial
            : BackupOperationStatus.Failed;
    }

    private async Task<IReadOnlyList<RetentionSessionCandidate>> ClassifyRetentionDirectoriesAsync(
        string backupRoot,
        ICollection<BackupRetentionRowResult> rows,
        CancellationToken ct)
    {
        var candidates = new List<RetentionSessionCandidate>();
        foreach (var dir in Directory.GetDirectories(backupRoot))
        {
            ct.ThrowIfCancellationRequested();
            var metadataPath = Path.Combine(dir, "session.json");
            if (!File.Exists(metadataPath))
            {
                _logger.Debug("Skipping malformed backup session directory without session.json: {Dir}", dir);
                rows.Add(new BackupRetentionRowResult(dir, BackupRetentionRowStatus.Kept, null));
                continue;
            }

            try
            {
                await using var stream = new FileStream(metadataPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var session = await JsonSerializer.DeserializeAsync<BackupSession>(stream, cancellationToken: ct).ConfigureAwait(false);
                if (session is null)
                {
                    rows.Add(new BackupRetentionRowResult(dir, BackupRetentionRowStatus.Kept, null));
                    continue;
                }

                candidates.Add(new RetentionSessionCandidate(dir, session.Timestamp));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                _logger.Warning("Skipping malformed backup session directory {Dir}: {Error}", dir, ex.Message);
                rows.Add(new BackupRetentionRowResult(dir, BackupRetentionRowStatus.Kept, null));
            }
        }

        return candidates;
    }

    private async Task<(BackupRetentionRowResult Row, bool Canceled)> DeleteRetentionCandidateAsync(string sessionDirectory, CancellationToken ct)
    {
        try
        {
            await _sessionDeleter.DeleteAsync(sessionDirectory, ct).ConfigureAwait(false);
            _logger.Information("Deleted old backup session: {Dir}", sessionDirectory);
            return (new BackupRetentionRowResult(sessionDirectory, BackupRetentionRowStatus.Deleted, null), Canceled: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning("Retrying old backup session deletion after transient failure for {Dir}: {Error}", sessionDirectory, ex.Message);
            try
            {
                // Windows antivirus or Explorer can briefly hold a directory handle; one short delay avoids false warnings.
                await Task.Delay(TimeSpan.FromMilliseconds(250), ct).ConfigureAwait(false);
                await _sessionDeleter.DeleteAsync(sessionDirectory, ct).ConfigureAwait(false);
                _logger.Information("Deleted old backup session after retry: {Dir}", sessionDirectory);
                return (new BackupRetentionRowResult(sessionDirectory, BackupRetentionRowStatus.Deleted, null), Canceled: false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.Information("Backup retention cleanup canceled before retrying deletion of {Dir}", sessionDirectory);
                return (new BackupRetentionRowResult(sessionDirectory, BackupRetentionRowStatus.Kept, BackupFailureReason.Canceled), Canceled: true);
            }
            catch (Exception retryEx) when (retryEx is IOException or UnauthorizedAccessException)
            {
                _logger.Warning("Failed to delete old backup session {Dir} after retry: {Error}", sessionDirectory, retryEx.Message);
                return (new BackupRetentionRowResult(sessionDirectory, BackupRetentionRowStatus.Failed, BackupFailureReason.CleanupDeletionFailed), Canceled: false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.Information("Backup retention cleanup canceled while deleting {Dir}", sessionDirectory);
            return (new BackupRetentionRowResult(sessionDirectory, BackupRetentionRowStatus.Kept, BackupFailureReason.Canceled), Canceled: true);
        }
    }

    private static void AddRemainingRetentionRows(
        IEnumerable<RetentionSessionCandidate> candidates,
        ICollection<BackupRetentionRowResult> rows,
        string? currentSessionFullPath)
    {
        foreach (var candidate in candidates)
        {
            if (rows.Any(row => IsSamePath(row.SessionDirectory, candidate.Directory)))
            {
                continue;
            }

            var reason = IsSamePath(candidate.Directory, currentSessionFullPath) ? (BackupFailureReason?)null : BackupFailureReason.Canceled;
            rows.Add(new BackupRetentionRowResult(candidate.Directory, BackupRetentionRowStatus.Kept, reason));
        }
    }

    private static void AddDirectoryRowsAsRemaining(IEnumerable<string> directories, ICollection<BackupRetentionRowResult> rows)
    {
        foreach (var directory in directories)
        {
            rows.Add(new BackupRetentionRowResult(directory, BackupRetentionRowStatus.Kept, BackupFailureReason.Canceled));
        }
    }

    private static string? GetComparableFullPath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);

    private static bool IsSamePath(string? left, string? right) =>
        left is not null && right is not null &&
        string.Equals(Path.GetFullPath(left), right, StringComparison.OrdinalIgnoreCase);

    private sealed record RetentionSessionCandidate(string Directory, DateTime Timestamp);
}
