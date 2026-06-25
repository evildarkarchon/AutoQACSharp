using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Models.Diagnostics;
using AutoQAC.Services.Cleaning;
using AutoQAC.Tests.Helpers;
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

    [Fact]
    public async Task FinalizeAsync_FailedRunnerResult_WithCompletionLineAndZeroStats_RemainsFailed_AndSuccessIsFalse()
    {
        // Arrange: runner reports a failed xEdit attempt, but the log slice happens to contain a completion
        // line and zero parsed stats. The OLD finalizer would have wrongly promoted this to AlreadyClean.
        var plugin = CreatePlugin("FailedButLogLooksClean.esp");
        var failedRunnerOutput = new PluginRunnerOutput
        {
            LastAttemptResult = new CleaningResult
            {
                Success = false,
                Status = CleaningStatus.Failed,
                Message = "xEdit exited with non-zero status."
            },
            AttemptCount = 1,
            MainLogOffset = 11,
            ExceptionLogOffset = 22,
            Duration = TimeSpan.FromSeconds(1),
            ReachedMaxRetryAttempts = false
        };

        _logFileServiceMock.ReadLogContentAsync(
                "xedit",
                GameType.SkyrimSe,
                failedRunnerOutput.MainLogOffset,
                failedRunnerOutput.ExceptionLogOffset,
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
            failedRunnerOutput,
            new TerminationFinalizeContext(ProcessMayStillBeRunning: false, StopWasRequested: false),
            CancellationToken.None);

        // Assert: failed runner outcomes MUST NOT be reclassified as AlreadyClean,
        // and Success must reflect the failed final status.
        result.Status.Should().Be(CleaningStatus.Failed);
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task FinalizeAsync_ExceptionLogContent_ForcesStatusFailedAndSuccessFalse()
    {
        // Arrange: runner reports success, but xEdit dropped an exception log. Final result MUST be Failed
        // AND the returned Success flag MUST be false (no Status=Failed/Success=true inconsistency).
        var plugin = CreatePlugin("CleanedButException.esp");
        var runnerOutput = CreateRunnerOutput(); // Success=true, Status=Cleaned

        _logFileServiceMock.ReadLogContentAsync(
                "xedit",
                GameType.SkyrimSe,
                runnerOutput.MainLogOffset,
                runnerOutput.ExceptionLogOffset,
                Arg.Any<CancellationToken>())
            .Returns(new LogReadResult
            {
                LogLines = [],
                ExceptionContent = "EAccessViolation: invalid pointer operation at 0x00401234"
            });

        // Act
        var result = await _sut.FinalizeAsync(
            plugin,
            GameType.SkyrimSe,
            "xedit",
            runnerOutput,
            new TerminationFinalizeContext(ProcessMayStillBeRunning: false, StopWasRequested: false),
            CancellationToken.None);

        // Assert
        result.Status.Should().Be(CleaningStatus.Failed);
        result.Success.Should().BeFalse();
        result.Message.Should().Be(DiagnosticTextFormatter.XEditReportedError("CleanedButException.esp"));
        result.LogParseWarning.Should().Be(DiagnosticTextFormatter.XEditReportedError("CleanedButException.esp"));
        AssertSafeResultBoundary(result.Message);
        AssertSafeResultBoundary(result.LogParseWarning);
    }

    [Fact]
    public async Task FinalizeAsync_FailedRunnerResultWithUnsafeMessage_ReplacesMessageWithSafePluginFailure()
    {
        // Arrange
        var plugin = CreatePlugin("Plugin.esp");
        var runnerOutput = new PluginRunnerOutput
        {
            LastAttemptResult = new CleaningResult
            {
                Success = false,
                Status = CleaningStatus.Failed,
                Message = @"Process failed at C:\Users\Alice\Tools\SSEEdit.exe -QAC"
            },
            AttemptCount = 1,
            MainLogOffset = 11,
            ExceptionLogOffset = 22,
            Duration = TimeSpan.FromSeconds(1),
            ReachedMaxRetryAttempts = false
        };

        _logFileServiceMock.ReadLogContentAsync(
                "xedit",
                GameType.SkyrimSe,
                runnerOutput.MainLogOffset,
                runnerOutput.ExceptionLogOffset,
                Arg.Any<CancellationToken>())
            .Returns(new LogReadResult { LogLines = [] });

        // Act
        var result = await _sut.FinalizeAsync(
            plugin,
            GameType.SkyrimSe,
            "xedit",
            runnerOutput,
            new TerminationFinalizeContext(ProcessMayStillBeRunning: false, StopWasRequested: false),
            CancellationToken.None);

        // Assert
        result.Message.Should().Be(DiagnosticTextFormatter.CleaningFailedForPlugin("Plugin.esp"));
        result.LogParseWarning.Should().BeNull();
        AssertSafeResultBoundary(result.Message);
    }

    [Fact]
    public async Task FinalizeAsync_ExceptionLogContent_UsesSafeMessageAndWarningWithoutLoggingRawContent()
    {
        // Arrange
        var plugin = CreatePlugin("Plugin.esp");
        var runnerOutput = CreateRunnerOutput();
        _logFileServiceMock.ReadLogContentAsync(
                "xedit",
                GameType.SkyrimSe,
                runnerOutput.MainLogOffset,
                runnerOutput.ExceptionLogOffset,
                Arg.Any<CancellationToken>())
            .Returns(new LogReadResult
            {
                LogLines = [],
                ExceptionContent = @"EAccessViolation at C:\Games\Skyrim\Data\Plugin.esp"
            });

        // Act
        var result = await _sut.FinalizeAsync(
            plugin,
            GameType.SkyrimSe,
            "xedit",
            runnerOutput,
            new TerminationFinalizeContext(ProcessMayStillBeRunning: false, StopWasRequested: false),
            CancellationToken.None);

        // Assert
        var expected = DiagnosticTextFormatter.XEditReportedError("Plugin.esp");
        result.Status.Should().Be(CleaningStatus.Failed);
        result.Success.Should().BeFalse();
        result.Message.Should().Be(expected);
        result.LogParseWarning.Should().Be(expected);
        AssertSafeResultBoundary(result.Message);
        AssertSafeResultBoundary(result.LogParseWarning);

        _loggerMock.Received(1).Warning(
            "xEdit reported an exception log for {Plugin}; user-facing details were suppressed.",
            "Plugin.esp");
        _loggerMock.DidNotReceive().Warning(
            "xEdit exception log for {Plugin}: {Content}",
            Arg.Any<object[]>());
    }

    /// <summary>
    /// Verifies that path-bearing xEdit log-read warnings are retained in local logs but replaced before user-facing result/report surfaces consume them.
    /// </summary>
    [Fact]
    public async Task FinalizeAsync_LogReadWarningWithMainLogPath_UsesSafeUserFacingWarningAndKeepsRawWarningInLogs()
    {
        // Arrange
        const string safeWarning = "xEdit log could not be read. See the latest AutoQAC log.";
        const string rawWarning = @"Main log file not found: C:\Users\Alice\AppData\Local\SSEEdit\SSEEdit_log.txt";
        var plugin = CreatePlugin("Plugin.esp");
        var runnerOutput = CreateRunnerOutput();

        _logFileServiceMock.ReadLogContentAsync(
                "xedit",
                GameType.SkyrimSe,
                runnerOutput.MainLogOffset,
                runnerOutput.ExceptionLogOffset,
                Arg.Any<CancellationToken>())
            .Returns(new LogReadResult
            {
                LogLines = [],
                Warning = rawWarning
            });

        // Act
        var result = await _sut.FinalizeAsync(
            plugin,
            GameType.SkyrimSe,
            "xedit",
            runnerOutput,
            new TerminationFinalizeContext(ProcessMayStillBeRunning: false, StopWasRequested: false),
            CancellationToken.None);
        var session = new CleaningSessionResult
        {
            StartTime = new DateTime(2026, 5, 1, 7, 30, 0),
            EndTime = new DateTime(2026, 5, 1, 7, 31, 0),
            GameType = GameType.SkyrimSe,
            PluginResults = [result]
        };
        var report = session.GenerateReport();

        // Assert
        result.LogParseWarning.Should().Be(safeWarning);
        AssertSharedUnsafeSentinelsExcluded(result.LogParseWarning);
        AssertSharedUnsafeSentinelsExcluded(result.Summary);
        AssertSharedUnsafeSentinelsExcluded(report);
        _loggerMock.Received(1).Warning(
            "Log read warning for {Plugin}: {Warning}",
            "Plugin.esp",
            rawWarning);
    }

    [Fact]
    public async Task FinalizeAsync_SkippedRunnerResult_DoesNotReadLogs_AndSuccessStaysFalse()
    {
        // Arrange: skipped runner output is a short-circuit result. The finalizer must not read logs,
        // and the final Success flag must stay false after the WR-01 finalStatus-based derivation.
        var plugin = CreatePlugin("SkippedByBackup.esp");
        var skippedRunnerOutput = new PluginRunnerOutput
        {
            LastAttemptResult = new CleaningResult
            {
                Success = false,
                Status = CleaningStatus.Skipped,
                Message = "Skipped by backup cancellation."
            },
            AttemptCount = 1,
            MainLogOffset = 11,
            ExceptionLogOffset = 22,
            Duration = TimeSpan.FromMilliseconds(10),
            ReachedMaxRetryAttempts = false
        };

        // Act
        var result = await _sut.FinalizeAsync(
            plugin,
            GameType.SkyrimSe,
            "xedit",
            skippedRunnerOutput,
            new TerminationFinalizeContext(ProcessMayStillBeRunning: false, StopWasRequested: false),
            CancellationToken.None);

        // Assert
        await _logFileServiceMock.DidNotReceive().ReadLogContentAsync(
            Arg.Any<string>(),
            Arg.Any<GameType>(),
            Arg.Any<long>(),
            Arg.Any<long>(),
            Arg.Any<CancellationToken>());
        result.Status.Should().Be(CleaningStatus.Skipped);
        result.Success.Should().BeFalse();
        result.Message.Should().Be("Skipped by backup cancellation.");
    }

    /// <summary>
    /// Verifies that service-shaped sanitized failure messages remain safe through finalizer, summary, and report output.
    /// </summary>
    [Fact]
    public async Task FinalizeAsync_FailedRunnerResultFromCleaningServiceUnsafePluginMessage_ShouldKeepSummaryAndReportSafe()
    {
        // Arrange
        var unsafePluginName = "Unsafe\"Plugin\t|-QAC-autoload.esp";
        var plugin = CreatePlugin(unsafePluginName);
        var runnerOutput = new PluginRunnerOutput
        {
            LastAttemptResult = new CleaningResult
            {
                Success = false,
                Status = CleaningStatus.Failed,
                Message = $"Could not build direct xEdit launch command for {DiagnosticTextFormatter.SafePluginName(unsafePluginName)}. No process was started. See the latest AutoQAC log."
            },
            AttemptCount = 1,
            MainLogOffset = 11,
            ExceptionLogOffset = 22,
            Duration = TimeSpan.FromSeconds(1),
            ReachedMaxRetryAttempts = false
        };

        _logFileServiceMock.ReadLogContentAsync(
                "xedit",
                GameType.SkyrimSe,
                runnerOutput.MainLogOffset,
                runnerOutput.ExceptionLogOffset,
                Arg.Any<CancellationToken>())
            .Returns(new LogReadResult { LogLines = [] });

        // Act
        var result = await _sut.FinalizeAsync(
            plugin,
            GameType.SkyrimSe,
            "xedit",
            runnerOutput,
            new TerminationFinalizeContext(ProcessMayStillBeRunning: false, StopWasRequested: false),
            CancellationToken.None);
        var session = new CleaningSessionResult
        {
            StartTime = new DateTime(2026, 5, 1, 7, 0, 0),
            EndTime = new DateTime(2026, 5, 1, 7, 1, 0),
            GameType = GameType.SkyrimSe,
            PluginResults = [result]
        };
        var report = session.GenerateReport();

        // Assert
        result.Message.Should().Contain("UnsafePlugin.esp");
        result.Message.Should().Contain("No process was started");
        result.Message.Should().Contain("See the latest AutoQAC log");
        result.Summary.Should().Contain("UnsafePlugin.esp");
        report.Should().Contain("UnsafePlugin.esp");
        report.Should().Contain("No process was started");
        AssertUnsafeServiceFailureFragmentsExcluded(result.Message);
        AssertUnsafeServiceFailureFragmentsExcluded(result.Summary);
        AssertUnsafeServiceFailureFragmentsExcluded(report);
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

    private static void AssertSafeResultBoundary(string? text)
    {
        text.Should().NotContain(@"C:\Users\Alice");
        text.Should().NotContain("-QAC");
        text.Should().NotContain("EAccessViolation");
    }

    /// <summary>
    /// Asserts that downstream failed-result text excludes unsafe service-message fragments and raw plugin basename characters.
    /// </summary>
    private static void AssertUnsafeServiceFailureFragmentsExcluded(string? text)
    {
        text.Should().NotBeNull();
        text.Should().NotContain("\"");
        text.Should().NotContain("`");
        text.Should().NotContain("|");
        text.Should().NotContain("\t");
        text.Should().NotContain("-QAC");
        text.Should().NotContain("-autoload");
        text.Should().NotContain(@"C:\Users\Alice");
        text.Should().NotContain("SSEEdit.exe -QAC");
        text.Should().NotContain("System.InvalidOperationException");
    }

    /// <summary>
    /// Asserts that user-facing finalizer/report text excludes the shared Phase 11 unsafe diagnostic fragments.
    /// </summary>
    private static void AssertSharedUnsafeSentinelsExcluded(string? text)
    {
        text.Should().NotBeNull();
        foreach (var sentinel in DiagnosticSentinels.UnsafeDiagnosticSentinels)
        {
            text.Should().NotContain(sentinel);
        }
    }
}
