using System.Globalization;
using System.Text.Json;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Backup;
using FluentAssertions;
using NSubstitute;

namespace AutoQAC.Tests.Services;

/// <summary>
/// Unit tests for <see cref="BackupService"/> covering plugin backup, restore,
/// session management, cleanup, and edge cases.
///
/// Uses temp directories for file system isolation -- BackupService accepts paths
/// via method parameters (injectable), making this standard C# unit test practice.
/// </summary>
public sealed class BackupServiceTests : IDisposable
{
    private readonly ILoggingService _mockLogger;
    private readonly BackupService _sut;
    private readonly string _testRoot;

    public BackupServiceTests()
    {
        _mockLogger = Substitute.For<ILoggingService>();
        _sut = new BackupService(_mockLogger);
        _testRoot = Path.Combine(Path.GetTempPath(), $"autoqac_backup_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            try { Directory.Delete(_testRoot, recursive: true); }
            catch { /* Best-effort cleanup */ }
        }
    }

    #region CreateSessionDirectory

    [Fact]
    public void CreateSessionDirectory_CreatesTimestampedDirectory()
    {
        // Arrange
        var backupRoot = Path.Combine(_testRoot, "backups");

        // Act
        var sessionDir = _sut.CreateSessionDirectory(backupRoot);

        // Assert
        Directory.Exists(sessionDir).Should().BeTrue("session directory should be created");
        var dirName = Path.GetFileName(sessionDir);
        // Format: yyyy-MM-dd_HH-mm-ss
        dirName.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}$",
            "directory name should match timestamp format yyyy-MM-dd_HH-mm-ss");
    }

    #endregion

    #region BackupPlugin

    [Fact]
    public void BackupPlugin_ValidPlugin_CopiesFileSuccessfully()
    {
        // Arrange
        var sourceFile = Path.Combine(_testRoot, "TestPlugin.esp");
        File.WriteAllText(sourceFile, "fake plugin data for testing");
        var plugin = new PluginInfo { FileName = "TestPlugin.esp", FullPath = sourceFile };
        var sessionDir = Path.Combine(_testRoot, "session1");
        Directory.CreateDirectory(sessionDir);

        // Act
        var result = _sut.BackupPlugin(plugin, sessionDir);

        // Assert
        result.Success.Should().BeTrue();
        result.FileSizeBytes.Should().BeGreaterThan(0);
        result.Error.Should().BeNull();
        File.Exists(Path.Combine(sessionDir, "TestPlugin.esp")).Should().BeTrue(
            "plugin file should be copied to session directory");
    }

    [Fact]
    public void BackupPlugin_NonRootedPath_ReturnsFailure()
    {
        // Arrange
        var plugin = new PluginInfo { FileName = "plugin.esp", FullPath = "relative/path.esp" };
        var sessionDir = Path.Combine(_testRoot, "session");

        // Act
        var result = _sut.BackupPlugin(plugin, sessionDir);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not a valid rooted path");
    }

    [Fact]
    public void BackupPlugin_EmptyPath_ReturnsFailure()
    {
        // Arrange
        var plugin = new PluginInfo { FileName = "plugin.esp", FullPath = "" };
        var sessionDir = Path.Combine(_testRoot, "session");

        // Act
        var result = _sut.BackupPlugin(plugin, sessionDir);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not a valid rooted path");
    }

    [Fact]
    public void BackupPlugin_MissingSourceFile_ReturnsFailure()
    {
        // Arrange
        var plugin = new PluginInfo
        {
            FileName = "missing.esp",
            FullPath = Path.Combine(_testRoot, "nonexistent", "missing.esp")
        };
        var sessionDir = Path.Combine(_testRoot, "session");

        // Act
        var result = _sut.BackupPlugin(plugin, sessionDir);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("does not exist");
    }

    [Fact]
    public void BackupPlugin_OverwriteFalse_FailsOnDuplicate()
    {
        // Arrange
        var sourceFile = Path.Combine(_testRoot, "DupePlugin.esp");
        File.WriteAllText(sourceFile, "fake plugin");
        var plugin = new PluginInfo { FileName = "DupePlugin.esp", FullPath = sourceFile };
        var sessionDir = Path.Combine(_testRoot, "session_dupe");
        Directory.CreateDirectory(sessionDir);

        // First backup succeeds
        var first = _sut.BackupPlugin(plugin, sessionDir);
        first.Success.Should().BeTrue();

        // Act -- second backup to same session dir should fail (overwrite: false)
        var second = _sut.BackupPlugin(plugin, sessionDir);

        // Assert
        second.Success.Should().BeFalse("File.Copy with overwrite:false throws IOException on duplicate");
        second.Error.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region WriteSessionMetadataAsync

    [Fact]
    public async Task WriteSessionMetadataAsync_WritesValidJson()
    {
        // Arrange
        var sessionDir = Path.Combine(_testRoot, "session_meta");
        Directory.CreateDirectory(sessionDir);
        var session = new BackupSession
        {
            Timestamp = new DateTime(2026, 2, 7, 10, 30, 0, DateTimeKind.Utc),
            GameType = "SSE",
            Plugins = new List<BackupPluginEntry>
            {
                new() { FileName = "Test.esp", OriginalPath = @"C:\Data\Test.esp", FileSizeBytes = 1024 }
            }
        };

        // Act
        await _sut.WriteSessionMetadataAsync(sessionDir, session);

        // Assert
        var metadataPath = Path.Combine(sessionDir, "session.json");
        File.Exists(metadataPath).Should().BeTrue("session.json should be written");

        var json = await File.ReadAllTextAsync(metadataPath);
        var deserialized = JsonSerializer.Deserialize<BackupSession>(json);
        deserialized.Should().NotBeNull();
        deserialized!.GameType.Should().Be("SSE");
        deserialized.Plugins.Should().HaveCount(1);
        deserialized.Plugins[0].FileName.Should().Be("Test.esp");
    }

    #endregion

    #region GetBackupSessionsAsync

    [Fact]
    public async Task GetBackupSessionsAsync_ReturnsSessionsNewestFirst()
    {
        // Arrange
        var backupRoot = Path.Combine(_testRoot, "sessions_order");
        var olderDir = Path.Combine(backupRoot, "2026-01-01_10-00-00");
        var newerDir = Path.Combine(backupRoot, "2026-02-07_10-00-00");
        Directory.CreateDirectory(olderDir);
        Directory.CreateDirectory(newerDir);

        var olderSession = new BackupSession { Timestamp = new DateTime(2026, 1, 1), GameType = "SSE" };
        var newerSession = new BackupSession { Timestamp = new DateTime(2026, 2, 7), GameType = "FO4" };

        await WriteSessionJson(olderDir, olderSession);
        await WriteSessionJson(newerDir, newerSession);

        // Act
        var result = await _sut.GetBackupSessionsAsync(backupRoot);

        // Assert
        result.Should().HaveCount(2);
        result[0].GameType.Should().Be("FO4", "newest session should be first");
        result[1].GameType.Should().Be("SSE", "oldest session should be last");
    }

    [Fact]
    public async Task GetBackupSessionsAsync_SkipsDirectoriesWithoutMetadata()
    {
        // Arrange
        var backupRoot = Path.Combine(_testRoot, "sessions_skip");
        var withMeta = Path.Combine(backupRoot, "2026-02-07_10-00-00");
        var withoutMeta = Path.Combine(backupRoot, "2026-02-06_10-00-00");
        Directory.CreateDirectory(withMeta);
        Directory.CreateDirectory(withoutMeta);

        await WriteSessionJson(withMeta, new BackupSession { GameType = "SSE" });
        // withoutMeta has no session.json

        // Act
        var result = await _sut.GetBackupSessionsAsync(backupRoot);

        // Assert
        result.Should().HaveCount(1, "only directory with session.json should be returned");
        result[0].GameType.Should().Be("SSE");
    }

    [Fact]
    public async Task GetBackupSessionsAsync_NonexistentRoot_ReturnsEmpty()
    {
        // Act
        var result = await _sut.GetBackupSessionsAsync(Path.Combine(_testRoot, "does_not_exist"));

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region RestorePlugin

    [Fact]
    public void RestorePlugin_CopiesBackToOriginalPath()
    {
        // Arrange
        var sessionDir = Path.Combine(_testRoot, "restore_session");
        Directory.CreateDirectory(sessionDir);
        var backupFile = Path.Combine(sessionDir, "Restored.esp");
        File.WriteAllText(backupFile, "backup content");

        var restorePath = Path.Combine(_testRoot, "restore_target", "Restored.esp");
        var entry = new BackupPluginEntry
        {
            FileName = "Restored.esp",
            OriginalPath = restorePath,
            FileSizeBytes = 14
        };

        // Act
        _sut.RestorePlugin(entry, sessionDir);

        // Assert
        File.Exists(restorePath).Should().BeTrue("file should be restored to original path");
        File.ReadAllText(restorePath).Should().Be("backup content");
    }

    [Fact]
    public void RestorePlugin_MissingBackupFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var sessionDir = Path.Combine(_testRoot, "restore_missing");
        Directory.CreateDirectory(sessionDir);
        var entry = new BackupPluginEntry
        {
            FileName = "Missing.esp",
            OriginalPath = Path.Combine(_testRoot, "target", "Missing.esp")
        };

        // Act
        var act = () => _sut.RestorePlugin(entry, sessionDir);

        // Assert
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public async Task RestoreSessionAsync_ContinuesAfterPluginFailure()
    {
        // Arrange
        var sessionDir = Path.Combine(_testRoot, "restore_partial_session");
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "Good.esp"), "restored good content");

        var goodTarget = Path.Combine(_testRoot, "restore_partial_targets", "Good.esp");
        var missingTarget = Path.Combine(_testRoot, "restore_partial_targets", "Missing.esp");
        var session = new BackupSession
        {
            SessionDirectory = sessionDir,
            Plugins = new List<BackupPluginEntry>
            {
                new() { FileName = "Missing.esp", OriginalPath = missingTarget, FileSizeBytes = 128 },
                new() { FileName = "Good.esp", OriginalPath = goodTarget, FileSizeBytes = 21 }
            }
        };

        // Act
        var result = await _sut.RestoreSessionAsync(session);

        // Assert
        result.Status.Should().Be(BackupOperationStatus.Partial);
        result.RestoredCount.Should().Be(1);
        result.FailedCount.Should().Be(1);
        result.Rows.Should().Contain(row =>
            row.FileName == "Missing.esp" &&
            row.Status == BackupRestoreRowStatus.Failed &&
            row.DisplayReason == "Missing backup file");
        File.ReadAllText(goodTarget).Should().Be("restored good content",
            "Restore All must continue after an earlier plugin failure");
    }

    /// <summary>
    /// Verifies that structured restore recreates a missing target directory before copying the backup.
    /// </summary>
    [Fact]
    public async Task RestorePluginAsync_MissingTargetDirectory_RecreatesDirectory()
    {
        // Arrange
        var sessionDir = Path.Combine(_testRoot, "restore_missing_target_dir_session");
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "Recreate.esp"), "backup content");

        var targetPath = Path.Combine(_testRoot, "missing_target_dir", "nested", "Recreate.esp");
        var entry = new BackupPluginEntry
        {
            FileName = "Recreate.esp",
            OriginalPath = targetPath,
            FileSizeBytes = 14
        };

        // Act
        var result = await _sut.RestorePluginAsync(entry, sessionDir);

        // Assert
        result.Status.Should().Be(BackupOperationStatus.Complete);
        Directory.Exists(Path.GetDirectoryName(targetPath)!).Should().BeTrue();
        File.ReadAllText(targetPath).Should().Be("backup content");
    }

    [Fact]
    public async Task RestorePluginAsync_CanceledAtomicRestore_PreservesExistingTarget()
    {
        // Arrange
        var sessionDir = Path.Combine(_testRoot, "restore_canceled_session");
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "Existing.esp"), "backup replacement content");

        var targetPath = Path.Combine(_testRoot, "restore_canceled_target", "Existing.esp");
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllText(targetPath, "current plugin content");

        var copier = new CapturingBackupFileCopier(BackupCopyResult.Canceled(
            Path.Combine(sessionDir, "Existing.esp"),
            targetPath,
            bytesCopied: 5,
            totalBytes: 26));
        var sut = new BackupService(copier, _mockLogger);
        var entry = new BackupPluginEntry
        {
            FileName = "Existing.esp",
            OriginalPath = targetPath,
            FileSizeBytes = 26
        };

        // Act
        var result = await sut.RestorePluginAsync(entry, sessionDir);

        // Assert
        result.Status.Should().Be(BackupOperationStatus.Canceled);
        result.CanceledCount.Should().Be(1);
        copier.Options.Should().Be(BackupCopyOptions.AtomicReplace,
            "restore must copy through the atomic replacement policy so existing plugins survive cancellation");
        File.ReadAllText(targetPath).Should().Be("current plugin content",
            "a canceled restore overwrite must not truncate or remove the existing plugin file");
    }

    [Fact]
    public async Task RestorePluginAsync_TargetFolderCreationFailure_ReturnsFailedRow()
    {
        // Arrange
        var sessionDir = Path.Combine(_testRoot, "restore_bad_target_session");
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "BadTarget.esp"), "backup content");

        var entry = new BackupPluginEntry
        {
            FileName = "BadTarget.esp",
            OriginalPath = Path.Combine(_testRoot, "bad\0folder", "BadTarget.esp"),
            FileSizeBytes = 14
        };

        // Act
        var result = await _sut.RestorePluginAsync(entry, sessionDir);

        // Assert
        result.Status.Should().Be(BackupOperationStatus.Failed);
        result.Rows.Single().DisplayReason.Should().Be("Target folder creation failed");
    }

    /// <summary>
    /// Verifies restore treats metadata file names as untrusted and rejects traversal before invoking the copier.
    /// </summary>
    [Fact]
    public async Task RestorePluginAsync_FileNameTraversal_ReturnsMissingBackupFileAndDoesNotCopy()
    {
        // Arrange
        var sessionDir = Path.Combine(_testRoot, "restore_traversal_session");
        Directory.CreateDirectory(sessionDir);
        var outsideFile = Path.Combine(_testRoot, "outside.esp");
        File.WriteAllText(outsideFile, "outside backup content");
        var targetPath = Path.Combine(_testRoot, "restore_traversal_target", "Plugin.esp");
        var copier = new CountingBackupFileCopier(BackupCopyResult.Complete(outsideFile, targetPath, 22, 22));
        var sut = new BackupService(copier, _mockLogger);
        var entry = new BackupPluginEntry
        {
            FileName = "..\\outside.esp",
            OriginalPath = targetPath,
            FileSizeBytes = 22
        };

        // Act
        var result = await sut.RestorePluginAsync(entry, sessionDir);

        // Assert
        result.Status.Should().Be(BackupOperationStatus.Failed);
        result.Rows.Single().DisplayReason.Should().Be("Missing backup file");
        copier.CallCount.Should().Be(0, "unsafe metadata must be rejected before copying");
        File.Exists(targetPath).Should().BeFalse("traversal metadata must not restore into the target path");
    }

    /// <summary>
    /// Verifies restore rejects absolute metadata file names before they can escape the selected session directory.
    /// </summary>
    [Fact]
    public async Task RestorePluginAsync_AbsoluteFileName_ReturnsMissingBackupFileAndDoesNotCopy()
    {
        // Arrange
        var sessionDir = Path.Combine(_testRoot, "restore_absolute_session");
        Directory.CreateDirectory(sessionDir);
        var absoluteBackup = Path.Combine(_testRoot, "absolute.esp");
        File.WriteAllText(absoluteBackup, "absolute backup content");
        var targetPath = Path.Combine(_testRoot, "restore_absolute_target", "Plugin.esp");
        var copier = new CountingBackupFileCopier(BackupCopyResult.Complete(absoluteBackup, targetPath, 23, 23));
        var sut = new BackupService(copier, _mockLogger);
        var entry = new BackupPluginEntry
        {
            FileName = absoluteBackup,
            OriginalPath = targetPath,
            FileSizeBytes = 23
        };

        // Act
        var result = await sut.RestorePluginAsync(entry, sessionDir);

        // Assert
        result.Status.Should().Be(BackupOperationStatus.Failed);
        result.Rows.Single().DisplayReason.Should().Be("Missing backup file");
        copier.CallCount.Should().Be(0, "absolute backup metadata must not reach the copier");
        File.Exists(targetPath).Should().BeFalse("absolute metadata must not restore into the target path");
    }

    /// <summary>
    /// Verifies restore rejects unrooted original targets because they cannot be proven safe overwrite destinations.
    /// </summary>
    [Fact]
    public async Task RestorePluginAsync_UnrootedOriginalPath_ReturnsTargetFolderCreationFailed()
    {
        // Arrange
        var sessionDir = Path.Combine(_testRoot, "restore_unrooted_target_session");
        Directory.CreateDirectory(sessionDir);
        var backupPath = Path.Combine(sessionDir, "Relative.esp");
        File.WriteAllText(backupPath, "backup content");
        var copier = new CountingBackupFileCopier(BackupCopyResult.Complete(backupPath, "relative.esp", 14, 14));
        var sut = new BackupService(copier, _mockLogger);
        var entry = new BackupPluginEntry
        {
            FileName = "Relative.esp",
            OriginalPath = "relative.esp",
            FileSizeBytes = 14
        };

        // Act
        var result = await sut.RestorePluginAsync(entry, sessionDir);

        // Assert
        result.Status.Should().Be(BackupOperationStatus.Failed);
        result.Rows.Single().DisplayReason.Should().Be("Target folder creation failed");
        copier.CallCount.Should().Be(0, "unsafe target metadata must be rejected before copying");
    }

    /// <summary>
    /// Verifies restore maps copier access-denied failures to the approved concise row label.
    /// </summary>
    [Fact]
    public async Task RestorePluginAsync_AccessDeniedCopyFailure_ReturnsAccessDenied()
    {
        // Arrange
        var sessionDir = Path.Combine(_testRoot, "restore_access_denied_session");
        Directory.CreateDirectory(sessionDir);
        var backupPath = Path.Combine(sessionDir, "Denied.esp");
        File.WriteAllText(backupPath, "backup content");
        var targetPath = Path.Combine(_testRoot, "restore_access_denied_target", "Denied.esp");
        var copier = new CapturingBackupFileCopier(BackupCopyResult.Failed(
            backupPath,
            targetPath,
            BackupFailureReason.AccessDenied));
        var sut = new BackupService(copier, _mockLogger);
        var entry = new BackupPluginEntry
        {
            FileName = "Denied.esp",
            OriginalPath = targetPath,
            FileSizeBytes = 14
        };

        // Act
        var result = await sut.RestorePluginAsync(entry, sessionDir);

        // Assert
        result.Status.Should().Be(BackupOperationStatus.Failed);
        result.Rows.Single().DisplayReason.Should().Be("Access denied");
    }

    #endregion

    #region CleanupOldSessions

    [Fact]
    public void CleanupOldSessions_DeletesOldestBeyondMax()
    {
        // Arrange -- 5 session directories (named so alphabetical = chronological order)
        var backupRoot = Path.Combine(_testRoot, "cleanup_max");
        var dirs = new[]
        {
            "2026-01-01_10-00-00",
            "2026-01-02_10-00-00",
            "2026-01-03_10-00-00",
            "2026-01-04_10-00-00",
            "2026-01-05_10-00-00"
        };
        foreach (var d in dirs)
            Directory.CreateDirectory(Path.Combine(backupRoot, d));

        // Act
        _sut.CleanupOldSessions(backupRoot, maxSessionCount: 3);

        // Assert
        var remaining = Directory.GetDirectories(backupRoot).Select(Path.GetFileName).OrderBy(n => n).ToList();
        remaining.Should().HaveCount(3);
        remaining.Should().Contain("2026-01-05_10-00-00", "newest should be kept");
        remaining.Should().Contain("2026-01-04_10-00-00");
        remaining.Should().Contain("2026-01-03_10-00-00");
        remaining.Should().NotContain("2026-01-01_10-00-00", "oldest should be deleted");
        remaining.Should().NotContain("2026-01-02_10-00-00", "second oldest should be deleted");
    }

    [Fact]
    public void CleanupOldSessions_ProtectsCurrentSession()
    {
        // Arrange -- 3 session directories
        var backupRoot = Path.Combine(_testRoot, "cleanup_protect");
        var oldest = Path.Combine(backupRoot, "2026-01-01_10-00-00");
        var middle = Path.Combine(backupRoot, "2026-01-02_10-00-00");
        var newest = Path.Combine(backupRoot, "2026-01-03_10-00-00");
        Directory.CreateDirectory(oldest);
        Directory.CreateDirectory(middle);
        Directory.CreateDirectory(newest);

        // Act -- keep only 1, but protect the oldest (current session)
        _sut.CleanupOldSessions(backupRoot, maxSessionCount: 1, currentSessionDir: oldest);

        // Assert
        Directory.Exists(oldest).Should().BeTrue("current session is always protected");
        Directory.Exists(newest).Should().BeTrue("newest session is kept within maxSessionCount");
        Directory.Exists(middle).Should().BeFalse("middle session should be deleted (exceeds max and not protected)");
    }

    [Fact]
    public void CleanupOldSessions_NonexistentRoot_DoesNotThrow()
    {
        // Act
        var act = () => _sut.CleanupOldSessions(Path.Combine(_testRoot, "nonexistent"), maxSessionCount: 3);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public async Task CleanupOldSessionsAsync_ProtectsCurrentSession()
    {
        // Arrange
        var backupRoot = Path.Combine(_testRoot, "cleanup_async_current");
        var current = await CreateSessionDirectoryWithMetadata(backupRoot, "2026-01-01_10-00-00");
        var newer = await CreateSessionDirectoryWithMetadata(backupRoot, "2026-01-03_10-00-00");
        var deleter = new RecordingBackupSessionDeleter();
        var sut = CreateBackupService(deleter);

        // Act
        var result = await sut.CleanupOldSessionsAsync(backupRoot, maxSessionCount: 0, currentSessionDir: current);

        // Assert
        result.Status.Should().Be(BackupOperationStatus.Complete);
        deleter.DeletedDirectories.Should().NotContain(current, "the current session must never be deleted");
        deleter.DeletedDirectories.Should().Contain(newer, "maxSessionCount 0 keeps no non-current valid sessions");
        result.Rows.Should().Contain(row => row.SessionDirectory == current && row.Status == BackupRetentionRowStatus.Kept);
    }

    [Fact]
    public async Task CleanupOldSessionsAsync_KeepsNewestMaxSessions()
    {
        // Arrange
        var backupRoot = Path.Combine(_testRoot, "cleanup_async_newest");
        var oldest = await CreateSessionDirectoryWithMetadata(backupRoot, "2026-01-01_10-00-00");
        var middle = await CreateSessionDirectoryWithMetadata(backupRoot, "2026-01-02_10-00-00");
        var newest = await CreateSessionDirectoryWithMetadata(backupRoot, "2026-01-03_10-00-00");
        var deleter = new RecordingBackupSessionDeleter();
        var sut = CreateBackupService(deleter);

        // Act
        var result = await sut.CleanupOldSessionsAsync(backupRoot, maxSessionCount: 2);

        // Assert
        result.Status.Should().Be(BackupOperationStatus.Complete);
        deleter.DeletedDirectories.Should().Equal(oldest);
        result.Rows.Should().Contain(row => row.SessionDirectory == newest && row.Status == BackupRetentionRowStatus.Kept);
        result.Rows.Should().Contain(row => row.SessionDirectory == middle && row.Status == BackupRetentionRowStatus.Kept);
    }

    [Fact]
    public async Task CleanupOldSessionsAsync_SkipsMalformedSessionDirectory()
    {
        // Arrange
        var backupRoot = Path.Combine(_testRoot, "cleanup_async_malformed");
        var malformed = Path.Combine(backupRoot, "not-a-session");
        Directory.CreateDirectory(malformed);
        var valid = await CreateSessionDirectoryWithMetadata(backupRoot, "2026-01-01_10-00-00");
        var deleter = new RecordingBackupSessionDeleter();
        var sut = CreateBackupService(deleter);

        // Act
        var result = await sut.CleanupOldSessionsAsync(backupRoot, maxSessionCount: 0);

        // Assert
        result.Status.Should().Be(BackupOperationStatus.Complete);
        deleter.DeletedDirectories.Should().Contain(valid);
        deleter.DeletedDirectories.Should().NotContain(malformed, "malformed directories are not valid cleanup candidates");
        result.Rows.Should().Contain(row => row.SessionDirectory == malformed && row.Status == BackupRetentionRowStatus.Kept);
    }

    [Fact]
    public async Task CleanupOldSessionsAsync_RetryDelayCancellation_ReturnsCanceled()
    {
        // Arrange
        var backupRoot = Path.Combine(_testRoot, "cleanup_async_retry_cancel");
        await CreateSessionDirectoryWithMetadata(backupRoot, "2026-01-01_10-00-00");
        var deleter = new RecordingBackupSessionDeleter(_ => throw new IOException("locked"));
        var sut = CreateBackupService(deleter);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        // Act
        var result = await sut.CleanupOldSessionsAsync(backupRoot, maxSessionCount: 0, ct: cts.Token);

        // Assert
        result.Status.Should().Be(BackupOperationStatus.Canceled);
        deleter.Attempts.Should().Be(1, "cancellation during the retry delay should stop before retrying deletion");
        result.RemainingCount.Should().Be(1);
    }

    [Fact]
    public async Task CleanupOldSessionsAsync_ReportsCanceled()
    {
        // Arrange
        var backupRoot = Path.Combine(_testRoot, "cleanup_async_canceled");
        await CreateSessionDirectoryWithMetadata(backupRoot, "2026-01-01_10-00-00");
        await CreateSessionDirectoryWithMetadata(backupRoot, "2026-01-02_10-00-00");
        var deleter = new RecordingBackupSessionDeleter();
        var sut = CreateBackupService(deleter);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await sut.CleanupOldSessionsAsync(backupRoot, maxSessionCount: 0, ct: cts.Token);

        // Assert
        result.Status.Should().Be(BackupOperationStatus.Canceled);
        result.DeletedCount.Should().Be(0);
        result.RemainingCount.Should().Be(2);
        deleter.DeletedDirectories.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies retention cleanup reports a warning row when deletion fails once and then fails again after retry.
    /// </summary>
    [Fact]
    public async Task CleanupOldSessionsAsync_DeletionFailureAfterRetry_ReturnsWarning()
    {
        // Arrange
        var backupRoot = Path.Combine(_testRoot, "cleanup_async_deletion_warning");
        var oldSession = await CreateSessionDirectoryWithMetadata(backupRoot, "2026-01-01_10-00-00");
        var deleter = new RecordingBackupSessionDeleter(_ => throw new IOException("locked"));
        var sut = CreateBackupService(deleter);

        // Act
        var result = await sut.CleanupOldSessionsAsync(backupRoot, maxSessionCount: 0);

        // Assert
        result.Status.Should().Be(BackupOperationStatus.Warning);
        deleter.Attempts.Should().Be(2, "deletion should be retried once after a transient failure");
        result.Rows.Should().ContainSingle(row =>
            row.SessionDirectory == oldSession &&
            row.Status == BackupRetentionRowStatus.Failed &&
            row.DisplayReason == "Cleanup deletion failed");
    }

    /// <summary>
    /// Verifies retention cleanup emits advancing count progress rather than only a static active operation.
    /// </summary>
    [Fact]
    public async Task CleanupOldSessionsAsync_ReportsRetentionCountProgress()
    {
        // Arrange
        var backupRoot = Path.Combine(_testRoot, "cleanup_async_progress");
        await CreateSessionDirectoryWithMetadata(backupRoot, "2026-01-01_10-00-00");
        await CreateSessionDirectoryWithMetadata(backupRoot, "2026-01-02_10-00-00");
        var progressUpdates = new List<BackupCopyProgress>();

        // Act
        await _sut.CleanupOldSessionsAsync(
            backupRoot,
            maxSessionCount: 1,
            progress: new Progress<BackupCopyProgress>(progressUpdates.Add));

        // Assert
        progressUpdates.Should().NotBeEmpty("retention cleanup must publish count progress for the UI");
        progressUpdates.Should().Contain(update => update.TotalFiles == 2 && update.FilesCompleted > 0);
        progressUpdates[^1].FilesCompleted.Should().Be(2);
        progressUpdates[^1].TotalFiles.Should().Be(2);
    }

    #endregion

    #region GetBackupRoot

    [Fact]
    public void GetBackupRoot_ReturnsParentSiblingDirectory()
    {
        // Act
        var result = _sut.GetBackupRoot(@"C:\Games\Skyrim\Data");

        // Assert
        result.Should().Be(@"C:\Games\Skyrim\AutoQAC Backups");
    }

    [Fact]
    public void GetBackupRoot_RootDrive_FallsBackToDataFolder()
    {
        // When the data folder path has no parent (e.g., root drive), use fallback
        var result = _sut.GetBackupRoot(@"C:\");

        // Assert -- GetDirectoryName("C:\") returns null on some platforms,
        // so the fallback uses the input path itself
        result.Should().Contain("AutoQAC Backups");
    }

    #endregion

    #region Helpers

    private static async Task WriteSessionJson(string dir, BackupSession session)
    {
        var path = Path.Combine(dir, "session.json");
        var json = JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }

    private BackupService CreateBackupService(IBackupSessionDeleter deleter) =>
        new(new BackupFileCopier(_mockLogger), _mockLogger, deleter);

    private static async Task<string> CreateSessionDirectoryWithMetadata(string backupRoot, string directoryName)
    {
        var dir = Path.Combine(backupRoot, directoryName);
        Directory.CreateDirectory(dir);
        await WriteSessionJson(dir, new BackupSession
        {
            Timestamp = DateTime.ParseExact(directoryName, "yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture),
            GameType = "SSE"
        });
        return dir;
    }

    private sealed class CapturingBackupFileCopier(BackupCopyResult result) : IBackupFileCopier
    {
        public BackupCopyOptions? Options { get; private set; }

        /// <inheritdoc />
        public Task<BackupCopyResult> CopyAsync(
            string sourcePath,
            string destinationPath,
            BackupCopyOptions options,
            IProgress<BackupCopyProgress>? progress,
            CancellationToken cancellationToken)
        {
            Options = options;
            return Task.FromResult(result);
        }
    }

    private sealed class CountingBackupFileCopier(BackupCopyResult result) : IBackupFileCopier
    {
        public int CallCount { get; private set; }

        /// <inheritdoc />
        public Task<BackupCopyResult> CopyAsync(
            string sourcePath,
            string destinationPath,
            BackupCopyOptions options,
            IProgress<BackupCopyProgress>? progress,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingBackupSessionDeleter(Action<string>? onDelete = null) : IBackupSessionDeleter
    {
        private readonly Action<string>? _onDelete = onDelete;

        public List<string> DeletedDirectories { get; } = [];

        public int Attempts { get; private set; }

        /// <inheritdoc />
        public Task DeleteAsync(string sessionDirectory, CancellationToken ct)
        {
            Attempts++;
            ct.ThrowIfCancellationRequested();
            _onDelete?.Invoke(sessionDirectory);
            DeletedDirectories.Add(sessionDirectory);
            return Task.CompletedTask;
        }
    }

    #endregion
}
