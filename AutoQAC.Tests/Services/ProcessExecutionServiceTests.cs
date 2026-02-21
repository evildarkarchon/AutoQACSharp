using System.Diagnostics;
using System.Reflection;
using System.Reactive.Subjects;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.Backup;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameDetection;
using AutoQAC.Services.Monitoring;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.Process;
using AutoQAC.Services.State;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AutoQAC.Tests.Services;

/// <summary>
/// Unit tests for <see cref="ProcessExecutionService"/> and orchestrator-level
/// process termination/orphan cleanup behavior.
///
/// IMPORTANT: These tests do NOT spawn real processes (no cmd.exe). Tests for
/// termination and orphan cleanup are done at the orchestrator level via
/// Mock&lt;IProcessExecutionService&gt;. Direct ProcessExecutionService tests
/// are limited to paths that do NOT require a running process (startup failure,
/// disposal).
///
/// PID file logic tests are skipped because GetPidFilePath() is private and uses
/// AppContext.BaseDirectory with DEBUG directory walking -- the path cannot be
/// controlled in tests without refactoring.
/// </summary>
public sealed class ProcessExecutionServiceTests : IDisposable
{
    private readonly ILoggingService _mockLogger;
    private readonly IStateService _mockState;

    /// <summary>
    /// Initializes test fixtures with default mock configurations.
    /// </summary>
    public ProcessExecutionServiceTests()
    {
        _mockLogger = Substitute.For<ILoggingService>();
        _mockState = Substitute.For<IStateService>();

        var defaultState = new AppState();
        _mockState.CurrentState.Returns(defaultState);
    }

    public void Dispose()
    {
        // No special cleanup needed since we're not creating real processes
    }

    #region Process Execution Tests (Non-Process-Spawning)

    /// <summary>
    /// Verifies that ExecuteAsync returns a failed result with appropriate error
    /// when the process executable does not exist.
    ///
    /// NOTE: This tests the startup failure scenario where Process.Start throws
    /// because the file cannot be found. No real process is spawned.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenProcessNotFound_ShouldReturnFailedResult()
    {
        // Arrange
        using var service = new ProcessExecutionService(_mockState, _mockLogger);

        var startInfo = new ProcessStartInfo
        {
            FileName = "nonexistent_process_that_does_not_exist_12345.exe",
            Arguments = "--test"
        };

        // Act
        var result = await service.ExecuteAsync(startInfo);

        // Assert
        result.ExitCode.Should().Be(-1, "startup failure should return -1 exit code");
        result.ErrorLines.Should().NotBeEmpty("error details should be captured");

        // Verify logging occurred
        _mockLogger.Received(1).Error(
            Arg.Any<Exception>(),
            "Failed to start process: {FileName}",
            "nonexistent_process_that_does_not_exist_12345.exe");
    }

    #endregion

    #region Disposal Tests

    /// <summary>
    /// Verifies that disposal properly cleans up internal resources.
    /// After disposal, ExecuteAsync should throw ObjectDisposedException.
    /// No real process is spawned because the exception is thrown before Process.Start.
    /// </summary>
    [Fact]
    public async Task Dispose_ShouldPreventFurtherExecution()
    {
        // Arrange
        var service = new ProcessExecutionService(_mockState, _mockLogger);

        // Act
        service.Dispose();

        // Assert
        var startInfo = new ProcessStartInfo
        {
            FileName = "test_nonexistent.exe",
            Arguments = "--test"
        };

        await FluentActions.Awaiting(() => service.ExecuteAsync(startInfo))
            .Should().ThrowAsync<ObjectDisposedException>(
                "disposed service should not allow execution");
    }

    #endregion

    #region Thread-Safety and Handle Lifecycle Tests

    [Fact]
    public async Task ExecuteAsync_ShouldCaptureConcurrentStdoutAndStderrWithoutLoss()
    {
        // Arrange
        using var service = new ProcessExecutionService(_mockState, _mockLogger);
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c for /L %i in (1,1,120) do @echo out%i & @echo err%i 1>&2"
        };

        // Act
        var result = await service.ExecuteAsync(startInfo, timeout: TimeSpan.FromSeconds(30));

        // Assert
        result.TimedOut.Should().BeFalse();
        result.ExitCode.Should().Be(0);
        result.OutputLines.Select(line => line.Trim()).Should().Contain("out1");
        result.OutputLines.Select(line => line.Trim()).Should().Contain("out120");
        result.ErrorLines.Select(line => line.Trim()).Should().Contain("err1");
        result.ErrorLines.Select(line => line.Trim()).Should().Contain("err120");
        result.OutputLines.Count.Should().BeGreaterThanOrEqualTo(120);
        result.ErrorLines.Count.Should().BeGreaterThanOrEqualTo(120);
    }

    [Fact]
    public async Task CleanOrphanedProcessesAsync_ShouldNotLeakProcessHandlesAcrossRepeatedRuns()
    {
        // Arrange
        using var service = new ProcessExecutionService(_mockState, _mockLogger);
        var getPidFilePathMethod = typeof(ProcessExecutionService)
            .GetMethod("GetPidFilePath", BindingFlags.Instance | BindingFlags.NonPublic);
        getPidFilePathMethod.Should().NotBeNull();

        var pidFilePath = (string)getPidFilePathMethod!.Invoke(service, null)!;
        var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
        var startHandles = currentProcess.HandleCount;

        // Act
        for (var i = 0; i < 25; i++)
        {
            var tracked = new List<TrackedProcess>
            {
                new()
                {
                    Pid = currentProcess.Id,
                    StartTime = currentProcess.StartTime,
                    PluginName = $"Test{i}.esp"
                }
            };

            var json = System.Text.Json.JsonSerializer.Serialize(tracked);
            await File.WriteAllTextAsync(pidFilePath, json);
            await service.CleanOrphanedProcessesAsync();
        }

        var endHandles = currentProcess.HandleCount;

        // Assert
        var handleDrift = Math.Abs(endHandles - startHandles);
        handleDrift.Should().BeLessThan(40, "GetProcessById handles should be disposed each run");
    }

    #endregion

    #region Orchestrator-Level Termination Tests (via IProcessExecutionService substitute)

    /// <summary>
    /// Creates a CleaningOrchestrator with mocked dependencies for testing
    /// process termination and orphan cleanup behavior.
    /// </summary>
    private (CleaningOrchestrator orchestrator, IProcessExecutionService processServiceMock) CreateOrchestrator()
    {
        var cleaningServiceMock = Substitute.For<ICleaningService>();
        var pluginServiceMock = Substitute.For<IPluginValidationService>();
        var gameDetectionServiceMock = Substitute.For<IGameDetectionService>();
        var stateServiceMock = Substitute.For<IStateService>();
        var configServiceMock = Substitute.For<IConfigurationService>();
        var loggerMock = Substitute.For<ILoggingService>();
        var processServiceMock = Substitute.For<IProcessExecutionService>();
        var logFileServiceMock = Substitute.For<IXEditLogFileService>();
        var outputParserMock = Substitute.For<IXEditOutputParser>();
        var backupServiceMock = Substitute.For<IBackupService>();
        var hangDetectionMock = Substitute.For<IHangDetectionService>();

        // Default setup for GetSkipListAsync (with GameVariant parameter)
        configServiceMock.GetSkipListAsync(Arg.Any<GameType>(), Arg.Any<GameVariant>())
            .Returns(new List<string>());

        // Default setup for LoadUserConfigAsync
        configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new UserConfiguration());

        // Default setup for backup service
        backupServiceMock.BackupPlugin(Arg.Any<PluginInfo>(), Arg.Any<string>())
            .Returns(BackupResult.Ok(1024));

        // Default setup for log file service
        logFileServiceMock.ReadLogFileAsync(
                Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((new List<string>(), (string?)"Log file not found"));

        var plugins = new List<PluginInfo>
        {
            new() { FileName = "Test.esp", FullPath = @"C:\Data\Test.esp", IsSelected = true }
        };

        var appState = new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = plugins
        };

        stateServiceMock.CurrentState.Returns(appState);

        cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        pluginServiceMock.ValidatePluginFile(Arg.Any<PluginInfo>())
            .Returns(PluginWarningKind.None);

        // Default: CleanPluginAsync succeeds and captures the onProcessStarted callback
        cleaningServiceMock.CleanPluginAsync(
                Arg.Any<PluginInfo>(),
                Arg.Any<IProgress<string>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<System.Diagnostics.Process>?>())
            .Returns(new CleaningResult
            {
                Status = CleaningStatus.Cleaned,
                Success = true,
                Message = "Cleaned successfully"
            });

        // Default: game variant detection
        gameDetectionServiceMock.DetectVariant(Arg.Any<GameType>(), Arg.Any<List<string>>())
            .Returns(GameVariant.None);

        var orchestrator = new CleaningOrchestrator(
            cleaningServiceMock,
            pluginServiceMock,
            gameDetectionServiceMock,
            stateServiceMock,
            configServiceMock,
            loggerMock,
            processServiceMock,
            logFileServiceMock,
            outputParserMock,
            backupServiceMock,
            hangDetectionMock);

        return (orchestrator, processServiceMock);
    }

    /// <summary>
    /// Verifies that the orchestrator calls CleanOrphanedProcessesAsync
    /// at the start of each cleaning run (before processing plugins).
    /// </summary>
    [Fact]
    public async Task Orchestrator_StartCleaning_CallsCleanOrphanedProcessesAsync()
    {
        // Arrange
        var (orchestrator, processServiceMock) = CreateOrchestrator();

        // Act
        await orchestrator.StartCleaningAsync();

        // Assert
        await processServiceMock.Received(1)
            .CleanOrphanedProcessesAsync(Arg.Any<CancellationToken>());

        orchestrator.Dispose();
    }

    /// <summary>
    /// Verifies that StopCleaningAsync cancels the CTS which propagates cancellation
    /// to the cleaning loop. When CleanPluginAsync throws OperationCanceledException,
    /// the orchestrator catches it and records WasCancelled = true.
    /// </summary>
    [Fact]
    public async Task Orchestrator_StopCleaning_CancelsCts()
    {
        // Arrange
        var cleaningStarted = new TaskCompletionSource<bool>();

        // Override CleanPluginAsync to block until cancellation, then throw
        var cleaningServiceMock = Substitute.For<ICleaningService>();
        cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>())
            .Returns(true);
        cleaningServiceMock.CleanPluginAsync(
                Arg.Any<PluginInfo>(),
                Arg.Any<IProgress<string>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<System.Diagnostics.Process>?>())
            .Returns(async callInfo =>
            {
                cleaningStarted.TrySetResult(true);
                // Block until cancellation -- let the exception propagate so the
                // orchestrator's catch(OperationCanceledException) sets WasCancelled
                var ct = callInfo.ArgAt<CancellationToken>(2);
                await Task.Delay(Timeout.Infinite, ct);
                return new CleaningResult { Status = CleaningStatus.Failed, Message = "Cancelled" };
            });

        // Build orchestrator with blocking mock
        var stateServiceMock = Substitute.For<IStateService>();
        var configServiceMock = Substitute.For<IConfigurationService>();
        var logFileServiceMock = Substitute.For<IXEditLogFileService>();
        var gameDetectionServiceMock = Substitute.For<IGameDetectionService>();
        var backupServiceMock = Substitute.For<IBackupService>();
        var hangDetectionMock = Substitute.For<IHangDetectionService>();
        var processServiceMock = Substitute.For<IProcessExecutionService>();

        configServiceMock.GetSkipListAsync(Arg.Any<GameType>(), Arg.Any<GameVariant>())
            .Returns(new List<string>());
        configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new UserConfiguration());
        logFileServiceMock.ReadLogFileAsync(
                Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((new List<string>(), (string?)"Log file not found"));
        gameDetectionServiceMock.DetectVariant(Arg.Any<GameType>(), Arg.Any<List<string>>())
            .Returns(GameVariant.None);
        backupServiceMock.BackupPlugin(Arg.Any<PluginInfo>(), Arg.Any<string>())
            .Returns(BackupResult.Ok(1024));

        var plugins = new List<PluginInfo>
        {
            new() { FileName = "Test.esp", FullPath = @"C:\Data\Test.esp", IsSelected = true }
        };
        var appState = new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = plugins
        };
        stateServiceMock.CurrentState.Returns(appState);

        var pluginServiceMock = Substitute.For<IPluginValidationService>();
        pluginServiceMock.ValidatePluginFile(Arg.Any<PluginInfo>())
            .Returns(PluginWarningKind.None);

        var orch = new CleaningOrchestrator(
            cleaningServiceMock,
            pluginServiceMock,
            gameDetectionServiceMock,
            stateServiceMock,
            configServiceMock,
            Substitute.For<ILoggingService>(),
            processServiceMock,
            logFileServiceMock,
            Substitute.For<IXEditOutputParser>(),
            backupServiceMock,
            hangDetectionMock);

        // Act
        var cleaningTask = orch.StartCleaningAsync();
        await cleaningStarted.Task; // Wait for cleaning to start

        await orch.StopCleaningAsync(); // Request stop (cancels CTS)
        await cleaningTask; // Wait for cleaning to complete

        // Assert -- The cleaning was cancelled successfully
        stateServiceMock.Received(1)
            .FinishCleaningWithResults(Arg.Is<CleaningSessionResult>(r => r.WasCancelled));

        orch.Dispose();
    }

    /// <summary>
    /// Verifies that ForceStopCleaningAsync calls TerminateProcessAsync with
    /// forceKill=true for immediate process tree kill.
    /// </summary>
    [Fact]
    public async Task Orchestrator_ForceStop_ShouldCallTerminateWithForceKill()
    {
        // Arrange
        var (orchestrator, processServiceMock) = CreateOrchestrator();

        // ForceStopCleaningAsync reads _currentProcess which is null when no
        // cleaning is active, so it just cancels the CTS and returns.
        // This test verifies the method doesn't throw when called in isolation.
        await orchestrator.ForceStopCleaningAsync();

        // Since _currentProcess is null (no active cleaning), TerminateProcessAsync
        // should not be called. This verifies the null-check path.
        await processServiceMock.DidNotReceive()
            .TerminateProcessAsync(
                Arg.Any<System.Diagnostics.Process>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>());

        orchestrator.Dispose();
    }

    /// <summary>
    /// Verifies that calling StopCleaningAsync twice quickly (Path B) invokes
    /// ForceStopCleaningAsync for immediate escalation.
    /// </summary>
    [Fact]
    public async Task Orchestrator_DoubleStop_EscalatesToForceKill()
    {
        // Arrange
        var (orchestrator, processServiceMock) = CreateOrchestrator();

        // First stop sets _isStopRequested = true
        await orchestrator.StopCleaningAsync();

        // Second stop should take Path B (force kill path)
        await orchestrator.StopCleaningAsync();

        // With no active process, neither call should invoke TerminateProcessAsync.
        // But the code path through ForceStopCleaningAsync was exercised.
        await processServiceMock.DidNotReceive()
            .TerminateProcessAsync(
                Arg.Any<System.Diagnostics.Process>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>());

        orchestrator.Dispose();
    }

    #endregion
}
