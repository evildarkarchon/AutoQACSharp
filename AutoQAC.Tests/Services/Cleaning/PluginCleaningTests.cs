using System.Diagnostics;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Models.Diagnostics;
using AutoQAC.Services.Cleaning;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AutoQAC.Tests.Services.Cleaning;

public sealed class PluginCleaningTests
{
    private const string XEditDirectory = "xedit";
    private const int TimeoutSeconds = 300;
    private const int MaxRetryAttempts = 3;

    private readonly ICleaningService _cleaningServiceMock;
    private readonly IXEditLogFileService _logFileServiceMock;
    private readonly IXEditOutputParser _outputParserMock;
    private readonly ICleaningSessionDecisionAdapter _decisionsMock;
    private readonly ICleaningTerminationCoordinator _terminationCoordinatorMock;
    private readonly IPluginCleaning _sut;

    public PluginCleaningTests()
    {
        _cleaningServiceMock = Substitute.For<ICleaningService>();
        _logFileServiceMock = Substitute.For<IXEditLogFileService>();
        _outputParserMock = Substitute.For<IXEditOutputParser>();
        _decisionsMock = Substitute.For<ICleaningSessionDecisionAdapter>();
        _terminationCoordinatorMock = Substitute.For<ICleaningTerminationCoordinator>();
        var loggerMock = Substitute.For<ILoggingService>();

        _logFileServiceMock.GetLogFilePath(Arg.Any<string>(), Arg.Any<GameType>())
            .Returns("main.log");
        _logFileServiceMock.GetExceptionLogFilePath(Arg.Any<string>(), Arg.Any<GameType>())
            .Returns("exception.log");
        _logFileServiceMock.CaptureOffset(Arg.Any<string>())
            .Returns(0L);
        _logFileServiceMock.ReadLogContentAsync(
                Arg.Any<string>(),
                Arg.Any<GameType>(),
                Arg.Any<long>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>())
            .Returns(new LogReadResult { LogLines = [] });
        _terminationCoordinatorMock.ProcessMayStillBeRunning.Returns(false);
        _terminationCoordinatorMock.IsStopRequested.Returns(false);

        _sut = new PluginCleaning(
            new PluginCleaningRunner(_cleaningServiceMock, _logFileServiceMock, loggerMock),
            new PluginResultFinalizer(_logFileServiceMock, _outputParserMock, loggerMock),
            _decisionsMock,
            _terminationCoordinatorMock);
    }

    [Fact]
    public async Task CleanAsync_WhenLaunchSucceeds_ReturnsFinalPluginResultFromLogSlice()
    {
        // Arrange
        var plugin = CreatePlugin("Clean.esp");
        _cleaningServiceMock.CleanPluginAsync(
                plugin,
                Arg.Any<Action<Process>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new CleaningResult
            {
                Success = true,
                Status = CleaningStatus.Cleaned,
                Message = "Cleaning completed successfully."
            });
        _logFileServiceMock.ReadLogContentAsync(
                XEditDirectory,
                GameType.SkyrimSe,
                0,
                0,
                Arg.Any<CancellationToken>())
            .Returns(new LogReadResult { LogLines = ["Removed ITM records."] });
        _outputParserMock.ParseOutput(Arg.Any<List<string>>())
            .Returns(new CleaningStatistics { ItemsRemoved = 2 });

        // Act
        var result = await _sut.CleanAsync(CreateContext(plugin), CancellationToken.None);

        // Assert
        result.PluginName.Should().Be("Clean.esp");
        result.Status.Should().Be(CleaningStatus.Cleaned);
        result.Success.Should().BeTrue();
        result.ItemsRemoved.Should().Be(2);
        _logFileServiceMock.Received(1).CaptureOffset("main.log");
        _logFileServiceMock.Received(1).CaptureOffset("exception.log");
        await _cleaningServiceMock.Received(1).CleanPluginAsync(
            plugin,
            Arg.Any<Action<Process>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CleanAsync_WhenTimedOutRetryIsAccepted_ReturnsSuccessfulFinalAttempt()
    {
        // Arrange
        var plugin = CreatePlugin("Retry.esp");
        _cleaningServiceMock.CleanPluginAsync(
                plugin,
                Arg.Any<Action<Process>?>(),
                Arg.Any<CancellationToken>())
            .Returns(
                new CleaningResult
                {
                    Success = false,
                    Status = CleaningStatus.Failed,
                    Message = "Cleaning timed out.",
                    TimedOut = true
                },
                new CleaningResult
                {
                    Success = true,
                    Status = CleaningStatus.Cleaned,
                    Message = "Cleaning completed successfully."
                });
        _decisionsMock.ShouldRetryTimedOutPluginAsync(
                plugin.FileName,
                TimeoutSeconds,
                1,
                MaxRetryAttempts,
                Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _sut.CleanAsync(CreateContext(plugin), CancellationToken.None);

        // Assert
        result.Status.Should().Be(CleaningStatus.Cleaned);
        result.Success.Should().BeTrue();
        await _cleaningServiceMock.Received(2).CleanPluginAsync(
            plugin,
            Arg.Any<Action<Process>?>(),
            Arg.Any<CancellationToken>());
        await _decisionsMock.Received(1).ShouldRetryTimedOutPluginAsync(
            plugin.FileName,
            TimeoutSeconds,
            1,
            MaxRetryAttempts,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CleanAsync_WhenTimedOutRetryIsDeclined_ReturnsTimedOutFailure()
    {
        // Arrange
        var plugin = CreatePlugin("NoRetry.esp");
        _cleaningServiceMock.CleanPluginAsync(
                plugin,
                Arg.Any<Action<Process>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new CleaningResult
            {
                Success = false,
                Status = CleaningStatus.Failed,
                Message = "Cleaning timed out.",
                TimedOut = true
            });
        _decisionsMock.ShouldRetryTimedOutPluginAsync(
                plugin.FileName,
                TimeoutSeconds,
                1,
                MaxRetryAttempts,
                Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var result = await _sut.CleanAsync(CreateContext(plugin), CancellationToken.None);

        // Assert
        result.Status.Should().Be(CleaningStatus.Failed);
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("timed out");
        await _cleaningServiceMock.Received(1).CleanPluginAsync(
            plugin,
            Arg.Any<Action<Process>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CleanAsync_WhenExceptionLogExists_ReturnsFailedResultEvenWhenLaunchSucceeded()
    {
        // Arrange
        var plugin = CreatePlugin("Exception.esp");
        _cleaningServiceMock.CleanPluginAsync(
                plugin,
                Arg.Any<Action<Process>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new CleaningResult
            {
                Success = true,
                Status = CleaningStatus.Cleaned,
                Message = "Cleaning completed successfully."
            });
        _logFileServiceMock.ReadLogContentAsync(
                XEditDirectory,
                GameType.SkyrimSe,
                0,
                0,
                Arg.Any<CancellationToken>())
            .Returns(new LogReadResult
            {
                LogLines = [],
                ExceptionContent = "EAccessViolation"
            });

        // Act
        var result = await _sut.CleanAsync(CreateContext(plugin), CancellationToken.None);

        // Assert
        result.Status.Should().Be(CleaningStatus.Failed);
        result.Success.Should().BeFalse();
        result.Message.Should().Be(DiagnosticTextFormatter.XEditReportedError(plugin.FileName));
        result.LogParseWarning.Should().Be(DiagnosticTextFormatter.XEditReportedError(plugin.FileName));
    }

    [Fact]
    public async Task CleanAsync_WhenCompletedLogSliceHasZeroStats_PromotesToAlreadyClean()
    {
        // Arrange
        var plugin = CreatePlugin("AlreadyClean.esp");
        _cleaningServiceMock.CleanPluginAsync(
                plugin,
                Arg.Any<Action<Process>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new CleaningResult
            {
                Success = true,
                Status = CleaningStatus.Cleaned,
                Message = "Cleaning completed successfully."
            });
        _logFileServiceMock.ReadLogContentAsync(
                XEditDirectory,
                GameType.SkyrimSe,
                0,
                0,
                Arg.Any<CancellationToken>())
            .Returns(new LogReadResult { LogLines = ["Done."] });
        _outputParserMock.ParseOutput(Arg.Any<List<string>>())
            .Returns(new CleaningStatistics());
        _outputParserMock.IsCompletionLine("Done.").Returns(true);

        // Act
        var result = await _sut.CleanAsync(CreateContext(plugin), CancellationToken.None);

        // Assert
        result.Status.Should().Be(CleaningStatus.AlreadyClean);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task CleanAsync_WhenCancellationHappensBeforeAttemptOutcome_BubblesCancellation()
    {
        // Arrange
        var plugin = CreatePlugin("Cancelled.esp");
        _cleaningServiceMock.CleanPluginAsync(
                plugin,
                Arg.Any<Action<Process>?>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        // Act
        var act = async () => await _sut.CleanAsync(CreateContext(plugin), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        _terminationCoordinatorMock.Received(1).DetachProcess();
    }

    [Fact]
    public async Task CleanAsync_AttachesEachAttemptAndDetachesAfterRetrySequence()
    {
        // Arrange
        var plugin = CreatePlugin("AttachDetach.esp");
        var events = new List<string>();
        _terminationCoordinatorMock.When(x => x.AttachProcess(Arg.Any<Process>()))
            .Do(_ => events.Add("attach"));
        _terminationCoordinatorMock.When(x => x.DetachProcess())
            .Do(_ => events.Add("detach"));
        _cleaningServiceMock.CleanPluginAsync(
                plugin,
                Arg.Any<Action<Process>?>(),
                Arg.Any<CancellationToken>())
            .Returns(
                callInfo =>
                {
                    callInfo.ArgAt<Action<Process>?>(1)?.Invoke(Process.GetCurrentProcess());
                    return new CleaningResult
                    {
                        Success = false,
                        Status = CleaningStatus.Failed,
                        Message = "Cleaning timed out.",
                        TimedOut = true
                    };
                },
                callInfo =>
                {
                    callInfo.ArgAt<Action<Process>?>(1)?.Invoke(Process.GetCurrentProcess());
                    return new CleaningResult
                    {
                        Success = true,
                        Status = CleaningStatus.Cleaned,
                        Message = "Cleaning completed successfully."
                    };
                });
        _decisionsMock.ShouldRetryTimedOutPluginAsync(
                plugin.FileName,
                TimeoutSeconds,
                1,
                MaxRetryAttempts,
                Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await _sut.CleanAsync(CreateContext(plugin), CancellationToken.None);

        // Assert
        events.Should().Equal("attach", "attach", "detach");
        _terminationCoordinatorMock.Received(2).AttachProcess(Arg.Any<Process>());
        _terminationCoordinatorMock.Received(1).DetachProcess();
    }

    private static PluginCleaningContext CreateContext(PluginInfo plugin) => new(
        plugin,
        GameType.SkyrimSe,
        XEditDirectory,
        TimeoutSeconds,
        MaxRetryAttempts);

    private static PluginInfo CreatePlugin(string fileName) => new()
    {
        FileName = fileName,
        FullPath = fileName,
        DetectedGameType = GameType.SkyrimSe
    };
}
