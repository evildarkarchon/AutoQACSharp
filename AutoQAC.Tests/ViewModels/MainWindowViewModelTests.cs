using System.Reactive.Concurrency;
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
using FluentAssertions;
using Moq;
using ReactiveUI;
using System.Reactive.Linq;

namespace AutoQAC.Tests.ViewModels;

public sealed class MainWindowViewModelTests
{
    private readonly Mock<IConfigurationService> _configServiceMock;
    private readonly Mock<IStateService> _stateServiceMock;
    private readonly Mock<ICleaningOrchestrator> _orchestratorMock;
    private readonly Mock<ILoggingService> _loggerMock;
    private readonly Mock<IFileDialogService> _fileDialogMock;
    private readonly Mock<IMessageDialogService> _messageDialogMock;
    private readonly Mock<IPluginValidationService> _pluginServiceMock;
    private readonly Mock<IPluginLoadingService> _pluginLoadingServiceMock;

    public MainWindowViewModelTests()
    {
        _configServiceMock = new Mock<IConfigurationService>();
        _stateServiceMock = new Mock<IStateService>();
        _orchestratorMock = new Mock<ICleaningOrchestrator>();
        _loggerMock = new Mock<ILoggingService>();
        _fileDialogMock = new Mock<IFileDialogService>();
        _messageDialogMock = new Mock<IMessageDialogService>();
        _pluginServiceMock = new Mock<IPluginValidationService>();
        _pluginLoadingServiceMock = new Mock<IPluginLoadingService>();

        // Default setup for plugin loading service
        _pluginLoadingServiceMock.Setup(x => x.GetAvailableGames())
            .Returns(new List<GameType> { GameType.SkyrimSe, GameType.Fallout4 });
        _pluginLoadingServiceMock.Setup(x => x.IsGameSupportedByMutagen(It.IsAny<GameType>()))
            .Returns(false);

        // Default setup for CleaningCompleted observable
        _stateServiceMock.Setup(s => s.CleaningCompleted)
            .Returns(Observable.Never<CleaningSessionResult>());

        // Default setup for SkipListChanged observable
        _configServiceMock.Setup(s => s.SkipListChanged)
            .Returns(Observable.Never<GameType>());

        RxApp.MainThreadScheduler = Scheduler.Immediate;
    }

    [Fact]
    public async Task StartCleaningCommand_ShouldCallOrchestrator_WhenCanStart()
    {
        // Arrange - create temp file to satisfy File.Exists check in ValidatePreClean
        var tempFile = Path.GetTempFileName();
        try
        {
            var stateWithPlugins = new AppState
            {
                XEditExecutablePath = tempFile,
                PluginsToClean = new List<PluginInfo>
                {
                    new() { FileName = "Test.esp", FullPath = "Test.esp", IsSelected = true }
                }
            };
            var stateSubject = new BehaviorSubject<AppState>(stateWithPlugins);
            _stateServiceMock.Setup(s => s.StateChanged).Returns(stateSubject);
            _stateServiceMock.Setup(s => s.CurrentState).Returns(stateWithPlugins);

            var vm = new MainWindowViewModel(
                _configServiceMock.Object,
                _stateServiceMock.Object,
                _orchestratorMock.Object,
                _loggerMock.Object,
                _fileDialogMock.Object,
                _messageDialogMock.Object,
                _pluginServiceMock.Object,
                _pluginLoadingServiceMock.Object);

            // Manually set properties to satisfy CanExecute (use temp file path)
            vm.LoadOrderPath = "plugins.txt";
            vm.XEditPath = tempFile; // Use actual existing file

            // Act
            await vm.StartCleaningCommand.Execute();

            // Assert - verify the 3-param overload with timeout and backup failure callbacks is called
            _orchestratorMock.Verify(
                x => x.StartCleaningAsync(It.IsAny<TimeoutRetryCallback>(), It.IsAny<BackupFailureCallback>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ConfigureLoadOrderCommand_ShouldUpdatePluginsList_WhenFileSelected()
    {
        // Arrange
        var stateSubject = new BehaviorSubject<AppState>(new AppState());
        _stateServiceMock.Setup(s => s.StateChanged).Returns(stateSubject);
        _stateServiceMock.Setup(s => s.CurrentState).Returns(new AppState());

        // Create temp file to satisfy File.Exists check
        var tempFile = Path.GetTempFileName();
        try
        {
            var vm = new MainWindowViewModel(
                _configServiceMock.Object,
                _stateServiceMock.Object,
                _orchestratorMock.Object,
                _loggerMock.Object,
                _fileDialogMock.Object,
                _messageDialogMock.Object,
                _pluginServiceMock.Object,
                _pluginLoadingServiceMock.Object);

            _fileDialogMock.Setup(x => x.OpenFileDialogAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>()))
                .ReturnsAsync(tempFile);

            _configServiceMock.Setup(x => x.LoadUserConfigAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UserConfiguration { LoadOrder = new(), XEdit = new(), ModOrganizer = new(), Settings = new() });

            var expectedPlugins = new List<PluginInfo>
            {
                new PluginInfo { FileName = "Update.esm", FullPath = "Update.esm", DetectedGameType = GameType.Unknown },
                new PluginInfo { FileName = "Dawnguard.esm", FullPath = "Dawnguard.esm", DetectedGameType = GameType.Unknown }
            };

            _pluginServiceMock.Setup(x => x.GetPluginsFromLoadOrderAsync(tempFile, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedPlugins);

            // Setup skip list (required for ApplySkipListStatus)
            _configServiceMock.Setup(x => x.GetSkipListAsync(It.IsAny<GameType>()))
                .ReturnsAsync(new List<string>());

            // Act
            await vm.ConfigureLoadOrderCommand.Execute();

            // Assert
            _stateServiceMock.Verify(x => x.UpdateConfigurationPaths(tempFile, It.IsAny<string>(), It.IsAny<string>()), Times.Once);
            _stateServiceMock.Verify(x => x.SetPluginsToClean(It.Is<List<PluginInfo>>(l => l.Count == 2 && l[0].FileName == "Update.esm")), Times.Once);
            _configServiceMock.Verify(x => x.SaveUserConfigAsync(It.IsAny<UserConfiguration>(), It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    #region Error Handling Tests

    /// <summary>
    /// Verifies that StartCleaningCommand handles orchestrator exceptions gracefully
    /// and shows inline validation errors instead of modal dialog.
    /// </summary>
    [Fact]
    public async Task StartCleaningCommand_ShouldHandleOrchestratorException()
    {
        // Arrange - create temp file to satisfy File.Exists check in ValidatePreClean
        var tempFile = Path.GetTempFileName();
        try
        {
            var stateWithPlugins = new AppState
            {
                XEditExecutablePath = tempFile,
                PluginsToClean = new List<PluginInfo>
                {
                    new() { FileName = "Test.esp", FullPath = "Test.esp", IsSelected = true }
                }
            };
            var stateSubject = new BehaviorSubject<AppState>(stateWithPlugins);
            _stateServiceMock.Setup(s => s.StateChanged).Returns(stateSubject);
            _stateServiceMock.Setup(s => s.CurrentState).Returns(stateWithPlugins);

            // Setup config service to avoid NullReferenceException during InitializeAsync
            _configServiceMock.Setup(x => x.LoadUserConfigAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UserConfiguration { LoadOrder = new(), XEdit = new(), ModOrganizer = new(), Settings = new() });

            var vm = new MainWindowViewModel(
                _configServiceMock.Object,
                _stateServiceMock.Object,
                _orchestratorMock.Object,
                _loggerMock.Object,
                _fileDialogMock.Object,
                _messageDialogMock.Object,
                _pluginServiceMock.Object,
                _pluginLoadingServiceMock.Object);

            // Wait a bit for InitializeAsync to complete
            await Task.Delay(50);

            // Set valid paths to enable command (use temp file for xEdit path)
            vm.LoadOrderPath = "plugins.txt";
            vm.XEditPath = tempFile;

            // Configure orchestrator to throw exception (use the 3-param overload)
            _orchestratorMock.Setup(x => x.StartCleaningAsync(It.IsAny<TimeoutRetryCallback>(), It.IsAny<BackupFailureCallback>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Configuration is invalid"));

            // Act
            await vm.StartCleaningCommand.Execute();

            // Assert - inline validation errors shown instead of modal dialog
            vm.StatusText.Should().Contain("error", "error message should be displayed in status");
            vm.HasValidationErrors.Should().BeTrue("validation errors should be visible");
            vm.ValidationErrors.Should().HaveCount(1, "one configuration error should be shown");
            vm.ValidationErrors[0].Title.Should().Be("Configuration error");

            // Verify that NO modal error dialog was shown (inline validation replaces modals)
            _messageDialogMock.Verify(
                m => m.ShowErrorAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()),
                Times.Never,
                "Modal error dialog should NOT be shown for InvalidOperationException");

            // Verify that the StartCleaningAsync error was logged
            _loggerMock.Verify(
                l => l.Error(It.IsAny<Exception>(), It.Is<string>(s => s.Contains("validation") || s.Contains("failed"))),
                Times.AtLeastOnce,
                "StartCleaningAsync exception should be logged");
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
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
            _messageDialogMock.Object,
            _pluginServiceMock.Object,
            _pluginLoadingServiceMock.Object);

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
    /// Verifies that ConfigureLoadOrderCommand handles plugin parsing errors gracefully
    /// and shows an error dialog.
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

        // Create temp file to satisfy File.Exists check
        var tempFile = Path.GetTempFileName();
        try
        {
            var vm = new MainWindowViewModel(
                _configServiceMock.Object,
                _stateServiceMock.Object,
                _orchestratorMock.Object,
                _loggerMock.Object,
                _fileDialogMock.Object,
                _messageDialogMock.Object,
                _pluginServiceMock.Object,
                _pluginLoadingServiceMock.Object);

            // Wait for InitializeAsync to complete
            await Task.Delay(50);

            _fileDialogMock.Setup(x => x.OpenFileDialogAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>()))
                .ReturnsAsync(tempFile);

            // Plugin service throws exception for the corrupted file path
            _pluginServiceMock.Setup(x => x.GetPluginsFromLoadOrderAsync(tempFile, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Failed to parse load order"));

            // Act
            await vm.ConfigureLoadOrderCommand.Execute();

            // Assert
            vm.StatusText.Should().Contain("Error", "error should be reflected in status");

            // Verify error dialog was shown
            _messageDialogMock.Verify(
                m => m.ShowErrorAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()),
                Times.Once,
                "Error dialog should be shown for parsing error");

            // Verify that the ConfigureLoadOrder error was logged
            _loggerMock.Verify(
                l => l.Error(It.IsAny<Exception>(), It.Is<string>(s => s.Contains("load order"))),
                Times.AtLeastOnce,
                "parsing error should be logged");
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
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
            _messageDialogMock.Object,
            _pluginServiceMock.Object,
            _pluginLoadingServiceMock.Object);

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
            _messageDialogMock.Object,
            _pluginServiceMock.Object,
            _pluginLoadingServiceMock.Object);

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
    /// Verifies that StopCleaningCommand calls orchestrator's StopCleaningAsync.
    /// </summary>
    [Fact]
    public async Task StopCleaningCommand_ShouldCallOrchestratorStop()
    {
        // Arrange
        var stateSubject = new BehaviorSubject<AppState>(new AppState { IsCleaning = true });
        _stateServiceMock.Setup(s => s.StateChanged).Returns(stateSubject);
        _stateServiceMock.Setup(s => s.CurrentState).Returns(new AppState { IsCleaning = true });

        _orchestratorMock.Setup(x => x.StopCleaningAsync()).Returns(Task.CompletedTask);
        _orchestratorMock.Setup(x => x.LastTerminationResult).Returns((TerminationResult?)null);

        var vm = new MainWindowViewModel(
            _configServiceMock.Object,
            _stateServiceMock.Object,
            _orchestratorMock.Object,
            _loggerMock.Object,
            _fileDialogMock.Object,
            _messageDialogMock.Object,
            _pluginServiceMock.Object,
            _pluginLoadingServiceMock.Object);

        // Act
        await vm.StopCleaningCommand.Execute();

        // Assert
        _orchestratorMock.Verify(x => x.StopCleaningAsync(), Times.Once);
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
            _messageDialogMock.Object,
            _pluginServiceMock.Object,
            _pluginLoadingServiceMock.Object);

        // Act
        var newState = new AppState
        {
            LoadOrderPath = "newpath/plugins.txt",
            XEditExecutablePath = "newpath/xedit.exe",
            Mo2ExecutablePath = "newpath/mo2.exe",
            Mo2ModeEnabled = true,
            PartialFormsEnabled = true
        };
        stateSubject.OnNext(newState);

        // Assert
        vm.LoadOrderPath.Should().Be("newpath/plugins.txt");
        vm.XEditPath.Should().Be("newpath/xedit.exe");
        vm.Mo2Path.Should().Be("newpath/mo2.exe");
        vm.Mo2ModeEnabled.Should().BeTrue();
        vm.PartialFormsEnabled.Should().BeTrue();
    }

    #endregion

    #region Game Selection Tests

    /// <summary>
    /// Verifies that AvailableGames is populated from IPluginLoadingService.
    /// </summary>
    [Fact]
    public void AvailableGames_ShouldBePopulatedFromPluginLoadingService()
    {
        // Arrange
        var expectedGames = new List<GameType> { GameType.SkyrimSe, GameType.Fallout4, GameType.SkyrimLe };
        _pluginLoadingServiceMock.Setup(x => x.GetAvailableGames())
            .Returns(expectedGames);

        var stateSubject = new BehaviorSubject<AppState>(new AppState());
        _stateServiceMock.Setup(s => s.StateChanged).Returns(stateSubject);
        _stateServiceMock.Setup(s => s.CurrentState).Returns(new AppState());

        // Act
        var vm = new MainWindowViewModel(
            _configServiceMock.Object,
            _stateServiceMock.Object,
            _orchestratorMock.Object,
            _loggerMock.Object,
            _fileDialogMock.Object,
            _messageDialogMock.Object,
            _pluginServiceMock.Object,
            _pluginLoadingServiceMock.Object);

        // Assert
        vm.AvailableGames.Should().BeEquivalentTo(expectedGames);
    }

    /// <summary>
    /// Verifies that IsMutagenSupported reflects the selected game correctly.
    /// </summary>
    [Fact]
    public void IsMutagenSupported_ShouldReflectSelectedGame()
    {
        // Arrange
        _pluginLoadingServiceMock.Setup(x => x.IsGameSupportedByMutagen(GameType.SkyrimSe))
            .Returns(true);
        _pluginLoadingServiceMock.Setup(x => x.IsGameSupportedByMutagen(GameType.Fallout3))
            .Returns(false);

        var stateSubject = new BehaviorSubject<AppState>(new AppState());
        _stateServiceMock.Setup(s => s.StateChanged).Returns(stateSubject);
        _stateServiceMock.Setup(s => s.CurrentState).Returns(new AppState());

        var vm = new MainWindowViewModel(
            _configServiceMock.Object,
            _stateServiceMock.Object,
            _orchestratorMock.Object,
            _loggerMock.Object,
            _fileDialogMock.Object,
            _messageDialogMock.Object,
            _pluginServiceMock.Object,
            _pluginLoadingServiceMock.Object);

        // Act & Assert - Default is Unknown (not supported)
        vm.IsMutagenSupported.Should().BeFalse();

        // Note: Due to Skip(1) in the subscription, the first change is consumed.
        // Testing the computed property directly after setting SelectedGame:
        vm.SelectedGame = GameType.SkyrimSe;
        vm.IsMutagenSupported.Should().BeTrue();

        vm.SelectedGame = GameType.Fallout3;
        vm.IsMutagenSupported.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that changing SelectedGame persists to configuration.
    /// </summary>
    [Fact]
    public async Task SelectedGame_ShouldPersistToConfiguration_WhenChanged()
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
            _messageDialogMock.Object,
            _pluginServiceMock.Object,
            _pluginLoadingServiceMock.Object);

        // Act
        vm.SelectedGame = GameType.SkyrimSe;

        // Allow async subscription to execute
        await Task.Delay(100);

        // Assert
        _configServiceMock.Verify(x => x.SetSelectedGameAsync(GameType.SkyrimSe, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that changing SelectedGame refreshes plugins via IPluginLoadingService.
    /// </summary>
    [Fact]
    public async Task SelectedGame_ShouldRefreshPlugins_WhenChangedToMutagenSupportedGame()
    {
        // Arrange
        var expectedPlugins = new List<PluginInfo>
        {
            new() { FileName = "Plugin1.esp", FullPath = "Data/Plugin1.esp" },
            new() { FileName = "Plugin2.esp", FullPath = "Data/Plugin2.esp" }
        };

        _pluginLoadingServiceMock.Setup(x => x.IsGameSupportedByMutagen(GameType.SkyrimSe))
            .Returns(true);
        _pluginLoadingServiceMock.Setup(x => x.GetPluginsAsync(GameType.SkyrimSe, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedPlugins);

        // Setup skip list (required for ApplySkipListStatus)
        _configServiceMock.Setup(x => x.GetSkipListAsync(It.IsAny<GameType>()))
            .ReturnsAsync(new List<string>());

        var stateSubject = new BehaviorSubject<AppState>(new AppState());
        _stateServiceMock.Setup(s => s.StateChanged).Returns(stateSubject);
        _stateServiceMock.Setup(s => s.CurrentState).Returns(new AppState());

        var vm = new MainWindowViewModel(
            _configServiceMock.Object,
            _stateServiceMock.Object,
            _orchestratorMock.Object,
            _loggerMock.Object,
            _fileDialogMock.Object,
            _messageDialogMock.Object,
            _pluginServiceMock.Object,
            _pluginLoadingServiceMock.Object);

        // Act
        vm.SelectedGame = GameType.SkyrimSe;

        // Allow async subscription to execute
        await Task.Delay(100);

        // Assert
        _pluginLoadingServiceMock.Verify(x => x.GetPluginsAsync(GameType.SkyrimSe, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _stateServiceMock.Verify(x => x.SetPluginsToClean(It.Is<List<PluginInfo>>(list =>
            list.Count == 2 &&
            list.Any(p => p.FileName == "Plugin1.esp") &&
            list.Any(p => p.FileName == "Plugin2.esp"))), Times.Once);
    }

    #endregion

    #region Cleanup Tests

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
            _messageDialogMock.Object,
            _pluginServiceMock.Object,
            _pluginLoadingServiceMock.Object);

        // Act & Assert
        FluentActions.Invoking(vm.Dispose)
            .Should().NotThrow("disposal should complete without exception");
    }

    #endregion
}
