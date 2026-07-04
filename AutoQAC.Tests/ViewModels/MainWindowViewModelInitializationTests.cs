using System.Reactive.Linq;
using System.Reactive.Subjects;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameCapability;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using AutoQAC.Services.Plugin;
using AutoQAC.Tests.TestInfrastructure;
using AutoQAC.ViewModels;
using NSubstitute;

namespace AutoQAC.Tests.ViewModels;

public sealed class MainWindowViewModelInitializationTests
{
    private readonly IConfigurationService _configServiceMock;
    private readonly IStateService _stateServiceMock;
    private readonly ICleaningSession _cleaningSessionMock;
    private readonly ILoggingService _loggerMock;
    private readonly IFileDialogService _fileDialogMock;
    private readonly IMessageDialogService _messageDialogMock;
    private readonly IPluginValidationService _pluginServiceMock;
    private readonly IPluginLoadingService _pluginLoadingServiceMock;
    private readonly IUiDispatcher _uiDispatcher;

    public MainWindowViewModelInitializationTests()
    {
        _configServiceMock = Substitute.For<IConfigurationService>();
        _stateServiceMock = Substitute.For<IStateService>();
        _cleaningSessionMock = Substitute.For<ICleaningSession>();
        _loggerMock = Substitute.For<ILoggingService>();
        _fileDialogMock = Substitute.For<IFileDialogService>();
        _messageDialogMock = Substitute.For<IMessageDialogService>();
        _pluginServiceMock = Substitute.For<IPluginValidationService>();
        _pluginLoadingServiceMock = Substitute.For<IPluginLoadingService>();
        _uiDispatcher = new SynchronousUiDispatcher();

        // Default setup for CleaningCompleted observable
        _stateServiceMock.CleaningCompleted
            .Returns(Observable.Never<CleaningSessionResult>());

        // Default setup for SkipListChanged observable
        _configServiceMock.SkipListChanged
            .Returns(Observable.Never<GameType>());
    }

    private static TaskCompletionSource<bool> CreateSignal()
    {
        return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static Task WaitForSignalAsync(TaskCompletionSource<bool> signal)
    {
        return signal.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Constructor_ShouldLoadConfigAndInitializeState()
    {
        // Arrange
        var initializationApplied = CreateSignal();
        var stateSubject = new BehaviorSubject<AppState>(new AppState());
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState());
        _stateServiceMock.When(x => x.UpdateState(Arg.Any<Func<AppState, AppState>>()))
            .Do(_ => initializationApplied.TrySetResult(true));

        var config = new UserConfiguration
        {
            LoadOrder = new LoadOrderConfig { File = "loadorder.txt" },
            ModOrganizer = new ModOrganizerConfig { Binary = "mo2.exe" },
            XEdit = new XEditConfig { Binary = "xedit.exe" },
            Settings = new AutoQacSettings { Mo2Mode = true }
        };

        _configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
            .Returns(config);
        using var refreshModule = new RecordingPluginRefreshModule();
        refreshModule.ExecuteHandler = (intent, _) => Task.FromResult(
            intent is PluginRefreshIntent.RefreshGame refresh
                ? RecordingPluginRefreshModule.CreateSnapshot(gameType: refresh.GameType)
                : refreshModule.CurrentSnapshot);
        var discoveryPlanner = new PluginRefreshDiscoveryPlanner(
            _configServiceMock,
            _pluginLoadingServiceMock,
            Substitute.For<AutoQAC.Services.MO2.IMo2InstanceService>());

        // Act
        var vm = new MainWindowViewModel(
            _configServiceMock,
            _stateServiceMock,
            _cleaningSessionMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock,
            _uiDispatcher,
            refreshModule,
            discoveryPlanner,
            new CleaningCommandReadiness(refreshModule, _stateServiceMock));

        await WaitForSignalAsync(initializationApplied);

        // Assert
        await _configServiceMock.Received(1).LoadUserConfigAsync(Arg.Any<CancellationToken>());

        _stateServiceMock.Received(1).UpdateConfigurationPaths(
            null,
            "mo2.exe",
            "xedit.exe");

        _stateServiceMock.Received().UpdateState(Arg.Any<Func<AppState, AppState>>());
    }
}
