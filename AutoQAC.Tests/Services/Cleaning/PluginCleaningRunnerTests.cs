using System.Diagnostics;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
using FluentAssertions;
using NSubstitute;

namespace AutoQAC.Tests.Services.Cleaning;

public sealed class PluginCleaningRunnerTests
{
    private const int TimeoutSeconds = 300;
    private const int MaxRetryAttempts = 3;

    private readonly ICleaningService _cleaningServiceMock;
    private readonly IXEditLogFileService _logFileServiceMock;
    private readonly ILoggingService _loggerMock;
    private readonly IPluginCleaningRunner _sut;

    public PluginCleaningRunnerTests()
    {
        _cleaningServiceMock = Substitute.For<ICleaningService>();
        _logFileServiceMock = Substitute.For<IXEditLogFileService>();
        _loggerMock = Substitute.For<ILoggingService>();
        _logFileServiceMock.GetLogFilePath(Arg.Any<string>(), Arg.Any<GameType>()).Returns("main.log");
        _logFileServiceMock.GetExceptionLogFilePath(Arg.Any<string>(), Arg.Any<GameType>()).Returns("exception.log");
        _logFileServiceMock.CaptureOffset(Arg.Any<string>()).Returns(0L);
        _sut = new PluginCleaningRunner(_cleaningServiceMock, _logFileServiceMock, _loggerMock);
    }

    [Fact]
    public async Task RunAsync_TimedOut_AndCallbackReturnsTrue_RetriesAndCapturesNewLogOffsetsBeforeEachAttempt()
    {
        // Arrange
        var plugin = CreatePlugin("Retry.esp");
        _cleaningServiceMock.CleanPluginAsync(plugin, Arg.Any<Action<Process>?>(), Arg.Any<CancellationToken>())
            .Returns(
                new CleaningResult { Success = false, Status = CleaningStatus.Failed, Message = "Timeout", TimedOut = true },
                new CleaningResult { Success = true, Status = CleaningStatus.Cleaned, Message = "Cleaned" });
        TimeoutRetryCallback onTimeout = (_, _, _) => Task.FromResult(true);

        // Act
        var output = await _sut.RunAsync(
            plugin,
            GameType.SkyrimSe,
            "xedit",
            onTimeout,
            TimeoutSeconds,
            MaxRetryAttempts,
            _ => { },
            () => { },
            CancellationToken.None);

        // Assert
        output.AttemptCount.Should().Be(2);
        _logFileServiceMock.Received(2).CaptureOffset("main.log");
        _logFileServiceMock.Received(2).CaptureOffset("exception.log");
        await _cleaningServiceMock.Received(2).CleanPluginAsync(
            plugin,
            Arg.Any<Action<Process>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_TimedOut_AndCallbackReturnsFalse_StopsAfterFirstAttempt()
    {
        // Arrange
        var plugin = CreatePlugin("NoRetry.esp");
        _cleaningServiceMock.CleanPluginAsync(plugin, Arg.Any<Action<Process>?>(), Arg.Any<CancellationToken>())
            .Returns(new CleaningResult { Success = false, Status = CleaningStatus.Failed, Message = "Timeout", TimedOut = true });
        TimeoutRetryCallback onTimeout = (_, _, _) => Task.FromResult(false);

        // Act
        var output = await _sut.RunAsync(
            plugin,
            GameType.SkyrimSe,
            "xedit",
            onTimeout,
            TimeoutSeconds,
            MaxRetryAttempts,
            _ => { },
            () => { },
            CancellationToken.None);

        // Assert
        output.AttemptCount.Should().Be(1);
        await _cleaningServiceMock.Received(1).CleanPluginAsync(
            plugin,
            Arg.Any<Action<Process>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_AttachAndDetachDelegates_AreCalledExactlyOncePerAttempt_AroundCleanPluginAsync()
    {
        // Arrange
        var plugin = CreatePlugin("Attach.esp");
        var attachCount = 0;
        var detachCount = 0;
        _cleaningServiceMock.CleanPluginAsync(plugin, Arg.Any<Action<Process>?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callInfo.ArgAt<Action<Process>?>(1)?.Invoke(Process.GetCurrentProcess());
                return new CleaningResult { Success = true, Status = CleaningStatus.Cleaned, Message = "Cleaned" };
            });

        // Act
        await _sut.RunAsync(
            plugin,
            GameType.SkyrimSe,
            "xedit",
            onTimeout: null,
            TimeoutSeconds,
            MaxRetryAttempts,
            _ => attachCount++,
            () => detachCount++,
            CancellationToken.None);

        // Assert
        attachCount.Should().Be(1);
        detachCount.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_TwoAttemptRetry_DetachProcessCalledExactlyOnce()
    {
        // Arrange
        var plugin = CreatePlugin("DetachOnce.esp");
        var detachCount = 0;
        _cleaningServiceMock.CleanPluginAsync(plugin, Arg.Any<Action<Process>?>(), Arg.Any<CancellationToken>())
            .Returns(
                new CleaningResult { Success = false, Status = CleaningStatus.Failed, Message = "Timeout", TimedOut = true },
                new CleaningResult { Success = true, Status = CleaningStatus.Cleaned, Message = "Cleaned" });
        TimeoutRetryCallback onTimeout = (_, _, _) => Task.FromResult(true);

        // Act
        var output = await _sut.RunAsync(
            plugin,
            GameType.SkyrimSe,
            "xedit",
            onTimeout,
            TimeoutSeconds,
            MaxRetryAttempts,
            _ => { },
            () => detachCount++,
            CancellationToken.None);

        // Assert
        output.AttemptCount.Should().Be(2);
        detachCount.Should().Be(1);
    }

    private static PluginInfo CreatePlugin(string fileName) => new()
    {
        FileName = fileName,
        FullPath = fileName,
        DetectedGameType = GameType.SkyrimSe
    };
}
