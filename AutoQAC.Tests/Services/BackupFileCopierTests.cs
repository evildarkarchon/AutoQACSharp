using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Backup;
using FluentAssertions;
using NSubstitute;

namespace AutoQAC.Tests.Services;

/// <summary>
/// Verifies cancellable backup copy behavior, including safe cleanup semantics for
/// newly-created backup files and atomic restore replacement targets.
/// </summary>
public sealed class BackupFileCopierTests : IDisposable
{
    private readonly string _testRoot;
    private readonly BackupFileCopier _sut;

    public BackupFileCopierTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"autoqac_copy_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
        _sut = new BackupFileCopier(Substitute.For<ILoggingService>());
    }

    public void Dispose()
    {
        if (!Directory.Exists(_testRoot))
        {
            return;
        }

        try
        {
            Directory.Delete(_testRoot, recursive: true);
        }
        catch
        {
            // Test cleanup is best-effort because antivirus/indexers can briefly hold temp files on Windows.
        }
    }

    /// <summary>
    /// Verifies a failed create-new backup attempt cannot remove an older backup at the requested path.
    /// </summary>
    [Fact]
    public async Task CopyAsync_CreateNewDestinationAlreadyExists_PreservesExistingDestination()
    {
        var sourcePath = Path.Combine(_testRoot, "new-source.esp");
        var destinationPath = Path.Combine(_testRoot, "existing-backup.esp");
        await File.WriteAllTextAsync(sourcePath, "new backup content");
        await File.WriteAllTextAsync(destinationPath, "existing backup content");

        var result = await _sut.CopyAsync(
            sourcePath,
            destinationPath,
            BackupCopyOptions.CreateNewBackup,
            progress: null,
            CancellationToken.None);

        result.Status.Should().Be(BackupOperationStatus.Failed);
        result.FailureReason.Should().Be(BackupFailureReason.TargetWriteFailed);
        File.Exists(destinationPath).Should()
            .BeTrue("a create-new failure happens before this copy attempt owns the destination");
        File.ReadAllText(destinationPath).Should().Be("existing backup content");
    }

    [Fact]
    public async Task CopyAsync_CanceledCreateNewCopy_DeletesPartialDestination()
    {
        var sourcePath = Path.Combine(_testRoot, "large-source.bin");
        var destinationPath = Path.Combine(_testRoot, "backup.bin");
        await WritePatternFileAsync(sourcePath, sizeBytes: 32 * 1024 * 1024);

        using var cts = new CancellationTokenSource();
        var progress = new ImmediateProgress(_ => cts.Cancel());

        var result = await _sut.CopyAsync(
            sourcePath,
            destinationPath,
            BackupCopyOptions.CreateNewBackup,
            progress,
            cts.Token);

        result.Status.Should().Be(BackupOperationStatus.Canceled);
        result.FailureReason.Should().Be(BackupFailureReason.Canceled);
        File.Exists(destinationPath).Should().BeFalse("canceled backup creation must remove the partial output file");
    }

    [Fact]
    public async Task CopyAsync_CanceledAtomicReplace_PreservesExistingTarget()
    {
        var sourcePath = Path.Combine(_testRoot, "large-restore-source.bin");
        var destinationPath = Path.Combine(_testRoot, "existing-plugin.esp");
        await WritePatternFileAsync(sourcePath, sizeBytes: 32 * 1024 * 1024);
        await File.WriteAllTextAsync(destinationPath, "original target content");

        using var cts = new CancellationTokenSource();
        var progress = new ImmediateProgress(_ => cts.Cancel());

        var result = await _sut.CopyAsync(
            sourcePath,
            destinationPath,
            BackupCopyOptions.AtomicReplace,
            progress,
            cts.Token);

        result.Status.Should().Be(BackupOperationStatus.Canceled);
        result.FailureReason.Should().Be(BackupFailureReason.Canceled);
        File.ReadAllText(destinationPath).Should().Be("original target content",
            "restore cancellation must not damage the existing plugin file");
        File.Exists(destinationPath + ".autoqac-tmp").Should()
            .BeFalse("only the temporary restore file should be cleaned up");
    }

    /// <summary>
    /// Verifies atomic restore cancellation still deletes the temp file that this copy attempt opened.
    /// </summary>
    [Fact]
    public async Task CopyAsync_CanceledAtomicReplace_DeletesTempFile_RegardlessOfOwnershipFlag()
    {
        var sourcePath = Path.Combine(_testRoot, "large-restore-source-owned-temp.bin");
        var destinationPath = Path.Combine(_testRoot, "existing-owned-temp-plugin.esp");
        await WritePatternFileAsync(sourcePath, sizeBytes: 32 * 1024 * 1024);
        await File.WriteAllTextAsync(destinationPath, "original target content");

        using var cts = new CancellationTokenSource();
        var progress = new ImmediateProgress(_ => cts.Cancel());

        var result = await _sut.CopyAsync(
            sourcePath,
            destinationPath,
            BackupCopyOptions.AtomicReplace,
            progress,
            cts.Token);

        result.Status.Should().Be(BackupOperationStatus.Canceled);
        result.FailureReason.Should().Be(BackupFailureReason.Canceled);
        File.ReadAllText(destinationPath).Should().Be("original target content",
            "restore cancellation must preserve the original plugin file");
        File.Exists(destinationPath + ".autoqac-tmp").Should()
            .BeFalse("atomic restore owns the temp path once the destination stream opens");
    }

    [Fact]
    public async Task CopyAsync_ReportsProgressWithoutPerChunkUiSpam()
    {
        var sourcePath = Path.Combine(_testRoot, "source.bin");
        var destinationPath = Path.Combine(_testRoot, "destination.bin");
        await WritePatternFileAsync(sourcePath, sizeBytes: 2 * 1024 * 1024);
        var updates = new List<BackupCopyProgress>();

        var result = await _sut.CopyAsync(
            sourcePath,
            destinationPath,
            BackupCopyOptions.CreateNewBackup,
            new ImmediateProgress(updates.Add),
            CancellationToken.None);

        result.Status.Should().Be(BackupOperationStatus.Complete);
        result.BytesCopied.Should().Be(new FileInfo(sourcePath).Length);
        updates.Should().NotBeEmpty("copy progress must surface byte progress for the UI");
        updates.Should()
            .HaveCountLessThan(10, "progress should be throttled instead of emitted for every 81920-byte chunk");
        updates[^1].FileName.Should().Be(Path.GetFileName(sourcePath));
        updates[^1].BytesCopied.Should().Be(result.BytesCopied);
        updates[^1].TotalBytes.Should().Be(result.BytesCopied);
    }

    [Fact]
    public async Task CopyAsync_MissingSource_ReturnsNeutralSourceMissingReason()
    {
        var result = await _sut.CopyAsync(
            Path.Combine(_testRoot, "missing.esp"),
            Path.Combine(_testRoot, "destination.esp"),
            BackupCopyOptions.CreateNewBackup,
            progress: null,
            CancellationToken.None);

        result.Status.Should().Be(BackupOperationStatus.Failed);
        result.FailureReason.Should().Be(BackupFailureReason.SourceMissing);
        result.DisplayReason.Should().BeNull("callers map source-missing to operation-specific user text");
    }

    /// <summary>
    /// Verifies copy reports a target write failure instead of creating an unexpected destination directory.
    /// </summary>
    [Fact]
    public async Task CopyAsync_DestinationDirectoryMissing_ReturnsTargetWriteFailed()
    {
        var sourcePath = Path.Combine(_testRoot, "source.esp");
        var destinationPath = Path.Combine(_testRoot, "missing", "destination.esp");
        await File.WriteAllTextAsync(sourcePath, "plugin content");

        var result = await _sut.CopyAsync(
            sourcePath,
            destinationPath,
            BackupCopyOptions.CreateNewBackup,
            progress: null,
            CancellationToken.None);

        result.Status.Should().Be(BackupOperationStatus.Failed);
        result.FailureReason.Should().Be(BackupFailureReason.TargetWriteFailed);
        File.Exists(destinationPath).Should().BeFalse("failed target writes must not leave a destination file behind");
    }

    private static async Task WritePatternFileAsync(string path, int sizeBytes)
    {
        var pattern = new byte[81920];
        for (var i = 0; i < pattern.Length; i++)
        {
            pattern[i] = (byte)(i % byte.MaxValue);
        }

        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var remaining = sizeBytes;
        while (remaining > 0)
        {
            var bytesToWrite = Math.Min(pattern.Length, remaining);
            await stream.WriteAsync(pattern.AsMemory(0, bytesToWrite));
            remaining -= bytesToWrite;
        }
    }

    /// <summary>
    /// Delivers copy progress inline so assertions do not depend on <see cref="Progress{T}" /> callback scheduling.
    /// </summary>
    private sealed class ImmediateProgress(Action<BackupCopyProgress> onProgress) : IProgress<BackupCopyProgress>
    {
        /// <inheritdoc />
        public void Report(BackupCopyProgress value) => onProgress(value);
    }
}
