using System.Reactive.Linq;
using System.Reactive.Subjects;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using AutoQAC.Tests.TestInfrastructure;
using AutoQAC.ViewModels;
using AutoQAC.ViewModels.MainWindow;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AutoQAC.Tests.ViewModels;

public sealed class MainWindowViewModelTests
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

    public MainWindowViewModelTests()
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
    }

    /// <summary>
    /// Tests that need <c>SelectedGame</c> assignment to trigger the auto-save / refresh
    /// pipeline must mark <see cref="ConfigurationViewModel"/> as initialized. The
    /// production code gates <c>OnSelectedGameChanged</c>'s side effects behind
    /// <c>_initialized = true</c>, which is set inside <c>InitializeAsync</c> after
    /// <see cref="IConfigurationService.LoadUserConfigAsync"/> succeeds. Without a real
    /// config from the mock, <c>InitializeAsync</c> NREs silently and the gate stays
    /// closed, so signal-based tests time out.
    /// </summary>
    private void EnableSelectedGameSideEffects()
    {
        _configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new UserConfiguration { LoadOrder = new(), XEdit = new(), ModOrganizer = new(), Settings = new() });
        _configServiceMock.GetSelectedGameAsync(Arg.Any<CancellationToken>())
            .Returns(GameType.Unknown);
    }

    private static TaskCompletionSource<bool> CreateSignal()
    {
        return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static Task WaitForSignalAsync(TaskCompletionSource<bool> signal)
    {
        return WaitForSignalAsync(signal.Task, "expected asynchronous test signal to be observed");
    }

    private static async Task WaitForSignalAsync(Task signalTask, string because)
    {
        var completedTask = await Task.WhenAny(signalTask, Task.Delay(TimeSpan.FromSeconds(2)));
        completedTask.Should().Be(signalTask, because);
        await signalTask;
    }

    [Fact]
    public async Task StartCleaningCommand_ShouldCallSession_WhenCanStart()
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
                    new() { FileName = "Test.esp", FullPath = "Test.esp" }
                }
            };
            var stateSubject = new BehaviorSubject<AppState>(stateWithPlugins);
            _stateServiceMock.StateChanged.Returns(stateSubject);
            _stateServiceMock.CurrentState.Returns(stateWithPlugins);

            var vm = new MainWindowViewModel(
                _configServiceMock,
                _stateServiceMock,
                _cleaningSessionMock,
                _loggerMock,
                _fileDialogMock,
                _messageDialogMock,
                _pluginServiceMock,
                _pluginLoadingServiceMock,
                _uiDispatcher);
            using var _ = vm.ShowProgressInteraction.RegisterHandler(_ => Task.FromResult(default(AutoQAC.Services.UI.Interactions.Unit)));

            // Manually set properties to satisfy CanExecute (use temp file path)
            vm.Configuration.LoadOrderPath = "plugins.txt";
            vm.Configuration.XEditPath = tempFile; // Use actual existing file

            // Act
            await vm.Commands.StartCleaningCommand.ExecuteAsync(null);

            await _cleaningSessionMock.Received(1).StartAsync(Arg.Any<CancellationToken>());
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task StartCleaningCommand_WhenProgressInteractionFails_ShouldHandleErrorBeforeStartingCleaning()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var stateWithPlugins = new AppState
            {
                XEditExecutablePath = tempFile,
                PluginsToClean = new List<PluginInfo>
                {
                    new() { FileName = "Test.esp", FullPath = "Test.esp" }
                }
            };
            var stateSubject = new BehaviorSubject<AppState>(stateWithPlugins);
            _stateServiceMock.StateChanged.Returns(stateSubject);
            _stateServiceMock.CurrentState.Returns(stateWithPlugins);
            _messageDialogMock.ShowErrorAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>())
                .Returns(Task.CompletedTask);

            var vm = new MainWindowViewModel(
                _configServiceMock,
                _stateServiceMock,
                _cleaningSessionMock,
                _loggerMock,
                _fileDialogMock,
                _messageDialogMock,
                _pluginServiceMock,
                _pluginLoadingServiceMock,
                _uiDispatcher);
            using var _ = vm.ShowProgressInteraction.RegisterHandler(_ => throw new ApplicationException("Progress window failed"));

            await vm.Commands.StartCleaningCommand.ExecuteAsync(null);

            await _cleaningSessionMock.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
            await _messageDialogMock.Received(1).ShowErrorAsync(
                "Cleaning Failed",
                Arg.Any<string>(),
                Arg.Any<string?>());
            _loggerMock.Received().Error(Arg.Any<ApplicationException>(), "StartAsync failed");
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task PreviewCommand_WhenPreviewInteractionFails_ShouldHandleError()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var stateWithPlugins = new AppState
            {
                XEditExecutablePath = tempFile,
                PluginsToClean = new List<PluginInfo>
                {
                    new() { FileName = "Test.esp", FullPath = "Test.esp" }
                }
            };
            var stateSubject = new BehaviorSubject<AppState>(stateWithPlugins);
            _stateServiceMock.StateChanged.Returns(stateSubject);
            _stateServiceMock.CurrentState.Returns(stateWithPlugins);
            _cleaningSessionMock.PreviewAsync(Arg.Any<CancellationToken>())
                .Returns(new List<DryRunResult>());
            _messageDialogMock.ShowErrorAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>())
                .Returns(Task.CompletedTask);

            var vm = new MainWindowViewModel(
                _configServiceMock,
                _stateServiceMock,
                _cleaningSessionMock,
                _loggerMock,
                _fileDialogMock,
                _messageDialogMock,
                _pluginServiceMock,
                _pluginLoadingServiceMock,
                _uiDispatcher);
            using var _ = vm.ShowPreviewInteraction.RegisterHandler(_ => throw new ApplicationException("Preview window failed"));

            await vm.Commands.PreviewCommand.ExecuteAsync(null);

            await _messageDialogMock.Received(1).ShowErrorAsync(
                "Preview Failed",
                Arg.Any<string>(),
                Arg.Any<string?>());
            _loggerMock.Received().Error(Arg.Any<ApplicationException>(), "RunPreviewAsync failed");
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
                _cleaningSessionMock,
                _loggerMock,
                _fileDialogMock,
                _messageDialogMock,
                _pluginServiceMock,
                _pluginLoadingServiceMock,
                _uiDispatcher);

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

            _pluginLoadingServiceMock.GetPluginsFromFileAsync(tempFile, Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(expectedPlugins);

            // Setup skip list (required for ApplySkipListStatus)
            _configServiceMock.GetSkipListAsync(
                    Arg.Any<GameType>(),
                    Arg.Any<GameVariant>(),
                    Arg.Any<CancellationToken>())
                .Returns(new List<string>());
            vm.Configuration.SelectedGame = GameType.FalloutNewVegas;

            // Act
            await vm.Configuration.ConfigureLoadOrderCommand.ExecuteAsync(null);

            // Assert
            _stateServiceMock.Received(1).UpdateConfigurationPaths(
                tempFile,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>());
            _stateServiceMock.Received(1).SetPluginsToClean(Arg.Is<List<PluginInfo>>(l => l.Count == 2 && l[0].FileName == "Update.esm"));
            await _configServiceMock.Received().SetGameLoadOrderOverrideAsync(GameType.FalloutNewVegas, tempFile, Arg.Any<CancellationToken>());
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
            var initializationApplied = CreateSignal();
            var stateWithPlugins = new AppState
            {
                XEditExecutablePath = tempFile,
                PluginsToClean = new List<PluginInfo>
                {
                    new() { FileName = "Test.esp", FullPath = "Test.esp" }
                }
            };
            var stateSubject = new BehaviorSubject<AppState>(stateWithPlugins);
            _stateServiceMock.StateChanged.Returns(stateSubject);
            _stateServiceMock.CurrentState.Returns(stateWithPlugins);

            // Setup config service to avoid NullReferenceException during InitializeAsync
            _configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
                .Returns(new UserConfiguration { LoadOrder = new(), XEdit = new(), ModOrganizer = new(), Settings = new() });
            _stateServiceMock.When(x => x.UpdateState(Arg.Any<Func<AppState, AppState>>()))
                .Do(_ => initializationApplied.TrySetResult(true));

            var vm = new MainWindowViewModel(
                _configServiceMock,
                _stateServiceMock,
                _cleaningSessionMock,
                _loggerMock,
                _fileDialogMock,
                _messageDialogMock,
                _pluginServiceMock,
                _pluginLoadingServiceMock,
                _uiDispatcher);
            using var _ = vm.ShowProgressInteraction.RegisterHandler(_ => Task.FromResult(default(AutoQAC.Services.UI.Interactions.Unit)));

            await WaitForSignalAsync(initializationApplied);

            // Set valid paths to enable command (use temp file for xEdit path)
            vm.Configuration.LoadOrderPath = "plugins.txt";
            vm.Configuration.XEditPath = tempFile;

            _cleaningSessionMock.StartAsync(Arg.Any<CancellationToken>())
                .ThrowsAsync(new InvalidOperationException("Configuration is invalid"));

            // Act
            await vm.Commands.StartCleaningCommand.ExecuteAsync(null);

            // Assert - inline validation errors shown instead of modal dialog
            vm.Commands.StatusText.Should().Contain("error", "error message should be displayed in status");
            vm.Commands.HasValidationErrors.Should().BeTrue("validation errors should be visible");
            vm.Commands.ValidationErrors.Should().HaveCount(1, "one configuration error should be shown");
            vm.Commands.ValidationErrors[0].Title.Should().Be("Configuration error");

            // Verify that NO modal error dialog was shown (inline validation replaces modals)
            await _messageDialogMock.DidNotReceive()
                .ShowErrorAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>());

            // Verify that the StartAsync error was logged
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
            _cleaningSessionMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock,
            _uiDispatcher);

        // Configure dialog to return null (user cancelled)
        _fileDialogMock.OpenFileDialogAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>())
            .Returns((string?)null);

        // Act
        await vm.Configuration.ConfigureLoadOrderCommand.ExecuteAsync(null);

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
        var initializationApplied = CreateSignal();
        var stateSubject = new BehaviorSubject<AppState>(new AppState());
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState());
        _stateServiceMock.When(x => x.UpdateState(Arg.Any<Func<AppState, AppState>>()))
            .Do(_ => initializationApplied.TrySetResult(true));

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
                _cleaningSessionMock,
                _loggerMock,
                _fileDialogMock,
                _messageDialogMock,
                _pluginServiceMock,
                _pluginLoadingServiceMock,
                _uiDispatcher);

            await WaitForSignalAsync(initializationApplied);

            _fileDialogMock.OpenFileDialogAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<string?>())
                .Returns(tempFile);

            // Plugin service throws exception for the corrupted file path
            _pluginLoadingServiceMock.GetPluginsFromFileAsync(tempFile, Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new InvalidOperationException("Failed to parse load order"));
            vm.Configuration.SelectedGame = GameType.FalloutNewVegas;

            // Act
            await vm.Configuration.ConfigureLoadOrderCommand.ExecuteAsync(null);

            // Assert
            vm.Configuration.StatusText.Should().Contain("failed", "error should be reflected in status");

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
            _cleaningSessionMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock,
            _uiDispatcher);

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
            _cleaningSessionMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock,
            _uiDispatcher);

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
    /// Verifies that StopCleaningCommand sends a stop control request to the active session.
    /// </summary>
    [Fact]
    public async Task StopCleaningCommand_ShouldRequestSessionStop()
    {
        // Arrange
        var stateSubject = new BehaviorSubject<AppState>(new AppState { IsCleaning = true });
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState { IsCleaning = true });

        _cleaningSessionMock.ControlAsync(CleaningSessionControl.RequestStop, Arg.Any<CancellationToken>())
            .Returns(new CleaningSessionControlResult(
                CleaningSessionControl.RequestStop,
                CleaningSessionControlStatus.StopRequested));

        var vm = new MainWindowViewModel(
            _configServiceMock,
            _stateServiceMock,
            _cleaningSessionMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock,
            _uiDispatcher);

        // Act
        await vm.Commands.StopCleaningCommand.ExecuteAsync(null);

        // Assert
        await _cleaningSessionMock.Received(1)
            .ControlAsync(CleaningSessionControl.RequestStop, Arg.Any<CancellationToken>());
        vm.Commands.StatusText.Should().Contain("Stopping");
    }

    [Fact]
    public void StopTerminationDialogContent_ShouldExposeSharedStopCopyContract()
    {
        StopTerminationDialogContent.ConfirmationTitle.Should().Be("Force Terminate xEdit?");
        StopTerminationDialogContent.ConfirmationMessage.Should().Be("xEdit did not exit after the stop request. AutoQAC can leave xEdit running, or force terminate it now. Force terminating can interrupt remaining file or log writes.");
        StopTerminationDialogContent.ForceTerminateButton.Should().Be("Force Terminate");
        StopTerminationDialogContent.LeaveRunningButton.Should().Be("Leave Running");
        StopTerminationDialogContent.ForceFailureTitle.Should().Be("Could Not Force Terminate xEdit");
        StopTerminationDialogContent.ForceFailureMessage.Should().Be("AutoQAC could not force terminate xEdit. xEdit may still be running; close it manually before starting another cleaning session. Technical details are in the latest AutoQAC log.");
        StopTerminationDialogContent.LeftRunningTitle.Should().Be("Cleaning Stopped");
        StopTerminationDialogContent.LeftRunningMessage.Should().Be("AutoQAC stopped the cleaning session. xEdit was left running by your choice; close it manually when it is safe.");
    }

    [Fact]
    public void MessageDialogViewModel_ShouldDefaultChoiceButtonTextToYesAndNo()
    {
        // Arrange
        var dialog = new MessageDialogViewModel();

        // Assert
        dialog.YesButtonText.Should().Be("Yes");
        dialog.NoButtonText.Should().Be("No");
    }

    [Fact]
    public async Task StopCleaningCommand_ForceStoppedStatus_ShouldNotPromptInViewModel()
    {
        // Arrange
        var stateSubject = new BehaviorSubject<AppState>(new AppState { IsCleaning = true });
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState { IsCleaning = true });

        _cleaningSessionMock.ControlAsync(CleaningSessionControl.RequestStop, Arg.Any<CancellationToken>())
            .Returns(new CleaningSessionControlResult(
                CleaningSessionControl.RequestStop,
                CleaningSessionControlStatus.ForceStopped,
                TerminationResult.ForceKilled));

        var vm = new MainWindowViewModel(
            _configServiceMock,
            _stateServiceMock,
            _cleaningSessionMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock,
            _uiDispatcher);

        // Act
        await vm.Commands.StopCleaningCommand.ExecuteAsync(null);

        // Assert
        await _cleaningSessionMock.Received(1)
            .ControlAsync(CleaningSessionControl.RequestStop, Arg.Any<CancellationToken>());
        await _messageDialogMock.DidNotReceive().ShowChoiceAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<MessageDialogIcon>(),
            Arg.Any<string?>());
    }

    [Fact]
    public async Task StopCleaningCommand_LeftRunningByUserStatus_ShouldShowWarning()
    {
        // Arrange
        var stateSubject = new BehaviorSubject<AppState>(new AppState { IsCleaning = true });
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState { IsCleaning = true });

        _cleaningSessionMock.ControlAsync(CleaningSessionControl.RequestStop, Arg.Any<CancellationToken>())
            .Returns(new CleaningSessionControlResult(
                CleaningSessionControl.RequestStop,
                CleaningSessionControlStatus.LeftRunningByUser,
                TerminationResult.GracePeriodExpired));

        var vm = new MainWindowViewModel(
            _configServiceMock,
            _stateServiceMock,
            _cleaningSessionMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock,
            _uiDispatcher);

        // Act
        await vm.Commands.StopCleaningCommand.ExecuteAsync(null);

        // Assert
        await _cleaningSessionMock.Received(1)
            .ControlAsync(CleaningSessionControl.RequestStop, Arg.Any<CancellationToken>());
        await _messageDialogMock.Received(1).ShowWarningAsync(
            StopTerminationDialogContent.LeftRunningTitle,
            StopTerminationDialogContent.LeftRunningMessage,
            null);
    }

    [Fact]
    public async Task StopCleaningCommand_WhenLeftRunningWarningDialogThrows_ShouldKeepLeftRunningOutcome()
    {
        var stateSubject = new BehaviorSubject<AppState>(new AppState { IsCleaning = true });
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState { IsCleaning = true });
        _cleaningSessionMock.ControlAsync(CleaningSessionControl.RequestStop, Arg.Any<CancellationToken>())
            .Returns(new CleaningSessionControlResult(
                CleaningSessionControl.RequestStop,
                CleaningSessionControlStatus.LeftRunningByUser,
                TerminationResult.GracePeriodExpired));
        _messageDialogMock.ShowWarningAsync(
                StopTerminationDialogContent.LeftRunningTitle,
                StopTerminationDialogContent.LeftRunningMessage,
                Arg.Any<string?>())
            .ThrowsAsync(new ApplicationException("warning dialog failed"));

        var vm = new MainWindowViewModel(
            _configServiceMock,
            _stateServiceMock,
            _cleaningSessionMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock,
            _uiDispatcher);

        await vm.Commands.StopCleaningCommand.ExecuteAsync(null);

        await _cleaningSessionMock.Received(1)
            .ControlAsync(CleaningSessionControl.RequestStop, Arg.Any<CancellationToken>());
        await _messageDialogMock.DidNotReceive().ShowErrorAsync(
            StopTerminationDialogContent.ForceFailureTitle,
            StopTerminationDialogContent.ForceFailureMessage,
            Arg.Any<string?>());
        vm.Commands.StatusText.Should().Be("Cleaning stopped; xEdit left running.");
        _loggerMock.Received(1).Error(Arg.Any<ApplicationException>(), "Failed to show left-running stop warning dialog");
    }

    [Fact]
    public async Task ConfigureLoadOrderCommand_ShouldUseSafeLoadOrderIdentifier_WhenParsingFails()
    {
        // Arrange
        var stateSubject = new BehaviorSubject<AppState>(new AppState());
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState());

        var selectedDirectory = Path.Combine(Path.GetTempPath(), "AutoQAC_AliceLoadOrder_" + Guid.NewGuid());
        Directory.CreateDirectory(selectedDirectory);
        var selectedPath = Path.Combine(selectedDirectory, "plugins.txt");
        await File.WriteAllTextAsync(selectedPath, "Skyrim.esm");
        string? dialogMessage = null;
        string? dialogDetails = null;
        _fileDialogMock.OpenFileDialogAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>())
            .Returns(selectedPath);
        _pluginLoadingServiceMock.GetPluginsFromFileAsync(selectedPath, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Access denied reading C:\\Users\\Alice\\AppData\\Local\\Skyrim Special Edition\\plugins.txt"));
        _messageDialogMock.ShowErrorAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>())
            .Returns(Task.CompletedTask)
            .AndDoes(callInfo =>
            {
                dialogMessage = callInfo.ArgAt<string>(1);
                dialogDetails = callInfo.ArgAt<string?>(2);
            });

        try
        {
            var vm = new MainWindowViewModel(
                _configServiceMock,
                _stateServiceMock,
                _cleaningSessionMock,
                _loggerMock,
                _fileDialogMock,
                _messageDialogMock,
                _pluginServiceMock,
                _pluginLoadingServiceMock,
                _uiDispatcher);
            vm.Configuration.SelectedGame = GameType.FalloutNewVegas;

            // Act
            await vm.Configuration.ConfigureLoadOrderCommand.ExecuteAsync(null);

            // Assert
            var userText = string.Join("\n", vm.Configuration.StatusText, dialogMessage, dialogDetails);
            userText.Should().Contain("Load Order File (plugins.txt)");
            userText.Should().Contain("See the latest AutoQAC log for technical details.");
            userText.Should().NotContain(@"C:\Users\Alice");
            userText.Should().NotContain("Access denied reading");
        }
        finally
        {
            Directory.Delete(selectedDirectory, true);
        }
    }

    [Fact]
    public async Task ConfigureGameDataFolderCommand_ShouldUseSafeGameFolderLabel_WhenFolderIsMissing()
    {
        // Arrange
        var stateSubject = new BehaviorSubject<AppState>(new AppState());
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState());

        const string selectedFolder = @"C:\Games\Skyrim Special Edition\Data";
        string? dialogMessage = null;
        string? dialogDetails = null;
        _fileDialogMock.OpenFolderDialogAsync("Select Game Data Folder", Arg.Any<string?>())
            .Returns(selectedFolder);
        _messageDialogMock.ShowErrorAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>())
            .Returns(Task.CompletedTask)
            .AndDoes(callInfo =>
            {
                dialogMessage = callInfo.ArgAt<string>(1);
                dialogDetails = callInfo.ArgAt<string?>(2);
            });

        var vm = new MainWindowViewModel(
            _configServiceMock,
            _stateServiceMock,
            _cleaningSessionMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock,
            _uiDispatcher);
        vm.Configuration.SelectedGame = GameType.SkyrimSe;

        // Act
        await vm.Configuration.ConfigureGameDataFolderCommand.ExecuteAsync(null);

        // Assert
        var userText = string.Join("\n", vm.Configuration.StatusText, dialogMessage, dialogDetails);
        dialogMessage.Should().Be("Skyrim Special Edition data folder is unavailable. Choose a valid Data folder or reset the override.");
        userText.Should().NotContain(selectedFolder);
        userText.Should().NotContain("latest AutoQAC log");
    }

    [Fact]
    public async Task StopCleaningCommand_ForceKillFailed_ShouldShowErrorCopy()
    {
        var stateSubject = new BehaviorSubject<AppState>(new AppState { IsCleaning = true });
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState { IsCleaning = true });
        _cleaningSessionMock.ControlAsync(CleaningSessionControl.RequestStop, Arg.Any<CancellationToken>())
            .Returns(new CleaningSessionControlResult(
                CleaningSessionControl.RequestStop,
                CleaningSessionControlStatus.ForceKillFailed,
                TerminationResult.ForceKillFailed));

        var vm = new MainWindowViewModel(
            _configServiceMock,
            _stateServiceMock,
            _cleaningSessionMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock,
            _uiDispatcher);

        await vm.Commands.StopCleaningCommand.ExecuteAsync(null);

        await _messageDialogMock.Received(1).ShowErrorAsync(
            StopTerminationDialogContent.ForceFailureTitle,
            StopTerminationDialogContent.ForceFailureMessage,
            null);
        await _messageDialogMock.DidNotReceive().ShowChoiceAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<MessageDialogIcon>(),
            Arg.Any<string?>());
    }

    [Fact]
    public async Task StopCleaningCommand_WhenStopThrows_ShouldShowSafeFailureCopy()
    {
        var stateSubject = new BehaviorSubject<AppState>(new AppState { IsCleaning = true });
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState { IsCleaning = true });
        _cleaningSessionMock.ControlAsync(CleaningSessionControl.RequestStop, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("stop failed"));
        _messageDialogMock.ShowErrorAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>())
            .Returns(Task.CompletedTask);

        var vm = new MainWindowViewModel(
            _configServiceMock,
            _stateServiceMock,
            _cleaningSessionMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock,
            _uiDispatcher);

        await vm.Commands.StopCleaningCommand.ExecuteAsync(null);

        await _messageDialogMock.Received(1).ShowErrorAsync(
            StopTerminationDialogContent.ForceFailureTitle,
            StopTerminationDialogContent.ForceFailureMessage,
            null);
        _loggerMock.Received().Error(Arg.Any<InvalidOperationException>(), "StopCleaningAsync failed");
    }

    [Fact]
    public async Task StopCleaningCommand_WhenFailureDialogThrows_ShouldLogAndComplete()
    {
        var stateSubject = new BehaviorSubject<AppState>(new AppState { IsCleaning = true });
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState { IsCleaning = true });
        _cleaningSessionMock.ControlAsync(CleaningSessionControl.RequestStop, Arg.Any<CancellationToken>())
            .Returns(new CleaningSessionControlResult(
                CleaningSessionControl.RequestStop,
                CleaningSessionControlStatus.ForceKillFailed,
                TerminationResult.ForceKillFailed));
        _messageDialogMock.ShowErrorAsync(
                StopTerminationDialogContent.ForceFailureTitle,
                StopTerminationDialogContent.ForceFailureMessage,
                Arg.Any<string?>())
            .ThrowsAsync(new ApplicationException("dialog failed"));

        var vm = new MainWindowViewModel(
            _configServiceMock,
            _stateServiceMock,
            _cleaningSessionMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock,
            _uiDispatcher);

        await vm.Commands.StopCleaningCommand.ExecuteAsync(null);

        _loggerMock.Received().Error(Arg.Any<ApplicationException>(), "Failed to show stop failure dialog");
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
            _cleaningSessionMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock,
            _uiDispatcher);

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

    [Fact]
    public async Task Mo2ModeEnabled_ShouldPersistAndUpdateRuntimeState_WhenToggledInMainWindow()
    {
        // Arrange
        using var stateService = new StateService();
        var configService = Substitute.For<IConfigurationService>();
        var refreshCoordinator = Substitute.For<IPluginRefreshCoordinator>();
        var refreshObserved = CreateSignal();
        UserConfiguration? savedConfig = null;

        configService.SkipListChanged.Returns(Observable.Never<GameType>());
        configService.LoadUserConfigAsync(Arg.Any<CancellationToken>())
            .Returns(_ => new UserConfiguration { LoadOrder = new(), XEdit = new(), ModOrganizer = new(), Settings = new() });
        configService.GetSelectedGameAsync(Arg.Any<CancellationToken>())
            .Returns(GameType.Unknown);
        configService.SaveUserConfigAsync(Arg.Any<UserConfiguration>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                savedConfig = callInfo.Arg<UserConfiguration>();
                return Task.CompletedTask;
            });

        refreshCoordinator.StatusChanged.Returns(Observable.Never<PluginRefreshStatus>());
        refreshCoordinator.RefreshForGameAsync(
                Arg.Any<GameType>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                refreshObserved.TrySetResult(true);
                return Task.FromResult(new PluginRefreshProjection(GameType.Unknown, AvailableProfiles: []));
            });

        var vm = new ConfigurationViewModel(
            configService,
            stateService,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock,
            pluginRefreshCoordinator: refreshCoordinator);

        try
        {
            await vm.InitializeAsync();

            // Act
            vm.Mo2ModeEnabled = true;
            await WaitForSignalAsync(refreshObserved);

            // Assert
            savedConfig.Should().NotBeNull();
            savedConfig!.Settings.Mo2Mode.Should().BeTrue();
            stateService.CurrentState.Mo2ModeEnabled.Should().BeTrue(
                "cleaning command construction reads MO2 mode from AppState");
        }
        finally
        {
            vm.Dispose();
        }
    }

    [Fact]
    public async Task PartialFormsEnabled_ShouldUpdateRuntimeState_WhenToggledInMainWindow()
    {
        // Arrange
        using var stateService = new StateService();
        var configService = Substitute.For<IConfigurationService>();
        var refreshCoordinator = Substitute.For<IPluginRefreshCoordinator>();

        configService.SkipListChanged.Returns(Observable.Never<GameType>());
        configService.LoadUserConfigAsync(Arg.Any<CancellationToken>())
            .Returns(_ => new UserConfiguration { LoadOrder = new(), XEdit = new(), ModOrganizer = new(), Settings = new() });
        configService.GetSelectedGameAsync(Arg.Any<CancellationToken>())
            .Returns(GameType.Unknown);
        refreshCoordinator.StatusChanged.Returns(Observable.Never<PluginRefreshStatus>());

        var vm = new ConfigurationViewModel(
            configService,
            stateService,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock,
            pluginRefreshCoordinator: refreshCoordinator);

        try
        {
            await vm.InitializeAsync();

            // Act
            vm.PartialFormsEnabled = true;

            // Assert
            stateService.CurrentState.PartialFormsEnabled.Should().BeTrue(
                "xEdit argument construction reads Partial Forms from AppState");
        }
        finally
        {
            vm.Dispose();
        }
    }

    [Fact]
    public async Task InitializeAsync_WhenSavedGameUnknownWithSavedLoadOrder_ShouldUsePluginRefreshSeam()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            using var stateService = new StateService();
            var configService = Substitute.For<IConfigurationService>();
            var refreshCoordinator = Substitute.For<IPluginRefreshCoordinator>();

            configService.SkipListChanged.Returns(Observable.Never<GameType>());
            configService.LoadUserConfigAsync(Arg.Any<CancellationToken>())
                .Returns(_ => new UserConfiguration
                {
                    LoadOrder = new LoadOrderConfig { File = tempFile },
                    XEdit = new XEditConfig(),
                    ModOrganizer = new ModOrganizerConfig(),
                    Settings = new AutoQacSettings()
                });
            configService.GetSelectedGameAsync(Arg.Any<CancellationToken>())
                .Returns(GameType.Unknown);
            refreshCoordinator.StatusChanged.Returns(Observable.Never<PluginRefreshStatus>());
            refreshCoordinator.RefreshForGameAsync(
                    GameType.Unknown,
                    Arg.Any<string?>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new PluginRefreshProjection(GameType.Unknown, AvailableProfiles: [])));

            var vm = new ConfigurationViewModel(
                configService,
                stateService,
                _loggerMock,
                _fileDialogMock,
                _messageDialogMock,
                _pluginServiceMock,
                _pluginLoadingServiceMock,
                pluginRefreshCoordinator: refreshCoordinator);

            await vm.InitializeAsync();

            await refreshCoordinator.Received(1).RefreshForGameAsync(
                GameType.Unknown,
                null,
                Arg.Any<CancellationToken>());
            await _pluginServiceMock.DidNotReceive().GetPluginsFromLoadOrderAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>());
            stateService.CurrentState.PluginsToClean.Should().BeEmpty(
                "no-game startup must not publish stale load-order rows outside Plugin refresh");
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
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
            _cleaningSessionMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock,
            _uiDispatcher);

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
            _cleaningSessionMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock,
            _uiDispatcher);

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
        EnableSelectedGameSideEffects();
        var selectedGamePersisted = CreateSignal();
        var stateSubject = new BehaviorSubject<AppState>(new AppState());
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState());
        _configServiceMock.SetSelectedGameAsync(Arg.Any<GameType>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                if (callInfo.ArgAt<GameType>(0) == GameType.SkyrimSe)
                {
                    selectedGamePersisted.TrySetResult(true);
                }

                return Task.CompletedTask;
            });

        var vm = new MainWindowViewModel(
            _configServiceMock,
            _stateServiceMock,
            _cleaningSessionMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock,
            _uiDispatcher);

        // Act
        vm.Configuration.SelectedGame = GameType.SkyrimSe;

        await WaitForSignalAsync(selectedGamePersisted);

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
        EnableSelectedGameSideEffects();
        var pluginsLoaded = CreateSignal();
        var expectedPlugins = new List<PluginInfo>
        {
            new() { FileName = "Plugin1.esp", FullPath = "Data/Plugin1.esp" },
            new() { FileName = "Plugin2.esp", FullPath = "Data/Plugin2.esp" }
        };

        _pluginLoadingServiceMock.IsGameSupportedByMutagen(GameType.SkyrimSe)
            .Returns(true);
        _pluginLoadingServiceMock.TryGetPluginsAsync(GameType.SkyrimSe, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new PluginLoadingResult
            {
                Status = PluginLoadingStatus.Success,
                Plugins = expectedPlugins,
                DataFolder = @"C:\Games\SkyrimSE\Data"
            });

        // Setup skip list (required for ApplySkipListStatus)
        _configServiceMock.GetSkipListAsync(
                Arg.Any<GameType>(),
                Arg.Any<GameVariant>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<string>());

        var stateSubject = new BehaviorSubject<AppState>(new AppState());
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState());
        _stateServiceMock.When(x => x.SetPluginsToClean(Arg.Any<List<PluginInfo>>()))
            .Do(callInfo =>
            {
                var plugins = callInfo.Arg<List<PluginInfo>>();
                if (plugins.Count == 2 &&
                    plugins.Any(p => p.FileName == "Plugin1.esp") &&
                    plugins.Any(p => p.FileName == "Plugin2.esp"))
                {
                    pluginsLoaded.TrySetResult(true);
                }
            });

        var vm = new MainWindowViewModel(
            _configServiceMock,
            _stateServiceMock,
            _cleaningSessionMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock,
            _uiDispatcher);

        // Act
        vm.Configuration.SelectedGame = GameType.SkyrimSe;

        await WaitForSignalAsync(pluginsLoaded);

        // Assert
        await _pluginLoadingServiceMock.Received(1).TryGetPluginsAsync(GameType.SkyrimSe, Arg.Any<string?>(), Arg.Any<CancellationToken>());
        _stateServiceMock.Received(1).SetPluginsToClean(Arg.Is<List<PluginInfo>>(list =>
            list.Count == 2 &&
            list.Any(p => p.FileName == "Plugin1.esp") &&
            list.Any(p => p.FileName == "Plugin2.esp")));
    }

    [Fact]
    public async Task SelectedGame_ShouldPublishPendingApproximationsThenMergeBackgroundResults()
    {
        EnableSelectedGameSideEffects();
        var approximationServiceMock = Substitute.For<IPluginIssueApproximationService>();
        var pendingPluginsPublished = CreateSignal();
        var approximationMergedBeforeCompletion = CreateSignal();
        var allowApproximationCompletion = CreateSignal();
        var expectedPlugins = new List<PluginInfo>
        {
            new() { FileName = "Plugin1.esp", FullPath = @"C:\Games\SkyrimSE\Data\Plugin1.esp" }
        };

        _pluginLoadingServiceMock.IsGameSupportedByMutagen(GameType.SkyrimSe).Returns(true);
        _pluginLoadingServiceMock.TryGetPluginsAsync(GameType.SkyrimSe, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new PluginLoadingResult
            {
                Status = PluginLoadingStatus.Success,
                Plugins = expectedPlugins,
                DataFolder = @"C:\Games\SkyrimSE\Data"
            });

        approximationServiceMock
            .GetApproximationsAsync(
                GameType.SkyrimSe,
                @"C:\Games\SkyrimSE\Data",
                Arg.Any<Action<PluginIssueApproximationResult>>(),
                Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var callback = callInfo.ArgAt<Action<PluginIssueApproximationResult>>(2);
                callback(new PluginIssueApproximationResult
                {
                    FileName = "Plugin1.esp",
                    FullPath = @"C:\Games\SkyrimSE\Data\Plugin1.esp",
                    Approximation = PluginIssueApproximation.Available(3, 2, 1)
                });

                await WaitForSignalAsync(approximationMergedBeforeCompletion.Task, "expected incremental approximation merge before completion");
                await allowApproximationCompletion.Task;

                return
                [
                    new PluginIssueApproximationResult
                    {
                        FileName = "Plugin1.esp",
                        FullPath = @"C:\Games\SkyrimSE\Data\Plugin1.esp",
                        Approximation = PluginIssueApproximation.Available(3, 2, 1)
                    }
                ];
            });

        _configServiceMock.GetSkipListAsync(
                Arg.Any<GameType>(),
                Arg.Any<GameVariant>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<string>());

        var stateSubject = new BehaviorSubject<AppState>(new AppState());
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState());
        _stateServiceMock.When(x => x.SetPluginsToClean(Arg.Any<List<PluginInfo>>()))
            .Do(callInfo =>
            {
                var plugins = callInfo.Arg<List<PluginInfo>>();
                if (plugins.Count == 1 &&
                    plugins[0].Approximation.Status == PluginIssueApproximationStatus.Pending)
                {
                    pendingPluginsPublished.TrySetResult(true);
                }
            });
        _stateServiceMock.When(x => x.MergePluginApproximation(Arg.Any<PluginIssueApproximationResult>()))
            .Do(callInfo =>
            {
                var result = callInfo.Arg<PluginIssueApproximationResult>();
                if (result.Approximation.Status == PluginIssueApproximationStatus.Available)
                {
                    approximationMergedBeforeCompletion.TrySetResult(true);
                }
            });

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
            pluginIssueApproximationService: approximationServiceMock);

        vm.Configuration.SelectedGame = GameType.SkyrimSe;
        await WaitForSignalAsync(pendingPluginsPublished);
        await WaitForSignalAsync(approximationMergedBeforeCompletion);
        allowApproximationCompletion.TrySetResult(true);

        _stateServiceMock.Received(1).SetPluginsToClean(Arg.Is<List<PluginInfo>>(list =>
            list.Count == 1 &&
            list[0].Approximation.Status == PluginIssueApproximationStatus.Pending));
        await approximationServiceMock.Received(1)
            .GetApproximationsAsync(
                GameType.SkyrimSe,
                @"C:\Games\SkyrimSE\Data",
                Arg.Any<Action<PluginIssueApproximationResult>>(),
                Arg.Any<CancellationToken>());
        _stateServiceMock.Received(1).MergePluginApproximation(Arg.Is<PluginIssueApproximationResult>(result =>
            result.Approximation.Status == PluginIssueApproximationStatus.Available &&
            result.Approximation.ItmCount == 3));
        _stateServiceMock.Received(1).MergePluginApproximation(Arg.Is<PluginIssueApproximationResult>(result =>
            result.Approximation.Status == PluginIssueApproximationStatus.Available &&
            result.Approximation.ItmCount == 3));
    }

    [Fact]
    public async Task SelectedGame_ShouldKeepPluginListLoadedWhenApproximationFails()
    {
        EnableSelectedGameSideEffects();
        var approximationServiceMock = Substitute.For<IPluginIssueApproximationService>();
        var pluginsLoaded = CreateSignal();
        var unavailableMerged = CreateSignal();
        var expectedPlugins = new List<PluginInfo>
        {
            new() { FileName = "Plugin1.esp", FullPath = @"C:\Games\SkyrimSE\Data\Plugin1.esp" }
        };

        _pluginLoadingServiceMock.IsGameSupportedByMutagen(GameType.SkyrimSe).Returns(true);
        _pluginLoadingServiceMock.TryGetPluginsAsync(GameType.SkyrimSe, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new PluginLoadingResult
            {
                Status = PluginLoadingStatus.Success,
                Plugins = expectedPlugins,
                DataFolder = @"C:\Games\SkyrimSE\Data"
            });

        approximationServiceMock
            .GetApproximationsAsync(
                GameType.SkyrimSe,
                @"C:\Games\SkyrimSE\Data",
                Arg.Any<Action<PluginIssueApproximationResult>>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Approximation failed"));

        _configServiceMock.GetSkipListAsync(
                Arg.Any<GameType>(),
                Arg.Any<GameVariant>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<string>());

        var stateSubject = new BehaviorSubject<AppState>(new AppState());
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState());
        _stateServiceMock.When(x => x.SetPluginsToClean(Arg.Any<List<PluginInfo>>()))
            .Do(callInfo =>
            {
                var plugins = callInfo.Arg<List<PluginInfo>>();
                if (plugins.Count == 1 && plugins[0].FileName == "Plugin1.esp")
                {
                    pluginsLoaded.TrySetResult(true);
                }
            });
        _stateServiceMock.When(x => x.MergePluginApproximation(Arg.Any<PluginIssueApproximationResult>()))
            .Do(callInfo =>
            {
                var result = callInfo.Arg<PluginIssueApproximationResult>();
                if (result.Approximation.Status == PluginIssueApproximationStatus.Unavailable)
                {
                    unavailableMerged.TrySetResult(true);
                }
            });

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
            pluginIssueApproximationService: approximationServiceMock);

        vm.Configuration.SelectedGame = GameType.SkyrimSe;
        await WaitForSignalAsync(pluginsLoaded);
        await WaitForSignalAsync(unavailableMerged);

        _stateServiceMock.Received(1).SetPluginsToClean(Arg.Is<List<PluginInfo>>(list =>
            list.Count == 1 && list[0].FileName == "Plugin1.esp"));
        _stateServiceMock.Received(1).MergePluginApproximation(Arg.Is<PluginIssueApproximationResult>(result =>
            result.Approximation.Status == PluginIssueApproximationStatus.Unavailable));
    }

    [Fact]
    public async Task SelectedGame_StalePluginLoad_DoesNotOverwriteCurrentListOrStartApproximation()
    {
        EnableSelectedGameSideEffects();
        var approximationServiceMock = Substitute.For<IPluginIssueApproximationService>();
        var approximationAttempted = CreateSignal();
        var firstLoadStarted = CreateSignal();
        var unknownSelectionApplied = CreateSignal();
        var stalePluginListApplied = CreateSignal();
        var firstLoadResult = new TaskCompletionSource<PluginLoadingResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var stalePlugins = new List<PluginInfo>
        {
            new() { FileName = "Plugin1.esp", FullPath = @"C:\Games\SkyrimSE\Data\Plugin1.esp" }
        };

        _pluginLoadingServiceMock.IsGameSupportedByMutagen(GameType.SkyrimSe).Returns(true);
        _pluginLoadingServiceMock.TryGetPluginsAsync(GameType.SkyrimSe, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                firstLoadStarted.TrySetResult(true);
                return firstLoadResult.Task;
            });

        _configServiceMock.GetSkipListAsync(
                Arg.Any<GameType>(),
                Arg.Any<GameVariant>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<string>());

        approximationServiceMock
            .GetApproximationsAsync(Arg.Any<GameType>(), Arg.Any<string>(), ct: Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                approximationAttempted.TrySetResult(true);
                return Task.FromResult<IReadOnlyList<PluginIssueApproximationResult>>([]);
            });

        _stateServiceMock.When(x => x.SetPluginsToClean(Arg.Any<List<PluginInfo>>()))
            .Do(callInfo =>
            {
                var plugins = callInfo.Arg<List<PluginInfo>>();
                if (plugins.Count == 0)
                {
                    unknownSelectionApplied.TrySetResult(true);
                }

                if (plugins.Count == 1 &&
                    plugins[0].FileName == "Plugin1.esp" &&
                    plugins[0].Approximation.Status == PluginIssueApproximationStatus.Pending)
                {
                    stalePluginListApplied.TrySetResult(true);
                }
            });

        var stateSubject = new BehaviorSubject<AppState>(new AppState());
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState());

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
            pluginIssueApproximationService: approximationServiceMock);

        vm.Configuration.SelectedGame = GameType.SkyrimSe;
        await WaitForSignalAsync(firstLoadStarted);

        vm.Configuration.SelectedGame = GameType.Unknown;
        await WaitForSignalAsync(unknownSelectionApplied);

        // Now release the superseded SkyrimSe load. The refresh's continuation should
        // observe the bumped generation / cancelled token and bail out without writing.
        firstLoadResult.SetResult(new PluginLoadingResult
        {
            Status = PluginLoadingStatus.Success,
            Plugins = stalePlugins,
            DataFolder = @"C:\Games\SkyrimSE\Data"
        });

        // Yield enough times for the awaiter continuation to run and exit.
        for (var i = 0; i < 5; i++) await Task.Yield();

        stalePluginListApplied.Task.IsCompleted.Should().BeFalse(
            "stale plugin loads must not overwrite the current game's plugin list");
        approximationAttempted.Task.IsCompleted.Should().BeFalse(
            "stale plugin loads should not start a background approximation refresh");
        await approximationServiceMock.DidNotReceive()
            .GetApproximationsAsync(Arg.Any<GameType>(), Arg.Any<string>(), ct: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfigureXEditAsync_PersistsNewlySelectedPath()
    {
        // Arrange
        const string oldPath = @"C:\Tools\xEdit-old\xEdit.exe";
        const string newPath = @"C:\Tools\xEdit-new\xEdit.exe";

        var initialConfig = new UserConfiguration
        {
            LoadOrder = new(),
            XEdit = new() { Binary = oldPath },
            ModOrganizer = new(),
            Settings = new()
        };
        _configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>()).Returns(initialConfig);
        _configServiceMock.GetSelectedGameAsync(Arg.Any<CancellationToken>()).Returns(GameType.Unknown);

        _fileDialogMock.OpenFileDialogAsync(
                "Select xEdit Executable",
                Arg.Any<string>())
            .Returns(newPath);

        var stateSubject = new BehaviorSubject<AppState>(new AppState { XEditExecutablePath = oldPath });
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState { XEditExecutablePath = oldPath });

        UserConfiguration? savedConfig = null;
        _configServiceMock.When(x => x.SaveUserConfigAsync(
                Arg.Any<UserConfiguration>(),
                Arg.Any<CancellationToken>()))
            .Do(callInfo => savedConfig = callInfo.Arg<UserConfiguration>());

        var vm = new MainWindowViewModel(
            _configServiceMock,
            _stateServiceMock,
            _cleaningSessionMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock,
            _uiDispatcher);

        // Act
        await vm.Configuration.ConfigureXEditCommand.ExecuteAsync(null);

        // Assert
        savedConfig.Should().NotBeNull("SaveUserConfigAsync should be invoked after picking a new path");
        savedConfig!.XEdit.Binary.Should().Be(newPath,
            "the persisted xEdit path must reflect the user's just-selected file, not the stale VM property");
    }

    [Fact]
    public async Task ConfigureXEditAsync_ShouldFlushBrowseSelectionToDisk()
    {
        // Arrange
        const string oldPath = @"C:\Tools\xEdit-old\xEdit.exe";
        const string newPath = @"C:\Tools\xEdit-new\xEdit.exe";
        var configDirectory = Path.Combine(Path.GetTempPath(), "AutoQAC_XEditBrowse_" + Guid.NewGuid());
        Directory.CreateDirectory(configDirectory);

        var configService = new ConfigurationService(Substitute.For<ILoggingService>(), configDirectory);
        var freshService = new ConfigurationService(Substitute.For<ILoggingService>(), configDirectory);
        try
        {
            await configService.SaveUserConfigAsync(new UserConfiguration
            {
                LoadOrder = new(),
                XEdit = new() { Binary = oldPath },
                ModOrganizer = new(),
                Settings = new()
            });
            await configService.FlushPendingSavesAsync();

            var fileDialog = Substitute.For<IFileDialogService>();
            fileDialog.OpenFileDialogAsync("Select xEdit Executable", Arg.Any<string>())
                .Returns(newPath);

            var stateService = new StateService();
            var vm = new ConfigurationViewModel(
                configService,
                stateService,
                Substitute.For<ILoggingService>(),
                fileDialog,
                _messageDialogMock,
                _pluginServiceMock,
                _pluginLoadingServiceMock);
            await vm.InitializeAsync();

            // Act
            await vm.ConfigureXEditCommand.ExecuteAsync(null);

            // Assert
            var loaded = await freshService.LoadUserConfigAsync();
            loaded.XEdit.Binary.Should().Be(newPath,
                "the Browse button is an explicit save action and should be durable without waiting for the debounce timer");
            stateService.CurrentState.XEditExecutablePath.Should().Be(newPath);
        }
        finally
        {
            await configService.DisposeAsync();
            await freshService.DisposeAsync();
            if (Directory.Exists(configDirectory))
            {
                Directory.Delete(configDirectory, true);
            }
        }
    }

    [Fact]
    public async Task ShowSettingsCommand_ShouldRefreshRuntimeConfigurationPaths_WhenSettingsAreSaved()
    {
        // Arrange
        const string loadOrderPath = @"C:\Users\Test\AppData\Local\Skyrim Special Edition\plugins.txt";
        const string mo2Path = @"C:\Tools\MO2\ModOrganizer.exe";
        const string xEditPath = @"C:\Tools\xEdit\SSEEdit.exe";

        var savedConfig = new UserConfiguration
        {
            LoadOrder = new LoadOrderConfig { File = loadOrderPath },
            XEdit = new XEditConfig { Binary = xEditPath },
            ModOrganizer = new ModOrganizerConfig { Binary = mo2Path },
            Settings = new AutoQacSettings { Mo2Mode = true, CleaningTimeout = 600 }
        };
        _configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>()).Returns(savedConfig);
        _configServiceMock.GetSelectedGameAsync(Arg.Any<CancellationToken>()).Returns(GameType.Unknown);

        var stateSubject = new BehaviorSubject<AppState>(new AppState());
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState());

        var vm = new MainWindowViewModel(
            _configServiceMock,
            _stateServiceMock,
            _cleaningSessionMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock,
            _uiDispatcher);
        using var _ = vm.ShowSettingsInteraction.RegisterHandler(_ => Task.FromResult(true));
        _stateServiceMock.ClearReceivedCalls();

        // Act
        await vm.Commands.ShowSettingsCommand.ExecuteAsync(null);

        // Assert
        _stateServiceMock.Received(1).UpdateConfigurationPaths(loadOrderPath, mo2Path, xEditPath);
    }

    [Fact]
    public async Task ConfigureMo2Async_PersistsNewlySelectedPath()
    {
        // Arrange
        const string oldPath = @"C:\Tools\MO2-old\ModOrganizer.exe";
        const string newPath = @"C:\Tools\MO2-new\ModOrganizer.exe";

        var initialConfig = new UserConfiguration
        {
            LoadOrder = new(),
            XEdit = new(),
            ModOrganizer = new() { Binary = oldPath },
            Settings = new()
        };
        _configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>()).Returns(initialConfig);
        _configServiceMock.GetSelectedGameAsync(Arg.Any<CancellationToken>()).Returns(GameType.Unknown);

        _fileDialogMock.OpenFileDialogAsync(
                "Select Mod Organizer 2 Executable",
                Arg.Any<string>())
            .Returns(newPath);

        var stateSubject = new BehaviorSubject<AppState>(new AppState { Mo2ExecutablePath = oldPath });
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState { Mo2ExecutablePath = oldPath });

        UserConfiguration? savedConfig = null;
        _configServiceMock.When(x => x.SaveUserConfigAsync(
                Arg.Any<UserConfiguration>(),
                Arg.Any<CancellationToken>()))
            .Do(callInfo => savedConfig = callInfo.Arg<UserConfiguration>());

        var vm = new MainWindowViewModel(
            _configServiceMock,
            _stateServiceMock,
            _cleaningSessionMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock,
            _uiDispatcher);

        // Act
        await vm.Configuration.ConfigureMo2Command.ExecuteAsync(null);

        // Assert
        savedConfig.Should().NotBeNull("SaveUserConfigAsync should be invoked after picking a new path");
        savedConfig!.ModOrganizer.Binary.Should().Be(newPath,
            "the persisted MO2 path must reflect the user's just-selected file, not the stale VM property");
    }

    [Fact]
    public async Task SelectedGame_ShouldShowSpecificStatusWithoutClaimingFallback_WhenMutagenReturnsNoPlugins()
    {
        // Arrange
        EnableSelectedGameSideEffects();
        var emptyPluginListPublished = CreateSignal();
        _pluginLoadingServiceMock.IsGameSupportedByMutagen(GameType.SkyrimSe)
            .Returns(true);
        _pluginLoadingServiceMock.TryGetPluginsAsync(GameType.SkyrimSe, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new PluginLoadingResult
            {
                Status = PluginLoadingStatus.NoPluginsDiscovered,
                Plugins = new List<PluginInfo>(),
                DataFolder = @"C:\Games\SkyrimSE\Data"
            });

        _configServiceMock.GetSkipListAsync(
                Arg.Any<GameType>(),
                Arg.Any<GameVariant>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<string>());

        var stateSubject = new BehaviorSubject<AppState>(new AppState());
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState());
        _stateServiceMock.When(x => x.SetPluginsToClean(Arg.Any<List<PluginInfo>>()))
            .Do(callInfo =>
            {
                if (callInfo.Arg<List<PluginInfo>>().Count == 0)
                {
                    emptyPluginListPublished.TrySetResult(true);
                }
            });

        var vm = new MainWindowViewModel(
            _configServiceMock,
            _stateServiceMock,
            _cleaningSessionMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock,
            _uiDispatcher);

        // Act
        vm.Configuration.SelectedGame = GameType.SkyrimSe;
        await WaitForSignalAsync(emptyPluginListPublished);

        // Assert
        vm.Configuration.StatusText.Should().Contain("No plugins discovered via Mutagen");
        vm.Configuration.StatusText.Should().NotContain("trying load order file fallback");
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
            _cleaningSessionMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock,
            _uiDispatcher);

        // Act & Assert
        FluentActions.Invoking(vm.Dispose)
            .Should().NotThrow("disposal should complete without exception");
    }

    #endregion
}
