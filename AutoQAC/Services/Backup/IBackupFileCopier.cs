using System;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;

namespace AutoQAC.Services.Backup;

/// <summary>
/// Performs cancellable file copies for backup and restore paths while reporting byte progress.
/// </summary>
public interface IBackupFileCopier
{
    /// <summary>
    /// Copies a file asynchronously with caller-selected destination overwrite semantics.
    /// </summary>
    /// <param name="sourcePath">Absolute source file path to copy from.</param>
    /// <param name="destinationPath">Absolute destination file path to create or replace.</param>
    /// <param name="options">Copy semantics that determine partial-file cleanup and destination replacement behavior.</param>
    /// <param name="progress">Optional progress sink receiving throttled byte-count updates.</param>
    /// <param name="cancellationToken">Token observed before and during read/write operations.</param>
    /// <returns>A structured copy result with concise failure reason and byte counts.</returns>
    Task<BackupCopyResult> CopyAsync(
        string sourcePath,
        string destinationPath,
        BackupCopyOptions options,
        IProgress<BackupCopyProgress>? progress,
        CancellationToken cancellationToken);
}
