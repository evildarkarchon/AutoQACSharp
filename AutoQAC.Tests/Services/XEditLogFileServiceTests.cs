using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
using FluentAssertions;
using NSubstitute;

namespace AutoQAC.Tests.Services;

/// <summary>
/// Comprehensive test suite for <see cref="XEditLogFileService"/> covering:
/// - Game-aware log filename resolution for all 8 game types (LOG-01, LOG-02)
/// - Exception log filename resolution (LOG-03)
/// - Offset capture for existing/missing/empty files (OFF-01, OFF-03)
/// - Offset-based reading that isolates session content (OFF-02)
/// - Truncation recovery when offset exceeds file length (OFF-02)
/// - Exception log content in LogReadResult (D-03, D-04)
/// - Retry on IOException with exponential backoff (OFF-04)
/// - Cancellation token support
///
/// Uses temp directories for file system isolation -- XEditLogFileService accepts
/// paths via method parameters (injectable), making this standard C# unit test practice.
/// </summary>
public sealed class XEditLogFileServiceTests : IDisposable
{
    private readonly ILoggingService _mockLogger;
    private readonly XEditLogFileService _sut;
    private readonly string _testRoot;

    public XEditLogFileServiceTests()
    {
        _mockLogger = Substitute.For<ILoggingService>();
        _sut = new XEditLogFileService(_mockLogger);
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

    #region GetLogFilePath -- Game-Aware Filename Mapping (LOG-01, LOG-02)

    [Theory]
    [InlineData(GameType.SkyrimLe, "TES5Edit_log.txt")]
    [InlineData(GameType.SkyrimSe, "SSEEdit_log.txt")]
    [InlineData(GameType.SkyrimVr, "TES5VREdit_log.txt")]
    [InlineData(GameType.Fallout4, "FO4Edit_log.txt")]
    [InlineData(GameType.Fallout4Vr, "FO4VREdit_log.txt")]
    [InlineData(GameType.Fallout3, "FO3Edit_log.txt")]
    [InlineData(GameType.FalloutNewVegas, "FNVEdit_log.txt")]
    [InlineData(GameType.Oblivion, "TES4Edit_log.txt")]
    public void GetLogFilePath_AllGameTypes_ReturnsCorrectFilename(GameType gameType, string expectedFilename)
    {
        // Act
        var result = _sut.GetLogFilePath(@"C:\xEdit", gameType);

        // Assert
        Path.GetFileName(result).Should().Be(expectedFilename);
    }

    [Fact]
    public void GetLogFilePath_UnknownGameType_ThrowsArgumentOutOfRangeException()
    {
        // Act
        var act = () => _sut.GetLogFilePath(@"C:\xEdit", GameType.Unknown);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GetLogFilePath_ReturnsFullPathInGivenDirectory()
    {
        // Act
        var result = _sut.GetLogFilePath(@"C:\Games\xEdit", GameType.SkyrimSe);

        // Assert
        result.Should().Be(@"C:\Games\xEdit\SSEEdit_log.txt");
        Path.GetDirectoryName(result).Should().Be(@"C:\Games\xEdit");
    }

    [Fact]
    public void GetLogFilePath_EmptyDirectory_ThrowsArgumentException()
    {
        // Act
        var act = () => _sut.GetLogFilePath("", GameType.SkyrimSe);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region GetExceptionLogFilePath (LOG-03)

    [Theory]
    [InlineData(GameType.SkyrimSe, "SSEEditException.log")]
    [InlineData(GameType.Fallout4, "FO4EditException.log")]
    [InlineData(GameType.Oblivion, "TES4EditException.log")]
    [InlineData(GameType.SkyrimLe, "TES5EditException.log")]
    [InlineData(GameType.Fallout3, "FO3EditException.log")]
    public void GetExceptionLogFilePath_MultipleGameTypes_ReturnsCorrectFilename(GameType gameType, string expectedFilename)
    {
        // Act
        var result = _sut.GetExceptionLogFilePath(@"C:\xEdit", gameType);

        // Assert
        Path.GetFileName(result).Should().Be(expectedFilename);
    }

    [Fact]
    public void GetExceptionLogFilePath_ReturnsFullPathInGivenDirectory()
    {
        // Act
        var result = _sut.GetExceptionLogFilePath(@"C:\Games\xEdit", GameType.Fallout4);

        // Assert
        result.Should().Be(@"C:\Games\xEdit\FO4EditException.log");
        Path.GetDirectoryName(result).Should().Be(@"C:\Games\xEdit");
    }

    [Fact]
    public void GetExceptionLogFilePath_EmptyDirectory_ThrowsArgumentException()
    {
        // Act
        var act = () => _sut.GetExceptionLogFilePath("", GameType.SkyrimSe);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetExceptionLogFilePath_UnknownGameType_ThrowsArgumentOutOfRangeException()
    {
        // Act
        var act = () => _sut.GetExceptionLogFilePath(@"C:\xEdit", GameType.Unknown);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    #endregion

    #region CaptureOffset (OFF-01, OFF-03)

    [Fact]
    public void CaptureOffset_FileDoesNotExist_ReturnsZero()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testRoot, "nonexistent_log.txt");

        // Act
        var offset = _sut.CaptureOffset(nonExistentPath);

        // Assert
        offset.Should().Be(0);
    }

    [Fact]
    public void CaptureOffset_FileExists_ReturnsFileLength()
    {
        // Arrange
        var logPath = Path.Combine(_testRoot, "SSEEdit_log.txt");
        var content = "Previous session content\r\nMore lines here\r\n";
        File.WriteAllText(logPath, content);
        var expectedLength = new FileInfo(logPath).Length;

        // Act
        var offset = _sut.CaptureOffset(logPath);

        // Assert
        offset.Should().Be(expectedLength);
        offset.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CaptureOffset_EmptyFile_ReturnsZero()
    {
        // Arrange
        var logPath = Path.Combine(_testRoot, "SSEEdit_log.txt");
        File.WriteAllText(logPath, "");

        // Act
        var offset = _sut.CaptureOffset(logPath);

        // Assert
        offset.Should().Be(0);
    }

    [Fact]
    public void CaptureOffset_KnownBytesFile_ReturnsExactByteCount()
    {
        // Arrange -- use WriteAllBytes for exact byte control
        var logPath = Path.Combine(_testRoot, "SSEEdit_log.txt");
        var bytes = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }; // "Hello" = 5 bytes
        File.WriteAllBytes(logPath, bytes);

        // Act
        var offset = _sut.CaptureOffset(logPath);

        // Assert
        offset.Should().Be(5);
    }

    #endregion

    #region ReadLogContentAsync -- Offset-Based Reading (OFF-02)

    [Fact]
    public async Task ReadLogContentAsync_ReadsOnlyContentAfterOffset()
    {
        // Arrange -- create log file with old + new content
        var logPath = Path.Combine(_testRoot, "SSEEdit_log.txt");
        var oldContent = "Old session line 1\r\nOld session line 2\r\n";
        File.WriteAllText(logPath, oldContent);
        var offset = new FileInfo(logPath).Length;

        // Append new content
        var newContent = "New session line 1\r\nNew session line 2\r\n";
        File.AppendAllText(logPath, newContent);

        // Act
        var result = await _sut.ReadLogContentAsync(_testRoot, GameType.SkyrimSe, offset, 0);

        // Assert
        result.LogLines.Should().HaveCount(2);
        result.LogLines[0].Should().Be("New session line 1");
        result.LogLines[1].Should().Be("New session line 2");
        result.LogLines.Should().NotContain("Old session line 1");
        result.LogLines.Should().NotContain("Old session line 2");
    }

    [Fact]
    public async Task ReadLogContentAsync_ZeroOffset_ReadsEntireFile()
    {
        // Arrange
        var logPath = Path.Combine(_testRoot, "SSEEdit_log.txt");
        File.WriteAllText(logPath, "Line 1\r\nLine 2\r\nLine 3\r\n");

        // Act
        var result = await _sut.ReadLogContentAsync(_testRoot, GameType.SkyrimSe, 0, 0);

        // Assert
        result.LogLines.Should().HaveCount(3);
        result.LogLines[0].Should().Be("Line 1");
        result.LogLines[1].Should().Be("Line 2");
        result.LogLines[2].Should().Be("Line 3");
    }

    [Fact]
    public async Task ReadLogContentAsync_FileDoesNotExist_ReturnsEmptyWithWarning()
    {
        // Arrange -- directory exists but no log file
        var xEditDir = _testRoot;

        // Act
        var result = await _sut.ReadLogContentAsync(xEditDir, GameType.SkyrimSe, 0, 0);

        // Assert
        result.LogLines.Should().BeEmpty();
        result.ExceptionContent.Should().BeNull();
        result.Warning.Should().NotBeNullOrEmpty("should warn about missing log file");
    }

    [Fact]
    public async Task ReadLogContentAsync_HandlesLineFeedOnlyNewlines()
    {
        // Arrange -- xEdit writes \r\n but verify handling of \n only
        var logPath = Path.Combine(_testRoot, "SSEEdit_log.txt");
        File.WriteAllText(logPath, "Line 1\nLine 2\nLine 3\n");

        // Act
        var result = await _sut.ReadLogContentAsync(_testRoot, GameType.SkyrimSe, 0, 0);

        // Assert
        result.LogLines.Should().HaveCount(3);
        result.LogLines[0].Should().Be("Line 1");
        result.LogLines[1].Should().Be("Line 2");
        result.LogLines[2].Should().Be("Line 3");
    }

    [Fact]
    public async Task ReadLogContentAsync_FiltersEmptyLines()
    {
        // Arrange
        var logPath = Path.Combine(_testRoot, "SSEEdit_log.txt");
        File.WriteAllText(logPath, "Line 1\r\n\r\nLine 2\r\n");

        // Act
        var result = await _sut.ReadLogContentAsync(_testRoot, GameType.SkyrimSe, 0, 0);

        // Assert
        result.LogLines.Should().HaveCount(2);
        result.LogLines[0].Should().Be("Line 1");
        result.LogLines[1].Should().Be("Line 2");
    }

    [Fact]
    public async Task ReadLogContentAsync_Fallout4_ReadsCorrectLogFile()
    {
        // Arrange -- verify game-type-aware path resolution during read
        var logPath = Path.Combine(_testRoot, "FO4Edit_log.txt");
        File.WriteAllText(logPath, "Fallout 4 log content\r\n");

        // Act
        var result = await _sut.ReadLogContentAsync(_testRoot, GameType.Fallout4, 0, 0);

        // Assert
        result.LogLines.Should().HaveCount(1);
        result.LogLines[0].Should().Be("Fallout 4 log content");
    }

    #endregion

    #region ReadLogContentAsync -- Truncation Handling (OFF-02)

    [Fact]
    public async Task ReadLogContentAsync_OffsetExceedsFileLength_ReadsEntireFile()
    {
        // Arrange -- simulate xEdit truncation: offset > file length
        var logPath = Path.Combine(_testRoot, "SSEEdit_log.txt");
        File.WriteAllText(logPath, "After truncation content\r\n");
        long staleOffset = 99999; // Way beyond actual file size

        // Act
        var result = await _sut.ReadLogContentAsync(_testRoot, GameType.SkyrimSe, staleOffset, 0);

        // Assert
        result.LogLines.Should().HaveCount(1);
        result.LogLines[0].Should().Be("After truncation content");
        result.Warning.Should().BeNull("truncation is silently recovered, not a warning");
    }

    [Fact]
    public async Task ReadLogContentAsync_ExceptionLogOffsetExceedsLength_ReadsEntireExceptionLog()
    {
        // Arrange
        var logPath = Path.Combine(_testRoot, "SSEEdit_log.txt");
        File.WriteAllText(logPath, "Main log line\r\n");

        var exceptionLogPath = Path.Combine(_testRoot, "SSEEditException.log");
        File.WriteAllText(exceptionLogPath, "Truncated exception content\r\n");

        // Act
        var result = await _sut.ReadLogContentAsync(_testRoot, GameType.SkyrimSe, 0, 99999);

        // Assert
        result.ExceptionContent.Should().Contain("Truncated exception content");
    }

    #endregion

    #region ReadLogContentAsync -- Exception Log (LOG-03, D-03, D-04)

    [Fact]
    public async Task ReadLogContentAsync_ReadsExceptionLogContent()
    {
        // Arrange
        var logPath = Path.Combine(_testRoot, "SSEEdit_log.txt");
        File.WriteAllText(logPath, "Main log line\r\n");

        var exceptionLogPath = Path.Combine(_testRoot, "SSEEditException.log");
        File.WriteAllText(exceptionLogPath, "Exception details here\r\n");

        // Act
        var result = await _sut.ReadLogContentAsync(_testRoot, GameType.SkyrimSe, 0, 0);

        // Assert
        result.LogLines.Should().HaveCount(1);
        result.ExceptionContent.Should().NotBeNull();
        result.ExceptionContent.Should().Contain("Exception details here");
    }

    [Fact]
    public async Task ReadLogContentAsync_NoExceptionLog_ReturnsNullExceptionContent()
    {
        // Arrange -- only create main log, no exception log
        var logPath = Path.Combine(_testRoot, "SSEEdit_log.txt");
        File.WriteAllText(logPath, "Main log line\r\n");

        // Act
        var result = await _sut.ReadLogContentAsync(_testRoot, GameType.SkyrimSe, 0, 0);

        // Assert
        result.ExceptionContent.Should().BeNull();
    }

    [Fact]
    public async Task ReadLogContentAsync_ExceptionLogWithOffset_ReadsOnlyNewContent()
    {
        // Arrange
        var logPath = Path.Combine(_testRoot, "SSEEdit_log.txt");
        File.WriteAllText(logPath, "Main content\r\n");

        var exceptionLogPath = Path.Combine(_testRoot, "SSEEditException.log");
        var oldExceptions = "Old exception\r\n";
        File.WriteAllText(exceptionLogPath, oldExceptions);
        var exceptionOffset = new FileInfo(exceptionLogPath).Length;

        // Append new exception
        File.AppendAllText(exceptionLogPath, "New exception after launch\r\n");

        // Act
        var result = await _sut.ReadLogContentAsync(_testRoot, GameType.SkyrimSe, 0, exceptionOffset);

        // Assert
        result.ExceptionContent.Should().Contain("New exception after launch");
        result.ExceptionContent.Should().NotContain("Old exception");
    }

    [Fact]
    public async Task ReadLogContentAsync_EmptyExceptionLogNewContent_ReturnsNullExceptionContent()
    {
        // Arrange -- exception log exists but no new content since offset
        var logPath = Path.Combine(_testRoot, "SSEEdit_log.txt");
        File.WriteAllText(logPath, "Main content\r\n");

        var exceptionLogPath = Path.Combine(_testRoot, "SSEEditException.log");
        File.WriteAllText(exceptionLogPath, "Previous exception\r\n");
        var exceptionOffset = new FileInfo(exceptionLogPath).Length;
        // No new content appended

        // Act
        var result = await _sut.ReadLogContentAsync(_testRoot, GameType.SkyrimSe, 0, exceptionOffset);

        // Assert
        result.ExceptionContent.Should().BeNull("no new exception content was appended after offset");
    }

    #endregion

    #region ReadLogContentAsync -- Cancellation

    [Fact]
    public async Task ReadLogContentAsync_CancelledToken_ThrowsOperationCancelled()
    {
        // Arrange
        var logPath = Path.Combine(_testRoot, "SSEEdit_log.txt");
        File.WriteAllText(logPath, "Some content\r\n");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = async () => await _sut.ReadLogContentAsync(
            _testRoot, GameType.SkyrimSe, 0, 0, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region ReadLogContentAsync -- Retry on IOException (OFF-04)

    [Fact]
    public async Task ReadLogContentAsync_RetriesOnIOException_EventuallySucceeds()
    {
        // Arrange -- lock the file exclusively, release after a short delay
        // to verify the exponential backoff retry handles file contention
        var logPath = Path.Combine(_testRoot, "SSEEdit_log.txt");
        File.WriteAllText(logPath, "log content after retry\r\n");

        // Lock the file exclusively (prevents reading by the service)
        using var lockStream = new FileStream(logPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        // Start the read on a background task -- first attempt(s) will hit IOException
        var readTask = Task.Run(async () =>
            await _sut.ReadLogContentAsync(_testRoot, GameType.SkyrimSe, 0, 0));

        // Release after 150ms -- the first retry is at 100ms (will likely fail),
        // second retry at 200ms (should succeed after lock is released)
        await Task.Delay(150);
        lockStream.Dispose();

        // Act
        var result = await readTask;

        // Assert
        result.LogLines.Should().NotBeEmpty();
        result.LogLines[0].Should().Be("log content after retry");
    }

    [Fact]
    public async Task ReadLogContentAsync_AllRetriesExhausted_ReturnsEmptyLines()
    {
        // Arrange -- lock the file for longer than the total retry window (~700ms)
        // to verify that after all retries are exhausted, the service returns empty
        var logPath = Path.Combine(_testRoot, "SSEEdit_log.txt");
        File.WriteAllText(logPath, "unreachable content\r\n");

        using var lockStream = new FileStream(logPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        // Act -- the service will retry 3 times (100ms + 200ms + 400ms = 700ms)
        // then return empty. We hold the lock the entire time.
        var result = await _sut.ReadLogContentAsync(_testRoot, GameType.SkyrimSe, 0, 0);

        // Assert -- after exhausting retries, returns empty log lines with no exception
        result.LogLines.Should().BeEmpty("all retries exhausted while file was locked");

        // Cleanup -- release lock explicitly before Dispose to avoid conflicts
        lockStream.Dispose();
    }

    #endregion

    #region Legacy methods (backward compatibility)

    [Fact]
    public void LegacyGetLogFilePath_StillWorks()
    {
        // Act -- verify the old stem-uppercase convention still functions
#pragma warning disable CS0618 // Obsolete
        var result = _sut.GetLogFilePath(@"C:\xEdit\SSEEdit.exe");
#pragma warning restore CS0618

        // Assert
        result.Should().Be(@"C:\xEdit\SSEEDIT_log.txt");
    }

    [Fact]
    public async Task LegacyReadLogFileAsync_FileNotFound_ReturnsError()
    {
        // Arrange
        var xEditPath = Path.Combine(_testRoot, "SSEEdit.exe");

        // Act
#pragma warning disable CS0618 // Obsolete
        var (lines, error) = await _sut.ReadLogFileAsync(xEditPath, DateTime.UtcNow);
#pragma warning restore CS0618

        // Assert
        lines.Should().BeEmpty();
        error.Should().Contain("not found");
    }

    [Fact]
    public async Task LegacyReadLogFileAsync_StaleLog_ReturnsError()
    {
        // Arrange -- create a log file that predates the process start
        var xEditPath = Path.Combine(_testRoot, "SSEEdit.exe");
        var logPath = Path.Combine(_testRoot, "SSEEDIT_log.txt");
        File.WriteAllText(logPath, "old log content");
        File.SetLastWriteTimeUtc(logPath, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var processStart = new DateTime(2026, 2, 7, 10, 0, 0, DateTimeKind.Utc);

        // Act
#pragma warning disable CS0618 // Obsolete
        var (lines, error) = await _sut.ReadLogFileAsync(xEditPath, processStart);
#pragma warning restore CS0618

        // Assert
        lines.Should().BeEmpty();
        error.Should().Contain("stale");
    }

    [Fact]
    public async Task LegacyReadLogFileAsync_FreshLog_ReturnsLines()
    {
        // Arrange -- create a log file newer than process start
        var xEditPath = Path.Combine(_testRoot, "SSEEdit.exe");
        var logPath = Path.Combine(_testRoot, "SSEEDIT_log.txt");
        File.WriteAllText(logPath, "Line 1\nLine 2\nLine 3");

        var processStart = DateTime.UtcNow.AddMinutes(-5);
        File.SetLastWriteTimeUtc(logPath, DateTime.UtcNow);

        // Act
#pragma warning disable CS0618 // Obsolete
        var (lines, error) = await _sut.ReadLogFileAsync(xEditPath, processStart);
#pragma warning restore CS0618

        // Assert
        error.Should().BeNull("fresh log file should be read successfully");
        lines.Should().HaveCount(3);
        lines[0].Should().Be("Line 1");
    }

    #endregion
}
