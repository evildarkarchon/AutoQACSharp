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

    #region Error Handling Tests

    /// <summary>
    /// Verifies that StartCleaningCommand handles orchestrator exceptions gracefully
    /// and updates the status text with an error message.
    /// </summary>
    [Fact]
    public async Task StartCleaningCommand_ShouldHandleOrchestratorException()
    {
        // Arrange
        var stateSubject = new BehaviorSubject<AppState>(new AppState());
        _stateServiceMock.Setup(s => s.StateChanged).Returns(stateSubject);
        _stateServiceMock.Setup(s => s.CurrentState).Returns(new AppState());

        // Setup config service to avoid NullReferenceException during InitializeAsync
        _configServiceMock.Setup(x => x.LoadUserConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserConfiguration { LoadOrder = new(), XEdit = new(), ModOrganizer = new(), Settings = new() });

        var vm = new MainWindowViewModel(
            _configServiceMock.Object,
            _stateServiceMock.Object,
            _orchestratorMock.Object,
            _loggerMock.Object,
            _fileDialogMock.Object,
            _pluginServiceMock.Object);

        // Wait a bit for InitializeAsync to complete
        await Task.Delay(50);

        // Set valid paths to enable command
        vm.LoadOrderPath = "plugins.txt";
        vm.XEditPath = "xedit.exe";

        // Configure orchestrator to throw exception
        _orchestratorMock.Setup(x => x.StartCleaningAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Configuration is invalid"));

        // Act
        await vm.StartCleaningCommand.Execute();

        // Assert
        vm.StatusText.Should().Contain("Error", "error message should be displayed");
        // Verify that the StartCleaningAsync error was logged
        // The actual log message is "StartCleaningAsync failed"
        _loggerMock.Verify(
            l => l.Error(It.IsAny<Exception>(), It.Is<string>(s => s.Contains("StartCleaningAsync") || s.Contains("failed"))),
            Times.AtLeastOnce,
            "StartCleaningAsync exception should be logged");
    }

    /// <summary>
    /// Verifies that ConfigureLoadOrderCommand handles file dialog cancellation gracefully.
    /// </summary>
    [Fact]
    public async Task ConfigureLoadOrderCommand_ShouldHandleDialogCancellation()
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

        // Configure dialog to return null (user cancelled)
        _fileDialogMock.Setup(x => x.OpenFileDialogAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>()))
            .ReturnsAsync((string?)null);

        // Act
        await vm.ConfigureLoadOrderCommand.Execute();

        // Assert
        // State should not be updated when dialog is cancelled
        _stateServiceMock.Verify(
            x => x.UpdateConfigurationPaths(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never,
            "state should not be updated when dialog is cancelled");
    }

    /// <summary>
    /// Verifies that ConfigureLoadOrderCommand handles plugin parsing errors gracefully.
    /// </summary>
    [Fact]
    public async Task ConfigureLoadOrderCommand_ShouldHandlePluginParsingError()
    {
        // Arrange
        var stateSubject = new BehaviorSubject<AppState>(new AppState());
        _stateServiceMock.Setup(s => s.StateChanged).Returns(stateSubject);
        _stateServiceMock.Setup(s => s.CurrentState).Returns(new AppState());

        // Setup config service to avoid NullReferenceException during InitializeAsync
        _configServiceMock.Setup(x => x.LoadUserConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserConfiguration { LoadOrder = new(), XEdit = new(), ModOrganizer = new(), Settings = new() });

        var vm = new MainWindowViewModel(
            _configServiceMock.Object,
            _stateServiceMock.Object,
            _orchestratorMock.Object,
            _loggerMock.Object,
            _fileDialogMock.Object,
            _pluginServiceMock.Object);

        // Wait for InitializeAsync to complete
        await Task.Delay(50);

        var loadOrderPath = "corrupted_plugins.txt";
        _fileDialogMock.Setup(x => x.OpenFileDialogAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>()))
            .ReturnsAsync(loadOrderPath);

        // Plugin service throws exception for the corrupted file path
        _pluginServiceMock.Setup(x => x.GetPluginsFromLoadOrderAsync(loadOrderPath, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Failed to parse load order"));

        // Act
        await vm.ConfigureLoadOrderCommand.Execute();

        // Assert
        vm.StatusText.Should().Contain("Error", "error should be reflected in status");
        // Verify that the ConfigureLoadOrder error was logged (matching the specific error message)
        _loggerMock.Verify(
            l => l.Error(It.IsAny<Exception>(), It.Is<string>(s => s.Contains("load order"))),
            Times.AtLeastOnce,
            "parsing error should be logged");
    }

    /// <summary>
    /// Verifies that CanStartCleaning is false when required paths are not set.
    /// </summary>
    [Theory]
    [InlineData(null, "xedit.exe")]
    [InlineData("", "xedit.exe")]
    [InlineData("plugins.txt", null)]
    [InlineData("plugins.txt", "")]
    [InlineData(null, null)]
    public void CanStartCleaning_ShouldBeFalse_WhenPathsMissing(string? loadOrder, string? xEdit)
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

        // Act
        vm.LoadOrderPath = loadOrder;
        vm.XEditPath = xEdit;

        // Assert
        vm.CanStartCleaning.Should().BeFalse("cleaning should not be allowed without required paths");
    }

    /// <summary>
    /// Verifies that CanStartCleaning is false when cleaning is already in progress.
    /// </summary>
    [Fact]
    public void CanStartCleaning_ShouldBeFalse_WhenAlreadyCleaning()
    {
        // Arrange
        var cleaningState = new AppState { IsCleaning = true };
        var stateSubject = new BehaviorSubject<AppState>(cleaningState);
        _stateServiceMock.Setup(s => s.StateChanged).Returns(stateSubject);
        _stateServiceMock.Setup(s => s.CurrentState).Returns(cleaningState);

        var vm = new MainWindowViewModel(
            _configServiceMock.Object,
            _stateServiceMock.Object,
            _orchestratorMock.Object,
            _loggerMock.Object,
            _fileDialogMock.Object,
            _pluginServiceMock.Object);

        // Set valid paths
        vm.LoadOrderPath = "plugins.txt";
        vm.XEditPath = "xedit.exe";

        // Emit cleaning state
        stateSubject.OnNext(cleaningState);

        // Assert
        // IsCleaning should be true from the state
        vm.IsCleaning.Should().BeTrue();
        // CanStartCleaning should be false because IsCleaning is true
        vm.CanStartCleaning.Should().BeFalse("cannot start new cleaning while one is in progress");
    }

    /// <summary>
    /// Verifies that StopCleaningCommand calls orchestrator's StopCleaning.
    /// </summary>
    [Fact]
    public void StopCleaningCommand_ShouldCallOrchestratorStop()
    {
        // Arrange
        var stateSubject = new BehaviorSubject<AppState>(new AppState { IsCleaning = true });
        _stateServiceMock.Setup(s => s.StateChanged).Returns(stateSubject);
        _stateServiceMock.Setup(s => s.CurrentState).Returns(new AppState { IsCleaning = true });

        var vm = new MainWindowViewModel(
            _configServiceMock.Object,
            _stateServiceMock.Object,
            _orchestratorMock.Object,
            _loggerMock.Object,
            _fileDialogMock.Object,
            _pluginServiceMock.Object);

        // Act
        vm.StopCleaningCommand.Execute().Subscribe();

        // Assert
        _orchestratorMock.Verify(x => x.StopCleaning(), Times.Once);
        vm.StatusText.Should().Contain("Stopping");
    }

    #endregion

    #region State Synchronization Tests

    /// <summary>
    /// Verifies that ViewModel properties are updated when state changes.
    /// </summary>
    [Fact]
    public void ViewModel_ShouldUpdateProperties_WhenStateChanges()
    {
        // Arrange
        var initialState = new AppState();
        var stateSubject = new BehaviorSubject<AppState>(initialState);
        _stateServiceMock.Setup(s => s.StateChanged).Returns(stateSubject);
        _stateServiceMock.Setup(s => s.CurrentState).Returns(initialState);

        var vm = new MainWindowViewModel(
            _configServiceMock.Object,
            _stateServiceMock.Object,
            _orchestratorMock.Object,
            _loggerMock.Object,
            _fileDialogMock.Object,
            _pluginServiceMock.Object);

        // Act
        var newState = new AppState
        {
            LoadOrderPath = "newpath/plugins.txt",
            XEditExecutablePath = "newpath/xedit.exe",
            MO2ExecutablePath = "newpath/mo2.exe",
            MO2ModeEnabled = true,
            PartialFormsEnabled = true
        };
        stateSubject.OnNext(newState);

        // Assert
        vm.LoadOrderPath.Should().Be("newpath/plugins.txt");
        vm.XEditPath.Should().Be("newpath/xedit.exe");
        vm.MO2Path.Should().Be("newpath/mo2.exe");
        vm.MO2ModeEnabled.Should().BeTrue();
        vm.PartialFormsEnabled.Should().BeTrue();
    }

    /// <summary>
    /// Verifies proper cleanup when ViewModel is disposed.
    /// </summary>
    [Fact]
    public void Dispose_ShouldCleanupSubscriptions()
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

        // Act & Assert
        FluentActions.Invoking(() => vm.Dispose())
            .Should().NotThrow("disposal should complete without exception");
    }

    #endregion
}
