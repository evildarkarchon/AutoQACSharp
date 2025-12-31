using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameDetection;
using AutoQAC.Services.Process;
using AutoQAC.Services.State;
using FluentAssertions;
using Moq;

namespace AutoQAC.Tests.Services;

public sealed class CleaningServiceTests
{
    private readonly Mock<IConfigurationService> _mockConfig;
    private readonly Mock<IGameDetectionService> _mockGameDetection;
    private readonly Mock<IStateService> _mockState;
    private readonly Mock<ILoggingService> _mockLogger;
    private readonly Mock<IProcessExecutionService> _mockProcess;
    private readonly Mock<IXEditCommandBuilder> _mockCommandBuilder;
    private readonly Mock<IXEditOutputParser> _mockOutputParser;

    public CleaningServiceTests()
    {
        _mockConfig = new Mock<IConfigurationService>();
        _mockGameDetection = new Mock<IGameDetectionService>();
        _mockState = new Mock<IStateService>();
        _mockLogger = new Mock<ILoggingService>();
        _mockProcess = new Mock<IProcessExecutionService>();
        _mockCommandBuilder = new Mock<IXEditCommandBuilder>();
        _mockOutputParser = new Mock<IXEditOutputParser>();
    }

    [Fact]
    public async Task CleanPluginAsync_ShouldCallProcessAndReturnSuccess()
    {
        // Arrange
        var service = new CleaningService(
            _mockConfig.Object,
            _mockGameDetection.Object,
            _mockState.Object,
            _mockLogger.Object,
            _mockProcess.Object,
            _mockCommandBuilder.Object,
            _mockOutputParser.Object);

        var plugin = new PluginInfo
        {
            FileName = "Mod.esp",
            FullPath = "Mod.esp",
            DetectedGameType = GameType.SkyrimSe,
            IsInSkipList = false
        };

        // Mock State
        var appState = new AppState { CurrentGameType = GameType.SkyrimSe };
        _mockState.Setup(s => s.CurrentState).Returns(appState);

        // Mock Command Builder
        var startInfo = new System.Diagnostics.ProcessStartInfo("xEdit.exe");
        _mockCommandBuilder.Setup(b => b.BuildCommand(plugin, GameType.SkyrimSe))
            .Returns(startInfo);

        // Mock Process Execution
        var processResult = new ProcessResult
        {
            ExitCode = 0,
            OutputLines = new List<string> { "Undeleting: Foo", "Done." },
            TimedOut = false
        };
        _mockProcess.Setup(p => p.ExecuteAsync(startInfo, It.IsAny<IProgress<string>>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(processResult);

        // Mock Output Parser
        var stats = new CleaningStatistics { ItemsUndeleted = 1 };
        _mockOutputParser.Setup(p => p.ParseOutput(processResult.OutputLines))
            .Returns(stats);

        // Act
        var result = await service.CleanPluginAsync(plugin);

        // Assert
        result.Success.Should().BeTrue();
        result.Status.Should().Be(CleaningStatus.Cleaned);
        result.Statistics.Should().NotBeNull();
        result.Statistics!.ItemsUndeleted.Should().Be(1);

        _mockProcess.Verify(p => p.ExecuteAsync(startInfo, It.IsAny<IProgress<string>>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CleanPluginAsync_WhenSkipped_ShouldReturnSkipped()
    {
        // Arrange
        var service = new CleaningService(
            _mockConfig.Object,
            _mockGameDetection.Object,
            _mockState.Object,
            _mockLogger.Object,
            _mockProcess.Object,
            _mockCommandBuilder.Object,
            _mockOutputParser.Object);

        var plugin = new PluginInfo
        {
            FileName = "Mod.esp",
            FullPath = "Mod.esp",
            DetectedGameType = GameType.SkyrimSe,
            IsInSkipList = true // SKIPPED
        };

        // Act
        var result = await service.CleanPluginAsync(plugin);

        // Assert
        result.Success.Should().BeTrue();
        result.Status.Should().Be(CleaningStatus.Skipped);
        _mockProcess.Verify(p => p.ExecuteAsync(It.IsAny<System.Diagnostics.ProcessStartInfo>(), It.IsAny<IProgress<string>>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #region Error Path Tests

    /// <summary>
    /// Verifies that CleanPluginAsync returns Failed status when the command builder
    /// fails to build a command (returns null). This can happen with invalid configurations.
    /// </summary>
    [Fact]
    public async Task CleanPluginAsync_WhenCommandBuilderReturnsNull_ShouldReturnFailed()
    {
        // Arrange
        var service = new CleaningService(
            _mockConfig.Object,
            _mockGameDetection.Object,
            _mockState.Object,
            _mockLogger.Object,
            _mockProcess.Object,
            _mockCommandBuilder.Object,
            _mockOutputParser.Object);

        var plugin = new PluginInfo
        {
            FileName = "Mod.esp",
            FullPath = "Mod.esp",
            DetectedGameType = GameType.SkyrimSe,
            IsInSkipList = false
        };

        // Configure state with a valid game type
        var appState = new AppState { CurrentGameType = GameType.SkyrimSe };
        _mockState.Setup(s => s.CurrentState).Returns(appState);

        // Command builder returns null - simulating build failure
        _mockCommandBuilder.Setup(b => b.BuildCommand(plugin, GameType.SkyrimSe))
            .Returns((System.Diagnostics.ProcessStartInfo?)null);

        // Act
        var result = await service.CleanPluginAsync(plugin);

        // Assert
        result.Success.Should().BeFalse("cleaning should fail when command cannot be built");
        result.Status.Should().Be(CleaningStatus.Failed);
        result.Message.Should().Contain("Failed to build xEdit command");

        // Process should never be called since command building failed
        _mockProcess.Verify(p => p.ExecuteAsync(
            It.IsAny<System.Diagnostics.ProcessStartInfo>(),
            It.IsAny<IProgress<string>>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that CleanPluginAsync returns Failed status when the xEdit process
    /// times out during execution.
    /// </summary>
    [Fact]
    public async Task CleanPluginAsync_WhenProcessTimesOut_ShouldReturnFailed()
    {
        // Arrange
        var service = new CleaningService(
            _mockConfig.Object,
            _mockGameDetection.Object,
            _mockState.Object,
            _mockLogger.Object,
            _mockProcess.Object,
            _mockCommandBuilder.Object,
            _mockOutputParser.Object);

        var plugin = new PluginInfo
        {
            FileName = "SlowPlugin.esp",
            FullPath = "SlowPlugin.esp",
            DetectedGameType = GameType.SkyrimSe,
            IsInSkipList = false
        };

        // Configure state with timeout setting
        var appState = new AppState
        {
            CurrentGameType = GameType.SkyrimSe,
            CleaningTimeout = 300 // 5 minute timeout
        };
        _mockState.Setup(s => s.CurrentState).Returns(appState);

        // Mock command builder to return valid command
        var startInfo = new System.Diagnostics.ProcessStartInfo("xEdit.exe");
        _mockCommandBuilder.Setup(b => b.BuildCommand(plugin, GameType.SkyrimSe))
            .Returns(startInfo);

        // Mock process to return timed out result
        var processResult = new ProcessResult
        {
            ExitCode = -1,
            OutputLines = new List<string> { "Started cleaning..." },
            TimedOut = true
        };
        _mockProcess.Setup(p => p.ExecuteAsync(startInfo, It.IsAny<IProgress<string>>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(processResult);

        // Act
        var result = await service.CleanPluginAsync(plugin);

        // Assert
        result.Success.Should().BeFalse("timed out cleaning should be marked as failed");
        result.Status.Should().Be(CleaningStatus.Failed);
        result.Message!.ToLower().Should().Contain("timed out");
    }

    /// <summary>
    /// Verifies that CleanPluginAsync returns Failed status when the xEdit process
    /// exits with a non-zero exit code.
    /// </summary>
    [Fact]
    public async Task CleanPluginAsync_WhenProcessExitsWithError_ShouldReturnFailed()
    {
        // Arrange
        var service = new CleaningService(
            _mockConfig.Object,
            _mockGameDetection.Object,
            _mockState.Object,
            _mockLogger.Object,
            _mockProcess.Object,
            _mockCommandBuilder.Object,
            _mockOutputParser.Object);

        var plugin = new PluginInfo
        {
            FileName = "CorruptMod.esp",
            FullPath = "CorruptMod.esp",
            DetectedGameType = GameType.SkyrimSe,
            IsInSkipList = false
        };

        // Configure state
        var appState = new AppState { CurrentGameType = GameType.SkyrimSe };
        _mockState.Setup(s => s.CurrentState).Returns(appState);

        // Mock command builder
        var startInfo = new System.Diagnostics.ProcessStartInfo("xEdit.exe");
        _mockCommandBuilder.Setup(b => b.BuildCommand(plugin, GameType.SkyrimSe))
            .Returns(startInfo);

        // Mock process to return error exit code
        var processResult = new ProcessResult
        {
            ExitCode = 1, // Non-zero indicates error
            OutputLines = new List<string> { "Error: Failed to read plugin file" },
            ErrorLines = new List<string> { "Exception occurred" },
            TimedOut = false
        };
        _mockProcess.Setup(p => p.ExecuteAsync(startInfo, It.IsAny<IProgress<string>>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(processResult);

        // Act
        var result = await service.CleanPluginAsync(plugin);

        // Assert
        result.Success.Should().BeFalse("process error should mark cleaning as failed");
        result.Status.Should().Be(CleaningStatus.Failed);
        result.Message.Should().Contain("exited with code 1");
    }

    /// <summary>
    /// Verifies that CleanPluginAsync handles GameType.Unknown by falling back
    /// to the plugin's detected game type.
    /// </summary>
    [Fact]
    public async Task CleanPluginAsync_WhenGameTypeUnknown_ShouldUsePluginDetectedType()
    {
        // Arrange
        var service = new CleaningService(
            _mockConfig.Object,
            _mockGameDetection.Object,
            _mockState.Object,
            _mockLogger.Object,
            _mockProcess.Object,
            _mockCommandBuilder.Object,
            _mockOutputParser.Object);

        var plugin = new PluginInfo
        {
            FileName = "Mod.esp",
            FullPath = "Mod.esp",
            DetectedGameType = GameType.Fallout4, // Plugin has detected type
            IsInSkipList = false
        };

        // Configure state with Unknown game type
        var appState = new AppState { CurrentGameType = GameType.Unknown };
        _mockState.Setup(s => s.CurrentState).Returns(appState);

        // Mock command builder - verify it's called with Fallout4 (plugin's type)
        var startInfo = new System.Diagnostics.ProcessStartInfo("xEdit.exe");
        _mockCommandBuilder.Setup(b => b.BuildCommand(plugin, GameType.Fallout4))
            .Returns(startInfo);

        // Mock process execution
        var processResult = new ProcessResult
        {
            ExitCode = 0,
            OutputLines = new List<string> { "Done." },
            TimedOut = false
        };
        _mockProcess.Setup(p => p.ExecuteAsync(startInfo, It.IsAny<IProgress<string>>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(processResult);

        // Mock output parser
        _mockOutputParser.Setup(p => p.ParseOutput(processResult.OutputLines))
            .Returns(new CleaningStatistics());

        // Act
        var result = await service.CleanPluginAsync(plugin);

        // Assert
        result.Success.Should().BeTrue();

        // Verify command builder was called with plugin's detected game type
        _mockCommandBuilder.Verify(
            b => b.BuildCommand(plugin, GameType.Fallout4),
            Times.Once,
            "should use plugin's detected game type when state has Unknown");
    }

    /// <summary>
    /// Verifies that CleanPluginAsync returns Skipped status with appropriate message
    /// when the operation is cancelled via CancellationToken.
    /// </summary>
    [Fact]
    public async Task CleanPluginAsync_WhenCancelled_ShouldReturnSkippedWithCancelMessage()
    {
        // Arrange
        var service = new CleaningService(
            _mockConfig.Object,
            _mockGameDetection.Object,
            _mockState.Object,
            _mockLogger.Object,
            _mockProcess.Object,
            _mockCommandBuilder.Object,
            _mockOutputParser.Object);

        var plugin = new PluginInfo
        {
            FileName = "Mod.esp",
            FullPath = "Mod.esp",
            DetectedGameType = GameType.SkyrimSe,
            IsInSkipList = false
        };

        // Configure state
        var appState = new AppState { CurrentGameType = GameType.SkyrimSe };
        _mockState.Setup(s => s.CurrentState).Returns(appState);

        // Mock command builder
        var startInfo = new System.Diagnostics.ProcessStartInfo("xEdit.exe");
        _mockCommandBuilder.Setup(b => b.BuildCommand(plugin, GameType.SkyrimSe))
            .Returns(startInfo);

        // Mock process to throw OperationCanceledException
        _mockProcess.Setup(p => p.ExecuteAsync(startInfo, It.IsAny<IProgress<string>>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await service.CleanPluginAsync(plugin);

        // Assert
        result.Success.Should().BeFalse("cancelled operation is not successful");
        result.Status.Should().Be(CleaningStatus.Skipped);
        result.Message!.ToLower().Should().Contain("cancelled");
    }

    /// <summary>
    /// Verifies that CleanPluginAsync handles unexpected exceptions gracefully
    /// and returns a Failed result with the error message.
    /// </summary>
    [Fact]
    public async Task CleanPluginAsync_WhenUnexpectedExceptionThrown_ShouldReturnFailed()
    {
        // Arrange
        var service = new CleaningService(
            _mockConfig.Object,
            _mockGameDetection.Object,
            _mockState.Object,
            _mockLogger.Object,
            _mockProcess.Object,
            _mockCommandBuilder.Object,
            _mockOutputParser.Object);

        var plugin = new PluginInfo
        {
            FileName = "Mod.esp",
            FullPath = "Mod.esp",
            DetectedGameType = GameType.SkyrimSe,
            IsInSkipList = false
        };

        // Configure state
        var appState = new AppState { CurrentGameType = GameType.SkyrimSe };
        _mockState.Setup(s => s.CurrentState).Returns(appState);

        // Mock command builder
        var startInfo = new System.Diagnostics.ProcessStartInfo("xEdit.exe");
        _mockCommandBuilder.Setup(b => b.BuildCommand(plugin, GameType.SkyrimSe))
            .Returns(startInfo);

        // Mock process to throw unexpected exception
        _mockProcess.Setup(p => p.ExecuteAsync(startInfo, It.IsAny<IProgress<string>>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Unexpected error during process execution"));

        // Act
        var result = await service.CleanPluginAsync(plugin);

        // Assert
        result.Success.Should().BeFalse("unexpected exception should result in failure");
        result.Status.Should().Be(CleaningStatus.Failed);
        result.Message.Should().Contain("Unexpected error");

        // Verify error was logged
        _mockLogger.Verify(
            l => l.Error(It.IsAny<Exception>(), It.Is<string>(s => s.Contains("Mod.esp"))),
            Times.Once,
            "exception should be logged with plugin name");
    }

    #endregion

    #region Environment Validation Tests

    /// <summary>
    /// Verifies that ValidateEnvironmentAsync returns false when xEdit path is missing.
    /// </summary>
    [Fact]
    public async Task ValidateEnvironmentAsync_WhenXEditPathMissing_ShouldReturnFalse()
    {
        // Arrange
        var service = new CleaningService(
            _mockConfig.Object,
            _mockGameDetection.Object,
            _mockState.Object,
            _mockLogger.Object,
            _mockProcess.Object,
            _mockCommandBuilder.Object,
            _mockOutputParser.Object);

        // State has load order but no xEdit path
        var appState = new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = null
        };
        _mockState.Setup(s => s.CurrentState).Returns(appState);

        // Act
        var result = await service.ValidateEnvironmentAsync();

        // Assert
        result.Should().BeFalse("validation should fail without xEdit path");
    }

    /// <summary>
    /// Verifies that ValidateEnvironmentAsync returns false when load order path is missing.
    /// </summary>
    [Fact]
    public async Task ValidateEnvironmentAsync_WhenLoadOrderPathMissing_ShouldReturnFalse()
    {
        // Arrange
        var service = new CleaningService(
            _mockConfig.Object,
            _mockGameDetection.Object,
            _mockState.Object,
            _mockLogger.Object,
            _mockProcess.Object,
            _mockCommandBuilder.Object,
            _mockOutputParser.Object);

        // State has xEdit but no load order path
        var appState = new AppState
        {
            LoadOrderPath = null,
            XEditExecutablePath = "xEdit.exe"
        };
        _mockState.Setup(s => s.CurrentState).Returns(appState);

        // Act
        var result = await service.ValidateEnvironmentAsync();

        // Assert
        result.Should().BeFalse("validation should fail without load order path");
    }

    #endregion
}
