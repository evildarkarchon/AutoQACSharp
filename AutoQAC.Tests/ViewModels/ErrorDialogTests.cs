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
    private readonly ICleaningSession _cleaningSessionMock;
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
        _configServiceMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new UserConfiguration { LoadOrder = new(), XEdit = new(), ModOrganizer = new(), Settings = new() });
        _configServiceMock.GetSkipListAsync(
                Arg.Any<GameType>(),
                Arg.Any<GameVariant>(),
                Arg.Any<CancellationToken>())
            .Returns([]);
    }

    private IPluginRefreshCoordinator CreateRefreshCoordinator()
    {
        var gameDetectionService = Substitute.For<AutoQAC.Services.GameDetection.IGameDetectionService>();
        gameDetectionService
            .DetectVariant(Arg.Any<GameType>(), Arg.Any<IReadOnlyList<string>>())
            .Returns(GameVariant.None);
        return new PluginRefreshCoordinator(
            _pluginLoadingServiceMock,
            new NoOpPluginIssueApproximationService(),
            _stateServiceMock,
            new StateServicePluginRefreshPublication(_stateServiceMock),
            CreateCapabilityPolicy(),
            _configServiceMock,
            new SkipListPolicy(_configServiceMock, gameDetectionService),
            Substitute.For<AutoQAC.Services.MO2.IMo2InstanceService>(),
            _loggerMock);
    }

    private IPluginRefreshCapabilityPolicy CreateCapabilityPolicy() =>
        new PluginRefreshCapabilityPolicy(_pluginLoadingServiceMock);

    private sealed class NoOpPluginIssueApproximationService : IPluginIssueApproximationService
    {
        public Task<IReadOnlyList<PluginIssueApproximationResult>> GetApproximationsAsync(
            GameType gameType,
            string dataFolder,
            Action<PluginIssueApproximationResult>? onApproximationReady = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PluginIssueApproximationResult>>([]);

        public Task<IReadOnlyList<PluginIssueApproximationResult>> GetApproximationsAsync(
            GameType gameType,
            string baseDataFolder,
            IReadOnlyList<string> orderedPluginNames,
            Func<Mutagen.Bethesda.Plugins.ModKey, string?> pathResolver,
            Action<PluginIssueApproximationResult>? onApproximationReady = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PluginIssueApproximationResult>>([]);
    }

    private MainWindowViewModel CreateViewModel()
    {
        var stateSubject = new BehaviorSubject<AppState>(new AppState());
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(new AppState());

        return new MainWindowViewModel(
            _configServiceMock,
            _stateServiceMock,
            _cleaningSessionMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock,
            _uiDispatcher,
            CreateRefreshCoordinator(),
            CreateCapabilityPolicy());
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
            _cleaningSessionMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock,
            _uiDispatcher,
            CreateRefreshCoordinator(),
            CreateCapabilityPolicy());
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
        await _cleaningSessionMock.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
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
    public async Task StartCleaningCommand_ShouldShowInlineValidation_WhenXEditPathIsWhitespace()
    {
        // Arrange - whitespace-only paths are not valid configured executable paths.
        var stateWithWhitespaceXEdit = new AppState
        {
            XEditExecutablePath = "   ",
            PluginsToClean = new List<PluginInfo>
            {
                new() { FileName = "Test.esp", FullPath = "Test.esp" }
            }
        };
        var stateSubject = new BehaviorSubject<AppState>(stateWithWhitespaceXEdit);
        _stateServiceMock.StateChanged.Returns(stateSubject);
        _stateServiceMock.CurrentState.Returns(stateWithWhitespaceXEdit);

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
            CreateRefreshCoordinator(),
            CreateCapabilityPolicy());

        // Act
        await vm.Commands.StartCleaningCommand.ExecuteAsync(null);

        // Assert
        vm.Commands.HasValidationErrors.Should().BeTrue();
        vm.Commands.ValidationErrors.Should().ContainSingle(e => e.Title == "xEdit not configured");
        await _cleaningSessionMock.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
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
            _cleaningSessionMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock,
            _uiDispatcher,
            CreateRefreshCoordinator(),
            CreateCapabilityPolicy());

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
            _cleaningSessionMock,
            _loggerMock,
            _fileDialogMock,
            _messageDialogMock,
            _pluginServiceMock,
            _pluginLoadingServiceMock,
            _uiDispatcher,
            CreateRefreshCoordinator(),
            CreateCapabilityPolicy());

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
                _cleaningSessionMock,
                _loggerMock,
                _fileDialogMock,
                _messageDialogMock,
                _pluginServiceMock,
                _pluginLoadingServiceMock,
                _uiDispatcher,
                CreateRefreshCoordinator(),
                CreateCapabilityPolicy());

            // Act
            await vm.Commands.StartCleaningCommand.ExecuteAsync(null);

            // Assert
            vm.Commands.HasValidationErrors.Should().BeTrue();
            vm.Commands.ValidationErrors.Should().Contain(e => e.Title == "Load order not configured");
            await _cleaningSessionMock.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
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
            vm.Configuration.SelectedGame = GameType.FalloutNewVegas;

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
            vm.Configuration.SelectedGame = GameType.FalloutNewVegas;

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

            _cleaningSessionMock.StartAsync(Arg.Any<CancellationToken>())
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

            _cleaningSessionMock.StartAsync(Arg.Any<CancellationToken>())
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
                _cleaningSessionMock,
                _loggerMock,
                _fileDialogMock,
                _messageDialogMock,
                _pluginServiceMock,
                _pluginLoadingServiceMock,
                _uiDispatcher,
                CreateRefreshCoordinator(),
                CreateCapabilityPolicy());

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
                _cleaningSessionMock,
                _loggerMock,
                _fileDialogMock,
                _messageDialogMock,
                _pluginServiceMock,
                _pluginLoadingServiceMock,
                _uiDispatcher,
                CreateRefreshCoordinator(),
                CreateCapabilityPolicy());

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
                _cleaningSessionMock,
                _loggerMock,
                _fileDialogMock,
                _messageDialogMock,
                _pluginServiceMock,
                _pluginLoadingServiceMock,
                _uiDispatcher,
                CreateRefreshCoordinator(),
                CreateCapabilityPolicy());

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
    public async Task StartCleaningCommand_ShouldShowSingleMo2ExecutableValidation_WhenMo2PathMissing()
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
                Mo2Profile = "Default",
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
                _cleaningSessionMock,
                _loggerMock,
                _fileDialogMock,
                _messageDialogMock,
                _pluginServiceMock,
                _pluginLoadingServiceMock,
                _uiDispatcher,
                CreateRefreshCoordinator(),
                CreateCapabilityPolicy());

            // Act
            await vm.Commands.StartCleaningCommand.ExecuteAsync(null);

            // Assert
            var error = vm.Commands.ValidationErrors.Should().ContainSingle().Subject;
            error.Title.Should().Be("MO2 not found");
            error.Message.Should().Be("MO2 Path (ModOrganizer.exe) is missing. Choose ModOrganizer.exe or disable MO2 Mode.");
            AssertValidationErrorDoesNotContainFullPath(error, @"C:\Users\Alice");
            await _cleaningSessionMock.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
        }
        finally
        {
            if (File.Exists(tempXEdit))
                File.Delete(tempXEdit);
        }
    }

    [Fact]
    public async Task StartCommand_WhenUnexpectedError_ShouldShowSafeDiagnosticCopy()
    {
        // Arrange - the exception contains representative path, command, exception, and stack-like sentinels.
        var tempFile = Path.GetTempFileName();
        try
        {
            var unsafeSentinel = @"C:\Users\Alice\Tools\SSEEdit.exe -QAC System.InvalidOperationException: boom at AutoQAC.Services.Cleaning Stack Trace";
            var vm = CreateViewModelWithValidState(tempFile);
            using var _ = vm.ShowProgressInteraction.RegisterHandler(_ => Task.FromResult(default(AutoQAC.Services.UI.Interactions.Unit)));
            vm.Configuration.XEditPath = tempFile;

            _cleaningSessionMock.StartAsync(Arg.Any<CancellationToken>())
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

            _cleaningSessionMock.PreviewAsync(Arg.Any<CancellationToken>())
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

}
