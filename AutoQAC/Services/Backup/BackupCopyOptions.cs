using System.IO;

namespace AutoQAC.Services.Backup;

/// <summary>
/// Defines how <see cref="IBackupFileCopier"/> handles an existing destination before copy work begins.
/// </summary>
public enum BackupCopyExistingTargetPolicy
{
    /// <summary>
    /// Create the destination with <see cref="FileMode.CreateNew"/> and fail if it already exists.
    /// </summary>
    FailIfExists,

    /// <summary>
    /// Copy to a temporary file in the target directory and replace the destination only after copy completion.
    /// </summary>
    ReplaceAtomically
}

/// <summary>
/// Options for a backup copy request, including whether an existing destination can be replaced.
/// </summary>
/// <param name="ExistingTargetPolicy">The destination-file policy selected by the caller for backup or restore semantics.</param>
public sealed record BackupCopyOptions(BackupCopyExistingTargetPolicy ExistingTargetPolicy)
{
    /// <summary>
    /// Uses create-new semantics for backup creation so accidental overwrites fail safely.
    /// </summary>
    public static BackupCopyOptions CreateNewBackup { get; } = new(BackupCopyExistingTargetPolicy.FailIfExists);

    /// <summary>
    /// Uses temporary-file replacement semantics so restore cancellation preserves the existing target.
    /// </summary>
    public static BackupCopyOptions AtomicReplace { get; } = new(BackupCopyExistingTargetPolicy.ReplaceAtomically);
}
