using System;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using AutoQAC.Services.Plugin;
using AutoQAC.ViewModels;
using FluentAssertions;
using Moq;
using ReactiveUI;
using Xunit;

namespace AutoQAC.Tests.ViewModels;

public sealed class MainWindowViewModelInitializationTests
{
    private readonly Mock<IConfigurationService> _configServiceMock;
    private readonly Mock<IStateService> _stateServiceMock;
    private readonly Mock<ICleaningOrchestrator> _orchestratorMock;
    private readonly Mock<ILoggingService> _loggerMock;
    private readonly Mock<IFileDialogService> _fileDialogMock;
    private readonly Mock<IPluginValidationService> _pluginServiceMock;

    public MainWindowViewModelInitializationTests()
    {
        _configServiceMock = new Mock<IConfigurationService>();
        _stateServiceMock = new Mock<IStateService>();
        _orchestratorMock = new Mock<ICleaningOrchestrator>();
        _loggerMock = new Mock<ILoggingService>();
        _fileDialogMock = new Mock<IFileDialogService>();
        _pluginServiceMock = new Mock<IPluginValidationService>();

        RxApp.MainThreadScheduler = Scheduler.Immediate;
    }

    [Fact]
    public async Task Constructor_ShouldLoadConfigAndInitializeState()
    {
        // Arrange
        var stateSubject = new BehaviorSubject<AppState>(new AppState());
        _stateServiceMock.Setup(s => s.StateChanged).Returns(stateSubject);
        _stateServiceMock.Setup(s => s.CurrentState).Returns(new AppState());

        var config = new UserConfiguration
        {
            LoadOrder = new LoadOrderConfig { File = "loadorder.txt" },
            ModOrganizer = new ModOrganizerConfig { Binary = "mo2.exe" },
            XEdit = new XEditConfig { Binary = "xedit.exe" },
            Settings = new PactSettings { MO2Mode = true }
        };

        _configServiceMock.Setup(c => c.LoadUserConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        // Act
        var vm = new MainWindowViewModel(
            _configServiceMock.Object,
            _stateServiceMock.Object,
            _orchestratorMock.Object,
            _loggerMock.Object,
            _fileDialogMock.Object,
            _pluginServiceMock.Object);

        // Allow async void to run
        await Task.Delay(100); 

        // Assert
        _configServiceMock.Verify(x => x.LoadUserConfigAsync(It.IsAny<CancellationToken>()), Times.Once);
        
        _stateServiceMock.Verify(x => x.UpdateConfigurationPaths(
            "loadorder.txt", 
            "mo2.exe", 
            "xedit.exe"), Times.Once);
            
        _stateServiceMock.Verify(x => x.UpdateState(It.IsAny<Func<AppState, AppState>>()), Times.AtLeastOnce);
    }
}
