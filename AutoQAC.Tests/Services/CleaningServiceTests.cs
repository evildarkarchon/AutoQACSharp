using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameDetection;
using AutoQAC.Services.Process;
using AutoQAC.Services.State;
using FluentAssertions;
using Moq;
using Xunit;

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
            DetectedGameType = GameType.SkyrimSpecialEdition,
            IsInSkipList = false
        };

        // Mock State
        var appState = new AppState { CurrentGameType = GameType.SkyrimSpecialEdition };
        _mockState.Setup(s => s.CurrentState).Returns(appState);

        // Mock Command Builder
        var startInfo = new System.Diagnostics.ProcessStartInfo("xEdit.exe");
        _mockCommandBuilder.Setup(b => b.BuildCommand(plugin, GameType.SkyrimSpecialEdition))
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
            DetectedGameType = GameType.SkyrimSpecialEdition,
            IsInSkipList = true // SKIPPED
        };

        // Act
        var result = await service.CleanPluginAsync(plugin);

        // Assert
        result.Success.Should().BeTrue();
        result.Status.Should().Be(CleaningStatus.Skipped);
        _mockProcess.Verify(p => p.ExecuteAsync(It.IsAny<System.Diagnostics.ProcessStartInfo>(), It.IsAny<IProgress<string>>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
