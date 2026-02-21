using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using AutoQAC.Services.Plugin;
using AutoQAC.ViewModels;
using NSubstitute;
using ReactiveUI;

namespace AutoQAC.Tests.ViewModels;

public sealed class MainWindowViewModelInitializationTests
{
    private readonly IConfigurationService _configServiceMock;
    private readonly IStateService _stateServiceMock;
    private readonly ICleaningOrchestrator _orchestratorMock;
    private readonly ILoggingService _loggerMock;
    private readonly IFileDialogService _fileDialogMock;
    private readonly IMessageDialogService _messageDialogMock;
    private readonly IPluginValidationService _pluginServiceMock;
    private readonly IPluginLoadingService _pluginLoadingServiceMock;

    public MainWindowViewModelInitializationTests()
    {
        _configServiceMock = Substitute.For<IConfigurationService>();
        _stateServiceMock = Substitute.For<IStateService>();
        _orchestratorMock = Substitute.For<ICleaningOrchestrator>();
        _loggerMock = Substitute.For<ILoggingService>();
        _fileDialogMock = Substitute.For<IFileDialogService>();
        _messageDialogMock = Substitute.For<IMessageDialogService>();
        _pluginServiceMock = Substitute.For<IPluginValidationService>();
        _pluginLoadingServiceMock = Substitute.For<IPluginLoadingService>();

        // Default setup for plugin loading service
        _pluginLoadingServiceMock.GetAvailableGames()
            .Returns(new List<GameType> { GameType.SkyrimSe, GameType.Fallout4 });
        _pluginLoadingServiceMock.IsGameSupportedByMutagen(Arg.Any<GameType>())
            .Returns(false);

        // Default setup for CleaningCompleted observable
        _stateServiceMock.CleaningCompleted
            .Returns(Observable.Never<CleaningSessionResult>());

        // Default setup for SkipListChanged observable
        _configServiceMock.SkipListChanged
            .Returns(Observable.Never<GameType>());

        RxApp.MainThreadScheduler = Scheduler.Immediate;
    }

    [Fact]
    public async Task Constructor_ShouldLoadConfigAndInitializeState()
    {
        // Arrange
        var stateSubject = new BehaviorSubject<AppState>(new AppState());
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState());

        var config = new UserConfiguration
        {
            LoadOrder = new LoadOrderConfig { File = "loadorder.txt" },
            ModOrganizer = new ModOrganizerConfig { Binary = "mo2.exe" },
            XEdit = new XEditConfig { Binary = "xedit.exe" },
            Settings = new AutoQacSettings { Mo2Mode = true }
        };

        _configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
            .Returns(config);

        // Act
        var vm = new MainWindowViewModel(
            _configServiceMock,
            _stateServiceMock,
            _orchestratorMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock);

        // Allow async void to run
        await Task.Delay(100);

        // Assert
        await _configServiceMock.Received(1).LoadUserConfigAsync(Arg.Any<CancellationToken>());

        _stateServiceMock.Received(1).UpdateConfigurationPaths(
            null,
            "mo2.exe",
            "xedit.exe");

        _stateServiceMock.Received().UpdateState(Arg.Any<Func<AppState, AppState>>());
    }
}
