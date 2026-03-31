using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
using FluentAssertions;
using NSubstitute;

namespace AutoQAC.Tests.Services;

/// <summary>
/// Unit tests for <see cref="XEditLogFileService"/> covering game-aware log filename resolution,
/// offset-based reading, truncation handling, exponential backoff retry, and legacy method compatibility.
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

    #region GameType-to-LogFilename Mapping (via public GetLogFilePath)

    [Theory]
    [InlineData(GameType.SkyrimLe, "TES5Edit_log.txt")]
    [InlineData(GameType.SkyrimSe, "SSEEdit_log.txt")]
    [InlineData(GameType.SkyrimVr, "TES5VREdit_log.txt")]
    [InlineData(GameType.Fallout4, "FO4Edit_log.txt")]
    [InlineData(GameType.Fallout4Vr, "FO4VREdit_log.txt")]
    [InlineData(GameType.Fallout3, "FO3Edit_log.txt")]
    [InlineData(GameType.FalloutNewVegas, "FNVEdit_log.txt")]
    [InlineData(GameType.Oblivion, "TES4Edit_log.txt")]
    public void GetLogFilePath_GameAware_AllGameTypes_ReturnsCorrectFilename(GameType gameType, string expectedFilename)
    {
        // Act
        var result = _sut.GetLogFilePath(@"C:\xEdit", gameType);

        // Assert
        Path.GetFileName(result).Should().Be(expectedFilename);
    }

    [Fact]
    public void GetLogFilePath_GameAware_Unknown_ThrowsArgumentOutOfRangeException()
    {
        // Act
        var act = () => _sut.GetLogFilePath(@"C:\xEdit", GameType.Unknown);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    #endregion

    #region GetLogFilePath (game-aware)

    [Fact]
    public void GetLogFilePath_GameAware_ReturnsCorrectPath()
    {
        // Act
        var result = _sut.GetLogFilePath(@"C:\xEdit", GameType.SkyrimSe);

        // Assert
        result.Should().Be(@"C:\xEdit\SSEEdit_log.txt");
    }

    [Fact]
    public void GetLogFilePath_GameAware_SkyrimLe_ReturnsCorrectPath()
    {
        // Act
        var result = _sut.GetLogFilePath(@"C:\xEdit", GameType.SkyrimLe);

        // Assert
        result.Should().Be(@"C:\xEdit\TES5Edit_log.txt");
    }

    [Fact]
    public void GetLogFilePath_GameAware_EmptyDirectory_ThrowsArgumentException()
    {
        // Act
        var act = () => _sut.GetLogFilePath("", GameType.SkyrimSe);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region GetExceptionLogFilePath

    [Fact]
    public void GetExceptionLogFilePath_ReturnsCorrectPath()
    {
        // Act
        var result = _sut.GetExceptionLogFilePath(@"C:\xEdit", GameType.SkyrimSe);

        // Assert
        result.Should().Be(@"C:\xEdit\SSEEditException.log");
    }

    [Fact]
    public void GetExceptionLogFilePath_Fallout4_ReturnsCorrectPath()
    {
        // Act
        var result = _sut.GetExceptionLogFilePath(@"C:\xEdit", GameType.Fallout4);

        // Assert
        result.Should().Be(@"C:\xEdit\FO4EditException.log");
    }

    [Fact]
    public void GetExceptionLogFilePath_EmptyDirectory_ThrowsArgumentException()
    {
        // Act
        var act = () => _sut.GetExceptionLogFilePath("", GameType.SkyrimSe);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region CaptureOffset

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

    #endregion

    #region ReadLogContentAsync

    [Fact]
    public async Task ReadLogContentAsync_MainLogDoesNotExist_ReturnsEmptyWithWarning()
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
    }

    [Fact]
    public async Task ReadLogContentAsync_OffsetZero_ReadsEntireFile()
    {
        // Arrange
        var logPath = Path.Combine(_testRoot, "SSEEdit_log.txt");
        File.WriteAllText(logPath, "Line 1\r\nLine 2\r\nLine 3\r\n");

        // Act
        var result = await _sut.ReadLogContentAsync(_testRoot, GameType.SkyrimSe, 0, 0);

        // Assert
        result.LogLines.Should().HaveCount(3);
        result.LogLines[0].Should().Be("Line 1");
    }

    [Fact]
    public async Task ReadLogContentAsync_TruncatedFile_ReadsEntireFile()
    {
        // Arrange -- simulate xEdit truncation: offset > file length
        var logPath = Path.Combine(_testRoot, "SSEEdit_log.txt");
        File.WriteAllText(logPath, "After truncation content\r\n");
        long staleOffset = 999999; // Way beyond actual file size

        // Act
        var result = await _sut.ReadLogContentAsync(_testRoot, GameType.SkyrimSe, staleOffset, 0);

        // Assert
        result.LogLines.Should().HaveCount(1);
        result.LogLines[0].Should().Be("After truncation content");
    }

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
        result.ExceptionContent.Should().Contain("Exception details here");
    }

    [Fact]
    public async Task ReadLogContentAsync_ExceptionLogDoesNotExist_ReturnsNullExceptionContent()
    {
        // Arrange
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
    public async Task ReadLogContentAsync_PassesCancellationToken()
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

    [Fact]
    public async Task ReadLogContentAsync_HandlesLineFeedOnlyNewlines()
    {
        // Arrange -- xEdit writes \r\n but test with \n only to verify handling
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

    #endregion

    #region Legacy methods (backward compatibility)

    [Fact]
    public void LegacyGetLogFilePath_StillWorks()
    {
        // Act
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

    #endregion
}
