using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;
using FluentAssertions;
using Moq;
using Xunit;

namespace AutoQAC.Tests.Services;

public sealed class CleaningOrchestratorTests
{
    private readonly Mock<ICleaningService> _cleaningServiceMock;
    private readonly Mock<IPluginValidationService> _pluginServiceMock;
    private readonly Mock<IStateService> _stateServiceMock;
    private readonly Mock<IConfigurationService> _configServiceMock;
    private readonly Mock<ILoggingService> _loggerMock;
    private readonly CleaningOrchestrator _orchestrator;

    public CleaningOrchestratorTests()
    {
        _cleaningServiceMock = new Mock<ICleaningService>();
        _pluginServiceMock = new Mock<IPluginValidationService>();
        _stateServiceMock = new Mock<IStateService>();
        _configServiceMock = new Mock<IConfigurationService>();
        _loggerMock = new Mock<ILoggingService>();

        _orchestrator = new CleaningOrchestrator(
            _cleaningServiceMock.Object,
            _pluginServiceMock.Object,
            _stateServiceMock.Object,
            _configServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task StartCleaningAsync_ShouldProcessPlugins_WhenConfigIsValid()
    {
        // Arrange
        var appState = new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSpecialEdition
        };

        _stateServiceMock.Setup(s => s.CurrentState).Returns(appState);

        _cleaningServiceMock.Setup(s => s.ValidateEnvironmentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var plugins = new List<PluginInfo>
        {
            new() { FileName = "Plugin1.esp", FullPath = "Path/Plugin1.esp" },
            new() { FileName = "Plugin2.esp", FullPath = "Path/Plugin2.esp" }
        };
        _pluginServiceMock.Setup(s => s.GetPluginsFromLoadOrderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(plugins);

        _configServiceMock.Setup(s => s.GetSkipListAsync(It.IsAny<GameType>()))
            .ReturnsAsync(new List<string>());

        _pluginServiceMock.Setup(s => s.FilterSkippedPlugins(plugins, It.IsAny<List<string>>()))
            .Returns(plugins); // No skip

        _cleaningServiceMock.Setup(s => s.CleanPluginAsync(It.IsAny<PluginInfo>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CleaningResult { Status = CleaningStatus.Cleaned });

        // Act
        await _orchestrator.StartCleaningAsync();

        // Assert
        _cleaningServiceMock.Verify(s => s.CleanPluginAsync(It.Is<PluginInfo>(p => p.FileName == "Plugin1.esp"), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        _cleaningServiceMock.Verify(s => s.CleanPluginAsync(It.Is<PluginInfo>(p => p.FileName == "Plugin2.esp"), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        _stateServiceMock.Verify(s => s.StartCleaning(It.IsAny<List<string>>()), Times.Once);
        _stateServiceMock.Verify(s => s.FinishCleaning(), Times.Once);
    }

    [Fact]
    public async Task StartCleaningAsync_ShouldThrow_WhenConfigIsInvalid()
    {
        // Arrange
        var appState = new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = "xedit.exe",
            CurrentGameType = GameType.SkyrimSpecialEdition
        };
        _stateServiceMock.Setup(s => s.CurrentState).Returns(appState);

        _cleaningServiceMock.Setup(s => s.ValidateEnvironmentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var act = () => _orchestrator.StartCleaningAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        _stateServiceMock.Verify(s => s.FinishCleaning(), Times.Once); // It calls Finish in catch block
    }
}
