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
using AutoQAC.Tests.Helpers;
using AutoQAC.Tests.TestInfrastructure;
using AutoQAC.ViewModels;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

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
    private readonly IUiDispatcher _uiDispatcher;

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
            _pluginLoadingServiceMock,
            _uiDispatcher);
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
                new() { FileName = "Test.esp", FullPath = "Test.esp" }
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
            _pluginLoadingServiceMock,
            _uiDispatcher);
    }

    #region xEdit Validation Tests (Inline Validation Panel)

    [Fact]
    public async Task StartCleaningCommand_ShouldShowInlineValidation_WhenXEditPathIsNull()
    {
        // Arrange - CurrentState has null xEdit path (default AppState)
        var vm = CreateViewModel();
        vm.Configuration.XEditPath = null;

        // Act
        await vm.Commands.StartCleaningCommand.ExecuteAsync(null);

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
        await vm.Commands.StartCleaningCommand.ExecuteAsync(null);

        // Assert - inline validation errors shown
        vm.Commands.HasValidationErrors.Should().BeTrue();
        vm.Commands.ValidationErrors.Should().Contain(e => e.Title == "xEdit not configured");
    }

    [Fact]
    public async Task StartCleaningCommand_ShouldShowInlineValidation_WhenXEditFileNotFound()
    {
        // Arrange - CurrentState has xEdit path that doesn't exist on disk
        var nonExistentPath = @"C:\Users\Alice\Tools\SSEEdit.exe";
        var stateWithBadXEdit = new AppState
        {
            XEditExecutablePath = nonExistentPath,
            PluginsToClean = new List<PluginInfo>
            {
                new() { FileName = "Test.esp", FullPath = "Test.esp" }
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
            _pluginLoadingServiceMock,
            _uiDispatcher);

        vm.Configuration.XEditPath = nonExistentPath;

        // Act
        await vm.Commands.StartCleaningCommand.ExecuteAsync(null);

        // Assert - inline validation errors shown
        vm.Commands.HasValidationErrors.Should().BeTrue();
        vm.Commands.ValidationErrors.Should().Contain(e => e.Title == "xEdit not found",
            "should show xEdit not found error");
        var error = vm.Commands.ValidationErrors.Single(e => e.Title == "xEdit not found");
        error.Message.Should().Be("xEdit Path (SSEEdit.exe) is missing. Choose the correct xEdit executable in Settings.");
        AssertValidationErrorDoesNotContainFullPath(error, @"C:\Users\Alice");
    }

    [Fact]
    public async Task StartCleaningCommand_ShouldUseFallbackIdentifier_WhenXEditBasenameIsUnsafe()
    {
        // Arrange - all basename characters are unsafe for display, so the fallback name should be used.
        var unsafeBasenamePath = "C:\\Users\\Alice\\Tools\\\"`|&;";
        var stateWithBadXEdit = new AppState
        {
            XEditExecutablePath = unsafeBasenamePath,
            PluginsToClean = new List<PluginInfo>
            {
                new() { FileName = "Test.esp", FullPath = "Test.esp" }
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
            _pluginLoadingServiceMock,
            _uiDispatcher);

        // Act
        await vm.Commands.StartCleaningCommand.ExecuteAsync(null);

        // Assert
        var error = vm.Commands.ValidationErrors.Single(e => e.Title == "xEdit not found");
        error.Message.Should().Be("xEdit Path (xEdit executable) is missing. Choose the correct xEdit executable in Settings.");
        AssertValidationErrorDoesNotContainFullPath(error, @"C:\Users\Alice");
    }

    #endregion

    #region Invalid Load Order Tests

    [Fact]
    public async Task StartCleaningCommand_ShouldShowInlineValidation_WhenNonMutagenGameLoadOrderMissing()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            var state = new AppState
            {
                CurrentGameType = GameType.Fallout3,
                LoadOrderPath = null,
                XEditExecutablePath = tempFile,
                PluginsToClean = new List<PluginInfo>
                {
                    new() { FileName = "Test.esp", FullPath = "Test.esp" }
                }
            };

            var stateSubject = new BehaviorSubject<AppState>(state);
            _stateServiceMock.StateChanged.Returns(stateSubject);
            _stateServiceMock.CurrentState.Returns(state);

            var vm = new MainWindowViewModel(
                _configServiceMock,
                _stateServiceMock,
                _orchestratorMock,
                _loggerMock,
                _fileDialogMock,
                _messageDialogMock,
                _pluginServiceMock,
                _pluginLoadingServiceMock,
                _uiDispatcher);

            // Act
            await vm.Commands.StartCleaningCommand.ExecuteAsync(null);

            // Assert
            vm.Commands.HasValidationErrors.Should().BeTrue();
            vm.Commands.ValidationErrors.Should().Contain(e => e.Title == "Load order not configured");
            await _orchestratorMock.DidNotReceive().StartCleaningAsync(
                Arg.Any<TimeoutRetryCallback>(),
                Arg.Any<BackupFailureCallback>(),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

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
        await vm.Configuration.ConfigureLoadOrderCommand.ExecuteAsync(null);

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

            // Return empty list from the coordinator-backed load-order path.
            _pluginLoadingServiceMock.GetPluginsFromFileAsync(tempFile, Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(new List<PluginInfo>());

            // Act
            await vm.Configuration.ConfigureLoadOrderCommand.ExecuteAsync(null);

            // Assert
            vm.Configuration.StatusText.Should().Contain("No plugins found");
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

            // Throw IOException from the coordinator-backed load-order path.
            _pluginLoadingServiceMock.GetPluginsFromFileAsync(tempFile, Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new IOException("File in use"));

            // Act
            await vm.Configuration.ConfigureLoadOrderCommand.ExecuteAsync(null);

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
            using var _ = vm.ShowProgressInteraction.RegisterHandler(_ => Task.FromResult(default(AutoQAC.Services.UI.Interactions.Unit)));
            vm.Configuration.XEditPath = tempFile;

            _orchestratorMock.StartCleaningAsync(Arg.Any<TimeoutRetryCallback>(), Arg.Any<BackupFailureCallback>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new InvalidOperationException("Configuration is invalid"));

            // Act
            await vm.Commands.StartCleaningCommand.ExecuteAsync(null);

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
            using var _ = vm.ShowProgressInteraction.RegisterHandler(_ => Task.FromResult(default(AutoQAC.Services.UI.Interactions.Unit)));
            vm.Configuration.XEditPath = tempFile;

            _orchestratorMock.StartCleaningAsync(Arg.Any<TimeoutRetryCallback>(), Arg.Any<BackupFailureCallback>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new Exception("Unexpected error"));

            // Act
            await vm.Commands.StartCleaningCommand.ExecuteAsync(null);

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

    [Fact]
    public async Task StartCleaningCommand_ShouldShowSafeLoadOrderIdentifier_WhenNonMutagenGameLoadOrderFileMissing()
    {
        // Arrange - Fallout 3 uses the file-load-order branch, so this covers non-Mutagen load-order validation.
        var tempXEdit = Path.GetTempFileName();
        try
        {
            var missingLoadOrder = @"C:\Users\Alice\AppData\Local\plugins.txt";
            var state = new AppState
            {
                CurrentGameType = GameType.Fallout3,
                LoadOrderPath = missingLoadOrder,
                XEditExecutablePath = tempXEdit,
                PluginsToClean = new List<PluginInfo>
                {
                    new() { FileName = "Test.esp", FullPath = "Test.esp" }
                }
            };

            var stateSubject = new BehaviorSubject<AppState>(state);
            _stateServiceMock.StateChanged.Returns(stateSubject);
            _stateServiceMock.CurrentState.Returns(state);

            var vm = new MainWindowViewModel(
                _configServiceMock,
                _stateServiceMock,
                _orchestratorMock,
                _loggerMock,
                _fileDialogMock,
                _messageDialogMock,
                _pluginServiceMock,
                _pluginLoadingServiceMock,
                _uiDispatcher);

            // Act
            await vm.Commands.StartCleaningCommand.ExecuteAsync(null);

            // Assert
            var error = vm.Commands.ValidationErrors.Single(e => e.Title == "Load order not found");
            error.Message.Should().Be("Load Order File (plugins.txt) is missing. Choose the current plugins.txt or loadorder.txt file.");
            AssertValidationErrorDoesNotContainFullPath(error, @"C:\Users\Alice");
        }
        finally
        {
            if (File.Exists(tempXEdit))
                File.Delete(tempXEdit);
        }
    }

    [Fact]
    public async Task StartCleaningCommand_ShouldNotValidateLoadOrder_WhenMutagenGameHasMissingLoadOrderFile()
    {
        // Arrange - Skyrim SE is Mutagen-supported, so the load-order file branch must not produce a false positive.
        var tempXEdit = Path.GetTempFileName();
        try
        {
            _pluginLoadingServiceMock.IsGameSupportedByMutagen(GameType.SkyrimSe).Returns(true);
            var state = new AppState
            {
                CurrentGameType = GameType.SkyrimSe,
                LoadOrderPath = @"C:\Users\Alice\AppData\Local\plugins.txt",
                XEditExecutablePath = tempXEdit,
                PluginsToClean = new List<PluginInfo>
                {
                    new() { FileName = "Test.esp", FullPath = "Test.esp" }
                }
            };

            var stateSubject = new BehaviorSubject<AppState>(state);
            _stateServiceMock.StateChanged.Returns(stateSubject);
            _stateServiceMock.CurrentState.Returns(state);

            var vm = new MainWindowViewModel(
                _configServiceMock,
                _stateServiceMock,
                _orchestratorMock,
                _loggerMock,
                _fileDialogMock,
                _messageDialogMock,
                _pluginServiceMock,
                _pluginLoadingServiceMock,
                _uiDispatcher);

            // Act
            await vm.Commands.StartCleaningCommand.ExecuteAsync(null);

            // Assert
            vm.Commands.ValidationErrors.Should().NotContain(e => e.Title.StartsWith("Load order", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(tempXEdit))
                File.Delete(tempXEdit);
        }
    }

    [Fact]
    public async Task StartCleaningCommand_ShouldShowSafeMo2Identifier_WhenMo2PathMissing()
    {
        // Arrange
        var tempXEdit = Path.GetTempFileName();
        try
        {
            var state = new AppState
            {
                XEditExecutablePath = tempXEdit,
                Mo2ModeEnabled = true,
                Mo2ExecutablePath = @"C:\Users\Alice\MO2\ModOrganizer.exe",
                PluginsToClean = new List<PluginInfo>
                {
                    new() { FileName = "Test.esp", FullPath = "Test.esp" }
                }
            };
            var stateSubject = new BehaviorSubject<AppState>(state);
            _stateServiceMock.StateChanged.Returns(stateSubject);
            _stateServiceMock.CurrentState.Returns(state);

            var vm = new MainWindowViewModel(
                _configServiceMock,
                _stateServiceMock,
                _orchestratorMock,
                _loggerMock,
                _fileDialogMock,
                _messageDialogMock,
                _pluginServiceMock,
                _pluginLoadingServiceMock,
                _uiDispatcher);

            // Act
            await vm.Commands.StartCleaningCommand.ExecuteAsync(null);

            // Assert
            var error = vm.Commands.ValidationErrors.Single(e => e.Title == "MO2 not found");
            error.Message.Should().Be("MO2 Path (ModOrganizer.exe) is missing. Choose ModOrganizer.exe or disable MO2 Mode.");
            AssertValidationErrorDoesNotContainFullPath(error, @"C:\Users\Alice");
        }
        finally
        {
            if (File.Exists(tempXEdit))
                File.Delete(tempXEdit);
        }
    }

    [Fact]
    public async Task StartCleaningAsync_WhenUnexpectedError_ShouldShowSafeDiagnosticCopy()
    {
        // Arrange - the exception contains representative path, command, exception, and stack-like sentinels.
        var tempFile = Path.GetTempFileName();
        try
        {
            var unsafeSentinel = @"C:\Users\Alice\Tools\SSEEdit.exe -QAC System.InvalidOperationException: boom at AutoQAC.Services.Cleaning Stack Trace";
            var vm = CreateViewModelWithValidState(tempFile);
            using var _ = vm.ShowProgressInteraction.RegisterHandler(_ => Task.FromResult(default(AutoQAC.Services.UI.Interactions.Unit)));
            vm.Configuration.XEditPath = tempFile;

            _orchestratorMock.StartCleaningAsync(
                    Arg.Any<TimeoutRetryCallback>(),
                    Arg.Any<BackupFailureCallback>(),
                    Arg.Any<CancellationToken>())
                .ThrowsAsync(new Exception(unsafeSentinel));

            // Act
            await vm.Commands.StartCleaningCommand.ExecuteAsync(null);

            // Assert
            var expectedMessage = "Cleaning failed. See the latest AutoQAC log for technical details.";
            var expectedDetails = "Technical details were written to the latest AutoQAC log.";
            vm.Commands.StatusText.Should().Be(expectedMessage);

            await _messageDialogMock.Received(1).ShowErrorAsync(
                "Cleaning Failed",
                expectedMessage,
                expectedDetails);

            AssertDoesNotContainUnsafeDiagnosticDetails(
                ["Cleaning Failed", expectedMessage, expectedDetails, vm.Commands.StatusText],
                unsafeSentinel);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task PreviewAsync_WhenUnexpectedError_ShouldShowSafeDiagnosticCopy()
    {
        // Arrange - the exception contains representative path, command, exception, and stack-like sentinels.
        var tempFile = Path.GetTempFileName();
        try
        {
            var unsafeSentinel = @"C:\Users\Alice\Tools\SSEEdit.exe -QAC System.InvalidOperationException: boom at AutoQAC.Services.Cleaning Stack Trace";
            var vm = CreateViewModelWithValidState(tempFile);
            vm.Configuration.XEditPath = tempFile;

            _orchestratorMock.RunDryRunAsync(Arg.Any<CancellationToken>())
                .ThrowsAsync(new Exception(unsafeSentinel));

            // Act
            await vm.Commands.PreviewCommand.ExecuteAsync(null);

            // Assert
            var expectedMessage = "Preview failed. See the latest AutoQAC log for technical details.";
            var expectedDetails = "Technical details were written to the latest AutoQAC log.";
            vm.Commands.StatusText.Should().Be(expectedMessage);

            await _messageDialogMock.Received(1).ShowErrorAsync(
                "Preview Failed",
                expectedMessage,
                expectedDetails);

            AssertDoesNotContainUnsafeDiagnosticDetails(
                ["Preview Failed", expectedMessage, expectedDetails, vm.Commands.StatusText],
                unsafeSentinel);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    private static void AssertDoesNotContainUnsafeDiagnosticDetails(IEnumerable<string?> userVisibleTexts, string unsafeSentinel)
    {
        var forbiddenFragments = new[]
        {
            @"C:\Users\Alice",
            "SSEEdit.exe -QAC",
            "System.InvalidOperationException",
            "AutoQAC.Services.Cleaning",
            "Stack Trace",
            unsafeSentinel
        };

        foreach (var text in userVisibleTexts)
        {
            foreach (var fragment in forbiddenFragments)
            {
                text.Should().NotContain(fragment);
            }
        }
    }

    private static void AssertValidationErrorDoesNotContainFullPath(ValidationError error, string forbiddenPathPrefix)
    {
        var userVisibleTexts = new[] { error.Title, error.Message, error.FixStep };
        foreach (var text in userVisibleTexts)
        {
            text.Should().NotContain(forbiddenPathPrefix);
            text.Should().NotContain("latest AutoQAC log", "simple missing-path validation should give direct fix guidance only");
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
            using var _ = vm.ShowProgressInteraction.RegisterHandler(_ => Task.FromResult(default(AutoQAC.Services.UI.Interactions.Unit)));
            vm.Configuration.XEditPath = tempFile;

            TimeoutRetryCallback? capturedCallback = null;
            _orchestratorMock.StartCleaningAsync(
                    Arg.Do<TimeoutRetryCallback?>(cb => capturedCallback = cb),
                    Arg.Any<BackupFailureCallback?>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            // Act
            await vm.Commands.StartCleaningCommand.ExecuteAsync(null);

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
            using var _ = vm.ShowProgressInteraction.RegisterHandler(_ => Task.FromResult(default(AutoQAC.Services.UI.Interactions.Unit)));
            vm.Configuration.XEditPath = tempFile;

            TimeoutRetryCallback? capturedCallback = null;
            _orchestratorMock.StartCleaningAsync(
                    Arg.Do<TimeoutRetryCallback?>(cb => capturedCallback = cb),
                    Arg.Any<BackupFailureCallback?>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            _messageDialogMock.ShowRetryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>())
                .Returns(true);

            await vm.Commands.StartCleaningCommand.ExecuteAsync(null);

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

    /// <summary>
    /// Verifies timeout retry callback data is projected to safe dialog copy before it crosses the user-facing D-07 boundary.
    /// </summary>
    [Fact]
    public async Task TimeoutCallback_WhenPluginNameContainsUnsafeDetails_ShouldShowSanitizedPluginName()
    {
        // Arrange - use valid state so ValidatePreClean passes
        var tempFile = Path.GetTempFileName();
        try
        {
            var vm = CreateViewModelWithValidState(tempFile);
            using var _ = vm.ShowProgressInteraction.RegisterHandler(_ => Task.FromResult(default(AutoQAC.Services.UI.Interactions.Unit)));
            vm.Configuration.XEditPath = tempFile;

            TimeoutRetryCallback? capturedCallback = null;
            _orchestratorMock.StartCleaningAsync(
                    Arg.Do<TimeoutRetryCallback?>(cb => capturedCallback = cb),
                    Arg.Any<BackupFailureCallback?>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            _messageDialogMock.ShowRetryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>())
                .Returns(true);

            await vm.Commands.StartCleaningCommand.ExecuteAsync(null);

            // Act - simulate a callback payload with path, command, quote, and backtick detail.
            capturedCallback.Should().NotBeNull();
            var result = await capturedCallback!("C:\\Users\\Alice\\Mods\\Bad\"Plugin` -QAC.esp", 300, 1);

            // Assert
            result.Should().BeTrue("ShowRetryAsync returned true");
            var dialogCall = _messageDialogMock.ReceivedCalls()
                .Single(call => call.GetMethodInfo().Name == nameof(IMessageDialogService.ShowRetryAsync));
            var message = (string)dialogCall.GetArguments()[1]!;

            message.Should().Contain("BadPlugin.esp", "D-07 requires safe plugin display copy in timeout dialogs");
            message.Should().NotContain(@"C:\Users\Alice");
            message.Should().NotContain("-QAC");
            message.Should().NotContain("\"");
            message.Should().NotContain("`");
            foreach (var sentinel in DiagnosticSentinels.UnsafeDiagnosticSentinels)
            {
                message.Should().NotContain(sentinel);
            }
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
            using var _ = vm.ShowProgressInteraction.RegisterHandler(_ => Task.FromResult(default(AutoQAC.Services.UI.Interactions.Unit)));
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

            await vm.Commands.StartCleaningCommand.ExecuteAsync(null);

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
