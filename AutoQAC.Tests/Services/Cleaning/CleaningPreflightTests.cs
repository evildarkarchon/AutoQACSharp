using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameDetection;
using AutoQAC.Services.MO2;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;
using FluentAssertions;
using NSubstitute;

namespace AutoQAC.Tests.Services.Cleaning;

public sealed class CleaningPreflightTests
{
    private readonly IConfigurationService _configMock;
    private readonly IGameDetectionService _gameDetectionMock;
    private readonly IPluginValidationService _validationMock;
    private readonly IMo2ValidationService _mo2ValidationMock;
    private readonly IMo2InstanceService _mo2InstanceMock;
    private readonly ICleaningService _cleaningServiceMock;
    private readonly IStateService _stateMock;
    private readonly ILoggingService _loggerMock;
    private readonly ICleaningPreflight _sut;

    public CleaningPreflightTests()
    {
        _configMock = Substitute.For<IConfigurationService>();
        _gameDetectionMock = Substitute.For<IGameDetectionService>();
        _validationMock = Substitute.For<IPluginValidationService>();
        _mo2ValidationMock = Substitute.For<IMo2ValidationService>();
        _mo2InstanceMock = Substitute.For<IMo2InstanceService>();
        _cleaningServiceMock = Substitute.For<ICleaningService>();
        _stateMock = Substitute.For<IStateService>();
        _loggerMock = Substitute.For<ILoggingService>();

        _configMock.FlushPendingSavesAsync(Arg.Any<CancellationToken>())
            .Returns(CreateFlushResult(ConfigPersistenceStatusKind.NoOp));
        _validationMock.ValidatePluginFile(Arg.Any<PluginInfo>()).Returns(PluginWarningKind.None);
        _configMock.GetSkipListAsync(
                Arg.Any<GameType>(),
                Arg.Any<GameVariant>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        _configMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new UserConfiguration());
        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>())
            .Returns(true);
        _gameDetectionMock.DetectVariant(Arg.Any<GameType>(), Arg.Any<IReadOnlyList<string>>())
            .Returns(GameVariant.None);
        var mo2Instance = new Mo2InstanceInfo(
            @"C:\MO2\Instances\SSE",
            @"C:\MO2\Instances\SSE\mods",
            @"C:\MO2\Instances\SSE\profiles",
            @"C:\MO2\Instances\SSE\overwrite",
            "Default",
            "Skyrim Special Edition",
            true,
            null);
        _mo2InstanceMock.ResolveInstanceAsync(
                Arg.Any<GameType>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(mo2Instance);
        _mo2InstanceMock.GetProfiles(Arg.Any<Mo2InstanceInfo>()).Returns(["Default"]);
        _mo2InstanceMock.GetLoadOrderPath(Arg.Any<Mo2InstanceInfo>(), Arg.Any<string>())
            .Returns(@"C:\MO2\Instances\SSE\profiles\Default\loadorder.txt");

        _stateMock.CurrentState.Returns(CreateState());
        _sut = new CleaningPreflight(
            _configMock,
            _gameDetectionMock,
            _validationMock,
            _mo2ValidationMock,
            _cleaningServiceMock,
            _stateMock,
            _loggerMock,
            _mo2InstanceMock);
    }

    [Fact]
    public async Task PrepareAsync_IsIdempotent_ForIdenticalState()
    {
        // Arrange
        var state = CreateState();
        _stateMock.CurrentState.Returns(state);

        // Act
        var first = await _sut.PrepareAsync(CancellationToken.None);
        var second = await _sut.PrepareAsync(CancellationToken.None);

        // Assert
        second.PluginRows.Should().Equal(first.PluginRows);
    }

    [Fact]
    public async Task PrepareAsync_Mo2Mode_ReportsBackupSkippedAndFileValidationSkippedPolicyFacts()
    {
        // Arrange
        var tempMo2Path = Path.GetTempFileName();
        var tempLoadOrderPath = Path.GetTempFileName();
        try
        {
            _mo2InstanceMock.GetLoadOrderPath(Arg.Any<Mo2InstanceInfo>(), Arg.Any<string>())
                .Returns(tempLoadOrderPath);
            _stateMock.CurrentState.Returns(CreateState(mo2Mode: true, mo2ExecutablePath: tempMo2Path, mo2Profile: "Default"));
            _configMock.LoadUserConfigAsync(Arg.Any<CancellationToken>())
                .Returns(new UserConfiguration
                {
                    Settings = new AutoQacSettings { Mo2Mode = true },
                    Backup = new BackupSettings { Enabled = true }
                });
            _mo2ValidationMock.ValidateMo2ExecutableAsync(Arg.Any<string>()).Returns(true);

            // Act
            var plan = await _sut.PrepareAsync(CancellationToken.None);

            // Assert
            plan.IsMo2ModeActive.Should().BeTrue();
            plan.BackupSkippedByPolicy.Should().BeTrue();
            plan.FileValidationSkippedByPolicy.Should().BeTrue();
            plan.LaunchModeLabel.Should().Be("MO2");
        }
        finally
        {
            if (File.Exists(tempMo2Path))
            {
                File.Delete(tempMo2Path);
            }

            if (File.Exists(tempLoadOrderPath))
            {
                File.Delete(tempLoadOrderPath);
            }
        }
    }

    [Fact]
    public async Task PrepareAsync_NonMo2Mode_FileNotFound_MapsToFileNotFoundSkipReason()
    {
        // Arrange
        var missingPlugin = CreatePlugin("missing.esp");
        _stateMock.CurrentState.Returns(CreateState([missingPlugin]));
        _validationMock.ValidatePluginFile(Arg.Is<PluginInfo>(p => p.FileName == "missing.esp"))
            .Returns(PluginWarningKind.NotFound);

        // Act
        var plan = await _sut.PrepareAsync(CancellationToken.None);

        // Assert
        plan.PluginRows.Should().ContainSingle().Which.Should().Match<PreflightPluginRow>(row =>
            row.Decision == PreflightDecision.Skip && row.SkipReason == PreflightSkipReason.FileNotFound);
    }

    [Fact]
    public async Task PrepareAsync_NonMo2Mode_ZeroByte_MapsToZeroByteSkipReason()
    {
        // Arrange
        var zeroBytePlugin = CreatePlugin("empty.esp");
        _stateMock.CurrentState.Returns(CreateState([zeroBytePlugin]));
        _validationMock.ValidatePluginFile(Arg.Is<PluginInfo>(p => p.FileName == "empty.esp"))
            .Returns(PluginWarningKind.ZeroByte);

        // Act
        var plan = await _sut.PrepareAsync(CancellationToken.None);

        // Assert
        plan.PluginRows.Should().ContainSingle().Which.Should().Match<PreflightPluginRow>(row =>
            row.Decision == PreflightDecision.Skip && row.SkipReason == PreflightSkipReason.ZeroByte);
    }

    [Fact]
    public async Task PrepareAsync_DoesNotMutateCleaningState_OverMultipleCalls()
    {
        // Arrange
        _stateMock.CurrentState.Returns(CreateState());

        // Act
        await _sut.PrepareAsync(CancellationToken.None);
        await _sut.PrepareAsync(CancellationToken.None);

        // Assert
        _stateMock.Received(0).UpdateState(Arg.Any<Func<AppState, AppState>>());
    }

    [Theory]
    [InlineData(GameType.Fallout3, null)]
    [InlineData(GameType.FalloutNewVegas, "")]
    [InlineData(GameType.Oblivion, @"C:\Games\Missing\plugins.txt")]
    public async Task PrepareAsync_UnknownGameDetectedAsFileLoadOrderGame_WithMissingLoadOrderPath_Throws(
        GameType detectedGameType,
        string? loadOrderPath)
    {
        var state = CreateState(loadOrderPath: loadOrderPath) with
        {
            CurrentGameType = GameType.Unknown,
            XEditExecutablePath = @"C:\Games\xEdit\xEdit.exe"
        };
        _stateMock.CurrentState.Returns(state);
        _gameDetectionMock.DetectFromExecutable(Arg.Any<string>()).Returns(detectedGameType);
        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>()).Returns(true);

        var act = () => _sut.PrepareAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _configMock.DidNotReceive().GetSkipListAsync(
            Arg.Any<GameType>(),
            Arg.Any<GameVariant>(),
            Arg.Any<CancellationToken>());
        _validationMock.DidNotReceive().ValidatePluginFile(Arg.Any<PluginInfo>());
    }

    [Fact]
    public async Task PrepareAsync_UnknownGameDetectedAsMutagenSupportedGame_WithMissingLoadOrderPath_Succeeds()
    {
        var state = CreateState(loadOrderPath: null) with
        {
            CurrentGameType = GameType.Unknown,
            XEditExecutablePath = @"C:\Games\xEdit\xEdit.exe"
        };
        _stateMock.CurrentState.Returns(state);
        _gameDetectionMock.DetectFromExecutable(Arg.Any<string>()).Returns(GameType.Fallout4);
        _cleaningServiceMock.ValidateEnvironmentAsync(Arg.Any<CancellationToken>()).Returns(true);

        var plan = await _sut.PrepareAsync(CancellationToken.None);

        plan.DetectedGameType.Should().Be(GameType.Fallout4);
        plan.PluginRows.Should().ContainSingle();
    }

    /// <summary>
    /// Verifies that Phase 10 D-26 aborts preflight with a typed persistence exception when the required flush fails.
    /// </summary>
    [Fact]
    public async Task PrepareAsync_FlushFailure_ThrowsConfigPersistenceFailureException()
    {
        // Arrange
        var failure = CreateFlushFailure();
        _configMock.FlushPendingSavesAsync(Arg.Any<CancellationToken>())
            .Returns(CreateFlushResult(ConfigPersistenceStatusKind.Failed, failure));

        // Act
        var act = () => _sut.PrepareAsync(CancellationToken.None);

        // Assert
        var thrown = await act.Should()
            .ThrowAsync<ConfigPersistenceFailureException>(
                "D-26 forces pre-cleaning flush failure to abort preflight with a typed exception");
        thrown.Which.Failure.Should().BeSameAs(failure);
        thrown.Which.Message.Should().Be(failure.SafeSummary);
    }

    /// <summary>
    /// Proves the flush-failure branch exits before any game detection, validation, or process-launch-adjacent collaborator runs.
    /// </summary>
    [Fact]
    public async Task PrepareAsync_FlushFailure_DoesNotInvokeAnyDownstreamCollaborator()
    {
        // Arrange
        var failure = CreateFlushFailure();
        _configMock.FlushPendingSavesAsync(Arg.Any<CancellationToken>())
            .Returns(CreateFlushResult(ConfigPersistenceStatusKind.Failed, failure));

        // Act
        var act = () => _sut.PrepareAsync(CancellationToken.None);

        // Assert
        await act.Should()
            .ThrowAsync<ConfigPersistenceFailureException>(
                "D-26 and success criterion #2 prevent any xEdit launch path on flush failure");
        _gameDetectionMock.DidNotReceive().DetectFromExecutable(Arg.Any<string>());
        await _gameDetectionMock.DidNotReceiveWithAnyArgs().DetectFromLoadOrderAsync(default!, default);
        _gameDetectionMock.DidNotReceive().DetectVariant(Arg.Any<GameType>(), Arg.Any<IReadOnlyList<string>?>());
        _validationMock.DidNotReceive().ValidatePluginFile(Arg.Any<PluginInfo>());
        await _mo2ValidationMock.DidNotReceiveWithAnyArgs().ValidateMo2ExecutableAsync(default!);
        await _cleaningServiceMock.DidNotReceive().ValidateEnvironmentAsync(Arg.Any<CancellationToken>());
        await _cleaningServiceMock.DidNotReceiveWithAnyArgs().CleanPluginAsync(default!, default, default);
        await _configMock.DidNotReceiveWithAnyArgs().LoadUserConfigAsync(default);
        await _configMock.Received(1).FlushPendingSavesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Confirms an explicit successful flush preserves existing preflight behavior and downstream validation.
    /// </summary>
    [Fact]
    public async Task PrepareAsync_FlushSuccess_ProceedsAsBefore()
    {
        // Arrange
        _configMock.FlushPendingSavesAsync(Arg.Any<CancellationToken>())
            .Returns(CreateFlushResult(ConfigPersistenceStatusKind.Success));

        // Act
        var plan = await _sut.PrepareAsync(CancellationToken.None);

        // Assert
        plan.Should().NotBeNull();
        await _configMock.Received(1).FlushPendingSavesAsync(Arg.Any<CancellationToken>());
        _gameDetectionMock.Received(1).DetectVariant(GameType.SkyrimSe, Arg.Any<IReadOnlyList<string>>());
        _validationMock.Received(1).ValidatePluginFile(Arg.Any<PluginInfo>());
    }

    /// <summary>
    /// Confirms a no-op flush, the common no-pending-save case, preserves existing preflight behavior.
    /// </summary>
    [Fact]
    public async Task PrepareAsync_FlushNoOp_ProceedsAsBefore()
    {
        // Arrange
        _configMock.FlushPendingSavesAsync(Arg.Any<CancellationToken>())
            .Returns(CreateFlushResult(ConfigPersistenceStatusKind.NoOp));

        // Act
        var plan = await _sut.PrepareAsync(CancellationToken.None);

        // Assert
        plan.Should().NotBeNull();
        await _configMock.Received(1).FlushPendingSavesAsync(Arg.Any<CancellationToken>());
        _gameDetectionMock.Received(1).DetectVariant(GameType.SkyrimSe, Arg.Any<IReadOnlyList<string>>());
        _validationMock.Received(1).ValidatePluginFile(Arg.Any<PluginInfo>());
    }

    /// <summary>
    /// Ensures the preflight failure log uses the typed safe summary rather than a raw exception detail.
    /// </summary>
    [Fact]
    public async Task PrepareAsync_FlushFailure_LogsErrorWithSafeSummary()
    {
        // Arrange
        var failure = CreateFlushFailure();
        _configMock.FlushPendingSavesAsync(Arg.Any<CancellationToken>())
            .Returns(CreateFlushResult(ConfigPersistenceStatusKind.Failed, failure));

        // Act
        var act = () => _sut.PrepareAsync(CancellationToken.None);

        // Assert
        await act.Should()
            .ThrowAsync<ConfigPersistenceFailureException>(
                "D-28 requires the log path to carry a safe typed summary, not raw exception details");
        _loggerMock.Received(1).Error(
            null,
            Arg.Any<string>(),
            Arg.Is<object[]>(args => ContainsSafeSummary(args)));
    }

    private static AppState CreateState(
        IReadOnlyList<PluginInfo>? plugins = null,
        bool mo2Mode = false,
        string mo2ExecutablePath = @"C:\MO2\ModOrganizer.exe",
        string? mo2Profile = null,
        string? loadOrderPath = @"C:\Games\Skyrim Special Edition\plugins.txt") =>
        new()
        {
            CurrentGameType = GameType.SkyrimSe,
            XEditExecutablePath = @"C:\Games\SSEEdit\SSEEdit.exe",
            LoadOrderPath = loadOrderPath,
            Mo2ExecutablePath = mo2ExecutablePath,
            Mo2Profile = mo2Profile,
            Mo2ModeEnabled = mo2Mode,
            CleaningTimeout = 300,
            PluginsToClean = plugins ?? [CreatePlugin("Update.esm")]
        };

    private static PluginInfo CreatePlugin(string fileName) =>
        new()
        {
            FileName = fileName,
            FullPath = $@"C:\Games\Skyrim Special Edition\Data\{fileName}",
            DetectedGameType = GameType.SkyrimSe
        };

    private static ConfigPersistenceFailure CreateFlushFailure() =>
        new(
            ConfigPersistenceOperationKind.Flush,
            ConfigPersistenceFailureKind.WriteFailed,
            "Could not write settings file (write_failed)",
            LogReference: null,
            Generation: 1);

    private static ConfigPersistenceResult CreateFlushResult(
        ConfigPersistenceStatusKind status,
        ConfigPersistenceFailure? failure = null) =>
        new(
            status,
            ConfigPersistenceOperationKind.Flush,
            Generation: 1,
            failure);

    private static bool ContainsSafeSummary(object[] args) =>
        args.Length > 0 && args[0] is string summary && summary.Contains("write_failed", StringComparison.Ordinal);
}
