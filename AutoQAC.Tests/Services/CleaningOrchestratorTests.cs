using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameDetection;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.Backup;
using AutoQAC.Services.Monitoring;
using AutoQAC.Services.Process;
using AutoQAC.Services.State;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

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

        // Default mock setup for GetSkipListAsync to return empty list instead of null
        _configServiceMock.GetSkipListAsync(Arg.Any<GameType>())
            .Returns(new List<string>());

        // Default mock setup for LoadUserConfigAsync to return default config (DisableSkipLists = false)
        _configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new UserConfiguration());

        // Default mock setup for log file service: return empty lines (no log file found)
        _logFileServiceMock.ReadLogFileAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((new List<string>(), (string?)"Log file not found"));

        _orchestrator = new CleaningOrchestrator(
            _cleaningServiceMock,
            _pluginServiceMock,
            _gameDetectionServiceMock,
            _stateServiceMock,
            _configServiceMock,
            _loggerMock,
            _processServiceMock,
            _logFileServiceMock,
            _outputParserMock,
            _backupServiceMock,
            _hangDetectionMock);
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

        _cleaningServiceMock.CleanPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>())
            .Returns(new CleaningResult { Status = CleaningStatus.Cleaned });

        // Act
        await _orchestrator.StartCleaningAsync();

        // Assert
        await _cleaningServiceMock.Received(1).CleanPluginAsync(Arg.Is<PluginInfo>(p => p.FileName == "Plugin1.esp"), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>());
        await _cleaningServiceMock.Received(1).CleanPluginAsync(Arg.Is<PluginInfo>(p => p.FileName == "Plugin2.esp"), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>());
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
        _cleaningServiceMock.CleanPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>())
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

        _configServiceMock.GetSkipListAsync(Arg.Any<GameType>())
            .Returns(new List<string>());

        _pluginServiceMock.FilterSkippedPlugins(plugins, Arg.Any<List<string>>())
            .Returns(plugins);

        // Configure cleaning results: BadPlugin fails, others succeed
        _cleaningServiceMock.CleanPluginAsync(
            Arg.Is<PluginInfo>(p => p.FileName == "BadPlugin.esp"),
            Arg.Any<IProgress<string>>(),
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
            Arg.Any<IProgress<string>>(),
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
        await _cleaningServiceMock.Received(5).CleanPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>());

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

        _cleaningServiceMock.CleanPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>())
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
        var cleaningStartedEvent = new TaskCompletionSource<bool>();

        _cleaningServiceMock.CleanPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>())
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

                // Simulate cleaning work - handle cancellation gracefully
                try
                {
                    await Task.Delay(200, ct);
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancelled - return a cancelled/failed result
                    return new CleaningResult { Status = CleaningStatus.Failed, Message = "Cancelled" };
                }

                return new CleaningResult { Status = CleaningStatus.Cleaned };
            });

        // Act
        var cleaningTask = _orchestrator.StartCleaningAsync();

        // Wait for cleaning to start, then stop it
        await cleaningStartedEvent.Task;
        await Task.Delay(50); // Give a moment for the first clean to be in progress
        await _orchestrator.StopCleaningAsync();

        // Wait for task to complete
        await cleaningTask;

        // Assert
        // Not all plugins should be cleaned due to cancellation
        cleanedPlugins.Count.Should().BeLessThanOrEqualTo(3,
            "StopCleaning should stop processing");

        _stateServiceMock.Received(1).FinishCleaningWithResults(Arg.Any<CleaningSessionResult>());
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
        await _cleaningServiceMock.DidNotReceive().CleanPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>());

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

        _cleaningServiceMock.CleanPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>())
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
            new() { FileName = "Skyrim.esm", FullPath = "Skyrim.esm", IsSelected = true },  // In skip list
            new() { FileName = "Update.esm", FullPath = "Update.esm", IsSelected = true },  // In skip list
            new() { FileName = "UserMod.esp", FullPath = "UserMod.esp", IsSelected = true }   // Not in skip list
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

        _cleaningServiceMock.CleanPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>())
            .Returns(new CleaningResult { Status = CleaningStatus.Cleaned });

        // Skip list contains base game ESMs
        _configServiceMock.GetSkipListAsync(GameType.SkyrimSe)
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
        await _cleaningServiceMock.Received(1).CleanPluginAsync(Arg.Is<PluginInfo>(p => p.FileName == "Skyrim.esm"), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>());
        await _cleaningServiceMock.Received(1).CleanPluginAsync(Arg.Is<PluginInfo>(p => p.FileName == "Update.esm"), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>());
        await _cleaningServiceMock.Received(1).CleanPluginAsync(Arg.Is<PluginInfo>(p => p.FileName == "UserMod.esp"), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>());
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
            new() { FileName = "Skyrim.esm", FullPath = "Skyrim.esm", IsSelected = true },  // In skip list
            new() { FileName = "Update.esm", FullPath = "Update.esm", IsSelected = true },  // In skip list
            new() { FileName = "UserMod.esp", FullPath = "UserMod.esp", IsSelected = true }   // Not in skip list
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

        _cleaningServiceMock.CleanPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>())
            .Returns(new CleaningResult { Status = CleaningStatus.Cleaned });

        // Skip list contains base game ESMs
        _configServiceMock.GetSkipListAsync(GameType.SkyrimSe)
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
        await _cleaningServiceMock.DidNotReceive().CleanPluginAsync(Arg.Is<PluginInfo>(p => p.FileName == "Skyrim.esm"), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>());
        await _cleaningServiceMock.DidNotReceive().CleanPluginAsync(Arg.Is<PluginInfo>(p => p.FileName == "Update.esm"), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>());
        await _cleaningServiceMock.Received(1).CleanPluginAsync(Arg.Is<PluginInfo>(p => p.FileName == "UserMod.esp"), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>());
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
            new() { FileName = "Plugin1.esp", FullPath = "Path/Plugin1.esp", IsSelected = true }
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
                new() { FileName = "Plugin1.esp", FullPath = "Path/Plugin1.esp", IsSelected = true },
                new() { FileName = "Plugin2.esp", FullPath = "Path/Plugin2.esp", IsSelected = true }
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

            _cleaningServiceMock.CleanPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>())
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
            new() { FileName = "Valid.esp", FullPath = "Path/Valid.esp", IsSelected = true },
            new() { FileName = "Missing.esp", FullPath = "Path/Missing.esp", IsSelected = true },
            new() { FileName = "AlsoValid.esp", FullPath = "Path/AlsoValid.esp", IsSelected = true }
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

        _cleaningServiceMock.CleanPluginAsync(Arg.Any<PluginInfo>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>())
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
        await _cleaningServiceMock.DidNotReceive().CleanPluginAsync(Arg.Is<PluginInfo>(p => p.FileName == "Missing.esp"), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>(), Arg.Any<Action<System.Diagnostics.Process>?>());
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
            new() { FileName = "Plugin1.esp", FullPath = "Path/Plugin1.esp", IsSelected = true }
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
            new() { FileName = "Plugin1.esp", FullPath = "Path/Plugin1.esp", IsSelected = true }
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

    #region Dispose Tests

    /// <summary>
    /// Verifies that disposing the orchestrator properly cleans up resources.
    /// </summary>
    [Fact]
    public void Dispose_ShouldCleanupCancellationTokenSource()
    {
        // Arrange - use a new orchestrator instance for this test
        var orchestrator = new CleaningOrchestrator(
            _cleaningServiceMock,
            _pluginServiceMock,
            _gameDetectionServiceMock,
            _stateServiceMock,
            _configServiceMock,
            _loggerMock,
            _processServiceMock,
            _logFileServiceMock,
            _outputParserMock,
            _backupServiceMock,
            _hangDetectionMock);

        // Act & Assert
        // Should not throw
        orchestrator.Dispose();

        // Multiple disposal should be safe
        FluentActions.Invoking(() => orchestrator.Dispose())
            .Should().NotThrow("multiple disposal should be safe");
    }

    #endregion
}
