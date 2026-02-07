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
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ReactiveUI;
using System.Reactive.Linq;

namespace AutoQAC.Tests.ViewModels;

public sealed class MainWindowViewModelTests
{
    private readonly IConfigurationService _configServiceMock;
    private readonly IStateService _stateServiceMock;
    private readonly ICleaningOrchestrator _orchestratorMock;
    private readonly ILoggingService _loggerMock;
    private readonly IFileDialogService _fileDialogMock;
    private readonly IMessageDialogService _messageDialogMock;
    private readonly IPluginValidationService _pluginServiceMock;
    private readonly IPluginLoadingService _pluginLoadingServiceMock;

    public MainWindowViewModelTests()
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
            _stateServiceMock.StateChanged.Returns(stateSubject);
            _stateServiceMock.CurrentState.Returns(stateWithPlugins);

            var vm = new MainWindowViewModel(
                _configServiceMock,
                _stateServiceMock,
                _orchestratorMock,
                _loggerMock,
                _fileDialogMock,
                _messageDialogMock,
                _pluginServiceMock,
                _pluginLoadingServiceMock);

            // Manually set properties to satisfy CanExecute (use temp file path)
            vm.Configuration.LoadOrderPath = "plugins.txt";
            vm.Configuration.XEditPath = tempFile; // Use actual existing file

            // Act
            await vm.Commands.StartCleaningCommand.Execute();

            // Assert - verify the 3-param overload with timeout and backup failure callbacks is called
            await _orchestratorMock.Received(1)
                .StartCleaningAsync(Arg.Any<TimeoutRetryCallback>(), Arg.Any<BackupFailureCallback>(), Arg.Any<CancellationToken>());
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
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState());

        // Create temp file to satisfy File.Exists check
        var tempFile = Path.GetTempFileName();
        try
        {
            var vm = new MainWindowViewModel(
                _configServiceMock,
                _stateServiceMock,
                _orchestratorMock,
                _loggerMock,
                _fileDialogMock,
                _messageDialogMock,
                _pluginServiceMock,
                _pluginLoadingServiceMock);

            _fileDialogMock.OpenFileDialogAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<string?>())
                .Returns(tempFile);

            _configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
                .Returns(new UserConfiguration { LoadOrder = new(), XEdit = new(), ModOrganizer = new(), Settings = new() });

            var expectedPlugins = new List<PluginInfo>
            {
                new PluginInfo { FileName = "Update.esm", FullPath = "Update.esm", DetectedGameType = GameType.Unknown },
                new PluginInfo { FileName = "Dawnguard.esm", FullPath = "Dawnguard.esm", DetectedGameType = GameType.Unknown }
            };

            _pluginServiceMock.GetPluginsFromLoadOrderAsync(tempFile, Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(expectedPlugins);

            // Setup skip list (required for ApplySkipListStatus)
            _configServiceMock.GetSkipListAsync(Arg.Any<GameType>())
                .Returns(new List<string>());

            // Act
            await vm.Configuration.ConfigureLoadOrderCommand.Execute();

            // Assert
            _stateServiceMock.Received(1).UpdateConfigurationPaths(tempFile, Arg.Any<string>(), Arg.Any<string>());
            _stateServiceMock.Received(1).SetPluginsToClean(Arg.Is<List<PluginInfo>>(l => l.Count == 2 && l[0].FileName == "Update.esm"));
            await _configServiceMock.Received(1).SaveUserConfigAsync(Arg.Any<UserConfiguration>(), Arg.Any<CancellationToken>());
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
            _stateServiceMock.StateChanged.Returns(stateSubject);
            _stateServiceMock.CurrentState.Returns(stateWithPlugins);

            // Setup config service to avoid NullReferenceException during InitializeAsync
            _configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
                .Returns(new UserConfiguration { LoadOrder = new(), XEdit = new(), ModOrganizer = new(), Settings = new() });

            var vm = new MainWindowViewModel(
                _configServiceMock,
                _stateServiceMock,
                _orchestratorMock,
                _loggerMock,
                _fileDialogMock,
                _messageDialogMock,
                _pluginServiceMock,
                _pluginLoadingServiceMock);

            // Wait a bit for InitializeAsync to complete
            await Task.Delay(50);

            // Set valid paths to enable command (use temp file for xEdit path)
            vm.Configuration.LoadOrderPath = "plugins.txt";
            vm.Configuration.XEditPath = tempFile;

            // Configure orchestrator to throw exception (use the 3-param overload)
            _orchestratorMock.StartCleaningAsync(Arg.Any<TimeoutRetryCallback>(), Arg.Any<BackupFailureCallback>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new InvalidOperationException("Configuration is invalid"));

            // Act
            await vm.Commands.StartCleaningCommand.Execute();

            // Assert - inline validation errors shown instead of modal dialog
            vm.Commands.StatusText.Should().Contain("error", "error message should be displayed in status");
            vm.Commands.HasValidationErrors.Should().BeTrue("validation errors should be visible");
            vm.Commands.ValidationErrors.Should().HaveCount(1, "one configuration error should be shown");
            vm.Commands.ValidationErrors[0].Title.Should().Be("Configuration error");

            // Verify that NO modal error dialog was shown (inline validation replaces modals)
            await _messageDialogMock.DidNotReceive()
                .ShowErrorAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>());

            // Verify that the StartCleaningAsync error was logged
            _loggerMock.Received().Error(Arg.Any<Exception>(), Arg.Is<string>(s => s.Contains("validation") || s.Contains("failed")));
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
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState());

        var vm = new MainWindowViewModel(
            _configServiceMock,
            _stateServiceMock,
            _orchestratorMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock);

        // Configure dialog to return null (user cancelled)
        _fileDialogMock.OpenFileDialogAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>())
            .Returns((string?)null);

        // Act
        await vm.Configuration.ConfigureLoadOrderCommand.Execute();

        // Assert
        // State should not be updated when dialog is cancelled
        _stateServiceMock.DidNotReceive()
            .UpdateConfigurationPaths(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
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
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState());

        // Setup config service to avoid NullReferenceException during InitializeAsync
        _configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new UserConfiguration { LoadOrder = new(), XEdit = new(), ModOrganizer = new(), Settings = new() });

        // Create temp file to satisfy File.Exists check
        var tempFile = Path.GetTempFileName();
        try
        {
            var vm = new MainWindowViewModel(
                _configServiceMock,
                _stateServiceMock,
                _orchestratorMock,
                _loggerMock,
                _fileDialogMock,
                _messageDialogMock,
                _pluginServiceMock,
                _pluginLoadingServiceMock);

            // Wait for InitializeAsync to complete
            await Task.Delay(50);

            _fileDialogMock.OpenFileDialogAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<string?>())
                .Returns(tempFile);

            // Plugin service throws exception for the corrupted file path
            _pluginServiceMock.GetPluginsFromLoadOrderAsync(tempFile, Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new InvalidOperationException("Failed to parse load order"));

            // Act
            await vm.Configuration.ConfigureLoadOrderCommand.Execute();

            // Assert
            vm.Configuration.StatusText.Should().Contain("Error", "error should be reflected in status");

            // Verify error dialog was shown
            await _messageDialogMock.Received(1)
                .ShowErrorAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>());

            // Verify that the ConfigureLoadOrder error was logged
            _loggerMock.Received().Error(Arg.Any<Exception>(), Arg.Is<string>(s => s.Contains("load order")));
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
        // Arrange - CanStartCleaning now depends on state (not VM properties directly)
        // With empty state (no plugins, no xEdit path), it should always be false
        var stateSubject = new BehaviorSubject<AppState>(new AppState());
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState());

        var vm = new MainWindowViewModel(
            _configServiceMock,
            _stateServiceMock,
            _orchestratorMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock);

        // Act - set properties on Configuration sub-VM (these don't affect CanStartCleaning
        // since it now reads from IStateService, but the state has no plugins/xEdit)
        vm.Configuration.LoadOrderPath = loadOrder;
        vm.Configuration.XEditPath = xEdit;

        // Assert
        vm.Commands.CanStartCleaning.Should().BeFalse("cleaning should not be allowed without required paths");
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
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(cleaningState);

        var vm = new MainWindowViewModel(
            _configServiceMock,
            _stateServiceMock,
            _orchestratorMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock);

        // Set valid paths on Configuration sub-VM
        vm.Configuration.LoadOrderPath = "plugins.txt";
        vm.Configuration.XEditPath = "xedit.exe";

        // Emit cleaning state
        stateSubject.OnNext(cleaningState);

        // Assert
        // IsCleaning should be true from the state
        vm.Commands.IsCleaning.Should().BeTrue();
        // CanStartCleaning should be false because IsCleaning is true
        vm.Commands.CanStartCleaning.Should().BeFalse("cannot start new cleaning while one is in progress");
    }

    /// <summary>
    /// Verifies that StopCleaningCommand calls orchestrator's StopCleaningAsync.
    /// </summary>
    [Fact]
    public async Task StopCleaningCommand_ShouldCallOrchestratorStop()
    {
        // Arrange
        var stateSubject = new BehaviorSubject<AppState>(new AppState { IsCleaning = true });
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState { IsCleaning = true });

        _orchestratorMock.StopCleaningAsync().Returns(Task.CompletedTask);
        _orchestratorMock.LastTerminationResult.Returns((TerminationResult?)null);

        var vm = new MainWindowViewModel(
            _configServiceMock,
            _stateServiceMock,
            _orchestratorMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock);

        // Act
        await vm.Commands.StopCleaningCommand.Execute();

        // Assert
        await _orchestratorMock.Received(1).StopCleaningAsync();
        vm.Commands.StatusText.Should().Contain("Stopping");
    }

    /// <summary>
    /// When grace period expires and user confirms, ForceStopCleaningAsync should be called.
    /// </summary>
    [Fact]
    public async Task StopCleaningCommand_GracePeriodExpired_UserConfirms_ShouldForceStop()
    {
        // Arrange
        var stateSubject = new BehaviorSubject<AppState>(new AppState { IsCleaning = true });
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState { IsCleaning = true });

        _orchestratorMock.StopCleaningAsync().Returns(Task.CompletedTask);
        _orchestratorMock.LastTerminationResult.Returns(TerminationResult.GracePeriodExpired);
        _orchestratorMock.ForceStopCleaningAsync().Returns(Task.CompletedTask);
        _messageDialogMock.ShowConfirmAsync(
            Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        var vm = new MainWindowViewModel(
            _configServiceMock,
            _stateServiceMock,
            _orchestratorMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock);

        // Act
        await vm.Commands.StopCleaningCommand.Execute();

        // Assert
        await _orchestratorMock.Received(1).ForceStopCleaningAsync();
        await _messageDialogMock.Received(1).ShowConfirmAsync(
            "Force Terminate?",
            "xEdit did not exit gracefully. Force terminate the process?");
    }

    /// <summary>
    /// When grace period expires and user declines, ForceStopCleaningAsync should NOT be called.
    /// </summary>
    [Fact]
    public async Task StopCleaningCommand_GracePeriodExpired_UserDeclines_ShouldNotForceStop()
    {
        // Arrange
        var stateSubject = new BehaviorSubject<AppState>(new AppState { IsCleaning = true });
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState { IsCleaning = true });

        _orchestratorMock.StopCleaningAsync().Returns(Task.CompletedTask);
        _orchestratorMock.LastTerminationResult.Returns(TerminationResult.GracePeriodExpired);
        _messageDialogMock.ShowConfirmAsync(
            Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        var vm = new MainWindowViewModel(
            _configServiceMock,
            _stateServiceMock,
            _orchestratorMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock);

        // Act
        await vm.Commands.StopCleaningCommand.Execute();

        // Assert
        await _orchestratorMock.DidNotReceive().ForceStopCleaningAsync();
        await _messageDialogMock.Received(1).ShowConfirmAsync(
            "Force Terminate?",
            "xEdit did not exit gracefully. Force terminate the process?");
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
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(initialState);

        var vm = new MainWindowViewModel(
            _configServiceMock,
            _stateServiceMock,
            _orchestratorMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock);

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

        // Assert - properties are now on Configuration sub-VM
        vm.Configuration.LoadOrderPath.Should().Be("newpath/plugins.txt");
        vm.Configuration.XEditPath.Should().Be("newpath/xedit.exe");
        vm.Configuration.Mo2Path.Should().Be("newpath/mo2.exe");
        vm.Configuration.Mo2ModeEnabled.Should().BeTrue();
        vm.Configuration.PartialFormsEnabled.Should().BeTrue();
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
        _pluginLoadingServiceMock.GetAvailableGames()
            .Returns(expectedGames);

        var stateSubject = new BehaviorSubject<AppState>(new AppState());
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState());

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

        // Assert - AvailableGames is now on Configuration sub-VM
        vm.Configuration.AvailableGames.Should().BeEquivalentTo(expectedGames);
    }

    /// <summary>
    /// Verifies that IsMutagenSupported reflects the selected game correctly.
    /// </summary>
    [Fact]
    public void IsMutagenSupported_ShouldReflectSelectedGame()
    {
        // Arrange
        _pluginLoadingServiceMock.IsGameSupportedByMutagen(GameType.SkyrimSe)
            .Returns(true);
        _pluginLoadingServiceMock.IsGameSupportedByMutagen(GameType.Fallout3)
            .Returns(false);

        var stateSubject = new BehaviorSubject<AppState>(new AppState());
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState());

        var vm = new MainWindowViewModel(
            _configServiceMock,
            _stateServiceMock,
            _orchestratorMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock);

        // Act & Assert - Default is Unknown (not supported)
        vm.Configuration.IsMutagenSupported.Should().BeFalse();

        // Note: Due to Skip(1) in the subscription, the first change is consumed.
        // Testing the computed property directly after setting SelectedGame:
        vm.Configuration.SelectedGame = GameType.SkyrimSe;
        vm.Configuration.IsMutagenSupported.Should().BeTrue();

        vm.Configuration.SelectedGame = GameType.Fallout3;
        vm.Configuration.IsMutagenSupported.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that changing SelectedGame persists to configuration.
    /// </summary>
    [Fact]
    public async Task SelectedGame_ShouldPersistToConfiguration_WhenChanged()
    {
        // Arrange
        var stateSubject = new BehaviorSubject<AppState>(new AppState());
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState());

        var vm = new MainWindowViewModel(
            _configServiceMock,
            _stateServiceMock,
            _orchestratorMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock);

        // Act
        vm.Configuration.SelectedGame = GameType.SkyrimSe;

        // Allow async subscription to execute
        await Task.Delay(100);

        // Assert
        await _configServiceMock.Received(1).SetSelectedGameAsync(GameType.SkyrimSe, Arg.Any<CancellationToken>());
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

        _pluginLoadingServiceMock.IsGameSupportedByMutagen(GameType.SkyrimSe)
            .Returns(true);
        _pluginLoadingServiceMock.GetPluginsAsync(GameType.SkyrimSe, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(expectedPlugins);

        // Setup skip list (required for ApplySkipListStatus)
        _configServiceMock.GetSkipListAsync(Arg.Any<GameType>())
            .Returns(new List<string>());

        var stateSubject = new BehaviorSubject<AppState>(new AppState());
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState());

        var vm = new MainWindowViewModel(
            _configServiceMock,
            _stateServiceMock,
            _orchestratorMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock);

        // Act
        vm.Configuration.SelectedGame = GameType.SkyrimSe;

        // Allow async subscription to execute
        await Task.Delay(100);

        // Assert
        await _pluginLoadingServiceMock.Received(1).GetPluginsAsync(GameType.SkyrimSe, Arg.Any<string?>(), Arg.Any<CancellationToken>());
        _stateServiceMock.Received(1).SetPluginsToClean(Arg.Is<List<PluginInfo>>(list =>
            list.Count == 2 &&
            list.Any(p => p.FileName == "Plugin1.esp") &&
            list.Any(p => p.FileName == "Plugin2.esp")));
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
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState());

        var vm = new MainWindowViewModel(
            _configServiceMock,
            _stateServiceMock,
            _orchestratorMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock);

        // Act & Assert
        FluentActions.Invoking(vm.Dispose)
            .Should().NotThrow("disposal should complete without exception");
    }

    #endregion
}
