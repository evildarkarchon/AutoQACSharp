using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;

namespace AutoQAC.Services.Backup;

/// <summary>
/// Managed-stream implementation of <see cref="IBackupFileCopier"/> with safe cancellation cleanup.
/// </summary>
public sealed class BackupFileCopier(ILoggingService logger) : IBackupFileCopier
{
    private const int BufferSize = 81920;
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);

    /// <inheritdoc />
    public async Task<BackupCopyResult> CopyAsync(
        string sourcePath,
        string destinationPath,
        BackupCopyOptions options,
        IProgress<BackupCopyProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath))
        {
            logger.Warning("Backup copy source is missing: {SourcePath}", sourcePath);
            return BackupCopyResult.Failed(sourcePath, destinationPath, BackupFailureReason.SourceMissing);
        }

        var actualOutputPath = GetOutputPath(destinationPath, options);
        var copiedBytes = 0L;
        var totalBytes = new FileInfo(sourcePath).Length;

        try
        {
            await CopyFileContentsAsync(
                sourcePath,
                actualOutputPath,
                options,
                progress,
                totalBytes,
                bytesCopied => copiedBytes = bytesCopied,
                cancellationToken).ConfigureAwait(false);

            if (options.ExistingTargetPolicy == BackupCopyExistingTargetPolicy.ReplaceAtomically)
            {
                File.Move(actualOutputPath, destinationPath, overwrite: true);
            }

            logger.Debug("Copied {SourcePath} to {DestinationPath} ({BytesCopied} bytes)", sourcePath, destinationPath, copiedBytes);
            return BackupCopyResult.Complete(sourcePath, destinationPath, copiedBytes, totalBytes);
        }
        catch (OperationCanceledException)
        {
            DeletePartialOutput(actualOutputPath, sourcePath, destinationPath);
            logger.Information("Backup copy canceled for {SourcePath} to {DestinationPath}", sourcePath, destinationPath);
            return BackupCopyResult.Canceled(sourcePath, destinationPath, copiedBytes, totalBytes);
        }
        catch (UnauthorizedAccessException ex)
        {
            DeletePartialOutput(actualOutputPath, sourcePath, destinationPath);
            logger.Warning("Access denied copying {SourcePath} to {DestinationPath}: {Error}", sourcePath, destinationPath, ex.Message);
            return BackupCopyResult.Failed(sourcePath, destinationPath, BackupFailureReason.AccessDenied, copiedBytes, totalBytes);
        }
        catch (IOException ex)
        {
            DeletePartialOutput(actualOutputPath, sourcePath, destinationPath);
            logger.Warning("I/O failure copying {SourcePath} to {DestinationPath}: {Error}", sourcePath, destinationPath, ex.Message);
            return BackupCopyResult.Failed(sourcePath, destinationPath, BackupFailureReason.TargetWriteFailed, copiedBytes, totalBytes);
        }
    }

    private static string GetOutputPath(string destinationPath, BackupCopyOptions options) =>
        options.ExistingTargetPolicy == BackupCopyExistingTargetPolicy.ReplaceAtomically
            ? destinationPath + ".autoqac-tmp"
            : destinationPath;

    private static async Task CopyFileContentsAsync(
        string sourcePath,
        string actualOutputPath,
        BackupCopyOptions options,
        IProgress<BackupCopyProgress>? progress,
        long totalBytes,
        Action<long> updateCopiedBytes,
        CancellationToken cancellationToken)
    {
        var fileMode = options.ExistingTargetPolicy == BackupCopyExistingTargetPolicy.FailIfExists
            ? FileMode.CreateNew
            : FileMode.Create;
        var fileName = Path.GetFileName(sourcePath);
        var buffer = new byte[BufferSize];
        var copiedBytes = 0L;
        var stopwatch = Stopwatch.StartNew();
        var lastProgressAt = TimeSpan.MinValue;

        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        await using var destination = new FileStream(actualOutputPath, fileMode, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytesRead = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            copiedBytes += bytesRead;
            updateCopiedBytes(copiedBytes);

            var reachedCompletion = copiedBytes == totalBytes;
            if (ShouldReportProgress(stopwatch.Elapsed, lastProgressAt, reachedCompletion))
            {
                progress?.Report(new BackupCopyProgress(fileName, copiedBytes, totalBytes));
                lastProgressAt = stopwatch.Elapsed;
            }
        }

        if (copiedBytes != totalBytes)
        {
            throw new IOException($"Copy ended before all bytes were written for '{sourcePath}'.");
        }

        // Ensure very fast small-file copies still publish at least one progress update for the UI.
        if (lastProgressAt == TimeSpan.MinValue)
        {
            progress?.Report(new BackupCopyProgress(fileName, copiedBytes, totalBytes));
        }
    }

    private static bool ShouldReportProgress(TimeSpan elapsed, TimeSpan lastProgressAt, bool reachedCompletion) =>
        reachedCompletion || lastProgressAt == TimeSpan.MinValue || elapsed - lastProgressAt >= ProgressInterval;

    private void DeletePartialOutput(string actualOutputPath, string sourcePath, string destinationPath)
    {
        try
        {
            if (File.Exists(actualOutputPath))
            {
                File.Delete(actualOutputPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cleanup failures are logged but not surfaced as raw exception text in user-facing copy results.
            logger.Warning("Failed to delete partial backup copy {PartialPath} for {SourcePath} to {DestinationPath}: {Error}",
                actualOutputPath,
                sourcePath,
                destinationPath,
                ex.Message);
        }
    }
}
