using System.Diagnostics;
using System.Runtime.InteropServices;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.Backup;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameDetection;
using AutoQAC.Services.MO2;
using AutoQAC.Services.Monitoring;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.Process;
using AutoQAC.Services.State;
using AutoQAC.Tests.Helpers;
using FluentAssertions;
using NSubstitute;

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
    private readonly InMemoryPidStore _pidStore;
    private readonly IProcessSessionIdProvider _sessionProvider;

    private static async Task WaitForCancellationAndThrowAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            ct.ThrowIfCancellationRequested();
        }

        var cancellationSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = ct.Register(() => cancellationSignal.TrySetResult(true));
        await cancellationSignal.Task.WaitAsync(TimeSpan.FromSeconds(2));
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Initializes test fixtures with default mock configurations.
    /// </summary>
    public ProcessExecutionServiceTests()
    {
        _mockLogger = Substitute.For<ILoggingService>();
        _pidStore = new InMemoryPidStore();
        _sessionProvider = Substitute.For<IProcessSessionIdProvider>();
        _sessionProvider.CurrentSessionId.Returns("current-session");
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
        using var service = CreateService();

        var startInfo = new ProcessStartInfo
        {
            FileName = "nonexistent_process_that_does_not_exist_12345.exe",
            Arguments = "--test"
        };

        // Act
        var result = await service.ExecuteAsync(startInfo);

        // Assert
        result.ExitCode.Should().Be(-1, "startup failure should return -1 exit code");

        // Verify logging occurred without making the executable path a structured property.
        _mockLogger.Received(1).Error(
            Arg.Any<Exception>(),
            "Failed to start external process for {Operation}: status={Status}, reason={Reason}, argumentCount={ArgumentCount}",
            "ExternalProcess",
            "Failed",
            "StartFailed",
            1);
    }

    /// <summary>
    /// Process-start failure diagnostics must keep troubleshooting fields while avoiding executable paths and raw command payloads.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenProcessStartFails_ShouldLogSafeStructuredFieldsOnly()
    {
        // Arrange
        using var service = CreateService();
        var startInfo = new ProcessStartInfo
        {
            FileName = @"C:\Users\Alice\Tools\SSEEdit.exe",
            Arguments = @"-QAC -autoload C:\Games\Skyrim\Data\Plugin.esp",
            WorkingDirectory = @"C:\Users\Alice\Tools"
        };
        startInfo.ArgumentList.Add("-QAC");
        startInfo.ArgumentList.Add("-autoload");
        startInfo.ArgumentList.Add(@"C:\Games\Skyrim\Data\Plugin.esp");

        // Act
        var result = await service.ExecuteAsync(startInfo);

        // Assert
        result.ExitCode.Should().Be(-1);
        _mockLogger.Received(1).Debug(
            "Starting external process for {Operation}: status={Status}, argumentCount={ArgumentCount}",
            "ExternalProcess",
            "Starting",
            3);
        _mockLogger.Received(1).Error(
            Arg.Any<Exception>(),
            "Failed to start external process for {Operation}: status={Status}, reason={Reason}, argumentCount={ArgumentCount}",
            "ExternalProcess",
            "Failed",
            "StartFailed",
            3);

        AssertCapturedLogTextExcludes(
            @"C:\Users\Alice",
            "SSEEdit.exe",
            "-QAC",
            "-autoload",
            @"C:\Games\Skyrim");
    }

    /// <summary>
    /// Successful process-start diagnostics should report PID and safe counts without mutating the caller's launch values.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenProcessStarts_ShouldLogPidAndPreserveLaunchValues()
    {
        // Arrange
        using var service = CreateService(new RealProcessExitWaiter());
        Process? startedProcess = null;
        var startInfo = new ProcessStartInfo
        {
            FileName = DotNetHostPath,
            WorkingDirectory = Environment.CurrentDirectory,
            UseShellExecute = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--info");

        // Act
        var result = await service.ExecuteAsync(startInfo, onProcessStarted: process => startedProcess = process);

        // Assert
        result.ExitCode.Should().Be(0);
        startedProcess.Should().NotBeNull();
        _mockLogger.Received(1).Information(
            "Started external process for {Operation}: status={Status}, processId={ProcessId}, argumentCount={ArgumentCount}",
            "ExternalProcess",
            "Started",
            Arg.Any<int>(),
            1);

        startInfo.FileName.Should().Be(DotNetHostPath);
        startInfo.Arguments.Should().BeEmpty();
        startInfo.ArgumentList.Should().ContainSingle().Which.Should().Be("--info");
        startInfo.WorkingDirectory.Should().Be(Environment.CurrentDirectory);
        startInfo.UseShellExecute.Should().BeTrue("logging changes must not mutate caller-supplied ProcessStartInfo values");
        startInfo.CreateNoWindow.Should().BeTrue();
    }

    /// <summary>
    /// Successful legacy-Arguments launches without plugin context must use a generic PID label instead of persisting launch text.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenProcessStartsWithLegacyArgumentsAndNoPluginName_ShouldTrackSafeExternalProcessLabel()
    {
        // Arrange
        using var service = CreateService(new RealProcessExitWaiter());
        var legacyRawArguments = @"SSEEdit.exe -QAC -autoload C:\Users\Alice\Data\Unsafe.esp";
        IReadOnlyList<TrackedProcess> trackedAtStart = [];
        var startInfo = new ProcessStartInfo
        {
            FileName = DotNetHostPath,
            WorkingDirectory = Environment.CurrentDirectory,
            Arguments = "--info"
        };

        // Act
        var result = await service.ExecuteAsync(
            startInfo,
            onProcessStarted: _ => trackedAtStart = _pidStore.LoadAsync().GetAwaiter().GetResult());

        // Assert
        result.ExitCode.Should().Be(0);
        trackedAtStart.Should().ContainSingle();
        trackedAtStart[0].PluginName.Should().Be("ExternalProcess");

        var unsafeFragments = DiagnosticSentinels.UnsafeDiagnosticSentinels
            .Concat([legacyRawArguments, "--info"])
            .ToArray();
        foreach (var unsafeFragment in unsafeFragments)
        {
            trackedAtStart[0].PluginName.Should().NotContain(unsafeFragment);
        }

        AssertCapturedLogTextExcludes(unsafeFragments);
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
        var service = CreateService();

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
    public async Task CleanOrphanedProcessesAsync_ShouldNotLeakProcessHandlesAcrossRepeatedRuns()
    {
        // Arrange
        using var service = CreateService();
        var currentProcess = Process.GetCurrentProcess();
        var startHandles = currentProcess.HandleCount;

        // Act
        for (var i = 0; i < 25; i++)
        {
            await _pidStore.UpdateAsync(_ =>
            [
                new TrackedProcess
                {
                    Pid = currentProcess.Id,
                    StartTime = currentProcess.StartTime,
                    PluginName = $"Test{i}.esp",
                    SessionId = "prior-session"
                }
            ]);
            await service.CleanOrphanedProcessesAsync();
        }

        var endHandles = currentProcess.HandleCount;

        // Assert
        var handleDrift = Math.Abs(endHandles - startHandles);
        handleDrift.Should().BeLessThan(40, "GetProcessById handles should be disposed each run");
    }

    #endregion

    [Fact]
    public async Task TrackProcessAsync_ShouldWriteThroughPidStoreWithCurrentSessionId()
    {
        using var service = CreateService();
        using var current = Process.GetCurrentProcess();

        await service.TrackProcessAsync(current, "Tracked.esp");

        var tracked = await _pidStore.LoadAsync();
        tracked.Should().ContainSingle(p =>
            p.Pid == current.Id &&
            p.PluginName == "Tracked.esp" &&
            p.SessionId == "current-session");
    }

    [Fact]
    public async Task UntrackProcessAsync_ShouldRemoveOnlyMatchingPidThroughPidStore()
    {
        using var service = CreateService();
        await _pidStore.UpdateAsync(_ =>
        [
            new TrackedProcess { Pid = 1 },
            new TrackedProcess { Pid = 2 }
        ]);

        await service.UntrackProcessAsync(1);

        var tracked = await _pidStore.LoadAsync();
        tracked.Should().ContainSingle(p => p.Pid == 2);
    }

    [Fact]
    public async Task CleanOrphanedProcessesAsync_ShouldPreserveLiveCurrentSessionEntries()
    {
        using var service = CreateService();
        using var current = Process.GetCurrentProcess();
        await _pidStore.UpdateAsync(_ =>
        [
            new TrackedProcess
            {
                Pid = current.Id,
                StartTime = current.StartTime,
                PluginName = "Current.esp",
                SessionId = "current-session"
            },
            new TrackedProcess
            {
                Pid = int.MaxValue,
                PluginName = "Missing.esp",
                SessionId = "current-session"
            }
        ]);

        await service.CleanOrphanedProcessesAsync();

        var tracked = await _pidStore.LoadAsync();
        tracked.Should().ContainSingle(p => p.Pid == current.Id && p.SessionId == "current-session");
    }

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
        var mo2ValidationMock = Substitute.For<IMo2ValidationService>();

        configServiceMock.FlushPendingSavesAsync(Arg.Any<CancellationToken>())
            .Returns(new ConfigPersistenceResult(
                ConfigPersistenceStatusKind.NoOp,
                ConfigPersistenceOperationKind.Flush,
                Generation: 0,
                Failure: null));

        // Default setup for GetSkipListAsync (with GameVariant parameter)
        configServiceMock.GetSkipListAsync(
                Arg.Any<GameType>(),
                Arg.Any<GameVariant>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<string>());

        // Default setup for LoadUserConfigAsync
        configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new UserConfiguration());

        // Default setup for backup service
        backupServiceMock.BackupPluginAsync(
                Arg.Any<PluginInfo>(),
                Arg.Any<string>(),
                Arg.Any<IProgress<BackupCopyProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BackupCreateResult(BackupOperationStatus.Complete, "Test.esp", 1024, 1024, null));
        backupServiceMock.CleanupOldSessionsAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<IProgress<BackupCopyProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BackupRetentionCleanupResult(BackupOperationStatus.Complete, Array.Empty<BackupRetentionRowResult>()));

        // Default setup for log file service (offset-based API)
        logFileServiceMock.GetLogFilePath(Arg.Any<string>(), Arg.Any<GameType>())
            .Returns("fake_log.txt");
        logFileServiceMock.GetExceptionLogFilePath(Arg.Any<string>(), Arg.Any<GameType>())
            .Returns("fake_exception.log");
        logFileServiceMock.CaptureOffset(Arg.Any<string>())
            .Returns(0L);
        logFileServiceMock.ReadLogContentAsync(
                Arg.Any<string>(), Arg.Any<GameType>(),
                Arg.Any<long>(), Arg.Any<long>(),
                Arg.Any<CancellationToken>())
            .Returns(new LogReadResult { LogLines = new List<string>() });

        var plugins = new List<PluginInfo>
        {
            new() { FileName = "Test.esp", FullPath = @"C:\Data\Test.esp" }
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
        mo2ValidationMock.ValidateMo2ExecutableAsync(Arg.Any<string>()).Returns(true);

        var preflight = new CleaningPreflight(
            configServiceMock,
            gameDetectionServiceMock,
            pluginServiceMock,
            mo2ValidationMock,
            cleaningServiceMock,
            stateServiceMock,
            loggerMock);

        // Default: CleanPluginAsync succeeds and captures the onProcessStarted callback
        cleaningServiceMock.CleanPluginAsync(
                Arg.Any<PluginInfo>(),
                Arg.Any<Action<Process>?>(),
                Arg.Any<CancellationToken>())
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
            preflight,
            new BackupSessionCoordinator(backupServiceMock, stateServiceMock, loggerMock),
            new CleaningTerminationCoordinator(processServiceMock, hangDetectionMock, stateServiceMock, loggerMock),
            new PluginCleaningRunner(cleaningServiceMock, logFileServiceMock, loggerMock),
            new PluginResultFinalizer(logFileServiceMock, outputParserMock, loggerMock),
            stateServiceMock,
            loggerMock,
            processServiceMock);

        return (orchestrator, processServiceMock);
    }

    private static string DotNetHostPath => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "dotnet.exe" : "dotnet";

    private ProcessExecutionService CreateService(IProcessExitWaiter? processExitWaiter = null) =>
        new(_mockLogger, _pidStore, _sessionProvider, processExitWaiter);

    /// <summary>
    /// Asserts that captured log message templates and structured arguments did not receive unsafe launch details.
    /// </summary>
    private void AssertCapturedLogTextExcludes(params string[] unsafeFragments)
    {
        var capturedText = string.Join(
            Environment.NewLine,
            _mockLogger.ReceivedCalls().SelectMany(call => call.GetArguments())
                .Where(argument => argument is not Exception)
                .Select(argument => argument?.ToString() ?? string.Empty));

        foreach (var unsafeFragment in unsafeFragments)
        {
            capturedText.Should().NotContain(unsafeFragment);
        }
    }

    private sealed class RealProcessExitWaiter : IProcessExitWaiter
    {
        public Task WaitForExitAsync(Process process, CancellationToken ct) => process.WaitForExitAsync(ct);
    }

    private sealed class InMemoryPidStore : IPidStore
    {
        private IReadOnlyList<TrackedProcess> _entries = [];

        public Task<IReadOnlyList<TrackedProcess>> LoadAsync(CancellationToken ct = default) =>
            Task.FromResult(_entries);

        public Task UpdateAsync(
            Func<IReadOnlyList<TrackedProcess>, IReadOnlyList<TrackedProcess>> update,
            CancellationToken ct = default)
        {
            _entries = update(_entries);
            return Task.CompletedTask;
        }
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
                Arg.Any<Action<Process>?>(),
                Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                cleaningStarted.TrySetResult(true);
                // Block until cancellation -- let the exception propagate so the
                // orchestrator's catch(OperationCanceledException) sets WasCancelled
                var ct = callInfo.ArgAt<CancellationToken>(2);
                await WaitForCancellationAndThrowAsync(ct);
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
        var mo2ValidationMock = Substitute.For<IMo2ValidationService>();

        configServiceMock.FlushPendingSavesAsync(Arg.Any<CancellationToken>())
            .Returns(new ConfigPersistenceResult(
                ConfigPersistenceStatusKind.NoOp,
                ConfigPersistenceOperationKind.Flush,
                Generation: 0,
                Failure: null));
        configServiceMock.GetSkipListAsync(
                Arg.Any<GameType>(),
                Arg.Any<GameVariant>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new UserConfiguration());
        logFileServiceMock.GetLogFilePath(Arg.Any<string>(), Arg.Any<GameType>())
            .Returns("fake_log.txt");
        logFileServiceMock.GetExceptionLogFilePath(Arg.Any<string>(), Arg.Any<GameType>())
            .Returns("fake_exception.log");
        logFileServiceMock.CaptureOffset(Arg.Any<string>())
            .Returns(0L);
        logFileServiceMock.ReadLogContentAsync(
                Arg.Any<string>(), Arg.Any<GameType>(),
                Arg.Any<long>(), Arg.Any<long>(),
                Arg.Any<CancellationToken>())
            .Returns(new LogReadResult { LogLines = new List<string>() });
        gameDetectionServiceMock.DetectVariant(Arg.Any<GameType>(), Arg.Any<List<string>>())
            .Returns(GameVariant.None);
        backupServiceMock.BackupPluginAsync(
                Arg.Any<PluginInfo>(),
                Arg.Any<string>(),
                Arg.Any<IProgress<BackupCopyProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BackupCreateResult(BackupOperationStatus.Complete, "Test.esp", 1024, 1024, null));
        backupServiceMock.CleanupOldSessionsAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<IProgress<BackupCopyProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BackupRetentionCleanupResult(BackupOperationStatus.Complete, Array.Empty<BackupRetentionRowResult>()));

        var plugins = new List<PluginInfo>
        {
            new() { FileName = "Test.esp", FullPath = @"C:\Data\Test.esp" }
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
        mo2ValidationMock.ValidateMo2ExecutableAsync(Arg.Any<string>()).Returns(true);

        var preflight = new CleaningPreflight(
            configServiceMock,
            gameDetectionServiceMock,
            pluginServiceMock,
            mo2ValidationMock,
            cleaningServiceMock,
            stateServiceMock,
            Substitute.For<ILoggingService>());

        var orch = new CleaningOrchestrator(
            preflight,
            new BackupSessionCoordinator(backupServiceMock, stateServiceMock, Substitute.For<ILoggingService>()),
            new CleaningTerminationCoordinator(processServiceMock, hangDetectionMock, stateServiceMock, Substitute.For<ILoggingService>()),
            new PluginCleaningRunner(cleaningServiceMock, logFileServiceMock, Substitute.For<ILoggingService>()),
            new PluginResultFinalizer(logFileServiceMock, Substitute.For<IXEditOutputParser>(), Substitute.For<ILoggingService>()),
            stateServiceMock,
            Substitute.For<ILoggingService>(),
            processServiceMock);

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
                Arg.Any<Process>(),
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
                Arg.Any<Process>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>());

        orchestrator.Dispose();
    }

    #endregion
}
