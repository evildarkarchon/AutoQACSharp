using System.Collections.Generic;
using System.Linq;

namespace AutoQAC.Models;

/// <summary>
/// Aggregate status for backup, restore, retention, and copy operations.
/// </summary>
public enum BackupOperationStatus
{
    /// <summary>The operation completed without row-level failures.</summary>
    Complete,

    /// <summary>The operation completed with at least one successful row and at least one failed or canceled row.</summary>
    Partial,

    /// <summary>The operation failed without producing a usable success outcome.</summary>
    Failed,

    /// <summary>The operation stopped because the caller requested cancellation.</summary>
    Canceled,

    /// <summary>The primary operation succeeded, but a secondary step such as retention cleanup reported failures.</summary>
    Warning
}

/// <summary>
/// Operation-neutral failure reasons that callers map to concise row-level UI labels.
/// </summary>
public enum BackupFailureReason
{
    /// <summary>The source path was missing; callers map this according to backup or restore context.</summary>
    SourceMissing,

    /// <summary>The restore source backup file was missing.</summary>
    MissingBackupFile,

    /// <summary>The operating system denied access to the source or target.</summary>
    AccessDenied,

    /// <summary>The target folder could not be created.</summary>
    TargetFolderCreationFailed,

    /// <summary>The target file could not be written.</summary>
    TargetWriteFailed,

    /// <summary>The caller canceled the operation.</summary>
    Canceled,

    /// <summary>An old backup session could not be deleted during retention cleanup.</summary>
    CleanupDeletionFailed
}

/// <summary>
/// Maps operation-neutral failure reasons to the concise labels allowed in user-facing restore and retention rows.
/// </summary>
public static class BackupFailureReasonExtensions
{
    private static readonly IReadOnlyDictionary<BackupFailureReason, string> DisplayLabels =
        new Dictionary<BackupFailureReason, string>
        {
            [BackupFailureReason.MissingBackupFile] = "Missing backup file",
            [BackupFailureReason.AccessDenied] = "Access denied",
            [BackupFailureReason.TargetFolderCreationFailed] = "Target folder creation failed",
            [BackupFailureReason.TargetWriteFailed] = "Target write failed",
            [BackupFailureReason.Canceled] = "Canceled",
            [BackupFailureReason.CleanupDeletionFailed] = "Cleanup deletion failed"
        };

    /// <summary>
    /// Returns the approved UI label for a reason, or null when callers must map neutral reasons themselves.
    /// </summary>
    public static string? ToDisplayLabel(this BackupFailureReason reason) =>
        DisplayLabels.GetValueOrDefault(reason);
}

/// <summary>
/// Row-level outcome for restoring one plugin from a backup session.
/// </summary>
public enum BackupRestoreRowStatus
{
    /// <summary>The plugin was restored to its target path.</summary>
    Restored,

    /// <summary>The plugin could not be restored.</summary>
    Failed,

    /// <summary>The restore was canceled before this row completed.</summary>
    Canceled
}

/// <summary>
/// Row-level outcome for one retention cleanup candidate.
/// </summary>
public enum BackupRetentionRowStatus
{
    /// <summary>The old backup session directory was deleted.</summary>
    Deleted,

    /// <summary>The session was intentionally kept, such as the current or newest retained session.</summary>
    Kept,

    /// <summary>The session could not be deleted and remains on disk.</summary>
    Failed
}

/// <summary>
/// Byte-level progress for a backup or restore file copy.
/// </summary>
/// <param name="FileName">The file or session name being processed, suitable for UI display.</param>
/// <param name="BytesCopied">Bytes copied so far, or zero for count-only operations.</param>
/// <param name="TotalBytes">Total bytes when known; null for count-only operations.</param>
/// <param name="FilesCompleted">Count of files or sessions completed for count-based progress.</param>
/// <param name="TotalFiles">Total files or sessions when known.</param>
public sealed record BackupCopyProgress(
    string FileName,
    long BytesCopied,
    long? TotalBytes,
    int FilesCompleted = 0,
    int? TotalFiles = null);

/// <summary>
/// Structured result from a single copy operation without raw exception text.
/// </summary>
/// <param name="Status">The copy status.</param>
/// <param name="SourcePath">The requested source path.</param>
/// <param name="DestinationPath">The requested final destination path.</param>
/// <param name="BytesCopied">Bytes copied before completion, failure, or cancellation.</param>
/// <param name="TotalBytes">Total source bytes when known.</param>
/// <param name="FailureReason">Concise failure reason, if any.</param>
public sealed record BackupCopyResult(
    BackupOperationStatus Status,
    string SourcePath,
    string DestinationPath,
    long BytesCopied,
    long? TotalBytes,
    BackupFailureReason? FailureReason)
{
    /// <summary>
    /// Gets the user-facing label for the failure reason, or null when callers must map the neutral reason themselves.
    /// </summary>
    public string? DisplayReason => FailureReason?.ToDisplayLabel();

    /// <summary>
    /// Creates a completed copy result.
    /// </summary>
    public static BackupCopyResult Complete(string sourcePath, string destinationPath, long bytesCopied,
        long? totalBytes) =>
        new(BackupOperationStatus.Complete, sourcePath, destinationPath, bytesCopied, totalBytes, FailureReason: null);

    /// <summary>
    /// Creates a failed copy result with a concise failure reason.
    /// </summary>
    public static BackupCopyResult Failed(
        string sourcePath,
        string destinationPath,
        BackupFailureReason reason,
        long bytesCopied = 0,
        long? totalBytes = null) =>
        new(BackupOperationStatus.Failed, sourcePath, destinationPath, bytesCopied, totalBytes, reason);

    /// <summary>
    /// Creates a canceled copy result.
    /// </summary>
    public static BackupCopyResult Canceled(string sourcePath, string destinationPath, long bytesCopied,
        long? totalBytes) =>
        new(BackupOperationStatus.Canceled, sourcePath, destinationPath, bytesCopied, totalBytes,
            BackupFailureReason.Canceled);
}

/// <summary>
/// Structured result for creating one plugin backup during a cleaning session.
/// </summary>
/// <param name="Status">Backup creation status.</param>
/// <param name="PluginName">Plugin file name shown in progress and results.</param>
/// <param name="BytesCopied">Bytes copied before completion, failure, or cancellation.</param>
/// <param name="TotalBytes">Total bytes when known.</param>
/// <param name="FailureReason">Concise failure reason, if any.</param>
public sealed record BackupCreateResult(
    BackupOperationStatus Status,
    string PluginName,
    long BytesCopied,
    long? TotalBytes,
    BackupFailureReason? FailureReason)
{
    /// <summary>
    /// Gets the approved UI display reason for failed or canceled backup creation.
    /// </summary>
    public string? DisplayReason => FailureReason?.ToDisplayLabel();
}

/// <summary>
/// Structured restore result for one plugin row.
/// </summary>
/// <param name="FileName">Plugin file name.</param>
/// <param name="Status">Row-level restore status.</param>
/// <param name="FailureReason">Concise failure reason, if any.</param>
/// <param name="BytesCopied">Bytes copied before completion, failure, or cancellation.</param>
/// <param name="TotalBytes">Total bytes when known.</param>
public sealed record BackupRestoreRowResult(
    string FileName,
    BackupRestoreRowStatus Status,
    BackupFailureReason? FailureReason,
    long BytesCopied,
    long? TotalBytes)
{
    /// <summary>
    /// Gets the approved UI display reason for failed or canceled restore rows.
    /// </summary>
    public string? DisplayReason => FailureReason?.ToDisplayLabel();
}

/// <summary>
/// Aggregate structured result for restoring one or more plugins from a backup session.
/// </summary>
/// <param name="Status">Aggregate restore status.</param>
/// <param name="Rows">Per-plugin restore rows.</param>
public sealed record BackupRestoreResult(BackupOperationStatus Status, IReadOnlyList<BackupRestoreRowResult> Rows)
{
    /// <summary>Number of plugins restored successfully.</summary>
    public int RestoredCount => Rows.Count(row => row.Status == BackupRestoreRowStatus.Restored);

    /// <summary>Number of plugins that failed to restore.</summary>
    public int FailedCount => Rows.Count(row => row.Status == BackupRestoreRowStatus.Failed);

    /// <summary>Number of restore rows canceled before completion.</summary>
    public int CanceledCount => Rows.Count(row => row.Status == BackupRestoreRowStatus.Canceled);
}

/// <summary>
/// Structured retention cleanup result for one backup session directory.
/// </summary>
/// <param name="SessionDirectory">Session directory considered for cleanup.</param>
/// <param name="Status">Row-level retention status.</param>
/// <param name="FailureReason">Concise failure reason, if any.</param>
public sealed record BackupRetentionRowResult(
    string SessionDirectory,
    BackupRetentionRowStatus Status,
    BackupFailureReason? FailureReason)
{
    /// <summary>
    /// Gets the approved UI display reason for failed retention cleanup rows.
    /// </summary>
    public string? DisplayReason => FailureReason?.ToDisplayLabel();
}

/// <summary>
/// Aggregate structured result for backup retention cleanup.
/// </summary>
/// <param name="Status">Aggregate retention status.</param>
/// <param name="Rows">Per-session retention cleanup rows.</param>
public sealed record BackupRetentionCleanupResult(
    BackupOperationStatus Status,
    IReadOnlyList<BackupRetentionRowResult> Rows)
{
    /// <summary>Number of old backup session directories deleted.</summary>
    public int DeletedCount => Rows.Count(row => row.Status == BackupRetentionRowStatus.Deleted);

    /// <summary>Number of backup session directories intentionally kept.</summary>
    public int SkippedCount => Rows.Count(row => row.Status == BackupRetentionRowStatus.Kept);

    /// <summary>Number of backup session directories still present after cleanup.</summary>
    public int RemainingCount =>
        Rows.Count(row => row.Status is BackupRetentionRowStatus.Kept or BackupRetentionRowStatus.Failed);
}

/// <summary>
/// Row-level outcome for deleting one backup session directory from RestoreWindow.
/// Distinct from <see cref="BackupRetentionRowResult"/> because Delete Session is a manual user action,
/// not part of automated retention cleanup.
/// </summary>
public enum BackupSessionDeleteStatus
{
    /// <summary>The session directory was deleted successfully.</summary>
    Deleted,

    /// <summary>The session directory was outside the configured backup root and was not deleted.</summary>
    RejectedOutsideBackupRoot,

    /// <summary>The recursive delete operation failed (locked file, missing directory, permission, etc.).</summary>
    Failed
}

/// <summary>
/// Structured result from a manual RestoreWindow "Delete Session" action.
/// </summary>
/// <param name="Status">Aggregate delete status.</param>
/// <param name="SessionDirectory">The session directory considered for deletion (preserved for logging).</param>
public sealed record BackupSessionDeleteResult(
    BackupSessionDeleteStatus Status,
    string SessionDirectory);
