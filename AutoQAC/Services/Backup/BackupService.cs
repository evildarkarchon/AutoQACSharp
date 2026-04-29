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
        var baseSessionName = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var sessionName = baseSessionName;
        var sessionDir = Path.Combine(backupRoot, sessionName);
        for (var suffix = 1; Directory.Exists(sessionDir); suffix++)
        {
            sessionName = $"{baseSessionName}-{suffix:00}";
            sessionDir = Path.Combine(backupRoot, sessionName);
        }

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

            if (!ValidateBackupDestination(plugin, sessionDir, out var destPath))
            {
                _logger.Warning("Rejected unsafe backup file name for {Plugin}: {FileName}", plugin.FullPath, plugin.FileName);
                return BackupResult.Failure("Invalid plugin file name for backup.");
            }

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

        try
        {
            Directory.CreateDirectory(sessionDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _logger.Warning("Failed to create backup session directory for {Plugin}: {Error}", plugin.FileName, ex.Message);
            return new BackupCreateResult(
                BackupOperationStatus.Failed,
                plugin.FileName,
                BytesCopied: 0,
                TotalBytes: null,
                ex is UnauthorizedAccessException ? BackupFailureReason.AccessDenied : BackupFailureReason.TargetFolderCreationFailed);
        }

        if (!ValidateBackupDestination(plugin, sessionDir, out var destinationPath))
        {
            _logger.Warning("Rejected unsafe async backup file name for {Plugin}: {FileName}", plugin.FullPath, plugin.FileName);
            return new BackupCreateResult(
                BackupOperationStatus.Failed,
                plugin.FileName,
                BytesCopied: 0,
                TotalBytes: null,
                BackupFailureReason.SourceMissing);
        }

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

    public void RestorePlugin(BackupPluginEntry entry, string sessionDir, string? trustedRestoreRoot)
    {
        if (!ValidateRestoreEntry(entry, sessionDir, trustedRestoreRoot, out var backupPath, out var targetPath, out _))
        {
            _logger.Warning("Rejected unsafe backup metadata for sync restore of {Plugin}", entry.FileName);
            throw new InvalidOperationException("Backup metadata is not safe to restore.");
        }

        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException($"Backup file not found: '{backupPath}'");
        }

        // Ensure the target directory exists
        var targetDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        File.Copy(backupPath, targetPath, overwrite: true);
        _logger.Information("Restored {Plugin} to {Path}", entry.FileName, targetPath);
    }

    public async Task<BackupRestoreResult> RestorePluginAsync(
        BackupPluginEntry entry,
        string sessionDir,
        string? trustedRestoreRoot,
        IProgress<BackupCopyProgress>? progress = null,
        CancellationToken ct = default)
    {
        var row = await RestorePluginRowAsync(entry, sessionDir, trustedRestoreRoot, progress, ct).ConfigureAwait(false);
        var status = row.Status switch
        {
            BackupRestoreRowStatus.Restored => BackupOperationStatus.Complete,
            BackupRestoreRowStatus.Canceled => BackupOperationStatus.Canceled,
            _ => BackupOperationStatus.Failed
        };

        return new BackupRestoreResult(status, [row]);
    }

    public void RestoreSession(BackupSession session, string? trustedRestoreRoot)
    {
        foreach (var entry in session.Plugins)
        {
            RestorePlugin(entry, session.SessionDirectory, trustedRestoreRoot);
        }
    }

    public async Task<BackupRestoreResult> RestoreSessionAsync(
        BackupSession session,
        string? trustedRestoreRoot,
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

            rows.Add(await RestorePluginRowAsync(entry, session.SessionDirectory, trustedRestoreRoot, progress, ct).ConfigureAwait(false));
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

        try
        {
            var currentSessionFullPath = GetComparableFullPath(currentSessionDir);
            var validSessions = await ClassifyRetentionDirectoriesAsync(backupRoot, rows, ct).ConfigureAwait(false);
            var retentionTotal = rows.Count + validSessions.Count;
            ReportRetentionProgress(progress, backupRoot, rows.Count, retentionTotal);
            if (ct.IsCancellationRequested)
            {
                AddRemainingRetentionRows(validSessions, rows, currentSessionFullPath);
                ReportRetentionProgress(progress, backupRoot, rows.Count, rows.Count);
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
                ReportRetentionProgress(progress, session.Directory, rows.Count, retentionTotal);
            }

            var currentSession = validSessions.FirstOrDefault(session => IsSamePath(session.Directory, currentSessionFullPath));
            if (currentSession is not null)
            {
                rows.Add(new BackupRetentionRowResult(currentSession.Directory, BackupRetentionRowStatus.Kept, null));
                ReportRetentionProgress(progress, currentSession.Directory, rows.Count, retentionTotal);
            }

            foreach (var session in validNonCurrent.Skip(keepNonCurrent))
            {
                ct.ThrowIfCancellationRequested();
                var deleteResult = await DeleteRetentionCandidateAsync(session.Directory, ct).ConfigureAwait(false);
                rows.Add(deleteResult.Row);
                ReportRetentionProgress(progress, session.Directory, rows.Count, retentionTotal);
                if (deleteResult.Canceled)
                {
                    AddRemainingRetentionRows(validNonCurrent.Skip(keepNonCurrent).Where(candidate => candidate.Directory != session.Directory), rows, currentSessionFullPath);
                    ReportRetentionProgress(progress, session.Directory, rows.Count, rows.Count);
                    return new BackupRetentionCleanupResult(BackupOperationStatus.Canceled, rows);
                }
            }

            var status = rows.Any(row => row.Status == BackupRetentionRowStatus.Failed)
                ? BackupOperationStatus.Warning
                : BackupOperationStatus.Complete;
            return new BackupRetentionCleanupResult(status, rows);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            AddDirectoryRowsAsRemaining(Directory.GetDirectories(backupRoot), rows);
            ReportRetentionProgress(progress, backupRoot, rows.Count, rows.Count);
            return new BackupRetentionCleanupResult(BackupOperationStatus.Canceled, rows);
        }
    }

    /// <inheritdoc />
    public async Task<BackupSessionDeleteResult> DeleteSessionAsync(
        BackupSession session,
        string backupRoot,
        CancellationToken ct = default)
    {
        // Fail closed without leaking path detail to callers when inputs are missing.
        // The dialog/status text in RestoreViewModel uses the canonical out-of-root sentence
        // for both this branch and the IsContained-rejection branch below.
        if (string.IsNullOrWhiteSpace(backupRoot) || string.IsNullOrWhiteSpace(session.SessionDirectory))
        {
            return new BackupSessionDeleteResult(
                BackupSessionDeleteStatus.RejectedOutsideBackupRoot,
                session.SessionDirectory ?? string.Empty);
        }

        // Delegate to the shared containment helper (Plan 07-14) so backup, restore, and delete
        // safety boundaries all use the same string-level normalization+trailing-separator policy.
        if (!BackupPathContainment.IsContained(session.SessionDirectory, backupRoot))
        {
            _logger.Warning(
                "Rejected backup session delete outside backup root. BackupRoot={BackupRoot}; SessionDirectory={SessionDirectory}",
                backupRoot,
                session.SessionDirectory);
            return new BackupSessionDeleteResult(
                BackupSessionDeleteStatus.RejectedOutsideBackupRoot,
                session.SessionDirectory);
        }

        try
        {
            await _sessionDeleter.DeleteAsync(session.SessionDirectory, ct).ConfigureAwait(false);
            _logger.Information("Deleted backup session: {SessionDirectory}", session.SessionDirectory);
            return new BackupSessionDeleteResult(BackupSessionDeleteStatus.Deleted, session.SessionDirectory);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is the caller's contract -- propagate so the UI cancellation surface
            // can distinguish "user canceled" from "deletion failed".
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // Expected filesystem failures (locked files, permission denied, missing directory)
            // map to a structured Failed result; the user-facing copy stays generic and the
            // technical detail lives in the log.
            _logger.Error(ex, "Failed to delete backup session at {SessionDirectory}", session.SessionDirectory);
            return new BackupSessionDeleteResult(BackupSessionDeleteStatus.Failed, session.SessionDirectory);
        }
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
        string? trustedRestoreRoot,
        IProgress<BackupCopyProgress>? progress,
        CancellationToken ct)
    {
        if (!ValidateRestoreEntry(entry, sessionDir, trustedRestoreRoot, out var backupPath, out var targetPath, out var failureReason))
        {
            return new BackupRestoreRowResult(entry.FileName, BackupRestoreRowStatus.Failed, failureReason, 0, entry.FileSizeBytes);
        }

        if (!File.Exists(backupPath))
        {
            return new BackupRestoreRowResult(entry.FileName, BackupRestoreRowStatus.Failed, BackupFailureReason.MissingBackupFile, 0, entry.FileSizeBytes);
        }

        var targetDir = Path.GetDirectoryName(targetPath);
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
            targetPath,
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

    /// <summary>
    /// Validates a backup destination file name and resolves the final backup path only after containment is proven.
    /// </summary>
    /// <param name="plugin">Plugin metadata whose file name came from discovery/configuration and is treated as untrusted path input.</param>
    /// <param name="sessionDir">Backup session directory that must contain the resolved destination path.</param>
    /// <param name="destinationPath">Resolved destination path when validation succeeds; otherwise an empty string.</param>
    /// <returns>True when the plugin file name is simple and the resolved destination remains inside the session directory.</returns>
    private static bool ValidateBackupDestination(PluginInfo plugin, string sessionDir, out string destinationPath)
    {
        destinationPath = string.Empty;
        if (!IsSafeSessionRelativeName(plugin.FileName, requirePluginExtension: false))
        {
            return false;
        }

        try
        {
            var sessionRoot = EnsureTrailingDirectorySeparator(Path.GetFullPath(sessionDir));
            var resolvedDestinationPath = Path.GetFullPath(Path.Combine(sessionRoot, plugin.FileName));
            if (!resolvedDestinationPath.StartsWith(sessionRoot, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            destinationPath = resolvedDestinationPath;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Checks whether a backup metadata name is a simple session-relative file name safe for path composition.
    /// </summary>
    /// <param name="fileName">Candidate file name from plugin discovery or backup session metadata.</param>
    /// <param name="requirePluginExtension">True to allow only active plugin extensions (.esm, .esp, .esl).</param>
    /// <returns>True when the name is non-rooted, single-segment, ADS-free, and optionally has an approved plugin extension.</returns>
    private static bool IsSafeSessionRelativeName(string fileName, bool requirePluginExtension)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.IsPathRooted(fileName))
        {
            return false;
        }

        if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) ||
            fileName.Contains(Path.DirectorySeparatorChar) ||
            fileName.Contains(Path.AltDirectorySeparatorChar) ||
            fileName.Contains(':'))
        {
            return false;
        }

        return !requirePluginExtension || IsApprovedPluginExtension(fileName);
    }

    /// <summary>
    /// Checks whether a path ends with an active Bethesda plugin extension accepted by Phase 7 restore policy.
    /// </summary>
    private static bool IsApprovedPluginExtension(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".esm", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".esp", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".esl", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Validates untrusted restore metadata before it is used for source or target filesystem paths.
    /// Backup file names must remain simple session-contained names, and restore targets must be rooted paths
    /// whose file name and extension match the backed-up plugin before overwrite attempts.
    /// </summary>
    private static bool ValidateRestoreEntry(
        BackupPluginEntry entry,
        string sessionDir,
        string? trustedRestoreRoot,
        out string backupPath,
        out string targetPath,
        out BackupFailureReason failureReason)
    {
        backupPath = string.Empty;
        targetPath = string.Empty;

        if (!IsSafeSessionRelativeName(entry.FileName, requirePluginExtension: true))
        {
            failureReason = IsSafeSessionRelativeName(entry.FileName, requirePluginExtension: false)
                ? BackupFailureReason.TargetFolderCreationFailed
                : BackupFailureReason.MissingBackupFile;
            return false;
        }

        try
        {
            var sessionRoot = EnsureTrailingDirectorySeparator(Path.GetFullPath(sessionDir));
            var resolvedBackupPath = Path.GetFullPath(Path.Combine(sessionRoot, entry.FileName));
            if (!resolvedBackupPath.StartsWith(sessionRoot, StringComparison.OrdinalIgnoreCase))
            {
                failureReason = BackupFailureReason.MissingBackupFile;
                return false;
            }

            backupPath = resolvedBackupPath;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            failureReason = BackupFailureReason.MissingBackupFile;
            return false;
        }

        try
        {
            if (!IsSafeRestoreTarget(entry))
            {
                failureReason = BackupFailureReason.TargetFolderCreationFailed;
                return false;
            }

            targetPath = Path.GetFullPath(entry.OriginalPath);
            if (!IsNormalLocalDriveRoot(Path.GetPathRoot(targetPath)) ||
                !IsApprovedPluginExtension(targetPath) ||
                !IsRestoreTargetInsideTrustedRoot(targetPath, trustedRestoreRoot))
            {
                failureReason = BackupFailureReason.TargetFolderCreationFailed;
                return false;
            }

            failureReason = default;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            failureReason = BackupFailureReason.TargetFolderCreationFailed;
            return false;
        }
    }

    /// <summary>
    /// Checks untrusted restore target metadata before directory creation or copy operations.
    /// </summary>
    /// <param name="entry">Backup entry whose original target path came from session metadata.</param>
    /// <returns>True when the target is a local-drive rooted plugin path whose file name matches the backup file name.</returns>
    private static bool IsSafeRestoreTarget(BackupPluginEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.OriginalPath) ||
            !Path.IsPathRooted(entry.OriginalPath) ||
            entry.OriginalPath.StartsWith(@"\\", StringComparison.Ordinal) ||
            entry.OriginalPath.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            entry.OriginalPath.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(Path.GetFileName(entry.OriginalPath), entry.FileName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsApprovedPluginExtension(entry.OriginalPath);
    }

    /// <summary>
    /// Checks string-level containment of a normalized restore target under the trusted Data-folder root.
    /// Delegates to <see cref="BackupPathContainment.IsContained"/> so the canonical containment policy
    /// is shared with RestoreViewModel.DeleteSessionAsync (Plan 07-13) and any future delete/restore
    /// safety boundaries. Does not resolve NTFS reparse points or symlinks; callers still constrain
    /// metadata before filesystem writes.
    /// </summary>
    /// <param name="targetPath">Restore target path that must remain inside the trusted root.</param>
    /// <param name="trustedRestoreRoot">Configured game Data folder that bounds restore overwrites.</param>
    /// <returns>True when both paths normalize and the target has the trusted root as a directory prefix.</returns>
    private static bool IsRestoreTargetInsideTrustedRoot(string targetPath, string? trustedRestoreRoot) =>
        BackupPathContainment.IsContained(targetPath, trustedRestoreRoot);

    /// <summary>
    /// Checks whether a path root is a normal Windows local drive root such as <c>C:\</c>.
    /// </summary>
    private static bool IsNormalLocalDriveRoot(string? root) =>
        root is { Length: 3 } &&
        char.IsLetter(root[0]) &&
        root[1] == ':' &&
        (root[2] == Path.DirectorySeparatorChar || root[2] == Path.AltDirectorySeparatorChar);

    /// <summary>
    /// Ensures session containment checks compare against a directory prefix instead of a similarly named sibling.
    /// </summary>
    private static string EnsureTrailingDirectorySeparator(string path) =>
        Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;

    /// <summary>
    /// Reports count-only retention cleanup progress through the shared backup progress model.
    /// </summary>
    private static void ReportRetentionProgress(
        IProgress<BackupCopyProgress>? progress,
        string sessionDirectory,
        int filesCompleted,
        int totalFiles) =>
        progress?.Report(new BackupCopyProgress(
            Path.GetFileName(sessionDirectory),
            BytesCopied: 0,
            TotalBytes: null,
            filesCompleted,
            totalFiles));

    private static BackupOperationStatus GetRestoreStatus(IReadOnlyCollection<BackupRestoreRowResult> rows)
    {
        if (rows.Count == 0 || rows.All(row => row.Status == BackupRestoreRowStatus.Restored))
        {
            return BackupOperationStatus.Complete;
        }

        if (rows.Any(row => row.Status == BackupRestoreRowStatus.Restored))
        {
            return BackupOperationStatus.Partial;
        }

        return rows.Any(row => row.Status == BackupRestoreRowStatus.Canceled)
            ? BackupOperationStatus.Canceled
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
            if (rows.Any(row => IsSamePath(row.SessionDirectory, directory)))
            {
                continue;
            }

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
