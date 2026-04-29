using System.Collections.Generic;

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
/// Byte-level progress for a backup or restore file copy.
/// </summary>
/// <param name="FileName">The file name being copied, suitable for UI display.</param>
/// <param name="BytesCopied">Bytes copied so far.</param>
/// <param name="TotalBytes">Total bytes when known.</param>
public sealed record BackupCopyProgress(string FileName, long BytesCopied, long? TotalBytes);

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
    private static readonly IReadOnlyDictionary<BackupFailureReason, string> DisplayLabels = new Dictionary<BackupFailureReason, string>
    {
        [BackupFailureReason.MissingBackupFile] = "Missing backup file",
        [BackupFailureReason.AccessDenied] = "Access denied",
        [BackupFailureReason.TargetFolderCreationFailed] = "Target folder creation failed",
        [BackupFailureReason.TargetWriteFailed] = "Target write failed",
        [BackupFailureReason.Canceled] = "Canceled",
        [BackupFailureReason.CleanupDeletionFailed] = "Cleanup deletion failed"
    };

    /// <summary>
    /// Gets the user-facing label for the failure reason, or null when callers must map the neutral reason themselves.
    /// </summary>
    public string? DisplayReason => FailureReason is { } reason && DisplayLabels.TryGetValue(reason, out var label) ? label : null;

    /// <summary>
    /// Creates a completed copy result.
    /// </summary>
    public static BackupCopyResult Complete(string sourcePath, string destinationPath, long bytesCopied, long? totalBytes) =>
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
    public static BackupCopyResult Canceled(string sourcePath, string destinationPath, long bytesCopied, long? totalBytes) =>
        new(BackupOperationStatus.Canceled, sourcePath, destinationPath, bytesCopied, totalBytes, BackupFailureReason.Canceled);
}
