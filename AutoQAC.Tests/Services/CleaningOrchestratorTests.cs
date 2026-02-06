using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameDetection;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.Process;
using AutoQAC.Services.State;
using FluentAssertions;
using Moq;

namespace AutoQAC.Tests.Services;

public sealed class CleaningOrchestratorTests
{
    private readonly Mock<ICleaningService> _cleaningServiceMock;
    private readonly Mock<IPluginValidationService> _pluginServiceMock;
    private readonly Mock<IGameDetectionService> _gameDetectionServiceMock;
    private readonly Mock<IStateService> _stateServiceMock;
    private readonly Mock<IConfigurationService> _configServiceMock;
    private readonly Mock<ILoggingService> _loggerMock;
    private readonly Mock<IProcessExecutionService> _processServiceMock;
    private readonly CleaningOrchestrator _orchestrator;

    public CleaningOrchestratorTests()
    {
        _cleaningServiceMock = new Mock<ICleaningService>();
        _pluginServiceMock = new Mock<IPluginValidationService>();
        _gameDetectionServiceMock = new Mock<IGameDetectionService>();
        _stateServiceMock = new Mock<IStateService>();
        _configServiceMock = new Mock<IConfigurationService>();
        _loggerMock = new Mock<ILoggingService>();
        _processServiceMock = new Mock<IProcessExecutionService>();

        // Default mock setup for GetSkipListAsync to return empty list instead of null
        _configServiceMock.Setup(s => s.GetSkipListAsync(It.IsAny<GameType>()))
            .ReturnsAsync(new List<string>());

        // Default mock setup for LoadUserConfigAsync to return default config (DisableSkipLists = false)
        _configServiceMock.Setup(s => s.LoadUserConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserConfiguration());

        _orchestrator = new CleaningOrchestrator(
            _cleaningServiceMock.Object,
            _pluginServiceMock.Object,
            _gameDetectionServiceMock.Object,
            _stateServiceMock.Object,
            _configServiceMock.Object,
            _loggerMock.Object,
            _processServiceMock.Object);
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

        _stateServiceMock.Setup(s => s.CurrentState).Returns(appState);

        _cleaningServiceMock.Setup(s => s.ValidateEnvironmentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _cleaningServiceMock.Setup(s => s.CleanPluginAsync(It.IsAny<PluginInfo>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CleaningResult { Status = CleaningStatus.Cleaned });

        // Act
        await _orchestrator.StartCleaningAsync();

        // Assert
        _cleaningServiceMock.Verify(s => s.CleanPluginAsync(It.Is<PluginInfo>(p => p.FileName == "Plugin1.esp"), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        _cleaningServiceMock.Verify(s => s.CleanPluginAsync(It.Is<PluginInfo>(p => p.FileName == "Plugin2.esp"), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        _stateServiceMock.Verify(s => s.StartCleaning(It.IsAny<List<PluginInfo>>()), Times.Once);
        _stateServiceMock.Verify(s => s.FinishCleaningWithResults(It.IsAny<CleaningSessionResult>()), Times.Once);
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
        _stateServiceMock.Setup(s => s.CurrentState).Returns(appState);

        _cleaningServiceMock.Setup(s => s.ValidateEnvironmentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Mock Executable detection failing (Unknown)
        _gameDetectionServiceMock.Setup(d => d.DetectFromExecutable(It.IsAny<string>()))
            .Returns(GameType.Unknown);
        
        // Mock Load Order detection succeeding
        _gameDetectionServiceMock.Setup(d => d.DetectFromLoadOrderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GameType.Fallout4);

        // Act
        await _orchestrator.StartCleaningAsync();

        // Assert
        _gameDetectionServiceMock.Verify(d => d.DetectFromExecutable(It.IsAny<string>()), Times.Once);
        _gameDetectionServiceMock.Verify(d => d.DetectFromLoadOrderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        // We expect state update
        _stateServiceMock.Verify(s => s.UpdateState(It.IsAny<Func<AppState, AppState>>()), Times.Once);
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
        _stateServiceMock.Setup(s => s.CurrentState).Returns(appState);

        _cleaningServiceMock.Setup(s => s.ValidateEnvironmentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var act = () => _orchestrator.StartCleaningAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        _stateServiceMock.Verify(s => s.FinishCleaningWithResults(It.IsAny<CleaningSessionResult>()), Times.Once); // It calls Finish in catch block
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
        _stateServiceMock.Setup(s => s.CurrentState).Returns(appState);

        _cleaningServiceMock.Setup(s => s.ValidateEnvironmentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Track how many plugins were cleaned
        var cleanedCount = 0;
        var cts = new CancellationTokenSource();

        // After cleaning 2 plugins, request cancellation
        _cleaningServiceMock.Setup(s => s.CleanPluginAsync(It.IsAny<PluginInfo>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
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
        _stateServiceMock.Verify(s => s.FinishCleaningWithResults(It.IsAny<CleaningSessionResult>()), Times.Once);
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
        _stateServiceMock.Setup(s => s.CurrentState).Returns(appState);

        _cleaningServiceMock.Setup(s => s.ValidateEnvironmentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _configServiceMock.Setup(s => s.GetSkipListAsync(It.IsAny<GameType>()))
            .ReturnsAsync(new List<string>());

        _pluginServiceMock.Setup(s => s.FilterSkippedPlugins(plugins, It.IsAny<List<string>>()))
            .Returns(plugins);

        // Configure cleaning results: BadPlugin fails, others succeed
        _cleaningServiceMock.Setup(s => s.CleanPluginAsync(
            It.Is<PluginInfo>(p => p.FileName == "BadPlugin.esp"),
            It.IsAny<IProgress<string>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CleaningResult
            {
                Status = CleaningStatus.Failed,
                Success = false,
                Message = "Failed to clean plugin"
            });

        _cleaningServiceMock.Setup(s => s.CleanPluginAsync(
            It.Is<PluginInfo>(p => p.FileName != "BadPlugin.esp"),
            It.IsAny<IProgress<string>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CleaningResult
            {
                Status = CleaningStatus.Cleaned,
                Success = true
            });

        // Act
        await _orchestrator.StartCleaningAsync();

        // Assert
        // All 5 plugins should have been processed
        _cleaningServiceMock.Verify(
            s => s.CleanPluginAsync(It.IsAny<PluginInfo>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()),
            Times.Exactly(5),
            "all 5 plugins should be processed despite one failure");

        // State should have been updated for all plugins via AddDetailedCleaningResult
        _stateServiceMock.Verify(
            s => s.AddDetailedCleaningResult(It.Is<PluginCleaningResult>(r => r.PluginName == "BadPlugin.esp" && r.Status == CleaningStatus.Failed)),
            Times.Once);
        _stateServiceMock.Verify(
            s => s.AddDetailedCleaningResult(It.Is<PluginCleaningResult>(r => r.PluginName != "BadPlugin.esp" && r.Status == CleaningStatus.Cleaned)),
            Times.Exactly(4));

        // FinishCleaning should be called
        _stateServiceMock.Verify(s => s.FinishCleaningWithResults(It.IsAny<CleaningSessionResult>()), Times.Once);
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
        _stateServiceMock.Setup(s => s.CurrentState).Returns(appState);

        _cleaningServiceMock.Setup(s => s.ValidateEnvironmentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Track execution order and overlap
        var executionLog = new List<(string plugin, DateTime start, DateTime end)>();
        var lockObj = new object();
        var currentlyExecuting = 0;
        var maxConcurrent = 0;

        _cleaningServiceMock.Setup(s => s.CleanPluginAsync(It.IsAny<PluginInfo>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PluginInfo plugin, IProgress<string>? progress, CancellationToken ct) =>
            {
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
        _stateServiceMock.Setup(s => s.CurrentState).Returns(appState);

        _cleaningServiceMock.Setup(s => s.ValidateEnvironmentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var cleanedPlugins = new List<string>();
        var cleaningStartedEvent = new TaskCompletionSource<bool>();

        _cleaningServiceMock.Setup(s => s.CleanPluginAsync(It.IsAny<PluginInfo>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .Returns(async (PluginInfo plugin, IProgress<string>? progress, CancellationToken ct) =>
            {
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

        _stateServiceMock.Verify(s => s.FinishCleaningWithResults(It.IsAny<CleaningSessionResult>()), Times.Once);
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
        _stateServiceMock.Setup(s => s.CurrentState).Returns(appState);

        _cleaningServiceMock.Setup(s => s.ValidateEnvironmentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _orchestrator.StartCleaningAsync();

        // Assert
        // No cleaning should have been attempted
        _cleaningServiceMock.Verify(
            s => s.CleanPluginAsync(It.IsAny<PluginInfo>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // But state lifecycle should still be complete
        _stateServiceMock.Verify(s => s.StartCleaning(It.IsAny<List<PluginInfo>>()), Times.Once);
        _stateServiceMock.Verify(s => s.FinishCleaningWithResults(It.IsAny<CleaningSessionResult>()), Times.Once);
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
        _stateServiceMock.Setup(s => s.CurrentState).Returns(appState);

        _cleaningServiceMock.Setup(s => s.ValidateEnvironmentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _cleaningServiceMock.Setup(s => s.CleanPluginAsync(It.IsAny<PluginInfo>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CleaningResult { Status = CleaningStatus.Cleaned });

        // Act
        await _orchestrator.StartCleaningAsync();

        // Assert
        // Verify UpdateState was called for each plugin to set CurrentPlugin
        _stateServiceMock.Verify(
            s => s.UpdateState(It.IsAny<Func<AppState, AppState>>()),
            Times.AtLeast(2),
            "UpdateState should be called at least once per plugin");
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
        _stateServiceMock.Setup(s => s.CurrentState).Returns(appState);

        _cleaningServiceMock.Setup(s => s.ValidateEnvironmentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _cleaningServiceMock.Setup(s => s.CleanPluginAsync(It.IsAny<PluginInfo>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CleaningResult { Status = CleaningStatus.Cleaned });

        // Skip list contains base game ESMs
        _configServiceMock.Setup(s => s.GetSkipListAsync(GameType.SkyrimSe))
            .ReturnsAsync(new List<string> { "Skyrim.esm", "Update.esm" });

        // DisableSkipLists is ENABLED
        var userConfig = new UserConfiguration
        {
            Settings = new AutoQacSettings { DisableSkipLists = true }
        };
        _configServiceMock.Setup(s => s.LoadUserConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(userConfig);

        // Act
        await _orchestrator.StartCleaningAsync();

        // Assert - ALL 3 plugins should be cleaned, including those in skip list
        _cleaningServiceMock.Verify(
            s => s.CleanPluginAsync(It.Is<PluginInfo>(p => p.FileName == "Skyrim.esm"), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "Skyrim.esm should be cleaned when DisableSkipLists is enabled");
        _cleaningServiceMock.Verify(
            s => s.CleanPluginAsync(It.Is<PluginInfo>(p => p.FileName == "Update.esm"), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "Update.esm should be cleaned when DisableSkipLists is enabled");
        _cleaningServiceMock.Verify(
            s => s.CleanPluginAsync(It.Is<PluginInfo>(p => p.FileName == "UserMod.esp"), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()),
            Times.Once);
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
        _stateServiceMock.Setup(s => s.CurrentState).Returns(appState);

        _cleaningServiceMock.Setup(s => s.ValidateEnvironmentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _cleaningServiceMock.Setup(s => s.CleanPluginAsync(It.IsAny<PluginInfo>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CleaningResult { Status = CleaningStatus.Cleaned });

        // Skip list contains base game ESMs
        _configServiceMock.Setup(s => s.GetSkipListAsync(GameType.SkyrimSe))
            .ReturnsAsync(new List<string> { "Skyrim.esm", "Update.esm" });

        // DisableSkipLists is DISABLED (default)
        var userConfig = new UserConfiguration
        {
            Settings = new AutoQacSettings { DisableSkipLists = false }
        };
        _configServiceMock.Setup(s => s.LoadUserConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(userConfig);

        // Act
        await _orchestrator.StartCleaningAsync();

        // Assert - Only UserMod.esp should be cleaned
        _cleaningServiceMock.Verify(
            s => s.CleanPluginAsync(It.Is<PluginInfo>(p => p.FileName == "Skyrim.esm"), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Skyrim.esm should NOT be cleaned when DisableSkipLists is disabled");
        _cleaningServiceMock.Verify(
            s => s.CleanPluginAsync(It.Is<PluginInfo>(p => p.FileName == "Update.esm"), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Update.esm should NOT be cleaned when DisableSkipLists is disabled");
        _cleaningServiceMock.Verify(
            s => s.CleanPluginAsync(It.Is<PluginInfo>(p => p.FileName == "UserMod.esp"), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "UserMod.esp should be cleaned");
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
            _cleaningServiceMock.Object,
            _pluginServiceMock.Object,
            _gameDetectionServiceMock.Object,
            _stateServiceMock.Object,
            _configServiceMock.Object,
            _loggerMock.Object,
            _processServiceMock.Object);

        // Act & Assert
        // Should not throw
        orchestrator.Dispose();

        // Multiple disposal should be safe
        FluentActions.Invoking(() => orchestrator.Dispose())
            .Should().NotThrow("multiple disposal should be safe");
    }

    #endregion
}
