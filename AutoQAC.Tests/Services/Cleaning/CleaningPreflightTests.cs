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
        _cleaningServiceMock = Substitute.For<ICleaningService>();
        _stateMock = Substitute.For<IStateService>();
        _loggerMock = Substitute.For<ILoggingService>();

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

        _stateMock.CurrentState.Returns(CreateState());
        _sut = new CleaningPreflight(
            _configMock,
            _gameDetectionMock,
            _validationMock,
            _mo2ValidationMock,
            _cleaningServiceMock,
            _stateMock,
            _loggerMock);
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
        try
        {
            _stateMock.CurrentState.Returns(CreateState(mo2Mode: true, mo2ExecutablePath: tempMo2Path));
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

    private static AppState CreateState(
        IReadOnlyList<PluginInfo>? plugins = null,
        bool mo2Mode = false,
        string mo2ExecutablePath = @"C:\MO2\ModOrganizer.exe",
        string? loadOrderPath = @"C:\Games\Skyrim Special Edition\plugins.txt") =>
        new()
        {
            CurrentGameType = GameType.SkyrimSe,
            XEditExecutablePath = @"C:\Games\SSEEdit\SSEEdit.exe",
            LoadOrderPath = loadOrderPath,
            Mo2ExecutablePath = mo2ExecutablePath,
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
}
