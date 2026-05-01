using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.GameDetection;
using AutoQAC.Services.Process;
using AutoQAC.Services.State;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AutoQAC.Tests.Services;

public sealed class CleaningServiceTests
{
    private readonly IGameDetectionService _mockGameDetection;
    private readonly IStateService _mockState;
    private readonly ILoggingService _mockLogger;
    private readonly IProcessExecutionService _mockProcess;
    private readonly IXEditCommandBuilder _mockCommandBuilder;

    public CleaningServiceTests()
    {
        _mockGameDetection = Substitute.For<IGameDetectionService>();
        _mockState = Substitute.For<IStateService>();
        _mockLogger = Substitute.For<ILoggingService>();
        _mockProcess = Substitute.For<IProcessExecutionService>();
        _mockCommandBuilder = Substitute.For<IXEditCommandBuilder>();
    }

    [Fact]
    public async Task CleanPluginAsync_ShouldCallProcessAndReturnSuccess()
    {
        // Arrange
        var service = new CleaningService(
            _mockGameDetection,
            _mockState,
            _mockLogger,
            _mockProcess,
            _mockCommandBuilder);

        var plugin = new PluginInfo
        {
            FileName = "Mod.esp",
            FullPath = "Mod.esp",
            DetectedGameType = GameType.SkyrimSe,
            IsInSkipList = false
        };

        // Mock State
        var appState = new AppState { CurrentGameType = GameType.SkyrimSe };
        _mockState.CurrentState.Returns(appState);

        // Mock Command Builder
        var startInfo = new System.Diagnostics.ProcessStartInfo("xEdit.exe");
        _mockCommandBuilder.BuildCommand(plugin, GameType.SkyrimSe)
            .Returns(startInfo);

        // Mock Process Execution
        var processResult = new ProcessResult
        {
            ExitCode = 0,
            TimedOut = false
        };
        _mockProcess.ExecuteAsync(startInfo, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(processResult);

        // Act
        var result = await service.CleanPluginAsync(plugin);

        // Assert
        result.Success.Should().BeTrue();
        result.Status.Should().Be(CleaningStatus.Cleaned);
        result.Statistics.Should().BeNull("CleaningService no longer parses output; orchestrator handles log parsing");

        await _mockProcess.Received(1).ExecuteAsync(startInfo, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CleanPluginAsync_WhenSkipped_ShouldReturnSkipped()
    {
        // Arrange
        var service = new CleaningService(
            _mockGameDetection,
            _mockState,
            _mockLogger,
            _mockProcess,
            _mockCommandBuilder);

        var plugin = new PluginInfo
        {
            FileName = "Mod.esp",
            FullPath = "Mod.esp",
            DetectedGameType = GameType.SkyrimSe,
            IsInSkipList = true // SKIPPED
        };

        // Act
        var result = await service.CleanPluginAsync(plugin);

        // Assert
        result.Success.Should().BeTrue();
        result.Status.Should().Be(CleaningStatus.Skipped);
        await _mockProcess.DidNotReceive().ExecuteAsync(Arg.Any<System.Diagnostics.ProcessStartInfo>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    #region Error Path Tests

    /// <summary>
    /// Direct xEdit launch diagnostics should use safe structured fields and leave command construction untouched.
    /// </summary>
    [Fact]
    public async Task CleanPluginAsync_WhenDirectLaunchSucceeds_ShouldLogSafeFieldsAndPreserveProcessStartInfo()
    {
        // Arrange
        var service = new CleaningService(
            _mockGameDetection,
            _mockState,
            _mockLogger,
            _mockProcess,
            _mockCommandBuilder);

        var plugin = new PluginInfo
        {
            FileName = "Plugin.esp",
            FullPath = @"C:\Games\Skyrim\Data\Plugin.esp",
            DetectedGameType = GameType.SkyrimSe,
            IsInSkipList = false
        };

        var appState = new AppState
        {
            CurrentGameType = GameType.SkyrimSe,
            Mo2ModeEnabled = false,
            CleaningTimeout = 300
        };
        _mockState.CurrentState.Returns(appState);
        _mockGameDetection.GetGameDisplayName(GameType.SkyrimSe).Returns("Skyrim SE");

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = @"C:\Users\Alice\Tools\SSEEdit.exe",
            WorkingDirectory = @"C:\Users\Alice\Tools"
        };
        startInfo.ArgumentList.Add("-QAC");
        startInfo.ArgumentList.Add("-autoload");
        startInfo.ArgumentList.Add("Plugin.esp");
        _mockCommandBuilder.BuildCommand(plugin, GameType.SkyrimSe).Returns(startInfo);

        System.Diagnostics.ProcessStartInfo? capturedStartInfo = null;
        _mockProcess.ExecuteAsync(
                Arg.Do<System.Diagnostics.ProcessStartInfo>(value => capturedStartInfo = value),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<System.Diagnostics.Process>?>(),
                Arg.Any<string?>())
            .Returns(new ProcessResult { ExitCode = 0 });

        // Act
        var result = await service.CleanPluginAsync(plugin);

        // Assert
        result.Success.Should().BeTrue();
        capturedStartInfo.Should().BeSameAs(startInfo);
        capturedStartInfo!.FileName.Should().Be(@"C:\Users\Alice\Tools\SSEEdit.exe");
        capturedStartInfo.WorkingDirectory.Should().Be(@"C:\Users\Alice\Tools");
        capturedStartInfo.Arguments.Should().BeEmpty();
        capturedStartInfo.ArgumentList.Should().Equal("-QAC", "-autoload", "Plugin.esp");

        _mockLogger.Received(1).Information(
            "Starting {Operation} launch: mode={LaunchMode}, game={Game}, plugin={Plugin}, argumentCount={ArgumentCount}, status={Status}",
            "QuickAutoClean",
            "direct xEdit",
            "Skyrim SE",
            "Plugin.esp",
            3,
            "Starting");
        _mockLogger.Received(1).Information(
            "Completed {Operation} launch: mode={LaunchMode}, game={Game}, plugin={Plugin}, argumentCount={ArgumentCount}, status={Status}, reason={Reason}",
            "QuickAutoClean",
            "direct xEdit",
            "Skyrim SE",
            "Plugin.esp",
            3,
            "Succeeded",
            "ProcessExited");

        AssertCapturedLogTextExcludes(@"C:\Users\Alice", @"C:\Games\Skyrim", "-QAC", "-autoload");
    }

    /// <summary>
    /// MO2 launch diagnostics should not log the nested xEdit payload while preserving the MO2 argv contract.
    /// </summary>
    [Fact]
    public async Task CleanPluginAsync_WhenMo2LaunchFails_ShouldLogSafeFieldsAndPreserveNestedPayload()
    {
        // Arrange
        var service = new CleaningService(
            _mockGameDetection,
            _mockState,
            _mockLogger,
            _mockProcess,
            _mockCommandBuilder);

        var plugin = new PluginInfo
        {
            FileName = "Plugin.esp",
            FullPath = @"C:\Games\Skyrim\Data\Plugin.esp",
            DetectedGameType = GameType.SkyrimSe,
            IsInSkipList = false
        };

        var appState = new AppState
        {
            CurrentGameType = GameType.SkyrimSe,
            Mo2ModeEnabled = true,
            CleaningTimeout = 300
        };
        _mockState.CurrentState.Returns(appState);
        _mockGameDetection.GetGameDisplayName(GameType.SkyrimSe).Returns("Skyrim SE");

        const string nestedPayload = @"-QAC -autoload C:\Games\Skyrim\Data\Plugin.esp";
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = @"C:\Users\Alice\MO2\ModOrganizer.exe"
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add(@"C:\Users\Alice\Tools\SSEEdit.exe");
        startInfo.ArgumentList.Add("-a");
        startInfo.ArgumentList.Add(nestedPayload);
        _mockCommandBuilder.BuildCommand(plugin, GameType.SkyrimSe).Returns(startInfo);

        System.Diagnostics.ProcessStartInfo? capturedStartInfo = null;
        _mockProcess.ExecuteAsync(
                Arg.Do<System.Diagnostics.ProcessStartInfo>(value => capturedStartInfo = value),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<System.Diagnostics.Process>?>(),
                Arg.Any<string?>())
            .Returns(new ProcessResult { ExitCode = 1 });

        // Act
        var result = await service.CleanPluginAsync(plugin);

        // Assert
        result.Success.Should().BeFalse();
        capturedStartInfo.Should().BeSameAs(startInfo);
        capturedStartInfo!.FileName.Should().Be(@"C:\Users\Alice\MO2\ModOrganizer.exe");
        capturedStartInfo.ArgumentList.Should().Equal(
            "run",
            @"C:\Users\Alice\Tools\SSEEdit.exe",
            "-a",
            nestedPayload);

        _mockLogger.Received(1).Information(
            "Starting {Operation} launch: mode={LaunchMode}, game={Game}, plugin={Plugin}, argumentCount={ArgumentCount}, status={Status}",
            "QuickAutoClean",
            "MO2",
            "Skyrim SE",
            "Plugin.esp",
            4,
            "Starting");
        _mockLogger.Received(1).Warning(
            "Completed {Operation} launch: mode={LaunchMode}, game={Game}, plugin={Plugin}, argumentCount={ArgumentCount}, status={Status}, reason={Reason}",
            "QuickAutoClean",
            "MO2",
            "Skyrim SE",
            "Plugin.esp",
            4,
            "Failed",
            "ExitCode");

        AssertCapturedLogTextExcludes(@"C:\Users\Alice", @"C:\Games\Skyrim", "-QAC", "-autoload", nestedPayload);
    }

    /// <summary>
    /// Verifies that direct xEdit command-build failures report a concise user-facing failure
    /// without exposing configured executable paths or attempting to start a process.
    /// </summary>
    [Fact]
    public async Task CleanPluginAsync_WhenDirectCommandBuildFails_ShouldReturnSafeFailureWithoutStartingProcess()
    {
        // Arrange
        var service = new CleaningService(
            _mockGameDetection,
            _mockState,
            _mockLogger,
            _mockProcess,
            _mockCommandBuilder);

        var plugin = new PluginInfo
        {
            FileName = "NestedPayload Probe.esp",
            FullPath = "NestedPayload Probe.esp",
            DetectedGameType = GameType.SkyrimSe,
            IsInSkipList = false
        };

        const string xEditPath = @"C:\Tools With Spaces\SSEEdit.exe";
        const string mo2Path = @"C:\MO2 With Spaces\ModOrganizer.exe";
        const string syntheticNestedPayload = "run SSEEdit.exe -a -autoload NestedPayload Probe.esp";

        var appState = new AppState
        {
            CurrentGameType = GameType.SkyrimSe,
            XEditExecutablePath = xEditPath,
            Mo2ExecutablePath = mo2Path,
            Mo2ModeEnabled = false
        };
        _mockState.CurrentState.Returns(appState);

        // Command builder returns null - simulating build failure
        _mockCommandBuilder.BuildCommand(plugin, GameType.SkyrimSe)
            .Returns((System.Diagnostics.ProcessStartInfo?)null);

        // Act
        var result = await service.CleanPluginAsync(plugin);

        // Assert
        result.Success.Should().BeFalse("cleaning should fail when command cannot be built");
        result.Status.Should().Be(CleaningStatus.Failed);
        result.Message.Should().Contain(plugin.FileName);
        result.Message.Should().Contain("direct xEdit");
        result.Message.Should().Contain("No process was started");
        result.Message.Should().Contain("See logs for technical details");
        result.Message.Should().NotContain(xEditPath);
        result.Message.Should().NotContain(mo2Path);
        result.Message.Should().NotContain("run ");
        result.Message.Should().NotContain("-a");
        result.Message.Should().NotContain(syntheticNestedPayload);

        // Process should never be called since command building failed
        await _mockProcess.DidNotReceive().ExecuteAsync(
            Arg.Any<System.Diagnostics.ProcessStartInfo>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<Action<System.Diagnostics.Process>?>(),
            Arg.Any<string?>());
    }

    /// <summary>
    /// Verifies that MO2 command-build failures identify MO2 mode while keeping the
    /// user-facing result free of executable paths, nested payloads, and process launches.
    /// </summary>
    [Fact]
    public async Task CleanPluginAsync_WhenMo2CommandBuildFails_ShouldReturnSafeFailureWithoutStartingProcess()
    {
        // Arrange
        var service = new CleaningService(
            _mockGameDetection,
            _mockState,
            _mockLogger,
            _mockProcess,
            _mockCommandBuilder);

        var plugin = new PluginInfo
        {
            FileName = "NestedPayload Probe.esp",
            FullPath = "NestedPayload Probe.esp",
            DetectedGameType = GameType.SkyrimSe,
            IsInSkipList = false
        };

        const string xEditPath = @"C:\Tools With Spaces\SSEEdit.exe";
        const string mo2Path = @"C:\MO2 With Spaces\ModOrganizer.exe";
        const string syntheticNestedPayload = "run SSEEdit.exe -a -autoload NestedPayload Probe.esp";

        var appState = new AppState
        {
            CurrentGameType = GameType.SkyrimSe,
            XEditExecutablePath = xEditPath,
            Mo2ExecutablePath = mo2Path,
            Mo2ModeEnabled = true
        };
        _mockState.CurrentState.Returns(appState);

        _mockCommandBuilder.BuildCommand(plugin, GameType.SkyrimSe)
            .Returns((System.Diagnostics.ProcessStartInfo?)null);

        // Act
        var result = await service.CleanPluginAsync(plugin);

        // Assert
        result.Success.Should().BeFalse("cleaning should fail when the MO2 command cannot be built");
        result.Status.Should().Be(CleaningStatus.Failed);
        result.Message.Should().Contain(plugin.FileName);
        result.Message.Should().Contain("MO2");
        result.Message.Should().Contain("No process was started");
        result.Message.Should().Contain("See logs for technical details");
        result.Message.Should().NotContain(xEditPath);
        result.Message.Should().NotContain(mo2Path);
        result.Message.Should().NotContain("run ");
        result.Message.Should().NotContain("-a");
        result.Message.Should().NotContain(syntheticNestedPayload);

        await _mockProcess.DidNotReceive().ExecuteAsync(
            Arg.Any<System.Diagnostics.ProcessStartInfo>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<Action<System.Diagnostics.Process>?>(),
            Arg.Any<string?>());
    }

    /// <summary>
    /// Verifies that mocked launch-start failures remain on the existing failed-flow path
    /// and do not disclose configured executable paths or command-line payloads.
    /// </summary>
    [Fact]
    public async Task CleanPluginAsync_WhenMockedLaunchStartFails_ShouldReturnExistingConciseFailure()
    {
        // Arrange
        var service = new CleaningService(
            _mockGameDetection,
            _mockState,
            _mockLogger,
            _mockProcess,
            _mockCommandBuilder);

        var plugin = new PluginInfo
        {
            FileName = "LaunchStartFailure.esp",
            FullPath = "LaunchStartFailure.esp",
            DetectedGameType = GameType.SkyrimSe,
            IsInSkipList = false
        };

        const string xEditPath = @"C:\Tools With Spaces\SSEEdit.exe";
        const string mo2Path = @"C:\MO2 With Spaces\ModOrganizer.exe";
        const string syntheticNestedPayload = "run SSEEdit.exe -a -autoload LaunchStartFailure.esp";

        var appState = new AppState
        {
            CurrentGameType = GameType.SkyrimSe,
            XEditExecutablePath = xEditPath,
            Mo2ExecutablePath = mo2Path,
            Mo2ModeEnabled = true
        };
        _mockState.CurrentState.Returns(appState);

        var startInfo = new System.Diagnostics.ProcessStartInfo(mo2Path);
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add(xEditPath);
        startInfo.ArgumentList.Add("-a");
        startInfo.ArgumentList.Add(syntheticNestedPayload);
        _mockCommandBuilder.BuildCommand(plugin, GameType.SkyrimSe)
            .Returns(startInfo);

        _mockProcess.ExecuteAsync(
                startInfo,
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<System.Diagnostics.Process>?>(),
                Arg.Any<string?>())
            .Returns(new ProcessResult { ExitCode = -1 });

        // Act
        var result = await service.CleanPluginAsync(plugin);

        // Assert
        result.Success.Should().BeFalse("a mocked launch-start failure should use the existing failed flow");
        result.Status.Should().Be(CleaningStatus.Failed);
        result.Message.Should().Be("xEdit exited with code -1");
        result.Message.Should().NotContain(xEditPath);
        result.Message.Should().NotContain(mo2Path);
        result.Message.Should().NotContain("run ");
        result.Message.Should().NotContain("-a");
        result.Message.Should().NotContain(syntheticNestedPayload);
    }

    /// <summary>
    /// Verifies that CleanPluginAsync returns Failed status when the xEdit process
    /// times out during execution.
    /// </summary>
    [Fact]
    public async Task CleanPluginAsync_WhenProcessTimesOut_ShouldReturnFailed()
    {
        // Arrange
        var service = new CleaningService(
            _mockGameDetection,
            _mockState,
            _mockLogger,
            _mockProcess,
            _mockCommandBuilder);

        var plugin = new PluginInfo
        {
            FileName = "SlowPlugin.esp",
            FullPath = "SlowPlugin.esp",
            DetectedGameType = GameType.SkyrimSe,
            IsInSkipList = false
        };

        // Configure state with timeout setting
        var appState = new AppState
        {
            CurrentGameType = GameType.SkyrimSe,
            CleaningTimeout = 300 // 5 minute timeout
        };
        _mockState.CurrentState.Returns(appState);

        // Mock command builder to return valid command
        var startInfo = new System.Diagnostics.ProcessStartInfo("xEdit.exe");
        _mockCommandBuilder.BuildCommand(plugin, GameType.SkyrimSe)
            .Returns(startInfo);

        // Mock process to return timed out result
        var processResult = new ProcessResult
        {
            ExitCode = -1,
            TimedOut = true
        };
        _mockProcess.ExecuteAsync(startInfo, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(processResult);

        // Act
        var result = await service.CleanPluginAsync(plugin);

        // Assert
        result.Success.Should().BeFalse("timed out cleaning should be marked as failed");
        result.Status.Should().Be(CleaningStatus.Failed);
        result.Message!.ToLower().Should().Contain("timed out");
    }

    /// <summary>
    /// Verifies that CleanPluginAsync returns Failed status when the xEdit process
    /// exits with a non-zero exit code.
    /// </summary>
    [Fact]
    public async Task CleanPluginAsync_WhenProcessExitsWithError_ShouldReturnFailed()
    {
        // Arrange
        var service = new CleaningService(
            _mockGameDetection,
            _mockState,
            _mockLogger,
            _mockProcess,
            _mockCommandBuilder);

        var plugin = new PluginInfo
        {
            FileName = "CorruptMod.esp",
            FullPath = "CorruptMod.esp",
            DetectedGameType = GameType.SkyrimSe,
            IsInSkipList = false
        };

        // Configure state
        var appState = new AppState { CurrentGameType = GameType.SkyrimSe };
        _mockState.CurrentState.Returns(appState);

        // Mock command builder
        var startInfo = new System.Diagnostics.ProcessStartInfo("xEdit.exe");
        _mockCommandBuilder.BuildCommand(plugin, GameType.SkyrimSe)
            .Returns(startInfo);

        // Mock process to return error exit code
        var processResult = new ProcessResult
        {
            ExitCode = 1, // Non-zero indicates error
            TimedOut = false
        };
        _mockProcess.ExecuteAsync(startInfo, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(processResult);

        // Act
        var result = await service.CleanPluginAsync(plugin);

        // Assert
        result.Success.Should().BeFalse("process error should mark cleaning as failed");
        result.Status.Should().Be(CleaningStatus.Failed);
        result.Message.Should().Contain("exited with code 1");
    }

    /// <summary>
    /// Verifies that CleanPluginAsync handles GameType.Unknown by falling back
    /// to the plugin's detected game type.
    /// </summary>
    [Fact]
    public async Task CleanPluginAsync_WhenGameTypeUnknown_ShouldUsePluginDetectedType()
    {
        // Arrange
        var service = new CleaningService(
            _mockGameDetection,
            _mockState,
            _mockLogger,
            _mockProcess,
            _mockCommandBuilder);

        var plugin = new PluginInfo
        {
            FileName = "Mod.esp",
            FullPath = "Mod.esp",
            DetectedGameType = GameType.Fallout4, // Plugin has detected type
            IsInSkipList = false
        };

        // Configure state with Unknown game type
        var appState = new AppState { CurrentGameType = GameType.Unknown };
        _mockState.CurrentState.Returns(appState);

        // Mock command builder - verify it's called with Fallout4 (plugin's type)
        var startInfo = new System.Diagnostics.ProcessStartInfo("xEdit.exe");
        _mockCommandBuilder.BuildCommand(plugin, GameType.Fallout4)
            .Returns(startInfo);

        // Mock process execution
        var processResult = new ProcessResult
        {
            ExitCode = 0,
            TimedOut = false
        };
        _mockProcess.ExecuteAsync(startInfo, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(processResult);

        // Act
        var result = await service.CleanPluginAsync(plugin);

        // Assert
        result.Success.Should().BeTrue();

        // Verify command builder was called with plugin's detected game type
        _mockCommandBuilder.Received(1).BuildCommand(plugin, GameType.Fallout4);
        _mockGameDetection.Received(1).GetGameDisplayName(GameType.Fallout4);
    }

    /// <summary>
    /// Verifies that CleanPluginAsync returns Skipped status with appropriate message
    /// when the operation is cancelled via CancellationToken.
    /// </summary>
    [Fact]
    public async Task CleanPluginAsync_WhenCancelled_ShouldReturnSkippedWithCancelMessage()
    {
        // Arrange
        var service = new CleaningService(
            _mockGameDetection,
            _mockState,
            _mockLogger,
            _mockProcess,
            _mockCommandBuilder);

        var plugin = new PluginInfo
        {
            FileName = "Mod.esp",
            FullPath = "Mod.esp",
            DetectedGameType = GameType.SkyrimSe,
            IsInSkipList = false
        };

        // Configure state
        var appState = new AppState { CurrentGameType = GameType.SkyrimSe };
        _mockState.CurrentState.Returns(appState);

        // Mock command builder
        var startInfo = new System.Diagnostics.ProcessStartInfo("xEdit.exe");
        _mockCommandBuilder.BuildCommand(plugin, GameType.SkyrimSe)
            .Returns(startInfo);

        // Mock process to throw OperationCanceledException
        _mockProcess.ExecuteAsync(startInfo, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await service.CleanPluginAsync(plugin);

        // Assert
        result.Success.Should().BeFalse("cancelled operation is not successful");
        result.Status.Should().Be(CleaningStatus.Skipped);
        result.Message!.ToLower().Should().Contain("cancelled");
    }

    /// <summary>
    /// Verifies that unexpected launch exceptions keep raw paths and command fragments out of
    /// the user-facing result while still logging the technical exception for diagnostics.
    /// </summary>
    [Fact]
    public async Task CleanPluginAsync_WhenUnexpectedLaunchExceptionContainsPath_ShouldReturnSafeFailure()
    {
        // Arrange
        var service = new CleaningService(
            _mockGameDetection,
            _mockState,
            _mockLogger,
            _mockProcess,
            _mockCommandBuilder);

        var plugin = new PluginInfo
        {
            FileName = "LaunchException.esp",
            FullPath = "LaunchException.esp",
            DetectedGameType = GameType.SkyrimSe,
            IsInSkipList = false
        };

        const string xEditPath = @"C:\Tools With Spaces\SSEEdit.exe";
        const string mo2Path = @"C:\MO2 With Spaces\ModOrganizer.exe";
        const string nestedPayload = "-autoload LaunchException.esp";

        var appState = new AppState
        {
            CurrentGameType = GameType.SkyrimSe,
            XEditExecutablePath = xEditPath,
            Mo2ExecutablePath = mo2Path,
            Mo2ModeEnabled = true
        };
        _mockState.CurrentState.Returns(appState);

        var startInfo = new System.Diagnostics.ProcessStartInfo(mo2Path);
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add(xEditPath);
        startInfo.ArgumentList.Add("-a");
        startInfo.ArgumentList.Add(nestedPayload);
        _mockCommandBuilder.BuildCommand(plugin, GameType.SkyrimSe)
            .Returns(startInfo);

        var exception = new InvalidOperationException(@"Failed to launch C:\Tools With Spaces\SSEEdit.exe via C:\MO2 With Spaces\ModOrganizer.exe run -a -autoload LaunchException.esp");
        _mockProcess.ExecuteAsync(
                startInfo,
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<System.Diagnostics.Process>?>(),
                Arg.Any<string?>())
            .ThrowsAsync(exception);

        // Act
        var result = await service.CleanPluginAsync(plugin);

        // Assert
        result.Success.Should().BeFalse("unexpected exception should result in failure");
        result.Status.Should().Be(CleaningStatus.Failed);
        result.Message.Should().Contain(plugin.FileName);
        result.Message.Should().Contain("See logs for technical details");
        result.Message.Should().NotContain(xEditPath);
        result.Message.Should().NotContain(mo2Path);
        result.Message.Should().NotContain("run ");
        result.Message.Should().NotContain("-a");
        result.Message.Should().NotContain("Failed to launch");

        // Verify error was logged
        _mockLogger.Received(1).Error(exception, "Error cleaning {Plugin}", plugin.FileName);
    }

    #endregion

    #region Environment Validation Tests

    /// <summary>
    /// Verifies that ValidateEnvironmentAsync returns false when xEdit path is missing.
    /// </summary>
    [Fact]
    public async Task ValidateEnvironmentAsync_WhenXEditPathMissing_ShouldReturnFalse()
    {
        // Arrange
        var service = new CleaningService(
            _mockGameDetection,
            _mockState,
            _mockLogger,
            _mockProcess,
            _mockCommandBuilder);

        // State has load order but no xEdit path
        var appState = new AppState
        {
            LoadOrderPath = "plugins.txt",
            XEditExecutablePath = null
        };
        _mockState.CurrentState.Returns(appState);

        // Act
        var result = await service.ValidateEnvironmentAsync();

        // Assert
        result.Should().BeFalse("validation should fail without xEdit path");
    }

    /// <summary>
    /// Verifies that ValidateEnvironmentAsync does not require a load order path
    /// for Mutagen-supported games.
    /// </summary>
    [Fact]
    public async Task ValidateEnvironmentAsync_WhenLoadOrderPathMissing_ForMutagenGame_ShouldReturnTrueIfXEditIsValid()
    {
        // Arrange
        var service = new CleaningService(
            _mockGameDetection,
            _mockState,
            _mockLogger,
            _mockProcess,
            _mockCommandBuilder);

        var tempXEdit = Path.GetTempFileName();
        try
        {
            var appState = new AppState
            {
                LoadOrderPath = null,
                XEditExecutablePath = tempXEdit,
                CurrentGameType = GameType.Fallout4
            };
            _mockState.CurrentState.Returns(appState);

            // Act
            var result = await service.ValidateEnvironmentAsync();

            // Assert
            result.Should().BeTrue("validation should allow missing load order path when xEdit is configured");
        }
        finally
        {
            if (File.Exists(tempXEdit))
            {
                File.Delete(tempXEdit);
            }
        }
    }

    /// <summary>
    /// Verifies that ValidateEnvironmentAsync requires a load order path
    /// for non-Mutagen games.
    /// </summary>
    [Fact]
    public async Task ValidateEnvironmentAsync_WhenLoadOrderPathMissing_ForNonMutagenGame_ShouldReturnFalse()
    {
        // Arrange
        var service = new CleaningService(
            _mockGameDetection,
            _mockState,
            _mockLogger,
            _mockProcess,
            _mockCommandBuilder);

        var tempXEdit = Path.GetTempFileName();
        try
        {
            var appState = new AppState
            {
                LoadOrderPath = null,
                XEditExecutablePath = tempXEdit,
                CurrentGameType = GameType.Fallout3
            };
            _mockState.CurrentState.Returns(appState);

            // Act
            var result = await service.ValidateEnvironmentAsync();

            // Assert
            result.Should().BeFalse("non-Mutagen games require a load order file path");
        }
        finally
        {
            if (File.Exists(tempXEdit))
            {
                File.Delete(tempXEdit);
            }
        }
    }

    #endregion

    /// <summary>
    /// Asserts that captured log message templates and structured arguments exclude unsafe command or path fragments.
    /// </summary>
    private void AssertCapturedLogTextExcludes(params string[] unsafeFragments)
    {
        var capturedText = string.Join(
            Environment.NewLine,
            _mockLogger.ReceivedCalls().SelectMany(call => call.GetArguments())
                .Where(argument => argument is not Exception)
                .Select(argument => argument?.ToString() ?? string.Empty));

        foreach (var unsafeFragment in unsafeFragments)
        {
            capturedText.Should().NotContain(unsafeFragment);
        }
    }
}
