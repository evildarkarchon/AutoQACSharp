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
using Moq;
using ReactiveUI;

namespace AutoQAC.Tests.ViewModels;

/// <summary>
/// Tests for error dialog functionality in MainWindowViewModel.
/// </summary>
public sealed class ErrorDialogTests
{
    private readonly Mock<IConfigurationService> _configServiceMock;
    private readonly Mock<IStateService> _stateServiceMock;
    private readonly Mock<ICleaningOrchestrator> _orchestratorMock;
    private readonly Mock<ILoggingService> _loggerMock;
    private readonly Mock<IFileDialogService> _fileDialogMock;
    private readonly Mock<IMessageDialogService> _messageDialogMock;
    private readonly Mock<IPluginValidationService> _pluginServiceMock;
    private readonly Mock<IPluginLoadingService> _pluginLoadingServiceMock;

    public ErrorDialogTests()
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

        RxApp.MainThreadScheduler = Scheduler.Immediate;
    }

    private MainWindowViewModel CreateViewModel()
    {
        var stateSubject = new BehaviorSubject<AppState>(new AppState());
        _stateServiceMock.Setup(s => s.StateChanged).Returns(stateSubject);
        _stateServiceMock.Setup(s => s.CurrentState).Returns(new AppState());

        return new MainWindowViewModel(
            _configServiceMock.Object,
            _stateServiceMock.Object,
            _orchestratorMock.Object,
            _loggerMock.Object,
            _fileDialogMock.Object,
            _messageDialogMock.Object,
            _pluginServiceMock.Object,
            _pluginLoadingServiceMock.Object);
    }

    #region xEdit Not Configured Tests

    [Fact]
    public async Task StartCleaningCommand_ShouldShowErrorDialog_WhenXEditPathIsNull()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.XEditPath = null;

        // Act
        await vm.StartCleaningCommand.Execute();

        // Assert
        _messageDialogMock.Verify(
            m => m.ShowErrorAsync(
                "xEdit Not Configured",
                It.IsAny<string>(),
                It.IsAny<string?>()),
            Times.Once,
            "Should show error dialog for missing xEdit path");

        // Orchestrator should NOT be called
        _orchestratorMock.Verify(
            x => x.StartCleaningAsync(It.IsAny<TimeoutRetryCallback>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Orchestrator should not be called when xEdit is not configured");
    }

    [Fact]
    public async Task StartCleaningCommand_ShouldShowErrorDialog_WhenXEditPathIsEmpty()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.XEditPath = string.Empty;

        // Act
        await vm.StartCleaningCommand.Execute();

        // Assert
        _messageDialogMock.Verify(
            m => m.ShowErrorAsync(
                "xEdit Not Configured",
                It.IsAny<string>(),
                It.IsAny<string?>()),
            Times.Once);
    }

    [Fact]
    public async Task StartCleaningCommand_ShouldShowErrorDialog_WhenXEditFileNotFound()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.XEditPath = @"C:\NonExistent\xedit.exe"; // Path that doesn't exist

        // Act
        await vm.StartCleaningCommand.Execute();

        // Assert
        _messageDialogMock.Verify(
            m => m.ShowErrorAsync(
                "xEdit Not Found",
                It.IsAny<string>(),
                It.IsAny<string?>()),
            Times.Once,
            "Should show error dialog when xEdit file doesn't exist");
    }

    #endregion

    #region Invalid Load Order Tests

    [Fact]
    public async Task ConfigureLoadOrderCommand_ShouldShowErrorDialog_WhenFileNotFound()
    {
        // Arrange
        var vm = CreateViewModel();
        var nonExistentPath = @"C:\NonExistent\plugins.txt";

        _fileDialogMock.Setup(x => x.OpenFileDialogAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>()))
            .ReturnsAsync(nonExistentPath);

        // Act
        await vm.ConfigureLoadOrderCommand.Execute();

        // Assert
        _messageDialogMock.Verify(
            m => m.ShowErrorAsync(
                "File Not Found",
                It.IsAny<string>(),
                It.IsAny<string?>()),
            Times.Once);
    }

    [Fact]
    public async Task ConfigureLoadOrderCommand_ShouldShowWarningDialog_WhenNoPluginsFound()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            var vm = CreateViewModel();

            _fileDialogMock.Setup(x => x.OpenFileDialogAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>()))
                .ReturnsAsync(tempFile);

            _configServiceMock.Setup(x => x.LoadUserConfigAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UserConfiguration { LoadOrder = new(), XEdit = new(), ModOrganizer = new(), Settings = new() });

            // Return empty list
            _pluginServiceMock.Setup(x => x.GetPluginsFromLoadOrderAsync(tempFile, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<PluginInfo>());

            // Act
            await vm.ConfigureLoadOrderCommand.Execute();

            // Assert
            _messageDialogMock.Verify(
                m => m.ShowWarningAsync(
                    "No Plugins Found",
                    It.IsAny<string>(),
                    It.IsAny<string?>()),
                Times.Once);
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

            _fileDialogMock.Setup(x => x.OpenFileDialogAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>()))
                .ReturnsAsync(tempFile);

            // Throw IOException
            _pluginServiceMock.Setup(x => x.GetPluginsFromLoadOrderAsync(tempFile, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("File in use"));

            // Act
            await vm.ConfigureLoadOrderCommand.Execute();

            // Assert
            _messageDialogMock.Verify(
                m => m.ShowErrorAsync(
                    "Read Error",
                    It.IsAny<string>(),
                    It.IsAny<string?>()),
                Times.Once);
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
    public async Task StartCleaningCommand_ShouldShowErrorDialog_WhenConfigurationInvalid()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            var vm = CreateViewModel();
            vm.XEditPath = tempFile;

            _orchestratorMock.Setup(x => x.StartCleaningAsync(It.IsAny<TimeoutRetryCallback>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Configuration is invalid"));

            // Act
            await vm.StartCleaningCommand.Execute();

            // Assert
            _messageDialogMock.Verify(
                m => m.ShowErrorAsync(
                    "Configuration Invalid",
                    It.IsAny<string>(),
                    It.IsAny<string?>()),
                Times.Once);
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
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            var vm = CreateViewModel();
            vm.XEditPath = tempFile;

            _orchestratorMock.Setup(x => x.StartCleaningAsync(It.IsAny<TimeoutRetryCallback>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Unexpected error"));

            // Act
            await vm.StartCleaningCommand.Execute();

            // Assert
            _messageDialogMock.Verify(
                m => m.ShowErrorAsync(
                    "Cleaning Failed",
                    It.IsAny<string>(),
                    It.IsAny<string?>()),
                Times.Once);
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
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            var vm = CreateViewModel();
            vm.XEditPath = tempFile;

            TimeoutRetryCallback? capturedCallback = null;
            _orchestratorMock.Setup(x => x.StartCleaningAsync(It.IsAny<TimeoutRetryCallback>(), It.IsAny<CancellationToken>()))
                .Callback<TimeoutRetryCallback?, CancellationToken>((callback, ct) => capturedCallback = callback)
                .Returns(Task.CompletedTask);

            // Act
            await vm.StartCleaningCommand.Execute();

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
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            var vm = CreateViewModel();
            vm.XEditPath = tempFile;

            TimeoutRetryCallback? capturedCallback = null;
            _orchestratorMock.Setup(x => x.StartCleaningAsync(It.IsAny<TimeoutRetryCallback>(), It.IsAny<CancellationToken>()))
                .Callback<TimeoutRetryCallback?, CancellationToken>((callback, ct) => capturedCallback = callback)
                .Returns(Task.CompletedTask);

            _messageDialogMock.Setup(m => m.ShowRetryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
                .ReturnsAsync(true);

            await vm.StartCleaningCommand.Execute();

            // Act - simulate timeout callback being invoked
            capturedCallback.Should().NotBeNull();
            var result = await capturedCallback!("TestPlugin.esp", 300, 1);

            // Assert
            _messageDialogMock.Verify(
                m => m.ShowRetryAsync(
                    "Plugin Timeout",
                    It.Is<string>(s => s.Contains("TestPlugin.esp")),
                    It.IsAny<string?>()),
                Times.Once);

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
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            var vm = CreateViewModel();
            vm.XEditPath = tempFile;

            TimeoutRetryCallback? capturedCallback = null;
            _orchestratorMock.Setup(x => x.StartCleaningAsync(It.IsAny<TimeoutRetryCallback>(), It.IsAny<CancellationToken>()))
                .Callback<TimeoutRetryCallback?, CancellationToken>((callback, ct) => capturedCallback = callback)
                .Returns(Task.CompletedTask);

            // User cancels retry
            _messageDialogMock.Setup(m => m.ShowRetryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
                .ReturnsAsync(false);

            await vm.StartCleaningCommand.Execute();

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
