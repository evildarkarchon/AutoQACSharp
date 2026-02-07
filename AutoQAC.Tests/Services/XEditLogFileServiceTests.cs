using AutoQAC.Infrastructure.Logging;
using AutoQAC.Services.Cleaning;
using FluentAssertions;
using Moq;

namespace AutoQAC.Tests.Services;

/// <summary>
/// Unit tests for <see cref="XEditLogFileService"/> covering log path computation,
/// stale log detection, missing log files, successful reads, and cancellation.
///
/// Uses temp directories for file system isolation -- XEditLogFileService accepts
/// paths via method parameters (injectable), making this standard C# unit test practice.
/// </summary>
public sealed class XEditLogFileServiceTests : IDisposable
{
    private readonly Mock<ILoggingService> _mockLogger;
    private readonly XEditLogFileService _sut;
    private readonly string _testRoot;

    public XEditLogFileServiceTests()
    {
        _mockLogger = new Mock<ILoggingService>();
        _sut = new XEditLogFileService(_mockLogger.Object);
        _testRoot = Path.Combine(Path.GetTempPath(), $"autoqac_xeditlog_test_{Guid.NewGuid():N}");
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

    #region GetLogFilePath

    [Fact]
    public void GetLogFilePath_ReturnsCorrectPath()
    {
        // Act
        var result = _sut.GetLogFilePath(@"C:\xEdit\SSEEdit.exe");

        // Assert
        result.Should().Be(@"C:\xEdit\SSEEDIT_log.txt");
    }

    [Fact]
    public void GetLogFilePath_HandlesLowerCase()
    {
        // Act
        var result = _sut.GetLogFilePath(@"C:\tools\xedit.exe");

        // Assert
        result.Should().Be(@"C:\tools\XEDIT_log.txt");
    }

    [Fact]
    public void GetLogFilePath_InvalidPath_Throws()
    {
        // Act & Assert -- empty string causes GetDirectoryName to return ""
        // which is not null, but the stem will be empty.
        // The actual guard in the implementation throws on null directory.
        // A truly null-directory path is hard to construct, so test the typical
        // pathological case: a bare filename with no directory.
        var act = () => _sut.GetLogFilePath("");

        act.Should().Throw<ArgumentException>("empty path cannot determine directory");
    }

    #endregion

    #region ReadLogFileAsync

    [Fact]
    public async Task ReadLogFileAsync_FileNotFound_ReturnsError()
    {
        // Arrange -- xEdit exe path in temp dir, but no log file exists
        var xEditPath = Path.Combine(_testRoot, "SSEEdit.exe");

        // Act
        var (lines, error) = await _sut.ReadLogFileAsync(xEditPath, DateTime.UtcNow);

        // Assert
        lines.Should().BeEmpty();
        error.Should().Contain("not found");
    }

    [Fact]
    public async Task ReadLogFileAsync_StaleLogFile_ReturnsError()
    {
        // Arrange -- create a log file that predates the process start
        var xEditPath = Path.Combine(_testRoot, "SSEEdit.exe");
        var logPath = Path.Combine(_testRoot, "SSEEDIT_log.txt");
        File.WriteAllText(logPath, "old log content");
        File.SetLastWriteTimeUtc(logPath, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var processStart = new DateTime(2026, 2, 7, 10, 0, 0, DateTimeKind.Utc);

        // Act
        var (lines, error) = await _sut.ReadLogFileAsync(xEditPath, processStart);

        // Assert
        lines.Should().BeEmpty();
        error.Should().Contain("stale");
    }

    [Fact]
    public async Task ReadLogFileAsync_FreshLogFile_ReturnsLines()
    {
        // Arrange -- create a log file that is newer than process start
        var xEditPath = Path.Combine(_testRoot, "SSEEdit.exe");
        var logPath = Path.Combine(_testRoot, "SSEEDIT_log.txt");
        var logContent = "Line 1\nLine 2\nLine 3";
        File.WriteAllText(logPath, logContent);

        // Set the log file modification time to now (fresher than process start)
        var processStart = DateTime.UtcNow.AddMinutes(-5);
        File.SetLastWriteTimeUtc(logPath, DateTime.UtcNow);

        // Act
        var (lines, error) = await _sut.ReadLogFileAsync(xEditPath, processStart);

        // Assert
        error.Should().BeNull("fresh log file should be read successfully");
        lines.Should().HaveCount(3);
        lines[0].Should().Be("Line 1");
        lines[1].Should().Be("Line 2");
        lines[2].Should().Be("Line 3");
    }

    [Fact]
    public async Task ReadLogFileAsync_Cancellation_ThrowsOperationCancelled()
    {
        // Arrange
        var xEditPath = Path.Combine(_testRoot, "SSEEdit.exe");
        var logPath = Path.Combine(_testRoot, "SSEEDIT_log.txt");
        File.WriteAllText(logPath, "some content");
        File.SetLastWriteTimeUtc(logPath, DateTime.UtcNow);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Already cancelled

        // Act
        var act = async () => await _sut.ReadLogFileAsync(
            xEditPath, DateTime.UtcNow.AddMinutes(-1), cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion
}
