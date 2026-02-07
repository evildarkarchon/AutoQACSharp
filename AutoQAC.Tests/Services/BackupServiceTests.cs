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

    #endregion
}
