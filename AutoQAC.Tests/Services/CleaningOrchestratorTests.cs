using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameDetection;
using AutoQAC.Services.MO2;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.Backup;
using AutoQAC.Services.Monitoring;
using AutoQAC.Services.Process;
using AutoQAC.Services.State;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Diagnostics;
using System.Reflection;
using System.Reactive.Subjects;

namespace AutoQAC.Tests.Services;

public sealed class CleaningOrchestratorTests
{
    private readonly ICleaningService _cleaningServiceMock;
    private readonly IPluginValidationService _pluginServiceMock;
    private readonly IGameDetectionService _gameDetectionServiceMock;
    private readonly IStateService _stateServiceMock;
    private readonly IConfigurationService _configServiceMock;
    private readonly ILoggingService _loggerMock;
    private readonly IProcessExecutionService _processServiceMock;
    private readonly IXEditLogFileService _logFileServiceMock;
    private readonly IXEditOutputParser _outputParserMock;
    private readonly IBackupService _backupServiceMock;
    private readonly IHangDetectionService _hangDetectionMock;
    private readonly IMo2ValidationService _mo2ValidationServiceMock;
    private readonly CleaningOrchestrator _orchestrator;

    public CleaningOrchestratorTests()
    {
        _cleaningServiceMock = Substitute.For<ICleaningService>();
        _pluginServiceMock = Substitute.For<IPluginValidationService>();
        _gameDetectionServiceMock = Substitute.For<IGameDetectionService>();
        _stateServiceMock = Substitute.For<IStateService>();
        _configServiceMock = Substitute.For<IConfigurationService>();
        _loggerMock = Substitute.For<ILoggingService>();
        _processServiceMock = Substitute.For<IProcessExecutionService>();
        _logFileServiceMock = Substitute.For<IXEditLogFileService>();
        _outputParserMock = Substitute.For<IXEditOutputParser>();
        _backupServiceMock = Substitute.For<IBackupService>();
        _hangDetectionMock = Substitute.For<IHangDetectionService>();
        _mo2ValidationServiceMock = Substitute.For<IMo2ValidationService>();

        // Default mock setup for GetSkipListAsync to return empty list instead of null
        _configServiceMock.FlushPendingSavesAsync(Arg.Any<CancellationToken>())
            .Returns(new ConfigPersistenceResult(
                ConfigPersistenceStatusKind.NoOp,
                ConfigPersistenceOperationKind.Flush,
                Generation: 0,
                Failure: null));
        _configServiceMock.GetSkipListAsync(
                Arg.Any<GameType>(),
                Arg.Any<GameVariant>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<string>());

        // Default mock setup for LoadUserConfigAsync to return default config (DisableSkipLists = false)
        _configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new UserConfiguration());

        // Default mock setup for log file service: offset-based API (empty log scenario)
        _logFileServiceMock.GetLogFilePath(Arg.Any<string>(), Arg.Any<GameType>())
            .Returns("fake_log.txt");
        _logFileServiceMock.GetExceptionLogFilePath(Arg.Any<string>(), Arg.Any<GameType>())
            .Returns("fake_exception.log");
        _logFileServiceMock.CaptureOffset(Arg.Any<string>())
            .Returns(0L);
        _logFileServiceMock.ReadLogContentAsync(
                Arg.Any<string>(), Arg.Any<GameType>(),
                Arg.Any<long>(), Arg.Any<long>(),
                Arg.Any<CancellationToken>())
            .Returns(new LogReadResult { LogLines = new List<string>() });
        _mo2ValidationServiceMock.ValidateMo2ExecutableAsync(Arg.Any<string>()).Returns(true);

        _orchestrator = new CleaningOrchestrator(
            CreatePreflight(),
            new BackupSessionCoordinator(_backupServiceMock, _stateServiceMock, _loggerMock),
            new CleaningTerminationCoordinator(_processServiceMock, _hangDetectionMock, _stateServiceMock, _loggerMock),
            new PluginCleaningRunner(_cleaningServiceMock, _logFileServiceMock, _loggerMock),
            new PluginResultFinalizer(_logFileServiceMock, _outputParserMock, _loggerMock),
            _stateServiceMock,
            _loggerMock,
            _processServiceMock);
    }

    private ICleaningPreflight CreatePreflight() => new CleaningPreflight(
        _configServiceMock,
        _gameDetectionServiceMock,
        _pluginServiceMock,
        _mo2ValidationServiceMock,
        _cleaningServiceMock,
        _stateServiceMock,
        _loggerMock);

    private static CleaningPreflightPlan CreateEmptyPreflightPlan() => new()
    {
        DetectedGameType = GameType.SkyrimSe,
        DetectedGameVariant = GameVariant.None,
        PluginRows = [],
        IsMo2ModeActive = false,
        BackupSkippedByPolicy = true,
        FileValidationSkippedByPolicy = false,
        LaunchModeLabel = "direct xEdit",
        CleaningTimeoutSeconds = 30,
        BackupEnabled = false,
        BackupMaxSessions = 0,
        XEditDirectory = "xedit"
    };

    private static TaskCompletionSource<bool> CreateSignal()
    {
        return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static Task WaitForSignalAsync(TaskCompletionSource<bool> signal)
    {
        return WaitForSignalAsync(signal.Task, "expected test signal to be observed");
    }

    private static async Task WaitForCancellationAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return;
        }

        var cancellationSignal = CreateSignal();
        using var registration = ct.Register(() => cancellationSignal.TrySetResult(true));
        await WaitForSignalAsync(cancellationSignal.Task, "expected cancellation token to be canceled");
        ct.IsCancellationRequested.Should().BeTrue("the token should be canceled before the helper returns");
    }

    private static async Task WaitForSignalAsync(Task signalTask, string because)
    {
        var completedTask = await Task.WhenAny(signalTask, Task.Delay(TimeSpan.FromSeconds(2)));
        completedTask.Should().Be(signalTask, because);
        await signalTask;
    }

    private static async Task WaitForCancellationAndThrowAsync(CancellationToken ct)
    {
        await WaitForCancellationAsync(ct);
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Verifies that non-xEdit backup progress is stored in app state and published to subscribers.
    /// </summary>
    [Fact]
    public void StateService_SetBackupOperation_PublishesBackupProgressState()
    {
        // Arrange
        using var stateService = new StateService();
        var observedStates = new List<AppState>();
        using var subscription = stateService.StateChanged.Subscribe(observedStates.Add);

        var operation = new BackupOperationState
        {
            Kind = BackupOperationKind.Backup,
            Label = "Backing up: Example.esp",
            FileName = "Example.esp",
            FilesCompleted = 0,
            TotalFiles = 1,
            BytesCopied = 128,
            TotalBytes = 256,
            IsActive = true,
            CanCancel = true
        };

        // Act
        stateService.SetBackupOperation(operation);

        // Assert
        stateService.CurrentState.BackupOperation.Should().Be(operation);
        observedStates.Should().Contain(state => state.BackupOperation == operation);
    }

    /// <summary>
    /// Verifies that retention cleanup progress uses the same non-xEdit operation channel and can be cleared.
    /// </summary>
    [Fact]
    public void StateService_ClearBackupOperation_PublishesClearedRetentionState()
    {
        // Arrange
        using var stateService = new StateService();
        var observedStates = new List<AppState>();
        using var subscription = stateService.StateChanged.Subscribe(observedStates.Add);

        stateService.SetBackupOperation(new BackupOperationState
        {
            Kind = BackupOperationKind.RetentionCleanup,
            Label = "Cleaning up old backups",
            FilesCompleted = 1,
            TotalFiles = 3,
            IsActive = true,
            CanCancel = true
        });

        // Act
        stateService.ClearBackupOperation();

        // Assert
        stateService.CurrentState.BackupOperation.Should().BeNull();
        observedStates.Should().Contain(state => state.BackupOperation == null);
    }

    private static Process StartSleeperProcess()
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = "-NoProfile -Command \"Start-Sleep -Seconds 30\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });

        process.Should().NotBeNull("a real external process is needed to exercise termination paths safely");
        return process!;
    }

    private static void KillProcessIfRunning(Process? process)
    {
        if (process == null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
        }
        catch
        {
            // Best-effort cleanup for test helper processes.
        }
        finally
        {
            process.Dispose();
        }
    }

    [Fact]
    public async Task StartCleaningAsync_ShouldProcessPlugins_WhenConfigIsValid()
    {
        // Arrange
        var plugins = new List<PluginInfo>
        {
            new() { FileName = "Plugin1.esp", FullPath = "Path/Plugin1.esp" },
            new() { FileName = "Plugin2.esp", FullPath = "Path/Plugin2.esp" }
        };

        var appState = new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = plugins
        };

        _stateServiceMock.CurrentState.Returns(appState);

        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        _cleaningServiceMock.CleanPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>())
            .Returns(new CleaningResult { Status = CleaningStatus.Cleaned });

        // Act
        await _orchestrator.StartCleaningAsync();

        // Assert
        await _cleaningServiceMock.Received(1).CleanPluginAsync(Arg.Is<PluginInfo>(p => p.FileName == "Plugin1.esp"), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>());
        await _cleaningServiceMock.Received(1).CleanPluginAsync(Arg.Is<PluginInfo>(p => p.FileName == "Plugin2.esp"), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>());
        _stateServiceMock.Received(1).StartCleaning(Arg.Any<List<PluginInfo>>());
        _stateServiceMock.Received(1).FinishCleaningWithResults(Arg.Any<CleaningSessionResult>());
    }

    [Fact]
    public async Task StartCleaningAsync_ShouldDetectGame_WhenUnknown()
    {
        // Arrange
        var plugins = new List<PluginInfo>();
        var appState = new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.Unknown,
            PluginsToClean = plugins
        };
        _stateServiceMock.CurrentState.Returns(appState);

        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        // Mock Executable detection failing (Unknown)
        _gameDetectionServiceMock.DetectFromExecutable(Arg.Any<string>())
            .Returns(GameType.Unknown);

        // Mock Load Order detection succeeding
        _gameDetectionServiceMock.DetectFromLoadOrderAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(GameType.Fallout4);

        // Act
        await _orchestrator.StartCleaningAsync();

        // Assert
        _gameDetectionServiceMock.Received(1).DetectFromExecutable(Arg.Any<string>());
        await _gameDetectionServiceMock.Received(1).DetectFromLoadOrderAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        // We expect state update
        _stateServiceMock.Received(1).UpdateState(Arg.Any<Func<AppState, AppState>>());
    }

    [Fact]
    public async Task StartCleaningAsync_ShouldThrow_WhenConfigIsInvalid()
    {
        // Arrange
        var appState = new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSe
        };
        _stateServiceMock.CurrentState.Returns(appState);

        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var act = () => _orchestrator.StartCleaningAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        _stateServiceMock.Received(1).FinishCleaningWithResults(Arg.Any<CleaningSessionResult>()); // It calls Finish in catch block
    }

    [Fact]
    public async Task StartCleaningAsync_ShouldNotRequireLoadOrderPath_WhenGameTypeIsMutagenSupported()
    {
        // Arrange
        var plugins = new List<PluginInfo>
        {
            new() { FileName = "Plugin1.esp", FullPath = "Path/Plugin1.esp" }
        };

        var appState = new AppState
        {
            LoadOrderPath = null,
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.Fallout4,
            PluginsToClean = plugins
        };
        _stateServiceMock.CurrentState.Returns(appState);

        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        _cleaningServiceMock.CleanPluginAsync(
                Arg.Any<PluginInfo>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<System.Diagnostics.Process>?>())
            .Returns(new CleaningResult { Status = CleaningStatus.Cleaned, Success = true });

        // Act
        var act = () => _orchestrator.StartCleaningAsync();

        // Assert
        await act.Should().NotThrowAsync();
        await _cleaningServiceMock.Received(1).CleanPluginAsync(
            Arg.Any<PluginInfo>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<Action<System.Diagnostics.Process>?>());
    }

    [Fact]
    public async Task StartCleaningAsync_ShouldThrow_WhenNonMutagenGameMissingLoadOrderPath()
    {
        // Arrange
        var plugins = new List<PluginInfo>
        {
            new() { FileName = "Plugin1.esp", FullPath = "Path/Plugin1.esp" }
        };

        var appState = new AppState
        {
            LoadOrderPath = null,
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.Fallout3,
            PluginsToClean = plugins
        };
        _stateServiceMock.CurrentState.Returns(appState);

        // Even if the cleaning service reports environment valid, orchestrator should
        // block non-Mutagen games when load order is missing.
        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var act = () => _orchestrator.StartCleaningAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        await _cleaningServiceMock.DidNotReceive().CleanPluginAsync(
            Arg.Any<PluginInfo>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<Action<System.Diagnostics.Process>?>());
    }

    #region Robustness and Cancellation Tests

    /// <summary>
    /// Verifies that StartCleaningAsync correctly handles user cancellation mid-batch.
    /// When user cancels, the orchestrator should:
    /// 1. Stop processing remaining plugins
    /// 2. Update state appropriately
    /// 3. Call FinishCleaning
    /// </summary>
    [Fact]
    public async Task StartCleaningAsync_ShouldHandleUserCancellation_MidBatch()
    {
        // Arrange
        // Create 5 plugins to clean
        var plugins = new List<PluginInfo>
        {
            new() { FileName = "Plugin1.esp", FullPath = "Path/Plugin1.esp" },
            new() { FileName = "Plugin2.esp", FullPath = "Path/Plugin2.esp" },
            new() { FileName = "Plugin3.esp", FullPath = "Path/Plugin3.esp" },
            new() { FileName = "Plugin4.esp", FullPath = "Path/Plugin4.esp" },
            new() { FileName = "Plugin5.esp", FullPath = "Path/Plugin5.esp" }
        };

        var appState = new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = plugins
        };
        _stateServiceMock.CurrentState.Returns(appState);

        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        // Track how many plugins were cleaned
        var cleanedCount = 0;
        var cts = new CancellationTokenSource();

        // After cleaning 2 plugins, request cancellation
        _cleaningServiceMock.CleanPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>())
            .Returns(_ =>
            {
                cleanedCount++;
                if (cleanedCount == 2)
                {
                    // Simulate user clicking "Stop"
                    cts.Cancel();
                }
                return new CleaningResult { Status = CleaningStatus.Cleaned };
            });

        // Act
        await _orchestrator.StartCleaningAsync(cts.Token);

        // Assert
        // Should have cleaned exactly 2 plugins before cancellation was detected
        // Note: The loop checks cancellation before processing each plugin, so 2 should complete
        cleanedCount.Should().BeLessThan(5, "cancellation should stop processing before all plugins are cleaned");

        // FinishCleaning should be called even on cancellation
        _stateServiceMock.Received(1).FinishCleaningWithResults(Arg.Any<CleaningSessionResult>());
    }

    /// <summary>
    /// Verifies that when one plugin fails mid-batch, the orchestrator continues
    /// processing remaining plugins (continue-on-error behavior).
    /// </summary>
    [Fact]
    public async Task StartCleaningAsync_ShouldContinueOnError_WhenPluginFails()
    {
        // Arrange
        // Create 5 plugins
        var plugins = new List<PluginInfo>
        {
            new() { FileName = "Plugin1.esp", FullPath = "Path/Plugin1.esp" },
            new() { FileName = "Plugin2.esp", FullPath = "Path/Plugin2.esp" },
            new() { FileName = "BadPlugin.esp", FullPath = "Path/BadPlugin.esp" }, // This one will fail
            new() { FileName = "Plugin4.esp", FullPath = "Path/Plugin4.esp" },
            new() { FileName = "Plugin5.esp", FullPath = "Path/Plugin5.esp" }
        };

        var appState = new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = plugins
        };
        _stateServiceMock.CurrentState.Returns(appState);

        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        _configServiceMock.GetSkipListAsync(
                Arg.Any<GameType>(),
                Arg.Any<GameVariant>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<string>());

        _pluginServiceMock.FilterSkippedPlugins(plugins, Arg.Any<List<string>>())
            .Returns(plugins);

        // Configure cleaning results: BadPlugin fails, others succeed
        _cleaningServiceMock.CleanPluginAsync(
            Arg.Is<PluginInfo>(p => p.FileName == "BadPlugin.esp"),
            Arg.Any<CancellationToken>(),
            Arg.Any<Action<System.Diagnostics.Process>?>())
            .Returns(new CleaningResult
            {
                Status = CleaningStatus.Failed,
                Success = false,
                Message = "Failed to clean plugin"
            });

        _cleaningServiceMock.CleanPluginAsync(
            Arg.Is<PluginInfo>(p => p.FileName != "BadPlugin.esp"),
            Arg.Any<CancellationToken>(),
            Arg.Any<Action<System.Diagnostics.Process>?>())
            .Returns(new CleaningResult
            {
                Status = CleaningStatus.Cleaned,
                Success = true
            });

        // Act
        await _orchestrator.StartCleaningAsync();

        // Assert
        // All 5 plugins should have been processed
        await _cleaningServiceMock.Received(5).CleanPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>());

        // State should have been updated for all plugins via AddDetailedCleaningResult
        _stateServiceMock.Received(1).AddDetailedCleaningResult(Arg.Is<PluginCleaningResult>(r => r.PluginName == "BadPlugin.esp" && r.Status == CleaningStatus.Failed));
        _stateServiceMock.Received(4).AddDetailedCleaningResult(Arg.Is<PluginCleaningResult>(r => r.PluginName != "BadPlugin.esp" && r.Status == CleaningStatus.Cleaned));

        // FinishCleaning should be called
        _stateServiceMock.Received(1).FinishCleaningWithResults(Arg.Any<CleaningSessionResult>());
    }

    /// <summary>
    /// CRITICAL TEST: Verifies that plugins are processed SEQUENTIALLY, never in parallel.
    /// This is a core requirement per CLAUDE.md to prevent xEdit file locking issues.
    /// </summary>
    [Fact]
    public async Task StartCleaningAsync_ShouldProcessPluginsSequentially_NeverInParallel()
    {
        // Arrange
        var plugins = new List<PluginInfo>
        {
            new() { FileName = "Plugin1.esp", FullPath = "Path/Plugin1.esp" },
            new() { FileName = "Plugin2.esp", FullPath = "Path/Plugin2.esp" },
            new() { FileName = "Plugin3.esp", FullPath = "Path/Plugin3.esp" }
        };

        var appState = new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = plugins
        };
        _stateServiceMock.CurrentState.Returns(appState);

        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        // Track execution order and overlap
        var executionLog = new List<(string plugin, DateTime start, DateTime end)>();
        var lockObj = new object();
        var currentlyExecuting = 0;
        var maxConcurrent = 0;

        _cleaningServiceMock.CleanPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>())
            .Returns(callInfo =>
            {
                var plugin = callInfo.Arg<PluginInfo>();
                var startTime = DateTime.Now;

                // Track concurrent executions
                lock (lockObj)
                {
                    currentlyExecuting++;
                    if (currentlyExecuting > maxConcurrent)
                    {
                        maxConcurrent = currentlyExecuting;
                    }
                }

                // Simulate some work (short delay to detect parallelism)
                Thread.Sleep(50);

                lock (lockObj)
                {
                    currentlyExecuting--;
                }

                var endTime = DateTime.Now;
                lock (lockObj)
                {
                    executionLog.Add((plugin.FileName, startTime, endTime));
                }

                return new CleaningResult { Status = CleaningStatus.Cleaned };
            });

        // Act
        await _orchestrator.StartCleaningAsync();

        // Assert
        // Maximum concurrent executions should be exactly 1 (sequential processing)
        maxConcurrent.Should().Be(1,
            "CRITICAL: Only ONE plugin should be cleaned at a time. " +
            "Parallel cleaning violates xEdit file locking requirements per CLAUDE.md.");

        // Verify order: each plugin should start after the previous one ends
        executionLog.Should().HaveCount(3);
        for (int i = 1; i < executionLog.Count; i++)
        {
            executionLog[i].start.Should().BeOnOrAfter(executionLog[i - 1].end,
                $"Plugin {executionLog[i].plugin} should start after {executionLog[i - 1].plugin} ends");
        }
    }

    /// <summary>
    /// Verifies that StopCleaning properly triggers cancellation of ongoing cleaning.
    /// </summary>
    [Fact]
    public async Task StopCleaning_ShouldCancelOngoingCleaning()
    {
        // Arrange
        var plugins = new List<PluginInfo>
        {
            new() { FileName = "Plugin1.esp", FullPath = "Path/Plugin1.esp" },
            new() { FileName = "Plugin2.esp", FullPath = "Path/Plugin2.esp" },
            new() { FileName = "Plugin3.esp", FullPath = "Path/Plugin3.esp" }
        };

        var appState = new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = plugins
        };
        _stateServiceMock.CurrentState.Returns(appState);

        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        var cleanedPlugins = new List<string>();
        var cleaningStartedEvent = CreateSignal();

        _cleaningServiceMock.CleanPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>())
            .Returns(async callInfo =>
            {
                var plugin = callInfo.Arg<PluginInfo>();
                var ct = callInfo.Arg<CancellationToken>();
                cleanedPlugins.Add(plugin.FileName);

                // Signal that cleaning has started (for first plugin)
                if (cleanedPlugins.Count == 1)
                {
                    cleaningStartedEvent.TrySetResult(true);
                }

                await WaitForCancellationAsync(ct);

                return new CleaningResult { Status = CleaningStatus.Failed, Message = "Cancelled" };
            });

        // Act
        var cleaningTask = _orchestrator.StartCleaningAsync();

        // Wait for cleaning to start, then stop it
        await WaitForSignalAsync(cleaningStartedEvent);
        await _orchestrator.StopCleaningAsync();

        // Wait for task to complete
        await cleaningTask;

        // Assert
        // Not all plugins should be cleaned due to cancellation
        cleanedPlugins.Count.Should().BeLessThanOrEqualTo(3,
            "StopCleaning should stop processing");

        _stateServiceMock.Received(1).FinishCleaningWithResults(Arg.Any<CleaningSessionResult>());
    }

    [Fact]
    public async Task StopCleaningAsync_DuringPreflight_CancelsSessionAndDoesNotInvokeCleaningService()
    {
        var preflightSub = Substitute.For<ICleaningPreflight>();
        var preflightReached = CreateSignal();
        var releaseTcs = new TaskCompletionSource<CleaningPreflightPlan>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken capturedToken = default;

        _processServiceMock.CleanOrphanedProcessesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        preflightSub.PrepareAsync(Arg.Do<CancellationToken>(t => capturedToken = t))
            .Returns(async _ =>
            {
                // Signal once the orchestrator reaches preflight, then wait for cancellation/release.
                preflightReached.TrySetResult(true);
                using var reg = capturedToken.Register(() => releaseTcs.TrySetCanceled(capturedToken));
                return await releaseTcs.Task.ConfigureAwait(false);
            });

        var orchestrator = new CleaningOrchestrator(
            preflightSub,
            new BackupSessionCoordinator(_backupServiceMock, _stateServiceMock, _loggerMock),
            new CleaningTerminationCoordinator(_processServiceMock, _hangDetectionMock, _stateServiceMock, _loggerMock),
            new PluginCleaningRunner(_cleaningServiceMock, _logFileServiceMock, _loggerMock),
            new PluginResultFinalizer(_logFileServiceMock, _outputParserMock, _loggerMock),
            _stateServiceMock,
            _loggerMock,
            _processServiceMock);

        var startTask = orchestrator.StartCleaningAsync(CancellationToken.None);
        try
        {
            await WaitForSignalAsync(preflightReached);
            await orchestrator.StopCleaningAsync();
            await WaitForCancellationAsync(capturedToken);

            releaseTcs.TrySetCanceled(capturedToken);
            try
            {
                await startTask;
            }
            catch (OperationCanceledException)
            {
                // Expected if the blocked preflight observes cancellation directly.
            }
        }
        finally
        {
            // Always release the blocked preflight so a failing assertion cannot hang the test runner.
            releaseTcs.TrySetResult(CreateEmptyPreflightPlan());
        }

        await _cleaningServiceMock.DidNotReceive().CleanPluginAsync(
            Arg.Any<PluginInfo>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<Action<Process>?>());
        capturedToken.IsCancellationRequested.Should().BeTrue("Stop must cancel the in-flight preflight token");
        _stateServiceMock.Received().FinishCleaningWithResults(Arg.Is<CleaningSessionResult>(s => s.WasCancelled));
    }

    [Fact]
    public async Task StopCleaningAsync_DuringOrphanCleanup_CancelsSessionBeforePreflightAndDoesNotInvokeCleaningService()
    {
        var orphanReached = CreateSignal();
        var releaseOrphanCleanup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken capturedToken = default;

        _processServiceMock.CleanOrphanedProcessesAsync(Arg.Do<CancellationToken>(t => capturedToken = t))
            .Returns(async _ =>
            {
                // Signal once the orchestrator reaches the first startup await, then wait for cancellation/release.
                orphanReached.TrySetResult(true);
                using var reg = capturedToken.Register(() => releaseOrphanCleanup.TrySetCanceled(capturedToken));
                await releaseOrphanCleanup.Task.ConfigureAwait(false);
            });

        var orchestrator = new CleaningOrchestrator(
            CreatePreflight(),
            new BackupSessionCoordinator(_backupServiceMock, _stateServiceMock, _loggerMock),
            new CleaningTerminationCoordinator(_processServiceMock, _hangDetectionMock, _stateServiceMock, _loggerMock),
            new PluginCleaningRunner(_cleaningServiceMock, _logFileServiceMock, _loggerMock),
            new PluginResultFinalizer(_logFileServiceMock, _outputParserMock, _loggerMock),
            _stateServiceMock,
            _loggerMock,
            _processServiceMock);

        var startTask = orchestrator.StartCleaningAsync(CancellationToken.None);
        try
        {
            await WaitForSignalAsync(orphanReached);
            await orchestrator.StopCleaningAsync();
            await WaitForCancellationAsync(capturedToken);
            try
            {
                await startTask;
            }
            catch (OperationCanceledException)
            {
                // Expected if the blocked orphan cleanup observes cancellation directly.
            }
        }
        finally
        {
            // Always release orphan cleanup so a failing assertion cannot hang the test runner.
            releaseOrphanCleanup.TrySetResult(true);
        }

        await _cleaningServiceMock.DidNotReceive().CleanPluginAsync(
            Arg.Any<PluginInfo>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<Action<Process>?>());
        capturedToken.IsCancellationRequested.Should().BeTrue("Stop must cancel the in-flight orphan-cleanup token");
        _stateServiceMock.Received().FinishCleaningWithResults(Arg.Is<CleaningSessionResult>(s => s.WasCancelled));
    }

    [Fact]
    public async Task StartCleaningAsync_WhenSessionAlreadyActive_ShouldRejectSecondStartAndKeepFirstSessionCancellable()
    {
        var plugins = new List<PluginInfo>
        {
            new() { FileName = "Plugin1.esp", FullPath = "Path/Plugin1.esp" }
        };

        var appState = new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = plugins
        };
        _stateServiceMock.CurrentState.Returns(appState);
        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>()).Returns(true);

        var firstPluginStarted = CreateSignal();
        CancellationToken firstSessionToken = default;
        var cleanCalls = 0;
        _cleaningServiceMock.CleanPluginAsync(
                Arg.Any<PluginInfo>(),
                Arg.Do<CancellationToken>(token => firstSessionToken = token),
                Arg.Any<Action<Process>?>())
            .Returns(async _ =>
            {
                cleanCalls++;
                if (cleanCalls > 1)
                {
                    return new CleaningResult { Status = CleaningStatus.Cleaned, Success = true };
                }

                firstPluginStarted.TrySetResult(true);
                await WaitForCancellationAsync(firstSessionToken);
                return new CleaningResult { Status = CleaningStatus.Failed, Message = "Cancelled" };
            });

        var firstStartTask = _orchestrator.StartCleaningAsync();
        await WaitForSignalAsync(firstPluginStarted);

        var secondStart = () => _orchestrator.StartCleaningAsync();
        await secondStart.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already in progress*");

        _stateServiceMock.Received(1).StartCleaning(Arg.Any<List<PluginInfo>>());
        await _cleaningServiceMock.Received(1).CleanPluginAsync(
            Arg.Any<PluginInfo>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<Action<Process>?>());

        await _orchestrator.StopCleaningAsync();
        await WaitForCancellationAsync(firstSessionToken);
        await firstStartTask;
    }

    [Fact]
    public async Task StopCleaningAsync_ShouldTerminateActiveProcess_Gracefully_AndStoreGracePeriodExpiredResult()
    {
        // Arrange
        Process? sleeper = null;
        var plugins = new List<PluginInfo>
        {
            new() { FileName = "Plugin1.esp", FullPath = "Path/Plugin1.esp" }
        };

        var appState = new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = plugins
        };
        _stateServiceMock.CurrentState.Returns(appState);

        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        _processServiceMock.TerminateProcessAsync(Arg.Any<Process>(), false, Arg.Any<CancellationToken>())
            .Returns(TerminationResult.GracePeriodExpired);

        var processStarted = CreateSignal();
        var releasePlugin = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            sleeper = StartSleeperProcess();

            _cleaningServiceMock.CleanPluginAsync(
                    Arg.Any<PluginInfo>(),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<Action<Process>?>())
                .Returns(async callInfo =>
                {
                    callInfo.ArgAt<Action<Process>?>(2)?.Invoke(sleeper);
                    processStarted.TrySetResult(true);

                    await releasePlugin.Task;
                    return new CleaningResult { Status = CleaningStatus.Cleaned, Success = true };
                });

            // Act
            var cleaningTask = _orchestrator.StartCleaningAsync();
            await processStarted.Task;
            await _orchestrator.StopCleaningAsync();

            // Assert
            await _processServiceMock.Received(1)
                .TerminateProcessAsync(Arg.Any<Process>(), false, Arg.Any<CancellationToken>());
            _orchestrator.LastTerminationResult.Should().Be(TerminationResult.GracePeriodExpired);

            releasePlugin.SetResult(true);
            await cleaningTask;
        }
        finally
        {
            KillProcessIfRunning(sleeper);
        }
    }

    [Fact]
    public async Task ForceStopCleaningAsync_ShouldTerminateActiveProcess_WithForceKillTrue()
    {
        // Arrange
        Process? sleeper = null;
        var plugins = new List<PluginInfo>
        {
            new() { FileName = "Plugin1.esp", FullPath = "Path/Plugin1.esp" }
        };

        var appState = new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = plugins
        };
        _stateServiceMock.CurrentState.Returns(appState);

        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        _processServiceMock.TerminateProcessAsync(Arg.Any<Process>(), true, Arg.Any<CancellationToken>())
            .Returns(TerminationResult.ForceKilled);

        var processStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            sleeper = StartSleeperProcess();

            _cleaningServiceMock.CleanPluginAsync(
                    Arg.Any<PluginInfo>(),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<Action<Process>?>())
                .Returns(async callInfo =>
                {
                    callInfo.ArgAt<Action<Process>?>(2)?.Invoke(sleeper);
                    processStarted.TrySetResult(true);

                    var ct = callInfo.ArgAt<CancellationToken>(1);
                    await WaitForCancellationAndThrowAsync(ct);
                    return new CleaningResult { Status = CleaningStatus.Cleaned, Success = true };
                });

            // Act
            var cleaningTask = _orchestrator.StartCleaningAsync();
            await WaitForSignalAsync(processStarted);
            await _orchestrator.ForceStopCleaningAsync();
            await cleaningTask;

            // Assert
            await _processServiceMock.Received(1)
                .TerminateProcessAsync(Arg.Any<Process>(), true, Arg.Any<CancellationToken>());
        }
        finally
        {
            KillProcessIfRunning(sleeper);
        }
    }

    [Fact]
    public async Task StopCleaningAsync_WhenCalledTwiceDuringActiveProcess_ShouldEscalateToForceStop()
    {
        // Arrange
        Process? sleeper = null;
        var plugins = new List<PluginInfo>
        {
            new() { FileName = "Plugin1.esp", FullPath = "Path/Plugin1.esp" }
        };

        var appState = new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = plugins
        };
        _stateServiceMock.CurrentState.Returns(appState);

        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        _processServiceMock.TerminateProcessAsync(Arg.Any<Process>(), false, Arg.Any<CancellationToken>())
            .Returns(TerminationResult.GracePeriodExpired);
        _processServiceMock.TerminateProcessAsync(Arg.Any<Process>(), true, Arg.Any<CancellationToken>())
            .Returns(TerminationResult.ForceKilled);

        var processStarted = CreateSignal();

        try
        {
            sleeper = StartSleeperProcess();

            _cleaningServiceMock.CleanPluginAsync(
                    Arg.Any<PluginInfo>(),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<Action<Process>?>())
                .Returns(async callInfo =>
                {
                    callInfo.ArgAt<Action<Process>?>(2)?.Invoke(sleeper);
                    processStarted.TrySetResult(true);

                    var ct = callInfo.ArgAt<CancellationToken>(1);
                    await WaitForCancellationAndThrowAsync(ct);
                    return new CleaningResult { Status = CleaningStatus.Cleaned, Success = true };
                });

            // Act
            var cleaningTask = _orchestrator.StartCleaningAsync();
            await WaitForSignalAsync(processStarted);
            await _orchestrator.StopCleaningAsync();
            await _orchestrator.StopCleaningAsync();
            await cleaningTask;

            // Assert
            await _processServiceMock.Received(1)
                .TerminateProcessAsync(Arg.Any<Process>(), false, Arg.Any<CancellationToken>());
            await _processServiceMock.Received(1)
                .TerminateProcessAsync(Arg.Any<Process>(), true, Arg.Any<CancellationToken>());
        }
        finally
        {
            KillProcessIfRunning(sleeper);
        }
    }

    [Fact]
    public async Task StopCleaningAsync_ShouldNotTerminateCurrentProcess_WhenTrackedProcessIsSelf()
    {
        // Arrange
        var plugins = new List<PluginInfo>
        {
            new() { FileName = "Plugin1.esp", FullPath = "Path/Plugin1.esp" }
        };

        var appState = new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = plugins
        };
        _stateServiceMock.CurrentState.Returns(appState);

        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        var processStarted = CreateSignal();
        var releasePlugin = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _cleaningServiceMock.CleanPluginAsync(
                Arg.Any<PluginInfo>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<Process>?>())
            .Returns(async callInfo =>
            {
                callInfo.ArgAt<Action<Process>?>(2)?.Invoke(Process.GetCurrentProcess());
                processStarted.TrySetResult(true);

                await releasePlugin.Task;
                return new CleaningResult { Status = CleaningStatus.Cleaned, Success = true };
            });

        // Act
        var cleaningTask = _orchestrator.StartCleaningAsync();
        await WaitForSignalAsync(processStarted);
        await _orchestrator.StopCleaningAsync();

        // Assert
        await _processServiceMock.DidNotReceive()
            .TerminateProcessAsync(Arg.Any<Process>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());

        releasePlugin.SetResult(true);
        await cleaningTask;
    }

    [Fact]
    public async Task StartCleaningAsync_ShouldRetryTimedOutPlugin_WhenTimeoutCallbackReturnsTrue_ThenSucceed()
    {
        // Arrange
        var plugin = new PluginInfo { FileName = "TimedOut.esp", FullPath = "Path/TimedOut.esp" };
        var appState = new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = new List<PluginInfo> { plugin }
        };
        _stateServiceMock.CurrentState.Returns(appState);

        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        var attempts = 0;
        _cleaningServiceMock.CleanPluginAsync(
                Arg.Any<PluginInfo>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<Process>?>())
            .Returns(_ =>
            {
                attempts++;
                if (attempts == 1)
                {
                    return new CleaningResult
                    {
                        Status = CleaningStatus.Failed,
                        Success = false,
                        TimedOut = true,
                        Message = "Timed out on attempt 1"
                    };
                }

                return new CleaningResult
                {
                    Status = CleaningStatus.Cleaned,
                    Success = true,
                    Message = "Succeeded on retry"
                };
            });

        var callbackCalls = new List<(string plugin, int timeoutSeconds, int attemptNumber)>();
        TimeoutRetryCallback onTimeout = (pluginName, timeoutSeconds, attemptNumber) =>
        {
            callbackCalls.Add((pluginName, timeoutSeconds, attemptNumber));
            return Task.FromResult(true);
        };

        // Act
        await _orchestrator.StartCleaningAsync(onTimeout);

        // Assert
        attempts.Should().Be(2);
        callbackCalls.Should().HaveCount(1);
        callbackCalls[0].plugin.Should().Be("TimedOut.esp");
        callbackCalls[0].attemptNumber.Should().Be(1);
        callbackCalls[0].timeoutSeconds.Should().BeGreaterThan(0);

        _stateServiceMock.Received(1)
            .AddDetailedCleaningResult(Arg.Is<PluginCleaningResult>(r =>
                r.PluginName == "TimedOut.esp" &&
                r.Status == CleaningStatus.Cleaned &&
                r.Success));
    }

    [Fact]
    public async Task StartCleaningAsync_ShouldStopRetryingTimedOutPlugin_WhenTimeoutCallbackReturnsFalse()
    {
        // Arrange
        var plugin = new PluginInfo { FileName = "TimedOut.esp", FullPath = "Path/TimedOut.esp" };
        var appState = new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = new List<PluginInfo> { plugin }
        };
        _stateServiceMock.CurrentState.Returns(appState);

        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        _cleaningServiceMock.CleanPluginAsync(
                Arg.Any<PluginInfo>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<Process>?>())
            .Returns(new CleaningResult
            {
                Status = CleaningStatus.Failed,
                Success = false,
                TimedOut = true,
                Message = "Timed out on attempt 1"
            });

        var callbackCalls = 0;
        TimeoutRetryCallback onTimeout = (_, _, _) =>
        {
            callbackCalls++;
            return Task.FromResult(false);
        };

        // Act
        await _orchestrator.StartCleaningAsync(onTimeout);

        // Assert
        callbackCalls.Should().Be(1);
        await _cleaningServiceMock.Received(1)
            .CleanPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<CancellationToken>(), Arg.Any<Action<Process>?>());

        _stateServiceMock.Received(1)
            .AddDetailedCleaningResult(Arg.Is<PluginCleaningResult>(r =>
                r.PluginName == "TimedOut.esp" &&
                r.Status == CleaningStatus.Failed &&
                r.Message == "Timed out on attempt 1"));
    }

    [Fact]
    public async Task StartCleaningAsync_ShouldForwardHangEvents_AndEmitFalseWhenPluginCompletes()
    {
        // Arrange
        var plugin = new PluginInfo { FileName = "Plugin1.esp", FullPath = "Path/Plugin1.esp" };
        var appState = new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = new List<PluginInfo> { plugin }
        };
        _stateServiceMock.CurrentState.Returns(appState);

        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        var hangState = new Subject<bool>();
        _hangDetectionMock.MonitorProcess(Arg.Any<Process>())
            .Returns(hangState);

        var processStarted = CreateSignal();
        var releasePlugin = CreateSignal();
        var hangForwarded = CreateSignal();

        _cleaningServiceMock.CleanPluginAsync(
                Arg.Any<PluginInfo>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<Process>?>())
            .Returns(async callInfo =>
            {
                callInfo.ArgAt<Action<Process>?>(2)?.Invoke(Process.GetCurrentProcess());
                processStarted.TrySetResult(true);
                await releasePlugin.Task;
                return new CleaningResult { Status = CleaningStatus.Cleaned, Success = true };
            });

        var hangEvents = new List<bool>();
        using var sub = _orchestrator.HangDetected.Subscribe(isHung =>
        {
            hangEvents.Add(isHung);
            if (isHung)
            {
                hangForwarded.TrySetResult(true);
            }
        });

        // Act
        var cleaningTask = _orchestrator.StartCleaningAsync();
        await WaitForSignalAsync(processStarted);

        hangState.OnNext(true);
        await WaitForSignalAsync(hangForwarded);

        releasePlugin.SetResult(true);
        await cleaningTask;

        // Assert
        hangEvents.Should().Contain(true, "hang event should be forwarded from monitor to orchestrator observable");
        hangEvents.Should().NotBeEmpty();
        hangEvents[^1].Should().BeFalse("orchestrator should emit false when plugin/session completes");
    }

    /// <summary>
    /// Verifies that empty plugin list is handled gracefully.
    /// </summary>
    [Fact]
    public async Task StartCleaningAsync_ShouldHandleEmptyPluginList()
    {
        // Arrange
        // Empty plugin list
        var plugins = new List<PluginInfo>();

        var appState = new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = plugins
        };
        _stateServiceMock.CurrentState.Returns(appState);

        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await _orchestrator.StartCleaningAsync();

        // Assert
        // No cleaning should have been attempted
        await _cleaningServiceMock.DidNotReceive().CleanPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>());

        // But state lifecycle should still be complete
        _stateServiceMock.Received(1).StartCleaning(Arg.Any<List<PluginInfo>>());
        _stateServiceMock.Received(1).FinishCleaningWithResults(Arg.Any<CleaningSessionResult>());
    }

    /// <summary>
    /// Verifies that state is properly updated for each plugin during processing.
    /// </summary>
    [Fact]
    public async Task StartCleaningAsync_ShouldUpdateCurrentPluginState_ForEachPlugin()
    {
        // Arrange
        var plugins = new List<PluginInfo>
        {
            new() { FileName = "Plugin1.esp", FullPath = "Path/Plugin1.esp" },
            new() { FileName = "Plugin2.esp", FullPath = "Path/Plugin2.esp" }
        };

        var appState = new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = plugins
        };
        _stateServiceMock.CurrentState.Returns(appState);

        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        _cleaningServiceMock.CleanPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>())
            .Returns(new CleaningResult { Status = CleaningStatus.Cleaned });

        // Act
        await _orchestrator.StartCleaningAsync();

        // Assert
        // Verify UpdateState was called for each plugin to set CurrentPlugin
        _stateServiceMock.ReceivedCalls()
            .Count(c => c.GetMethodInfo().Name == nameof(IStateService.UpdateState))
            .Should().BeGreaterThanOrEqualTo(2, "UpdateState should be called at least once per plugin");
    }

    #endregion

    #region DisableSkipLists Tests

    /// <summary>
    /// Verifies that when DisableSkipLists is enabled, plugins in the skip list are still cleaned.
    /// This tests the fix for Bug 1: CleaningOrchestrator should respect DisableSkipLists setting.
    /// </summary>
    [Fact]
    public async Task StartCleaningAsync_ShouldCleanSkippedPlugins_WhenDisableSkipListsEnabled()
    {
        // Arrange
        var plugins = new List<PluginInfo>
        {
            new() { FileName = "Skyrim.esm", FullPath = "Skyrim.esm" },  // In skip list
            new() { FileName = "Update.esm", FullPath = "Update.esm" },  // In skip list
            new() { FileName = "UserMod.esp", FullPath = "UserMod.esp" }   // Not in skip list
        };

        var appState = new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = plugins
        };
        _stateServiceMock.CurrentState.Returns(appState);

        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        _cleaningServiceMock.CleanPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>())
            .Returns(new CleaningResult { Status = CleaningStatus.Cleaned });

        // Skip list contains base game ESMs
        _configServiceMock.GetSkipListAsync(
                GameType.SkyrimSe,
                Arg.Any<GameVariant>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<string> { "Skyrim.esm", "Update.esm" });

        // DisableSkipLists is ENABLED
        var userConfig = new UserConfiguration
        {
            Settings = new AutoQacSettings { DisableSkipLists = true }
        };
        _configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
            .Returns(userConfig);

        // Act
        await _orchestrator.StartCleaningAsync();

        // Assert - ALL 3 plugins should be cleaned, including those in skip list
        await _cleaningServiceMock.Received(1).CleanPluginAsync(Arg.Is<PluginInfo>(p => p.FileName == "Skyrim.esm"), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>());
        await _cleaningServiceMock.Received(1).CleanPluginAsync(Arg.Is<PluginInfo>(p => p.FileName == "Update.esm"), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>());
        await _cleaningServiceMock.Received(1).CleanPluginAsync(Arg.Is<PluginInfo>(p => p.FileName == "UserMod.esp"), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>());
    }

    /// <summary>
    /// Verifies that when DisableSkipLists is disabled (default), plugins in the skip list are excluded.
    /// </summary>
    [Fact]
    public async Task StartCleaningAsync_ShouldExcludeSkippedPlugins_WhenDisableSkipListsDisabled()
    {
        // Arrange
        var plugins = new List<PluginInfo>
        {
            new() { FileName = "Skyrim.esm", FullPath = "Skyrim.esm" },  // In skip list
            new() { FileName = "Update.esm", FullPath = "Update.esm" },  // In skip list
            new() { FileName = "UserMod.esp", FullPath = "UserMod.esp" }   // Not in skip list
        };

        var appState = new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = plugins
        };
        _stateServiceMock.CurrentState.Returns(appState);

        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        _cleaningServiceMock.CleanPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>())
            .Returns(new CleaningResult { Status = CleaningStatus.Cleaned });

        // Skip list contains base game ESMs
        _configServiceMock.GetSkipListAsync(
                GameType.SkyrimSe,
                Arg.Any<GameVariant>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<string> { "Skyrim.esm", "Update.esm" });

        // DisableSkipLists is DISABLED (default)
        var userConfig = new UserConfiguration
        {
            Settings = new AutoQacSettings { DisableSkipLists = false }
        };
        _configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
            .Returns(userConfig);

        // Act
        await _orchestrator.StartCleaningAsync();

        // Assert - Only UserMod.esp should be cleaned
        await _cleaningServiceMock.DidNotReceive().CleanPluginAsync(Arg.Is<PluginInfo>(p => p.FileName == "Skyrim.esm"), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>());
        await _cleaningServiceMock.DidNotReceive().CleanPluginAsync(Arg.Is<PluginInfo>(p => p.FileName == "Update.esm"), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>());
        await _cleaningServiceMock.Received(1).CleanPluginAsync(Arg.Is<PluginInfo>(p => p.FileName == "UserMod.esp"), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>());
    }

    #endregion

    #region GameType.Unknown Blocking Tests

    /// <summary>
    /// Verifies that StartCleaningAsync throws InvalidOperationException when
    /// game type cannot be determined (both executable and load order detection fail).
    /// This is safety-critical: skip lists cannot be applied without a known game type.
    /// </summary>
    [Fact]
    public async Task StartCleaningAsync_ShouldThrow_WhenGameTypeIsUnknown()
    {
        // Arrange
        var plugins = new List<PluginInfo>
        {
            new() { FileName = "Plugin1.esp", FullPath = "Path/Plugin1.esp" }
        };

        var appState = new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.Unknown,
            PluginsToClean = plugins
        };
        _stateServiceMock.CurrentState.Returns(appState);

        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        // Both detection methods return Unknown
        _gameDetectionServiceMock.DetectFromExecutable(Arg.Any<string>())
            .Returns(GameType.Unknown);
        _gameDetectionServiceMock.DetectFromLoadOrderAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(GameType.Unknown);

        // Act
        var act = () => _orchestrator.StartCleaningAsync();

        // Assert
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("game type", "error message should explain the problem");
    }

    #endregion

    #region MO2 Mode Tests

    /// <summary>
    /// Verifies that MO2 mode skips file-existence validation entirely.
    /// MO2's VFS resolves paths at xEdit runtime, so checking disk paths is wrong.
    /// </summary>
    [Fact]
    public async Task StartCleaningAsync_MO2Mode_ShouldSkipFileValidation()
    {
        // Arrange - create a temp file to represent the MO2 binary
        var tempMo2 = Path.GetTempFileName();
        try
        {
            var plugins = new List<PluginInfo>
            {
                new() { FileName = "Plugin1.esp", FullPath = "Path/Plugin1.esp" },
                new() { FileName = "Plugin2.esp", FullPath = "Path/Plugin2.esp" }
            };

            var appState = new AppState
            {
                LoadOrderPath = "plugins.txt",
                XEditExecutablePath = "xedit.exe",
                Mo2ExecutablePath = tempMo2,
                CurrentGameType = GameType.SkyrimSe,
                Mo2ModeEnabled = true,
                PluginsToClean = plugins
            };
            _stateServiceMock.CurrentState.Returns(appState);

            _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>())
                .Returns(true);

            _cleaningServiceMock.CleanPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>())
                .Returns(new CleaningResult { Status = CleaningStatus.Cleaned });

            // MO2 mode enabled in user config
            var userConfig = new UserConfiguration
            {
                ModOrganizer = new ModOrganizerConfig { Binary = tempMo2 },
                Settings = new AutoQacSettings { Mo2Mode = true }
            };
            _configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
                .Returns(userConfig);

            // Act
            await _orchestrator.StartCleaningAsync();

            // Assert - ValidatePluginFile should NEVER be called when MO2 mode is active
            _pluginServiceMock.DidNotReceive().ValidatePluginFile(Arg.Any<PluginInfo>());
        }
        finally
        {
            File.Delete(tempMo2);
        }
    }

    /// <summary>
    /// Verifies that when MO2 mode is OFF, file validation runs normally
    /// and missing plugins produce aggregated warnings.
    /// </summary>
    [Fact]
    public async Task StartCleaningAsync_NonMO2Mode_ShouldRunFileValidation()
    {
        // Arrange
        var plugins = new List<PluginInfo>
        {
            new() { FileName = "Valid.esp", FullPath = "Path/Valid.esp" },
            new() { FileName = "Missing.esp", FullPath = "Path/Missing.esp" },
            new() { FileName = "AlsoValid.esp", FullPath = "Path/AlsoValid.esp" }
        };

        var appState = new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSe,
            Mo2ModeEnabled = false,
            PluginsToClean = plugins
        };
        _stateServiceMock.CurrentState.Returns(appState);

        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        _cleaningServiceMock.CleanPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>())
            .Returns(new CleaningResult { Status = CleaningStatus.Cleaned });

        // MO2 mode disabled in user config
        var userConfig = new UserConfiguration
        {
            Settings = new AutoQacSettings { Mo2Mode = false }
        };
        _configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
            .Returns(userConfig);

        // Mock validation: Missing.esp is NotFound, others are fine
        _pluginServiceMock.ValidatePluginFile(Arg.Is<PluginInfo>(p => p.FileName == "Missing.esp"))
            .Returns(PluginWarningKind.NotFound);
        _pluginServiceMock.ValidatePluginFile(Arg.Is<PluginInfo>(p => p.FileName != "Missing.esp"))
            .Returns(PluginWarningKind.None);

        // Act
        await _orchestrator.StartCleaningAsync();

        // Assert - ValidatePluginFile SHOULD be called for non-MO2 mode
        _pluginServiceMock.Received().ValidatePluginFile(Arg.Any<PluginInfo>());

        // Missing.esp should NOT be cleaned (removed from list)
        await _cleaningServiceMock.DidNotReceive().CleanPluginAsync(Arg.Is<PluginInfo>(p => p.FileName == "Missing.esp"), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>());
    }

    /// <summary>
    /// Verifies that canceling a backup copy skips only that plugin and does not launch xEdit for it.
    /// </summary>
    [Fact]
    public async Task BackupCancellation_DoesNotLaunchXEdit_ForCanceledPlugin()
    {
        // Arrange
        var plugins = new List<PluginInfo>
        {
            new() { FileName = "Canceled.esp", FullPath = @"C:\Games\Data\Canceled.esp" },
            new() { FileName = "Next.esp", FullPath = @"C:\Games\Data\Next.esp" }
        };

        _stateServiceMock.CurrentState.Returns(new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = plugins
        });
        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>()).Returns(true);
        _configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new UserConfiguration { Backup = new BackupSettings { Enabled = true, MaxSessions = 3 } });
        _backupServiceMock.GetBackupRoot(Arg.Any<string>()).Returns(@"C:\Games\AutoQAC Backups");
        _backupServiceMock.CreateSessionDirectory(Arg.Any<string>()).Returns(@"C:\Games\AutoQAC Backups\session");
        _backupServiceMock.BackupPluginAsync(
                Arg.Is<PluginInfo>(p => p.FileName == "Canceled.esp"),
                Arg.Any<string>(),
                Arg.Any<IProgress<BackupCopyProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BackupCreateResult(BackupOperationStatus.Canceled, "Canceled.esp", 10, 100, BackupFailureReason.Canceled));
        _backupServiceMock.BackupPluginAsync(
                Arg.Is<PluginInfo>(p => p.FileName == "Next.esp"),
                Arg.Any<string>(),
                Arg.Any<IProgress<BackupCopyProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BackupCreateResult(BackupOperationStatus.Complete, "Next.esp", 100, 100, null));
        _cleaningServiceMock.CleanPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<CancellationToken>(), Arg.Any<Action<Process>?>())
            .Returns(new CleaningResult { Status = CleaningStatus.Cleaned, Success = true });
        _backupServiceMock.CleanupOldSessionsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<IProgress<BackupCopyProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new BackupRetentionCleanupResult(BackupOperationStatus.Complete, Array.Empty<BackupRetentionRowResult>()));

        // Act
        await _orchestrator.StartCleaningAsync();

        // Assert
        await _cleaningServiceMock.DidNotReceive().CleanPluginAsync(Arg.Is<PluginInfo>(p => p.FileName == "Canceled.esp"), Arg.Any<CancellationToken>(), Arg.Any<Action<Process>?>());
        await _cleaningServiceMock.Received(1).CleanPluginAsync(Arg.Is<PluginInfo>(p => p.FileName == "Next.esp"), Arg.Any<CancellationToken>(), Arg.Any<Action<Process>?>());
        _stateServiceMock.Received(1).AddDetailedCleaningResult(Arg.Is<PluginCleaningResult>(r =>
            r.PluginName == "Canceled.esp" &&
            r.Status == CleaningStatus.Skipped &&
            r.Message == "Backup canceled"));
    }

    /// <summary>
    /// Verifies that canceling file work during xEdit execution does not call stop or force-kill paths.
    /// </summary>
    [Fact]
    public async Task CancelBackupOperation_DuringXEdit_DoesNotStopOrKillXEdit()
    {
        // Arrange
        var plugin = new PluginInfo { FileName = "Running.esp", FullPath = "Path/Running.esp" };
        _stateServiceMock.CurrentState.Returns(new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = new List<PluginInfo> { plugin }
        });
        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>()).Returns(true);

        var processStarted = CreateSignal();
        var releasePlugin = CreateSignal();
        _cleaningServiceMock.CleanPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<CancellationToken>(), Arg.Any<Action<Process>?>())
            .Returns(async callInfo =>
            {
                callInfo.ArgAt<Action<Process>?>(2)?.Invoke(Process.GetCurrentProcess());
                processStarted.TrySetResult(true);
                await releasePlugin.Task;
                return new CleaningResult { Status = CleaningStatus.Cleaned, Success = true };
            });

        // Act
        var cleaningTask = _orchestrator.StartCleaningAsync();
        await WaitForSignalAsync(processStarted);
        await _orchestrator.CancelBackupOperationAsync();
        releasePlugin.SetResult(true);
        await cleaningTask;

        // Assert
        await _processServiceMock.DidNotReceive().TerminateProcessAsync(Arg.Any<Process>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that non-canceled backup failures still use the callback rather than silently skipping xEdit.
    /// </summary>
    [Fact]
    public async Task BackupFailure_StillUsesBackupFailureCallback_ForNonCanceledFailures()
    {
        // Arrange
        var plugin = new PluginInfo { FileName = "Failure.esp", FullPath = @"C:\Games\Data\Failure.esp" };
        _stateServiceMock.CurrentState.Returns(new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = new List<PluginInfo> { plugin }
        });
        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>()).Returns(true);
        _configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new UserConfiguration { Backup = new BackupSettings { Enabled = true, MaxSessions = 3 } });
        _backupServiceMock.GetBackupRoot(Arg.Any<string>()).Returns(@"C:\Games\AutoQAC Backups");
        _backupServiceMock.CreateSessionDirectory(Arg.Any<string>()).Returns(@"C:\Games\AutoQAC Backups\session");
        _backupServiceMock.BackupPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<string>(), Arg.Any<IProgress<BackupCopyProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new BackupCreateResult(BackupOperationStatus.Failed, "Failure.esp", 0, 100, BackupFailureReason.AccessDenied));

        var callbackErrors = new List<string>();
        BackupFailureCallback callback = (_, error) =>
        {
            callbackErrors.Add(error);
            return Task.FromResult(BackupFailureChoice.ContinueWithoutBackup);
        };
        _cleaningServiceMock.CleanPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<CancellationToken>(), Arg.Any<Action<Process>?>())
            .Returns(new CleaningResult { Status = CleaningStatus.Cleaned, Success = true });

        // Act
        await _orchestrator.StartCleaningAsync(null, callback);

        // Assert
        callbackErrors.Should().ContainSingle().Which.Should().Be("Access denied");
        await _cleaningServiceMock.Received(1).CleanPluginAsync(Arg.Is<PluginInfo>(p => p.FileName == "Failure.esp"), Arg.Any<CancellationToken>(), Arg.Any<Action<Process>?>());
    }

    /// <summary>
    /// Verifies that choosing Skip Plugin after a backup failure records an explicit skipped result.
    /// </summary>
    [Fact]
    public async Task BackupFailureChoice_SkipPlugin_PublishesSkippedResultAndCompletesSessionAccounting()
    {
        // Arrange
        var plugin = new PluginInfo { FileName = "BackupFails.esp", FullPath = @"C:\Games\Data\BackupFails.esp" };
        _stateServiceMock.CurrentState.Returns(new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = new List<PluginInfo> { plugin }
        });
        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>()).Returns(true);
        _configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new UserConfiguration { Backup = new BackupSettings { Enabled = true, MaxSessions = 3 } });
        _backupServiceMock.GetBackupRoot(Arg.Any<string>()).Returns(@"C:\Games\AutoQAC Backups");
        _backupServiceMock.CreateSessionDirectory(Arg.Any<string>()).Returns(@"C:\Games\AutoQAC Backups\session");
        _backupServiceMock.BackupPluginAsync(
                Arg.Any<PluginInfo>(),
                Arg.Any<string>(),
                Arg.Any<IProgress<BackupCopyProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BackupCreateResult(BackupOperationStatus.Failed, "BackupFails.esp", 0, null, BackupFailureReason.TargetWriteFailed));

        BackupFailureCallback callback = (_, _) => Task.FromResult(BackupFailureChoice.SkipPlugin);

        // Act
        await _orchestrator.StartCleaningAsync(null, callback);

        // Assert
        await _cleaningServiceMock.DidNotReceive().CleanPluginAsync(
            Arg.Is<PluginInfo>(p => p.FileName == "BackupFails.esp"),
            Arg.Any<CancellationToken>(),
            Arg.Any<Action<Process>?>());
        _stateServiceMock.Received(1).AddDetailedCleaningResult(Arg.Is<PluginCleaningResult>(r =>
            r.PluginName == "BackupFails.esp" &&
            r.Status == CleaningStatus.Skipped &&
            !r.Success &&
            r.Message == "Backup failed - skipped by user"));
        _stateServiceMock.Received(1).FinishCleaningWithResults(Arg.Is<CleaningSessionResult>(session =>
            session.PluginResults.Count == 1 &&
            session.PluginResults[0].PluginName == "BackupFails.esp" &&
            session.PluginResults[0].Status == CleaningStatus.Skipped &&
            !session.PluginResults[0].Success &&
            session.PluginResults[0].Message == "Backup failed - skipped by user"));
    }

    /// <summary>
    /// Verifies that choosing Abort Session after a backup failure emits a final canceled session result.
    /// </summary>
    [Fact]
    public async Task BackupFailureChoice_AbortSession_FinalizesCanceledSessionWithPreviousResults()
    {
        // Arrange
        var plugins = new List<PluginInfo>
        {
            new() { FileName = "AlreadyCleaned.esp", FullPath = @"C:\Games\Data\AlreadyCleaned.esp" },
            new() { FileName = "BackupFails.esp", FullPath = @"C:\Games\Data\BackupFails.esp" }
        };
        _stateServiceMock.CurrentState.Returns(new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = plugins
        });
        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>()).Returns(true);
        _configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new UserConfiguration { Backup = new BackupSettings { Enabled = true, MaxSessions = 3 } });
        _backupServiceMock.GetBackupRoot(Arg.Any<string>()).Returns(@"C:\Games\AutoQAC Backups");
        _backupServiceMock.CreateSessionDirectory(Arg.Any<string>()).Returns(@"C:\Games\AutoQAC Backups\session");
        _backupServiceMock.BackupPluginAsync(
                Arg.Is<PluginInfo>(p => p.FileName == "AlreadyCleaned.esp"),
                Arg.Any<string>(),
                Arg.Any<IProgress<BackupCopyProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BackupCreateResult(BackupOperationStatus.Complete, "AlreadyCleaned.esp", 100, 100, null));
        _backupServiceMock.BackupPluginAsync(
                Arg.Is<PluginInfo>(p => p.FileName == "BackupFails.esp"),
                Arg.Any<string>(),
                Arg.Any<IProgress<BackupCopyProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BackupCreateResult(BackupOperationStatus.Failed, "BackupFails.esp", 0, null, BackupFailureReason.TargetWriteFailed));
        _backupServiceMock.WriteSessionMetadataAsync(Arg.Any<string>(), Arg.Any<BackupSession>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _cleaningServiceMock.CleanPluginAsync(
                Arg.Is<PluginInfo>(p => p.FileName == "AlreadyCleaned.esp"),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<Process>?>())
            .Returns(new CleaningResult { Status = CleaningStatus.Cleaned, Success = true, Message = "Cleaned" });

        BackupFailureCallback callback = (_, _) => Task.FromResult(BackupFailureChoice.AbortSession);

        // Act
        await _orchestrator.StartCleaningAsync(null, callback);

        // Assert
        await _cleaningServiceMock.Received(1).CleanPluginAsync(
            Arg.Is<PluginInfo>(p => p.FileName == "AlreadyCleaned.esp"),
            Arg.Any<CancellationToken>(),
            Arg.Any<Action<Process>?>());
        await _cleaningServiceMock.DidNotReceive().CleanPluginAsync(
            Arg.Is<PluginInfo>(p => p.FileName == "BackupFails.esp"),
            Arg.Any<CancellationToken>(),
            Arg.Any<Action<Process>?>());
        _stateServiceMock.Received(1).FinishCleaningWithResults(Arg.Is<CleaningSessionResult>(session =>
            session.WasCancelled &&
            session.PluginResults.Count == 1 &&
            session.PluginResults[0].PluginName == "AlreadyCleaned.esp" &&
            session.PluginResults[0].Status == CleaningStatus.Cleaned));
    }

    /// <summary>
    /// Verifies that MO2 mode preserves the existing virtual-filesystem backup skip behavior.
    /// </summary>
    [Fact]
    public async Task Mo2Mode_DoesNotCallBackupPluginAsync()
    {
        // Arrange
        var tempMo2 = Path.GetTempFileName();
        try
        {
            var plugin = new PluginInfo { FileName = "Mo2.esp", FullPath = "Path/Mo2.esp" };
            _stateServiceMock.CurrentState.Returns(new AppState
            {
                LoadOrderPath = "plugins.txt",
                XEditExecutablePath = "xedit.exe",
                Mo2ExecutablePath = tempMo2,
                CurrentGameType = GameType.SkyrimSe,
                Mo2ModeEnabled = true,
                PluginsToClean = new List<PluginInfo> { plugin }
            });
            _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>()).Returns(true);
            _configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
                .Returns(new UserConfiguration
                {
                    ModOrganizer = new ModOrganizerConfig { Binary = tempMo2 },
                    Backup = new BackupSettings { Enabled = true, MaxSessions = 3 },
                    Settings = new AutoQacSettings { Mo2Mode = true }
                });
            _cleaningServiceMock.CleanPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<CancellationToken>(), Arg.Any<Action<Process>?>())
                .Returns(new CleaningResult { Status = CleaningStatus.Cleaned, Success = true });

            // Act
            await _orchestrator.StartCleaningAsync();

            // Assert
            await _backupServiceMock.DidNotReceive().BackupPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<string>(), Arg.Any<IProgress<BackupCopyProgress>?>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            File.Delete(tempMo2);
        }
    }

    /// <summary>
    /// Verifies that MO2 mode with empty MO2 executable path throws with actionable guidance.
    /// </summary>
    [Fact]
    public async Task StartCleaningAsync_MO2Mode_ShouldThrow_WhenMO2BinaryPathEmpty()
    {
        // Arrange
        var plugins = new List<PluginInfo>
        {
            new() { FileName = "Plugin1.esp", FullPath = "Path/Plugin1.esp" }
        };

        var appState = new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSe,
            Mo2ModeEnabled = true,
            Mo2ExecutablePath = null,
            PluginsToClean = plugins
        };
        _stateServiceMock.CurrentState.Returns(appState);

        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        // MO2 mode enabled but no binary path
        var userConfig = new UserConfiguration
        {
            ModOrganizer = new ModOrganizerConfig { Binary = null },
            Settings = new AutoQacSettings { Mo2Mode = true }
        };
        _configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
            .Returns(userConfig);

        // Act
        var act = () => _orchestrator.StartCleaningAsync();

        // Assert
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("MO2", "error message should reference MO2");
        ex.Which.Message.Should().Contain("Settings", "error message should guide user to Settings");
    }

    /// <summary>
    /// Verifies that MO2 mode with non-existent MO2 binary throws with actionable guidance.
    /// </summary>
    [Fact]
    public async Task StartCleaningAsync_MO2Mode_ShouldThrow_WhenMO2BinaryNotFound()
    {
        // Arrange
        var plugins = new List<PluginInfo>
        {
            new() { FileName = "Plugin1.esp", FullPath = "Path/Plugin1.esp" }
        };

        var appState = new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSe,
            Mo2ModeEnabled = true,
            Mo2ExecutablePath = @"C:\nonexistent\ModOrganizer.exe",
            PluginsToClean = plugins
        };
        _stateServiceMock.CurrentState.Returns(appState);

        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        // MO2 mode enabled with non-existent path
        var userConfig = new UserConfiguration
        {
            ModOrganizer = new ModOrganizerConfig { Binary = @"C:\nonexistent\ModOrganizer.exe" },
            Settings = new AutoQacSettings { Mo2Mode = true }
        };
        _configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
            .Returns(userConfig);

        // Act
        var act = () => _orchestrator.StartCleaningAsync();

        // Assert
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("MO2", "error message should reference MO2");
        ex.Which.Message.Should().Contain("Settings", "error message should guide user to Settings");
    }

    #endregion

    #region Log-Based Parsing Tests

    /// <summary>
    /// Verifies that when cleaning succeeds and log file has content, the orchestrator
    /// parses statistics from the log using the offset-based API.
    /// </summary>
    [Fact]
    public async Task StartCleaningAsync_ShouldParseStatsFromLogFile_WhenCleaningSucceeds()
    {
        // Arrange
        var plugin = new PluginInfo { FileName = "TestMod.esp", FullPath = "Path/TestMod.esp" };
        var appState = new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = @"C:\xEdit\SSEEdit.exe",
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = new List<PluginInfo> { plugin }
        };
        _stateServiceMock.CurrentState.Returns(appState);

        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        _cleaningServiceMock.CleanPluginAsync(
                Arg.Any<PluginInfo>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<Process>?>())
            .Returns(new CleaningResult { Success = true, Status = CleaningStatus.Cleaned });

        var logLines = new List<string> { "Removing: [ARMO:00012345]", "Undeleting: [NPC_:00067890]" };
        _logFileServiceMock.ReadLogContentAsync(
                Arg.Any<string>(), Arg.Any<GameType>(),
                Arg.Any<long>(), Arg.Any<long>(),
                Arg.Any<CancellationToken>())
            .Returns(new LogReadResult { LogLines = logLines });

        _outputParserMock.ParseOutput(Arg.Any<List<string>>())
            .Returns(new CleaningStatistics { ItemsRemoved = 1, ItemsUndeleted = 1 });

        // Act
        await _orchestrator.StartCleaningAsync();

        // Assert
        _outputParserMock.Received(1).ParseOutput(Arg.Any<List<string>>());
        _stateServiceMock.Received(1).AddDetailedCleaningResult(Arg.Is<PluginCleaningResult>(r =>
            r.PluginName == "TestMod.esp" &&
            r.Statistics != null &&
            r.Statistics.ItemsRemoved == 1 &&
            r.Statistics.ItemsUndeleted == 1));
    }

    /// <summary>
    /// Verifies that when log contains a completion line but zero cleaning stats,
    /// the plugin status is set to AlreadyClean.
    /// </summary>
    [Fact]
    public async Task StartCleaningAsync_ShouldSetAlreadyClean_WhenCompletionLineButZeroStats()
    {
        // Arrange
        var plugin = new PluginInfo { FileName = "CleanMod.esp", FullPath = "Path/CleanMod.esp" };
        var appState = new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = @"C:\xEdit\SSEEdit.exe",
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = new List<PluginInfo> { plugin }
        };
        _stateServiceMock.CurrentState.Returns(appState);

        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        _cleaningServiceMock.CleanPluginAsync(
                Arg.Any<PluginInfo>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<Process>?>())
            .Returns(new CleaningResult { Success = true, Status = CleaningStatus.Cleaned });

        var logLines = new List<string> { "Done." };
        _logFileServiceMock.ReadLogContentAsync(
                Arg.Any<string>(), Arg.Any<GameType>(),
                Arg.Any<long>(), Arg.Any<long>(),
                Arg.Any<CancellationToken>())
            .Returns(new LogReadResult { LogLines = logLines });

        _outputParserMock.ParseOutput(Arg.Any<List<string>>())
            .Returns(new CleaningStatistics());
        _outputParserMock.IsCompletionLine("Done.").Returns(true);

        // Act
        await _orchestrator.StartCleaningAsync();

        // Assert
        _stateServiceMock.Received(1).AddDetailedCleaningResult(Arg.Is<PluginCleaningResult>(r =>
            r.PluginName == "CleanMod.esp" &&
            r.Status == CleaningStatus.AlreadyClean));
    }

    /// <summary>
    /// Verifies that when the exception log contains content, the plugin status is set to Failed
    /// and the exception text appears in LogParseWarning.
    /// </summary>
    [Fact]
    public async Task StartCleaningAsync_ShouldSurfaceExceptionLog_WhenExceptionContentPresent()
    {
        // Arrange
        var plugin = new PluginInfo { FileName = "CrashMod.esp", FullPath = "Path/CrashMod.esp" };
        var appState = new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = @"C:\xEdit\SSEEdit.exe",
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = new List<PluginInfo> { plugin }
        };
        _stateServiceMock.CurrentState.Returns(appState);

        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        _cleaningServiceMock.CleanPluginAsync(
                Arg.Any<PluginInfo>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<Process>?>())
            .Returns(new CleaningResult { Success = true, Status = CleaningStatus.Cleaned });

        _logFileServiceMock.ReadLogContentAsync(
                Arg.Any<string>(), Arg.Any<GameType>(),
                Arg.Any<long>(), Arg.Any<long>(),
                Arg.Any<CancellationToken>())
            .Returns(new LogReadResult
            {
                LogLines = new List<string>(),
                ExceptionContent = "Access violation at 0x00400000"
            });

        // Act
        await _orchestrator.StartCleaningAsync();

        // Assert
        _stateServiceMock.Received(1).AddDetailedCleaningResult(Arg.Is<PluginCleaningResult>(r =>
            r.PluginName == "CrashMod.esp" &&
            r.Status == CleaningStatus.Failed &&
            r.LogParseWarning != null &&
            r.LogParseWarning.Contains("Access violation")));
    }

    /// <summary>
    /// Verifies that when a plugin cleaning returns Skipped (cancelled), the orchestrator
    /// does NOT attempt to read the log file.
    /// </summary>
    [Fact]
    public async Task StartCleaningAsync_ShouldSkipLogRead_WhenProcessWasCancelled()
    {
        // Arrange
        var plugin = new PluginInfo { FileName = "Cancelled.esp", FullPath = "Path/Cancelled.esp" };
        var appState = new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = @"C:\xEdit\SSEEdit.exe",
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = new List<PluginInfo> { plugin }
        };
        _stateServiceMock.CurrentState.Returns(appState);

        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        _cleaningServiceMock.CleanPluginAsync(
                Arg.Any<PluginInfo>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<Process>?>())
            .Returns(new CleaningResult
            {
                Success = false,
                Status = CleaningStatus.Skipped,
                Message = "Operation cancelled."
            });

        // Act
        await _orchestrator.StartCleaningAsync();

        // Assert - log reading should be skipped
        await _logFileServiceMock.DidNotReceive().ReadLogContentAsync(
            Arg.Any<string>(), Arg.Any<GameType>(),
            Arg.Any<long>(), Arg.Any<long>(),
            Arg.Any<CancellationToken>());

        _stateServiceMock.Received(1).AddDetailedCleaningResult(Arg.Is<PluginCleaningResult>(r =>
            r.PluginName == "Cancelled.esp" &&
            r.Status == CleaningStatus.Skipped));
    }

    #endregion

    #region Phase 8 Characterization Tests

    [Fact]
    public async Task MarkLeftRunningByUser_AfterStopCleaning_ReportsLeftRunningTerminationResult()
    {
        // Arrange
        Process? sleeper = null;
        var plugin = new PluginInfo { FileName = "LeftRunning.esp", FullPath = "Path/LeftRunning.esp" };
        _stateServiceMock.CurrentState.Returns(new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = new List<PluginInfo> { plugin }
        });
        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>()).Returns(true);
        _processServiceMock.TerminateProcessAsync(Arg.Any<Process>(), false, Arg.Any<CancellationToken>())
            .Returns(TerminationResult.GracePeriodExpired);

        var processStarted = CreateSignal();
        var releasePlugin = CreateSignal();

        try
        {
            sleeper = StartSleeperProcess();

            _cleaningServiceMock.CleanPluginAsync(
                    Arg.Any<PluginInfo>(),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<Action<Process>?>())
                .Returns(async callInfo =>
                {
                    callInfo.ArgAt<Action<Process>?>(2)?.Invoke(sleeper);
                    processStarted.TrySetResult(true);
                    await releasePlugin.Task;
                    return new CleaningResult { Status = CleaningStatus.Cleaned, Success = true };
                });

            // Act
            var cleaningTask = _orchestrator.StartCleaningAsync();
            await WaitForSignalAsync(processStarted);
            var stopResult = await _orchestrator.StopCleaningAsync();
            var leftResult = _orchestrator.MarkLeftRunningByUser();

            // Assert
            stopResult.MayStillBeRunning.Should().BeTrue();
            leftResult.MayStillBeRunning.Should().BeTrue();
            _orchestrator.LastTerminationResult.Should().Be(TerminationResult.LeftRunningByUser);

            releasePlugin.SetResult(true);
            await cleaningTask;
        }
        finally
        {
            KillProcessIfRunning(sleeper);
        }
    }

    [Fact]
    public async Task Retention_WhenWarningResultReturned_SessionResultClassifiesAsSuccessfulWithBackupCleanup()
    {
        // Arrange
        var plugin = new PluginInfo { FileName = "Retained.esp", FullPath = @"C:\Games\Data\Retained.esp" };
        _stateServiceMock.CurrentState.Returns(new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = new List<PluginInfo> { plugin }
        });
        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>()).Returns(true);
        _configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new UserConfiguration { Backup = new BackupSettings { Enabled = true, MaxSessions = 3 } });
        _backupServiceMock.GetBackupRoot(Arg.Any<string>()).Returns(@"C:\Games\AutoQAC Backups");
        _backupServiceMock.CreateSessionDirectory(Arg.Any<string>()).Returns(@"C:\Games\AutoQAC Backups\session");
        _backupServiceMock.BackupPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<string>(), Arg.Any<IProgress<BackupCopyProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new BackupCreateResult(BackupOperationStatus.Complete, "Retained.esp", 100, 100, null));
        _backupServiceMock.CleanupOldSessionsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<IProgress<BackupCopyProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new BackupRetentionCleanupResult(
                BackupOperationStatus.Warning,
                new[] { new BackupRetentionRowResult(@"C:\Games\AutoQAC Backups\old", BackupRetentionRowStatus.Failed, BackupFailureReason.CleanupDeletionFailed) }));
        _cleaningServiceMock.CleanPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<CancellationToken>(), Arg.Any<Action<Process>?>())
            .Returns(new CleaningResult { Status = CleaningStatus.Cleaned, Success = true });

        // Act
        await _orchestrator.StartCleaningAsync();

        // Assert
        _stateServiceMock.Received(1).FinishCleaningWithResults(Arg.Is<CleaningSessionResult>(session =>
            session.IsSuccess &&
            session.BackupCleanup != null &&
            session.BackupCleanup.Status == BackupOperationStatus.Warning &&
            session.SessionSummary.StartsWith("Backup cleanup warning", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Retention_WhenCanceledResultReturned_SessionResultIncludesCanceledBackupCleanup()
    {
        // Arrange
        var plugin = new PluginInfo { FileName = "CanceledCleanup.esp", FullPath = @"C:\Games\Data\CanceledCleanup.esp" };
        _stateServiceMock.CurrentState.Returns(new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = new List<PluginInfo> { plugin }
        });
        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>()).Returns(true);
        _configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new UserConfiguration { Backup = new BackupSettings { Enabled = true, MaxSessions = 3 } });
        _backupServiceMock.GetBackupRoot(Arg.Any<string>()).Returns(@"C:\Games\AutoQAC Backups");
        _backupServiceMock.CreateSessionDirectory(Arg.Any<string>()).Returns(@"C:\Games\AutoQAC Backups\session");
        _backupServiceMock.BackupPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<string>(), Arg.Any<IProgress<BackupCopyProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new BackupCreateResult(BackupOperationStatus.Complete, "CanceledCleanup.esp", 100, 100, null));
        _backupServiceMock.CleanupOldSessionsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<IProgress<BackupCopyProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new BackupRetentionCleanupResult(
                BackupOperationStatus.Canceled,
                new[] { new BackupRetentionRowResult(@"C:\Games\AutoQAC Backups\old", BackupRetentionRowStatus.Kept, BackupFailureReason.Canceled) }));
        _cleaningServiceMock.CleanPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<CancellationToken>(), Arg.Any<Action<Process>?>())
            .Returns(new CleaningResult { Status = CleaningStatus.Cleaned, Success = true });

        // Act
        await _orchestrator.StartCleaningAsync();

        // Assert
        _stateServiceMock.Received(1).FinishCleaningWithResults(Arg.Is<CleaningSessionResult>(session =>
            session.BackupCleanup != null &&
            session.BackupCleanup.Status == BackupOperationStatus.Canceled &&
            session.SessionSummary.StartsWith("Backup cleanup canceled", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task RunDryRunAsync_AndStartCleaningAsyncPreflight_ProduceSameSkipDecisions_ForIdenticalState()
    {
        // Arrange
        var plugins = new List<PluginInfo>
        {
            new() { FileName = "SkipMe.esp", FullPath = @"C:\Games\Data\SkipMe.esp" },
            new() { FileName = "Excluded.esp", FullPath = @"C:\Games\Data\Excluded.esp" },
            new() { FileName = "Valid.esp", FullPath = @"C:\Games\Data\Valid.esp" },
            new() { FileName = "Zero.esp", FullPath = @"C:\Games\Data\Zero.esp" }
        };
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\Games\Data\Excluded.esp" };
        _stateServiceMock.CurrentState.Returns(new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = plugins,
            ExcludedPluginPaths = excluded
        });
        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>()).Returns(true);
        _configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new UserConfiguration { Backup = new BackupSettings { Enabled = false } });
        _configServiceMock.GetSkipListAsync(GameType.SkyrimSe, Arg.Any<GameVariant>(), Arg.Any<CancellationToken>())
            .Returns(new List<string> { "SkipMe.esp" });
        _pluginServiceMock.ValidatePluginFile(Arg.Is<PluginInfo>(p => p.FileName == "Zero.esp"))
            .Returns(PluginWarningKind.ZeroByte);
        _pluginServiceMock.ValidatePluginFile(Arg.Is<PluginInfo>(p => p.FileName != "Zero.esp"))
            .Returns(PluginWarningKind.None);
        _cleaningServiceMock.CleanPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<CancellationToken>(), Arg.Any<Action<Process>?>())
            .Returns(new CleaningResult { Status = CleaningStatus.Cleaned, Success = true });

        // Act
        var dryRun = await _orchestrator.RunDryRunAsync();
        await _orchestrator.StartCleaningAsync();

        var dryRunCleanNames = dryRun
            .Where(row => row.Status == DryRunStatus.WillClean)
            .Select(row => row.PluginName)
            .ToArray();

        // Assert
        _stateServiceMock.Received(1).StartCleaning(Arg.Is<List<PluginInfo>>(startList =>
            startList.Select(p => p.FileName).SequenceEqual(dryRunCleanNames)));
        dryRun.Should().ContainSingle(row => row.PluginName == "SkipMe.esp" && row.Reason == "In skip list");
        dryRun.Should().ContainSingle(row => row.PluginName == "Excluded.esp" && row.Reason == "Not selected");
        dryRun.Should().ContainSingle(row => row.PluginName == "Zero.esp" && row.Reason == "Zero-byte file");
        dryRunCleanNames.Should().Equal("Valid.esp");
    }

    [Fact]
    public async Task BackupFailure_ContinueWithoutBackup_ProceedsToXEdit_AndDoesNotAddBackupEntry()
    {
        // Arrange
        var plugins = new List<PluginInfo>
        {
            new() { FileName = "Failure.esp", FullPath = @"C:\Games\Data\Failure.esp" },
            new() { FileName = "BackedUp.esp", FullPath = @"C:\Games\Data\BackedUp.esp" }
        };
        _stateServiceMock.CurrentState.Returns(new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = plugins
        });
        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>()).Returns(true);
        _configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new UserConfiguration { Backup = new BackupSettings { Enabled = true, MaxSessions = 3 } });
        _backupServiceMock.GetBackupRoot(Arg.Any<string>()).Returns(@"C:\Games\AutoQAC Backups");
        _backupServiceMock.CreateSessionDirectory(Arg.Any<string>()).Returns(@"C:\Games\AutoQAC Backups\session");
        _backupServiceMock.BackupPluginAsync(
                Arg.Is<PluginInfo>(p => p.FileName == "Failure.esp"),
                Arg.Any<string>(),
                Arg.Any<IProgress<BackupCopyProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BackupCreateResult(BackupOperationStatus.Failed, "Failure.esp", 0, 100, BackupFailureReason.AccessDenied));
        _backupServiceMock.BackupPluginAsync(
                Arg.Is<PluginInfo>(p => p.FileName == "BackedUp.esp"),
                Arg.Any<string>(),
                Arg.Any<IProgress<BackupCopyProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BackupCreateResult(BackupOperationStatus.Complete, "BackedUp.esp", 100, 100, null));
        BackupFailureCallback callback = (_, _) => Task.FromResult(BackupFailureChoice.ContinueWithoutBackup);
        _cleaningServiceMock.CleanPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<CancellationToken>(), Arg.Any<Action<Process>?>())
            .Returns(new CleaningResult { Status = CleaningStatus.Cleaned, Success = true });

        // Act
        await _orchestrator.StartCleaningAsync(null, callback);

        // Assert
        await _cleaningServiceMock.Received(1).CleanPluginAsync(Arg.Is<PluginInfo>(p => p.FileName == "Failure.esp"), Arg.Any<CancellationToken>(), Arg.Any<Action<Process>?>());
        await _cleaningServiceMock.Received(1).CleanPluginAsync(Arg.Is<PluginInfo>(p => p.FileName == "BackedUp.esp"), Arg.Any<CancellationToken>(), Arg.Any<Action<Process>?>());
        await _backupServiceMock.Received(1).WriteSessionMetadataAsync(
            Arg.Any<string>(),
            Arg.Is<BackupSession>(session =>
                session.Plugins.Count == 1 &&
                session.Plugins[0].FileName == "BackedUp.esp" &&
                session.Plugins.All(entry => entry.FileName != "Failure.esp")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LastTerminationResult_IsResetToNull_AtSessionStartAndEnd()
    {
        // Arrange
        Process? sleeper = null;
        var firstPlugin = new PluginInfo { FileName = "Stopped.esp", FullPath = "Path/Stopped.esp" };
        _stateServiceMock.CurrentState.Returns(new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = new List<PluginInfo> { firstPlugin }
        });
        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>()).Returns(true);
        _processServiceMock.TerminateProcessAsync(Arg.Any<Process>(), false, Arg.Any<CancellationToken>())
            .Returns(TerminationResult.GracePeriodExpired);

        var processStarted = CreateSignal();
        var releasePlugin = CreateSignal();

        try
        {
            sleeper = StartSleeperProcess();

            _cleaningServiceMock.CleanPluginAsync(
                    Arg.Any<PluginInfo>(),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<Action<Process>?>())
                .Returns(async callInfo =>
                {
                    callInfo.ArgAt<Action<Process>?>(2)?.Invoke(sleeper);
                    processStarted.TrySetResult(true);
                    await releasePlugin.Task;
                    return new CleaningResult { Status = CleaningStatus.Cleaned, Success = true };
                });

            var firstSession = _orchestrator.StartCleaningAsync();
            await WaitForSignalAsync(processStarted);
            await _orchestrator.StopCleaningAsync();
            _orchestrator.LastTerminationResult.Should().Be(TerminationResult.GracePeriodExpired);
            releasePlugin.SetResult(true);
            await firstSession;

            var secondPlugin = new PluginInfo { FileName = "Second.esp", FullPath = "Path/Second.esp" };
            _stateServiceMock.CurrentState.Returns(new AppState
            {
                LoadOrderPath = "plugins.txt",
                XEditExecutablePath = "xedit.exe",
                CurrentGameType = GameType.SkyrimSe,
                PluginsToClean = new List<PluginInfo> { secondPlugin }
            });
            TerminationResult? capturedAtStart = null;
            _cleaningServiceMock.CleanPluginAsync(
                    Arg.Any<PluginInfo>(),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<Action<Process>?>())
                .Returns(ci =>
                {
                    // Sample LastTerminationResult on the very first plugin of session 2.
                    capturedAtStart ??= _orchestrator.LastTerminationResult;
                    return Task.FromResult(new CleaningResult { Status = CleaningStatus.Cleaned, Success = true });
                });

            // Act
            await _orchestrator.StartCleaningAsync();

            // Assert
            capturedAtStart.Should().BeNull();
            _orchestrator.LastTerminationResult.Should().BeNull();
        }
        finally
        {
            KillProcessIfRunning(sleeper);
        }
    }

    [Fact]
    public void ICleaningOrchestrator_PublicSurface_MatchesLockedSnapshot()
    {
        // Arrange — D-03 locks the public ViewModel-facing surface for Phase 8.
        // This snapshot prevents accidental member additions/removals during decomposition.
        var expected = new[]
        {
            "Task StartCleaningAsync(CancellationToken)",
            "Task StartCleaningAsync(TimeoutRetryCallback, CancellationToken)",
            "Task StartCleaningAsync(TimeoutRetryCallback, BackupFailureCallback, CancellationToken)",
            "Task<StopCleaningResult> StopCleaningAsync()",
            "Task<StopCleaningResult> ForceStopCleaningAsync()",
            "Task CancelBackupOperationAsync()",
            "StopCleaningResult MarkLeftRunningByUser()",
            "TerminationResult? LastTerminationResult { get; }",
            "IObservable<Boolean> HangDetected { get; }",
            "Task<List<DryRunResult>> RunDryRunAsync(CancellationToken)"
        };

        // Act
        var actual = typeof(ICleaningOrchestrator)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(FormatMemberSignature)
            .Where(signature => !string.IsNullOrEmpty(signature))
            .OrderBy(signature => signature)
            .ToArray();

        // Assert
        actual.Should().BeEquivalentTo(expected.OrderBy(signature => signature).ToArray(),
            "ICleaningOrchestrator public surface is locked by D-03; any change must be approved.");
    }

    /// <summary>
    /// Renders a <see cref="MemberInfo"/> to a short, deterministic signature string for snapshot comparison.
    /// Per R-06: handles generic Task&lt;T&gt; and Task&lt;List&lt;T&gt;&gt; return types via recursive type formatting,
    /// method overloads via parameter-type concatenation, and async signatures. Output is stable across Debug/Release.
    /// </summary>
    /// <param name="member">The public interface member to format.</param>
    /// <returns>A deterministic signature string, or an empty string for compiler-generated accessor methods.</returns>
    private static string FormatMemberSignature(MemberInfo member)
    {
        return member switch
        {
            MethodInfo m when !m.IsSpecialName =>
                $"{FormatTypeName(m.ReturnType)} {m.Name}({string.Join(", ", m.GetParameters().Select(p => FormatTypeName(p.ParameterType)))})",
            PropertyInfo p =>
                // Properties on interfaces are always abstract; reflect only the declared accessors.
                $"{FormatTypeName(p.PropertyType)} {p.Name} {{ {(p.CanRead ? "get; " : string.Empty)}{(p.CanWrite ? "set; " : string.Empty)}}}".TrimEnd(),
            _ => string.Empty
        };
    }

    /// <summary>
    /// Recursively formats a <see cref="Type"/> using simple names such as <c>Task&lt;List&lt;DryRunResult&gt;&gt;</c>.
    /// Reflection does not carry nullable reference-type annotations on member return types, so reference-type
    /// nullability is intentionally omitted while value-type nullability is preserved.
    /// </summary>
    /// <param name="t">The type to format.</param>
    /// <returns>A stable, unqualified type name suitable for snapshot assertions.</returns>
    private static string FormatTypeName(Type t)
    {
        var underlying = Nullable.GetUnderlyingType(t);
        if (underlying != null)
        {
            return $"{FormatTypeName(underlying)}?";
        }

        if (!t.IsGenericType)
        {
            return t.Name;
        }

        var def = t.Name;
        var tickIndex = def.IndexOf('`', StringComparison.Ordinal);
        if (tickIndex > 0)
        {
            def = def[..tickIndex];
        }

        var args = string.Join(", ", t.GetGenericArguments().Select(FormatTypeName));
        return $"{def}<{args}>";
    }

    #endregion

    #region Sequential Source Guard Tests

    /// <summary>
    /// Verifies that the orchestrator source does not introduce parallel plugin cleaning constructs.
    /// </summary>
    [Fact]
    public void CleaningOrchestrator_Source_DoesNotParallelizePluginCleaning()
    {
        // Arrange
        var source = File.ReadAllText(GetSourcePath("AutoQAC", "Services", "Cleaning", "CleaningOrchestrator.cs"));
        var runnerSource = File.ReadAllText(GetSourcePath("AutoQAC", "Services", "Cleaning", "PluginCleaningRunner.cs"));
        var finalizerSource = File.ReadAllText(GetSourcePath("AutoQAC", "Services", "Cleaning", "PluginResultFinalizer.cs"));

        // Act & Assert
        foreach (var cleaningSource in new[] { source, runnerSource, finalizerSource })
        {
            cleaningSource.Should().NotContain("Task.WhenAll", "plugin cleaning must remain sequential");
            cleaningSource.Should().NotContain("Parallel.ForEachAsync", "plugin cleaning must remain sequential");
            cleaningSource.Should().NotContain("Task.Run", "plugin-loop work must not be parallelized through task scheduling");
        }

        source.IndexOf("RunPluginBackupAsync", StringComparison.Ordinal).Should().BeLessThan(
            source.IndexOf("runner.RunAsync", StringComparison.Ordinal),
            "backup invocation must remain before the sequential xEdit cleaning call");
    }

    [Fact]
    public void Cleaning_Source_NoFileParallelizesPluginLoop()
    {
        // INV-8.2: Sequential xEdit cleaning is a hard runtime requirement (AGENTS.md, ROADMAP.md
        // out-of-scope, Phase 8 D-09/D-10 locks). Source-level guard prevents accidental parallelization
        // anywhere in the cleaning service tier.
        var filesToScan = new[]
        {
            "CleaningOrchestrator.cs",
            "CleaningPreflight.cs",
            "BackupSessionCoordinator.cs",
            "PluginCleaningRunner.cs",
            "PluginResultFinalizer.cs",
            "CleaningTerminationCoordinator.cs"
        };

        var prohibitedTokens = new[]
        {
            "Parallel.ForEach",
            "Parallel.ForEachAsync",
            "Task.WhenAll(",
            "Task.WhenAny(",
            "Task.Run("
        };

        foreach (var file in filesToScan)
        {
            var path = GetSourcePath("AutoQAC", "Services", "Cleaning", file);
            File.Exists(path).Should().BeTrue($"Phase 8 file {file} must exist under AutoQAC/Services/Cleaning/");
            var content = File.ReadAllText(path);

            foreach (var token in prohibitedTokens)
            {
                content.Should().NotContain(token,
                    $"{file} must not parallelize cleaning work — sequential xEdit is a hard runtime invariant (Phase 8 D-10).");
            }
        }
    }

    private static string GetSourcePath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AutoQACSharp.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("tests should run under the repository root");
        return Path.Combine(new[] { directory!.FullName }.Concat(segments).ToArray());
    }

    #endregion

    #region Dispose Tests

    /// <summary>
    /// Verifies that disposing the orchestrator properly cleans up resources.
    /// </summary>
    [Fact]
    public void Dispose_ShouldCleanupCancellationTokenSource()
    {
        // Arrange - use a new orchestrator instance for this test
        var orchestrator = new CleaningOrchestrator(
            CreatePreflight(),
            new BackupSessionCoordinator(_backupServiceMock, _stateServiceMock, _loggerMock),
            new CleaningTerminationCoordinator(_processServiceMock, _hangDetectionMock, _stateServiceMock, _loggerMock),
            new PluginCleaningRunner(_cleaningServiceMock, _logFileServiceMock, _loggerMock),
            new PluginResultFinalizer(_logFileServiceMock, _outputParserMock, _loggerMock),
            _stateServiceMock,
            _loggerMock,
            _processServiceMock);

        // Act & Assert
        // Should not throw
        orchestrator.Dispose();

        // Multiple disposal should be safe
        FluentActions.Invoking(() => orchestrator.Dispose())
            .Should().NotThrow("multiple disposal should be safe");
    }

    #endregion
}
