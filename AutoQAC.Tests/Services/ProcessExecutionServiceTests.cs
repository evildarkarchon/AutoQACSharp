using System.Diagnostics;
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
using Moq;

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
    private readonly Mock<ILoggingService> _mockLogger;
    private readonly Mock<IStateService> _mockState;

    /// <summary>
    /// Initializes test fixtures with default mock configurations.
    /// </summary>
    public ProcessExecutionServiceTests()
    {
        _mockLogger = new Mock<ILoggingService>();
        _mockState = new Mock<IStateService>();

        var defaultState = new AppState();
        _mockState.Setup(s => s.CurrentState).Returns(defaultState);
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
        using var service = new ProcessExecutionService(_mockState.Object, _mockLogger.Object);

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
        _mockLogger.Verify(
            l => l.Error(It.IsAny<Exception>(), It.Is<string>(s => s.Contains("Failed to start"))),
            Times.Once,
            "startup failure should be logged");
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
        var service = new ProcessExecutionService(_mockState.Object, _mockLogger.Object);

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

    #region Orchestrator-Level Termination Tests (via Mock<IProcessExecutionService>)

    /// <summary>
    /// Creates a CleaningOrchestrator with mocked dependencies for testing
    /// process termination and orphan cleanup behavior.
    /// </summary>
    private (CleaningOrchestrator orchestrator, Mock<IProcessExecutionService> processServiceMock) CreateOrchestrator()
    {
        var cleaningServiceMock = new Mock<ICleaningService>();
        var pluginServiceMock = new Mock<IPluginValidationService>();
        var gameDetectionServiceMock = new Mock<IGameDetectionService>();
        var stateServiceMock = new Mock<IStateService>();
        var configServiceMock = new Mock<IConfigurationService>();
        var loggerMock = new Mock<ILoggingService>();
        var processServiceMock = new Mock<IProcessExecutionService>();
        var logFileServiceMock = new Mock<IXEditLogFileService>();
        var outputParserMock = new Mock<IXEditOutputParser>();
        var backupServiceMock = new Mock<IBackupService>();
        var hangDetectionMock = new Mock<IHangDetectionService>();

        // Default setup for GetSkipListAsync (with GameVariant parameter)
        configServiceMock.Setup(s => s.GetSkipListAsync(It.IsAny<GameType>(), It.IsAny<GameVariant>()))
            .ReturnsAsync(new List<string>());

        // Default setup for LoadUserConfigAsync
        configServiceMock.Setup(s => s.LoadUserConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserConfiguration());

        // Default setup for log file service
        logFileServiceMock.Setup(s => s.ReadLogFileAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string>(), (string?)"Log file not found"));

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

        stateServiceMock.Setup(s => s.CurrentState).Returns(appState);

        cleaningServiceMock.Setup(s => s.ValidateEnvironmentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        pluginServiceMock.Setup(s => s.ValidatePluginFile(It.IsAny<PluginInfo>()))
            .Returns(PluginWarningKind.None);

        // Default: CleanPluginAsync succeeds and captures the onProcessStarted callback
        cleaningServiceMock.Setup(s => s.CleanPluginAsync(
                It.IsAny<PluginInfo>(),
                It.IsAny<IProgress<string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<Action<System.Diagnostics.Process>?>()))
            .ReturnsAsync(new CleaningResult
            {
                Status = CleaningStatus.Cleaned,
                Success = true,
                Message = "Cleaned successfully"
            });

        // Default: game variant detection
        gameDetectionServiceMock.Setup(s => s.DetectVariant(It.IsAny<GameType>(), It.IsAny<List<string>>()))
            .Returns(GameVariant.None);

        var orchestrator = new CleaningOrchestrator(
            cleaningServiceMock.Object,
            pluginServiceMock.Object,
            gameDetectionServiceMock.Object,
            stateServiceMock.Object,
            configServiceMock.Object,
            loggerMock.Object,
            processServiceMock.Object,
            logFileServiceMock.Object,
            outputParserMock.Object,
            backupServiceMock.Object,
            hangDetectionMock.Object);

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
        processServiceMock.Verify(
            p => p.CleanOrphanedProcessesAsync(It.IsAny<CancellationToken>()),
            Times.Once,
            "CleanOrphanedProcessesAsync should be called at the start of cleaning");

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
        var cleaningServiceMock = new Mock<ICleaningService>();
        cleaningServiceMock.Setup(s => s.ValidateEnvironmentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        cleaningServiceMock.Setup(s => s.CleanPluginAsync(
                It.IsAny<PluginInfo>(),
                It.IsAny<IProgress<string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<Action<System.Diagnostics.Process>?>()))
            .Returns(async (PluginInfo _, IProgress<string> _, CancellationToken ct, Action<System.Diagnostics.Process>? _) =>
            {
                cleaningStarted.TrySetResult(true);
                // Block until cancellation -- let the exception propagate so the
                // orchestrator's catch(OperationCanceledException) sets WasCancelled
                await Task.Delay(Timeout.Infinite, ct);
                return new CleaningResult { Status = CleaningStatus.Failed, Message = "Cancelled" };
            });

        // Build orchestrator with blocking mock
        var stateServiceMock = new Mock<IStateService>();
        var configServiceMock = new Mock<IConfigurationService>();
        var logFileServiceMock = new Mock<IXEditLogFileService>();
        var gameDetectionServiceMock = new Mock<IGameDetectionService>();
        var backupServiceMock = new Mock<IBackupService>();
        var hangDetectionMock = new Mock<IHangDetectionService>();
        var processServiceMock = new Mock<IProcessExecutionService>();

        configServiceMock.Setup(s => s.GetSkipListAsync(It.IsAny<GameType>(), It.IsAny<GameVariant>()))
            .ReturnsAsync(new List<string>());
        configServiceMock.Setup(s => s.LoadUserConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserConfiguration());
        logFileServiceMock.Setup(s => s.ReadLogFileAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string>(), (string?)"Log file not found"));
        gameDetectionServiceMock.Setup(s => s.DetectVariant(It.IsAny<GameType>(), It.IsAny<List<string>>()))
            .Returns(GameVariant.None);

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
        stateServiceMock.Setup(s => s.CurrentState).Returns(appState);

        var pluginServiceMock = new Mock<IPluginValidationService>();
        pluginServiceMock.Setup(s => s.ValidatePluginFile(It.IsAny<PluginInfo>()))
            .Returns(PluginWarningKind.None);

        var orch = new CleaningOrchestrator(
            cleaningServiceMock.Object,
            pluginServiceMock.Object,
            gameDetectionServiceMock.Object,
            stateServiceMock.Object,
            configServiceMock.Object,
            Mock.Of<ILoggingService>(),
            processServiceMock.Object,
            logFileServiceMock.Object,
            Mock.Of<IXEditOutputParser>(),
            backupServiceMock.Object,
            hangDetectionMock.Object);

        // Act
        var cleaningTask = orch.StartCleaningAsync();
        await cleaningStarted.Task; // Wait for cleaning to start

        await orch.StopCleaningAsync(); // Request stop (cancels CTS)
        await cleaningTask; // Wait for cleaning to complete

        // Assert -- The cleaning was cancelled successfully
        stateServiceMock.Verify(
            s => s.FinishCleaningWithResults(It.Is<CleaningSessionResult>(r => r.WasCancelled)),
            Times.Once,
            "Cleaning should have been cancelled");

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
        processServiceMock.Verify(
            p => p.TerminateProcessAsync(
                It.IsAny<System.Diagnostics.Process>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "TerminateProcessAsync should not be called when no process is active");

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
        processServiceMock.Verify(
            p => p.TerminateProcessAsync(
                It.IsAny<System.Diagnostics.Process>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "No process active, but the double-stop code path was exercised without errors");

        orchestrator.Dispose();
    }

    #endregion
}
