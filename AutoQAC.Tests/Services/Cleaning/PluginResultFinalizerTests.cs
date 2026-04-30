using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
using FluentAssertions;
using NSubstitute;

namespace AutoQAC.Tests.Services.Cleaning;

public sealed class PluginResultFinalizerTests
{
    private readonly IXEditLogFileService _logFileServiceMock;
    private readonly IXEditOutputParser _outputParserMock;
    private readonly ILoggingService _loggerMock;
    private readonly IPluginResultFinalizer _sut;

    public PluginResultFinalizerTests()
    {
        _logFileServiceMock = Substitute.For<IXEditLogFileService>();
        _outputParserMock = Substitute.For<IXEditOutputParser>();
        _loggerMock = Substitute.For<ILoggingService>();
        _sut = new PluginResultFinalizer(_logFileServiceMock, _outputParserMock, _loggerMock);
    }

    [Fact]
    public async Task FinalizeAsync_WhenProcessMayStillBeRunning_DoesNotReadLogs_AndAttachesTerminationWarningMessage()
    {
        // Arrange
        var plugin = CreatePlugin("StillRunning.esp");
        var runnerOutput = CreateRunnerOutput();
        var terminationContext = new TerminationFinalizeContext(ProcessMayStillBeRunning: true, StopWasRequested: false);

        // Act
        var result = await _sut.FinalizeAsync(
            plugin,
            GameType.SkyrimSe,
            "xedit",
            runnerOutput,
            terminationContext,
            CancellationToken.None);

        // Assert
        await _logFileServiceMock.DidNotReceive().ReadLogContentAsync(
            Arg.Any<string>(),
            Arg.Any<GameType>(),
            Arg.Any<long>(),
            Arg.Any<long>(),
            Arg.Any<CancellationToken>());
        result.LogParseWarning.Should().Contain("xEdit was terminated");
    }

    [Fact]
    public async Task FinalizeAsync_CompletionLineWithZeroStats_ProducesAlreadyCleanStatus()
    {
        // Arrange
        var plugin = CreatePlugin("Clean.esp");
        var runnerOutput = CreateRunnerOutput();
        _logFileServiceMock.ReadLogContentAsync(
                "xedit",
                GameType.SkyrimSe,
                runnerOutput.MainLogOffset,
                runnerOutput.ExceptionLogOffset,
                Arg.Any<CancellationToken>())
            .Returns(new LogReadResult { LogLines = ["Done."] });
        _outputParserMock.ParseOutput(Arg.Any<List<string>>())
            .Returns(new CleaningStatistics());
        _outputParserMock.IsCompletionLine("Done.").Returns(true);

        // Act
        var result = await _sut.FinalizeAsync(
            plugin,
            GameType.SkyrimSe,
            "xedit",
            runnerOutput,
            new TerminationFinalizeContext(ProcessMayStillBeRunning: false, StopWasRequested: false),
            CancellationToken.None);

        // Assert
        result.Status.Should().Be(CleaningStatus.AlreadyClean);
    }

    [Fact]
    public async Task FinalizeAsync_StopWasRequested_DoesNotReadLogs()
    {
        // Arrange
        var plugin = CreatePlugin("Stopped.esp");
        var runnerOutput = CreateRunnerOutput();
        var terminationContext = new TerminationFinalizeContext(ProcessMayStillBeRunning: false, StopWasRequested: true);

        // Act
        var result = await _sut.FinalizeAsync(
            plugin,
            GameType.SkyrimSe,
            "xedit",
            runnerOutput,
            terminationContext,
            CancellationToken.None);

        // Assert
        await _logFileServiceMock.DidNotReceive().ReadLogContentAsync(
            Arg.Any<string>(),
            Arg.Any<GameType>(),
            Arg.Any<long>(),
            Arg.Any<long>(),
            Arg.Any<CancellationToken>());
        result.LogParseWarning.Should().Contain("xEdit was terminated");
    }

    private static PluginInfo CreatePlugin(string fileName) => new()
    {
        FileName = fileName,
        FullPath = fileName,
        DetectedGameType = GameType.SkyrimSe
    };

    private static PluginRunnerOutput CreateRunnerOutput() => new()
    {
        LastAttemptResult = new CleaningResult
        {
            Success = true,
            Status = CleaningStatus.Cleaned,
            Message = "Cleaning completed successfully."
        },
        AttemptCount = 1,
        MainLogOffset = 11,
        ExceptionLogOffset = 22,
        Duration = TimeSpan.FromSeconds(1),
        ReachedMaxRetryAttempts = false
    };
}
