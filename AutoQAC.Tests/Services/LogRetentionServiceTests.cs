using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.Configuration;
using FluentAssertions;
using Moq;

namespace AutoQAC.Tests.Services;

/// <summary>
/// Unit tests for <see cref="LogRetentionService"/> covering age-based and count-based
/// retention modes, active log file protection, empty directories, and config errors.
///
/// LogRetentionService uses a hardcoded "logs" directory path relative to CWD.
/// Tests create a "logs" subdirectory in a unique temp working directory,
/// set CWD there, run the test, then restore CWD and clean up.
/// </summary>
public sealed class LogRetentionServiceTests : IDisposable
{
    private readonly Mock<IConfigurationService> _mockConfig;
    private readonly Mock<ILoggingService> _mockLogger;
    private readonly LogRetentionService _sut;
    private readonly string _testRoot;
    private readonly string _originalCwd;

    public LogRetentionServiceTests()
    {
        _mockConfig = new Mock<IConfigurationService>();
        _mockLogger = new Mock<ILoggingService>();
        _sut = new LogRetentionService(_mockConfig.Object, _mockLogger.Object);

        _testRoot = Path.Combine(Path.GetTempPath(), $"autoqac_logretention_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);

        // Save and change CWD so the hardcoded "logs" resolves under our temp dir
        _originalCwd = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_testRoot);
    }

    public void Dispose()
    {
        // Restore original CWD before deleting
        Directory.SetCurrentDirectory(_originalCwd);

        if (Directory.Exists(_testRoot))
        {
            try { Directory.Delete(_testRoot, recursive: true); }
            catch { /* Best-effort cleanup */ }
        }
    }

    private string LogsDir => Path.Combine(_testRoot, "logs");

    private void SetupConfig(RetentionMode mode, int maxAgeDays = 30, int maxFileCount = 50)
    {
        var userConfig = new UserConfiguration
        {
            LogRetention = new RetentionSettings
            {
                Mode = mode,
                MaxAgeDays = maxAgeDays,
                MaxFileCount = maxFileCount
            }
        };
        _mockConfig.Setup(c => c.LoadUserConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(userConfig);
    }

    private string CreateLogFile(string name, DateTime lastWriteUtc)
    {
        Directory.CreateDirectory(LogsDir);
        var path = Path.Combine(LogsDir, name);
        File.WriteAllText(path, $"log content for {name}");
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }

    #region No Log Directory

    [Fact]
    public async Task CleanupAsync_NoLogDirectory_DoesNothing()
    {
        // Arrange -- ensure no "logs" directory exists under CWD
        // (CWD is set to _testRoot by constructor; explicitly remove logs if any race created it)
        var logsPath = Path.Combine(_testRoot, "logs");
        if (Directory.Exists(logsPath))
            Directory.Delete(logsPath, recursive: true);

        SetupConfig(RetentionMode.AgeBased);

        // Act -- should complete without error
        var act = async () => await _sut.CleanupAsync();

        // Assert -- no exception, early return before any file operations
        await act.Should().NotThrowAsync("should return early when log directory doesn't exist");
        Directory.Exists(logsPath).Should().BeFalse("logs directory should not be created by cleanup");
    }

    #endregion

    #region Age-Based Retention

    [Fact]
    public async Task CleanupAsync_AgeBased_DeletesOldFiles()
    {
        // Arrange
        SetupConfig(RetentionMode.AgeBased, maxAgeDays: 7);

        var now = DateTime.UtcNow;
        // Recent file (1 day old) -- should be kept
        var recent = CreateLogFile("autoqac-recent.log", now.AddDays(-1));
        // Old files (10+ days old) -- should be deleted
        var old1 = CreateLogFile("autoqac-old1.log", now.AddDays(-10));
        var old2 = CreateLogFile("autoqac-old2.log", now.AddDays(-15));
        var old3 = CreateLogFile("autoqac-old3.log", now.AddDays(-20));
        // The newest file is "recent" and will be skipped as active Serilog log

        // Act
        await _sut.CleanupAsync();

        // Assert
        File.Exists(recent).Should().BeTrue("newest file (active Serilog log) is always kept");
        File.Exists(old1).Should().BeFalse("file older than 7 days should be deleted");
        File.Exists(old2).Should().BeFalse("file older than 7 days should be deleted");
        File.Exists(old3).Should().BeFalse("file older than 7 days should be deleted");
    }

    #endregion

    #region Count-Based Retention

    [Fact]
    public async Task CleanupAsync_CountBased_KeepsMaxCount()
    {
        // Arrange
        SetupConfig(RetentionMode.CountBased, maxFileCount: 3);

        var now = DateTime.UtcNow;
        // Create 6 files with varying dates (newest first in naming for clarity)
        CreateLogFile("autoqac-f1.log", now);           // #1 newest (active, always kept)
        CreateLogFile("autoqac-f2.log", now.AddHours(-1)); // #2 kept (within count)
        CreateLogFile("autoqac-f3.log", now.AddHours(-2)); // #3 kept (within count)
        CreateLogFile("autoqac-f4.log", now.AddHours(-3)); // #4 should be deleted
        CreateLogFile("autoqac-f5.log", now.AddHours(-4)); // #5 should be deleted
        CreateLogFile("autoqac-f6.log", now.AddHours(-5)); // #6 should be deleted

        // Act
        await _sut.CleanupAsync();

        // Assert
        var remaining = Directory.GetFiles(LogsDir, "autoqac-*.log");
        remaining.Should().HaveCount(3, "MaxFileCount=3 means keep 3 total files");
    }

    #endregion

    #region Active File Protection

    [Fact]
    public async Task CleanupAsync_AlwaysKeepsNewestFile()
    {
        // Arrange -- 2 files, both older than retention cutoff
        SetupConfig(RetentionMode.AgeBased, maxAgeDays: 1);

        var now = DateTime.UtcNow;
        var newer = CreateLogFile("autoqac-newer.log", now.AddDays(-5));
        var older = CreateLogFile("autoqac-older.log", now.AddDays(-10));

        // Act
        await _sut.CleanupAsync();

        // Assert
        File.Exists(newer).Should().BeTrue("newest file is always kept as active Serilog log, even if old");
        File.Exists(older).Should().BeFalse("non-active old file should be deleted");
    }

    #endregion

    #region Single File

    [Fact]
    public async Task CleanupAsync_SingleFile_DoesNothing()
    {
        // Arrange
        SetupConfig(RetentionMode.AgeBased, maxAgeDays: 1);
        CreateLogFile("autoqac-only.log", DateTime.UtcNow.AddDays(-30));

        // Act
        await _sut.CleanupAsync();

        // Assert
        var files = Directory.GetFiles(LogsDir, "autoqac-*.log");
        files.Should().HaveCount(1, "single file should always be kept (count <= 1 early return)");
    }

    #endregion

    #region Config Error

    [Fact]
    public async Task CleanupAsync_ConfigError_LogsWarningAndDoesNotThrow()
    {
        // Arrange -- config throws
        Directory.CreateDirectory(LogsDir);
        // Need at least 2 files to get past the early return
        File.WriteAllText(Path.Combine(LogsDir, "autoqac-a.log"), "a");
        File.WriteAllText(Path.Combine(LogsDir, "autoqac-b.log"), "b");

        _mockConfig.Setup(c => c.LoadUserConfigAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Config corrupted"));

        // Act
        var act = async () => await _sut.CleanupAsync();

        // Assert
        await act.Should().NotThrowAsync("service catches exceptions and logs warning");
        _mockLogger.Verify(
            l => l.Warning(It.IsAny<string>(), It.IsAny<object[]>()),
            Times.AtLeastOnce,
            "should log a warning about the config error");
    }

    #endregion
}
