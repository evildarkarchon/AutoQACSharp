using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Reactive.Subjects;
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
using System.Reactive.Linq;

namespace AutoQAC.Tests.ViewModels;

public sealed class MainWindowViewModelTests
{
    private readonly Mock<IConfigurationService> _configServiceMock;
    private readonly Mock<IStateService> _stateServiceMock;
    private readonly Mock<ICleaningOrchestrator> _orchestratorMock;
    private readonly Mock<ILoggingService> _loggerMock;
    private readonly Mock<IFileDialogService> _fileDialogMock;
    private readonly Mock<IPluginValidationService> _pluginServiceMock;

    public MainWindowViewModelTests()
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
    public async Task StartCleaningCommand_ShouldCallOrchestrator_WhenCanStart()
    {
        // Arrange
        var stateSubject = new BehaviorSubject<AppState>(new AppState());
        _stateServiceMock.Setup(s => s.StateChanged).Returns(stateSubject);
        _stateServiceMock.Setup(s => s.CurrentState).Returns(new AppState());

        var vm = new MainWindowViewModel(
            _configServiceMock.Object,
            _stateServiceMock.Object,
            _orchestratorMock.Object,
            _loggerMock.Object,
            _fileDialogMock.Object,
            _pluginServiceMock.Object);

        // Manually set properties to satisfy CanExecute
        vm.LoadOrderPath = "plugins.txt";
        vm.XEditPath = "xedit.exe";
        
        // Act
        // Verify CanExecute is true
        // var canExec = await vm.StartCleaningCommand.CanExecute.FirstAsync();
        // canExec.Should().BeTrue(); 
        
        await vm.StartCleaningCommand.Execute();

        // Assert
        _orchestratorMock.Verify(x => x.StartCleaningAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfigureLoadOrderCommand_ShouldUpdatePluginsList_WhenFileSelected()
    {
        // Arrange
        var stateSubject = new BehaviorSubject<AppState>(new AppState());
        _stateServiceMock.Setup(s => s.StateChanged).Returns(stateSubject);
        _stateServiceMock.Setup(s => s.CurrentState).Returns(new AppState());

        var vm = new MainWindowViewModel(
            _configServiceMock.Object,
            _stateServiceMock.Object,
            _orchestratorMock.Object,
            _loggerMock.Object,
            _fileDialogMock.Object,
            _pluginServiceMock.Object);

        var loadOrderPath = "plugins.txt";
        _fileDialogMock.Setup(x => x.OpenFileDialogAsync(
                It.IsAny<string>(), 
                It.IsAny<string>(), 
                It.IsAny<string?>()))
            .ReturnsAsync(loadOrderPath);

        _configServiceMock.Setup(x => x.LoadUserConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserConfiguration { LoadOrder = new(), XEdit = new(), ModOrganizer = new(), Settings = new() });

        var expectedPlugins = new List<PluginInfo>
        {
            new PluginInfo { FileName = "Update.esm", FullPath = "Update.esm", DetectedGameType = GameType.Unknown },
            new PluginInfo { FileName = "Dawnguard.esm", FullPath = "Dawnguard.esm", DetectedGameType = GameType.Unknown }
        };

        _pluginServiceMock.Setup(x => x.GetPluginsFromLoadOrderAsync(loadOrderPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedPlugins);

        // Act
        await vm.ConfigureLoadOrderCommand.Execute();

        // Assert
        _stateServiceMock.Verify(x => x.UpdateConfigurationPaths(loadOrderPath, It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        _stateServiceMock.Verify(x => x.SetPluginsToClean(It.Is<List<string>>(l => l.Count == 2 && l[0] == "Update.esm")), Times.Once);
        _configServiceMock.Verify(x => x.SaveUserConfigAsync(It.IsAny<UserConfiguration>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
