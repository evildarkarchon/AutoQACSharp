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
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ReactiveUI;

namespace AutoQAC.Tests.ViewModels;

/// <summary>
/// Tests for error dialog functionality in MainWindowViewModel.
/// </summary>
public sealed class ErrorDialogTests
{
    private readonly IConfigurationService _configServiceMock;
    private readonly IStateService _stateServiceMock;
    private readonly ICleaningOrchestrator _orchestratorMock;
    private readonly ILoggingService _loggerMock;
    private readonly IFileDialogService _fileDialogMock;
    private readonly IMessageDialogService _messageDialogMock;
    private readonly IPluginValidationService _pluginServiceMock;
    private readonly IPluginLoadingService _pluginLoadingServiceMock;

    public ErrorDialogTests()
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

    private MainWindowViewModel CreateViewModel()
    {
        var stateSubject = new BehaviorSubject<AppState>(new AppState());
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState());

        return new MainWindowViewModel(
            _configServiceMock,
            _stateServiceMock,
            _orchestratorMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock);
    }

    /// <summary>
    /// Creates a ViewModel with a valid CurrentState that passes ValidatePreClean,
    /// allowing tests to reach the orchestrator call.
    /// </summary>
    private MainWindowViewModel CreateViewModelWithValidState(string xEditPath)
    {
        var validState = new AppState
        {
            XEditExecutablePath = xEditPath,
            PluginsToClean = new List<PluginInfo>
            {
                new() { FileName = "Test.esp", FullPath = "Test.esp", IsSelected = true }
            }
        };
        var stateSubject = new BehaviorSubject<AppState>(validState);
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(validState);

        return new MainWindowViewModel(
            _configServiceMock,
            _stateServiceMock,
            _orchestratorMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock);
    }

    #region xEdit Validation Tests (Inline Validation Panel)

    [Fact]
    public async Task StartCleaningCommand_ShouldShowInlineValidation_WhenXEditPathIsNull()
    {
        // Arrange - CurrentState has null xEdit path (default AppState)
        var vm = CreateViewModel();
        vm.Configuration.XEditPath = null;

        // Act
        await vm.Commands.StartCleaningCommand.Execute();

        // Assert - inline validation errors shown, no modal dialog
        vm.Commands.HasValidationErrors.Should().BeTrue("validation errors should be visible");
        vm.Commands.ValidationErrors.Should().Contain(e => e.Title == "xEdit not configured",
            "should show xEdit not configured error");

        // No modal dialog should be shown
        await _messageDialogMock.DidNotReceive().ShowErrorAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>());

        // Orchestrator should NOT be called
        await _orchestratorMock.DidNotReceive().StartCleaningAsync(Arg.Any<TimeoutRetryCallback>(), Arg.Any<BackupFailureCallback>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartCleaningCommand_ShouldShowInlineValidation_WhenXEditPathIsEmpty()
    {
        // Arrange - CurrentState has empty xEdit path
        var vm = CreateViewModel();
        vm.Configuration.XEditPath = string.Empty;

        // Act
        await vm.Commands.StartCleaningCommand.Execute();

        // Assert - inline validation errors shown
        vm.Commands.HasValidationErrors.Should().BeTrue();
        vm.Commands.ValidationErrors.Should().Contain(e => e.Title == "xEdit not configured");
    }

    [Fact]
    public async Task StartCleaningCommand_ShouldShowInlineValidation_WhenXEditFileNotFound()
    {
        // Arrange - CurrentState has xEdit path that doesn't exist on disk
        var nonExistentPath = @"C:\NonExistent\xedit.exe";
        var stateWithBadXEdit = new AppState
        {
            XEditExecutablePath = nonExistentPath,
            PluginsToClean = new List<PluginInfo>
            {
                new() { FileName = "Test.esp", FullPath = "Test.esp", IsSelected = true }
            }
        };
        var stateSubject = new BehaviorSubject<AppState>(stateWithBadXEdit);
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(stateWithBadXEdit);

        var vm = new MainWindowViewModel(
            _configServiceMock,
            _stateServiceMock,
            _orchestratorMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock);

        vm.Configuration.XEditPath = nonExistentPath;

        // Act
        await vm.Commands.StartCleaningCommand.Execute();

        // Assert - inline validation errors shown
        vm.Commands.HasValidationErrors.Should().BeTrue();
        vm.Commands.ValidationErrors.Should().Contain(e => e.Title == "xEdit not found",
            "should show xEdit not found error");
    }

    #endregion

    #region Invalid Load Order Tests

    [Fact]
    public async Task ConfigureLoadOrderCommand_ShouldShowErrorDialog_WhenFileNotFound()
    {
        // Arrange
        var vm = CreateViewModel();
        var nonExistentPath = @"C:\NonExistent\plugins.txt";

        _fileDialogMock.OpenFileDialogAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>())
            .Returns(nonExistentPath);

        // Act
        await vm.Configuration.ConfigureLoadOrderCommand.Execute();

        // Assert
        await _messageDialogMock.Received(1).ShowErrorAsync(
                "File Not Found",
                Arg.Any<string>(),
                Arg.Any<string?>());
    }

    [Fact]
    public async Task ConfigureLoadOrderCommand_ShouldShowWarningDialog_WhenNoPluginsFound()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            var vm = CreateViewModel();

            _fileDialogMock.OpenFileDialogAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<string?>())
                .Returns(tempFile);

            _configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
                .Returns(new UserConfiguration { LoadOrder = new(), XEdit = new(), ModOrganizer = new(), Settings = new() });

            // Return empty list
            _pluginServiceMock.GetPluginsFromLoadOrderAsync(tempFile, Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(new List<PluginInfo>());

            // Act
            await vm.Configuration.ConfigureLoadOrderCommand.Execute();

            // Assert
            await _messageDialogMock.Received(1).ShowWarningAsync(
                    "No Plugins Found",
                    Arg.Any<string>(),
                    Arg.Any<string?>());
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ConfigureLoadOrderCommand_ShouldShowErrorDialog_WhenIOExceptionThrown()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            var vm = CreateViewModel();

            _fileDialogMock.OpenFileDialogAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<string?>())
                .Returns(tempFile);

            // Throw IOException
            _pluginServiceMock.GetPluginsFromLoadOrderAsync(tempFile, Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new IOException("File in use"));

            // Act
            await vm.Configuration.ConfigureLoadOrderCommand.Execute();

            // Assert
            await _messageDialogMock.Received(1).ShowErrorAsync(
                    "Read Error",
                    Arg.Any<string>(),
                    Arg.Any<string?>());
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    #endregion

    #region Cleaning Failure Tests

    [Fact]
    public async Task StartCleaningCommand_ShouldShowInlineError_WhenConfigurationInvalid()
    {
        // Arrange - use valid state so ValidatePreClean passes and orchestrator is reached
        var tempFile = Path.GetTempFileName();
        try
        {
            var vm = CreateViewModelWithValidState(tempFile);
            vm.Configuration.XEditPath = tempFile;

            _orchestratorMock.StartCleaningAsync(Arg.Any<TimeoutRetryCallback>(), Arg.Any<BackupFailureCallback>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new InvalidOperationException("Configuration is invalid"));

            // Act
            await vm.Commands.StartCleaningCommand.Execute();

            // Assert - inline validation error shown instead of modal dialog
            vm.Commands.HasValidationErrors.Should().BeTrue("validation errors should be visible");
            vm.Commands.ValidationErrors.Should().HaveCount(1);
            vm.Commands.ValidationErrors[0].Title.Should().Be("Configuration error");
            vm.Commands.StatusText.Should().Contain("error");

            // No modal dialog should be shown
            await _messageDialogMock.DidNotReceive().ShowErrorAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>());
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task StartCleaningCommand_ShouldShowGenericErrorDialog_WhenUnexpectedExceptionThrown()
    {
        // Arrange - use valid state so ValidatePreClean passes and orchestrator is reached
        var tempFile = Path.GetTempFileName();
        try
        {
            var vm = CreateViewModelWithValidState(tempFile);
            vm.Configuration.XEditPath = tempFile;

            _orchestratorMock.StartCleaningAsync(Arg.Any<TimeoutRetryCallback>(), Arg.Any<BackupFailureCallback>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new Exception("Unexpected error"));

            // Act
            await vm.Commands.StartCleaningCommand.Execute();

            // Assert - generic exceptions still use modal dialog (truly unexpected)
            await _messageDialogMock.Received(1).ShowErrorAsync(
                    "Cleaning Failed",
                    Arg.Any<string>(),
                    Arg.Any<string?>());
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    #endregion

    #region Timeout Retry Tests

    [Fact]
    public async Task StartCleaningCommand_ShouldPassTimeoutCallback_ToOrchestrator()
    {
        // Arrange - use valid state so ValidatePreClean passes
        var tempFile = Path.GetTempFileName();
        try
        {
            var vm = CreateViewModelWithValidState(tempFile);
            vm.Configuration.XEditPath = tempFile;

            TimeoutRetryCallback? capturedCallback = null;
            _orchestratorMock.StartCleaningAsync(
                    Arg.Do<TimeoutRetryCallback?>(cb => capturedCallback = cb),
                    Arg.Any<BackupFailureCallback?>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            // Act
            await vm.Commands.StartCleaningCommand.Execute();

            // Assert
            capturedCallback.Should().NotBeNull("Timeout callback should be passed to orchestrator");
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task TimeoutCallback_ShouldCallShowRetryAsync()
    {
        // Arrange - use valid state so ValidatePreClean passes
        var tempFile = Path.GetTempFileName();
        try
        {
            var vm = CreateViewModelWithValidState(tempFile);
            vm.Configuration.XEditPath = tempFile;

            TimeoutRetryCallback? capturedCallback = null;
            _orchestratorMock.StartCleaningAsync(
                    Arg.Do<TimeoutRetryCallback?>(cb => capturedCallback = cb),
                    Arg.Any<BackupFailureCallback?>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            _messageDialogMock.ShowRetryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>())
                .Returns(true);

            await vm.Commands.StartCleaningCommand.Execute();

            // Act - simulate timeout callback being invoked
            capturedCallback.Should().NotBeNull();
            var result = await capturedCallback!("TestPlugin.esp", 300, 1);

            // Assert
            await _messageDialogMock.Received(1).ShowRetryAsync(
                    "Plugin Timeout",
                    Arg.Is<string>(s => s.Contains("TestPlugin.esp")),
                    Arg.Any<string?>());

            result.Should().BeTrue("ShowRetryAsync returned true");
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task TimeoutCallback_ShouldReturnFalse_WhenUserCancels()
    {
        // Arrange - use valid state so ValidatePreClean passes
        var tempFile = Path.GetTempFileName();
        try
        {
            var vm = CreateViewModelWithValidState(tempFile);
            vm.Configuration.XEditPath = tempFile;

            TimeoutRetryCallback? capturedCallback = null;
            _orchestratorMock.StartCleaningAsync(
                    Arg.Do<TimeoutRetryCallback?>(cb => capturedCallback = cb),
                    Arg.Any<BackupFailureCallback?>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            // User cancels retry
            _messageDialogMock.ShowRetryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>())
                .Returns(false);

            await vm.Commands.StartCleaningCommand.Execute();

            // Act
            capturedCallback.Should().NotBeNull();
            var result = await capturedCallback!("TestPlugin.esp", 300, 1);

            // Assert
            result.Should().BeFalse("User cancelled the retry");
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    #endregion
}
