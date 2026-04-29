using AutoQAC.Models;
using AutoQAC.Services.Backup;
using FluentAssertions;

namespace AutoQAC.Tests.Models;

/// <summary>
/// Verifies structured backup, restore, and retention result contracts expose the
/// aggregate states and concise row-level labels needed by Phase 7 UI flows.
/// </summary>
public sealed class BackupOperationResultTests
{
    [Fact]
    public void BackupFailureReason_DisplayLabels_AreConciseUiLabels()
    {
        BackupFailureReason.MissingBackupFile.ToDisplayLabel().Should().Be("Missing backup file");
        BackupFailureReason.AccessDenied.ToDisplayLabel().Should().Be("Access denied");
        BackupFailureReason.TargetFolderCreationFailed.ToDisplayLabel().Should().Be("Target folder creation failed");
        BackupFailureReason.TargetWriteFailed.ToDisplayLabel().Should().Be("Target write failed");
        BackupFailureReason.Canceled.ToDisplayLabel().Should().Be("Canceled");
        BackupFailureReason.CleanupDeletionFailed.ToDisplayLabel().Should().Be("Cleanup deletion failed");
    }

    [Fact]
    public void BackupRestoreResult_ComputesRestoredFailedAndCanceledCounts()
    {
        var rows = new[]
        {
            new BackupRestoreRowResult("Restored.esp", BackupRestoreRowStatus.Restored, null, 128, 128),
            new BackupRestoreRowResult("Failed.esp", BackupRestoreRowStatus.Failed, BackupFailureReason.AccessDenied, 0, 128),
            new BackupRestoreRowResult("Canceled.esp", BackupRestoreRowStatus.Canceled, BackupFailureReason.Canceled, 64, 128)
        };

        var result = new BackupRestoreResult(BackupOperationStatus.Partial, rows);

        result.RestoredCount.Should().Be(1);
        result.FailedCount.Should().Be(1);
        result.CanceledCount.Should().Be(1);
    }

    [Fact]
    public void BackupRetentionCleanupResult_ComputesDeletedSkippedAndRemainingCounts()
    {
        var rows = new[]
        {
            new BackupRetentionRowResult("old", BackupRetentionRowStatus.Deleted, null),
            new BackupRetentionRowResult("current", BackupRetentionRowStatus.Kept, null),
            new BackupRetentionRowResult("locked", BackupRetentionRowStatus.Failed, BackupFailureReason.CleanupDeletionFailed)
        };

        var result = new BackupRetentionCleanupResult(BackupOperationStatus.Warning, rows);

        result.DeletedCount.Should().Be(1);
        result.SkippedCount.Should().Be(1);
        result.RemainingCount.Should().Be(2, "kept and failed sessions remain available on disk");
    }

    [Fact]
    public void BackupCreateResult_UsesStructuredStatusAndFailureReason()
    {
        var result = new BackupCreateResult(
            BackupOperationStatus.Failed,
            "Example.esp",
            0,
            1024,
            BackupFailureReason.TargetWriteFailed);

        result.Status.Should().Be(BackupOperationStatus.Failed);
        result.FailureReason.Should().Be(BackupFailureReason.TargetWriteFailed);
        result.DisplayReason.Should().Be("Target write failed");
    }

    [Fact]
    public void IBackupService_ExposesAsyncStructuredOperations()
    {
        var methods = typeof(IBackupService).GetMethods().Select(method => method.Name).ToArray();

        methods.Should().Contain("BackupPluginAsync");
        methods.Should().Contain("RestorePluginAsync");
        methods.Should().Contain("RestoreSessionAsync");
        methods.Should().Contain("CleanupOldSessionsAsync");
    }
}
