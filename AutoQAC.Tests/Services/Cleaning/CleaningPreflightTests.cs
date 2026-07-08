using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameCapability;
using AutoQAC.Services.MO2;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;
using AutoQAC.Tests.TestInfrastructure;
using FluentAssertions;
using NSubstitute;

namespace AutoQAC.Tests.Services.Cleaning;

public sealed class CleaningPreflightTests
{
    [Fact]
    public async Task PrepareAsync_IsIdempotent_ForIdenticalPublication()
    {
        var xEditPath = await CreateTempFileAsync();
        try
        {
            using var refresh = new RecordingPluginRefreshModule();
            refresh.CurrentPublication = CreateFreshPublication(CreatePlugin("Update.esm"));
            var sut = CreateSut(CreateState(xEditPath: xEditPath), refresh);

            var first = await sut.PrepareAsync(CancellationToken.None);
            var second = await sut.PrepareAsync(CancellationToken.None);

            second.PluginRows.Should().Equal(first.PluginRows);
        }
        finally
        {
            File.Delete(xEditPath);
        }
    }

    [Fact]
    public async Task PrepareAsync_Mo2Mode_ReportsBackupSkippedAndFileValidationSkippedPolicyFacts()
    {
        var xEditPath = await CreateTempFileAsync();
        var mo2Path = await CreateTempMo2ExecutableAsync();
        var loadOrderPath = await CreateTempFileAsync();
        var instanceDirectory = Directory.CreateTempSubdirectory();
        try
        {
            using var refresh = new RecordingPluginRefreshModule();
            refresh.CurrentPublication = CreateMo2Publication(
                xEditPath,
                mo2Path,
                instanceDirectory.FullName,
                loadOrderPath,
                selectedProfile: "Default");
            var config = CreateConfig(new UserConfiguration
            {
                Backup = new BackupSettings { Enabled = true },
                Settings = new AutoQacSettings { Mo2Mode = true }
            });
            var mo2Validation = Substitute.For<IMo2ValidationService>();
            mo2Validation.ValidateMo2ExecutableAsync(mo2Path).Returns(true);
            var sut = CreateSut(
                CreateState(xEditPath: xEditPath, mo2Mode: true, mo2Path: mo2Path, mo2Profile: "Default"),
                refresh,
                config: config,
                mo2Validation: mo2Validation);

            var plan = await sut.PrepareAsync(CancellationToken.None);

            plan.IsMo2ModeActive.Should().BeTrue();
            plan.BackupSkippedByPolicy.Should().BeTrue();
            plan.FileValidationSkippedByPolicy.Should().BeTrue();
            plan.LaunchModeLabel.Should().Be("MO2");
        }
        finally
        {
            File.Delete(xEditPath);
            DeleteTempMo2Executable(mo2Path);
            File.Delete(loadOrderPath);
            instanceDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(PluginWarningKind.NotFound, PreflightSkipReason.FileNotFound)]
    [InlineData(PluginWarningKind.ZeroByte, PreflightSkipReason.ZeroByte)]
    public async Task PrepareAsync_NonMo2Mode_FileValidationWarning_MapsToSkipReason(
        PluginWarningKind warning,
        PreflightSkipReason expectedReason)
    {
        var xEditPath = await CreateTempFileAsync();
        try
        {
            var plugin = CreatePlugin("NeedsValidation.esp");
            using var refresh = new RecordingPluginRefreshModule();
            refresh.CurrentPublication = CreateFreshPublication(plugin);
            var validation = Substitute.For<IPluginValidationService>();
            validation.ValidatePluginFile(Arg.Is<PluginInfo>(p => p.FileName == plugin.FileName))
                .Returns(warning);
            var sut = CreateSut(CreateState(xEditPath: xEditPath), refresh, validation: validation);

            var plan = await sut.PrepareAsync(CancellationToken.None);

            plan.PluginRows.Should().ContainSingle().Which.Should().Match<PreflightPluginRow>(row =>
                row.Decision == PreflightDecision.Skip && row.SkipReason == expectedReason);
        }
        finally
        {
            File.Delete(xEditPath);
        }
    }

    [Fact]
    public async Task PrepareAsync_DoesNotMutateCleaningState()
    {
        var xEditPath = await CreateTempFileAsync();
        try
        {
            using var refresh = new RecordingPluginRefreshModule();
            refresh.CurrentPublication = CreateFreshPublication(CreatePlugin("Update.esm"));
            var state = Substitute.For<IStateService>();
            state.CurrentState.Returns(CreateState(xEditPath: xEditPath));
            var sut = CreateSut(state.CurrentState, refresh, stateService: state);

            await sut.PrepareAsync(CancellationToken.None);

            state.Received(0).UpdateState(Arg.Any<Func<AppState, AppState>>());
        }
        finally
        {
            File.Delete(xEditPath);
        }
    }

    [Fact]
    public async Task PrepareAsync_MissingPublication_ThrowsTypedPreflightFailure()
    {
        var xEditPath = await CreateTempFileAsync();
        try
        {
            using var refresh = new RecordingPluginRefreshModule(
                RecordingPluginRefreshModule.CreateSnapshot(GameType.SkyrimSe));
            var sut = CreateSut(CreateState(xEditPath: xEditPath), refresh);

            var act = () => sut.PrepareAsync(CancellationToken.None);

            var thrown = await act.Should().ThrowAsync<CleaningPreflightException>();
            thrown.Which.Failure.Kind.Should().Be(CleaningPreflightFailureKind.MissingPluginRefreshPublication);
        }
        finally
        {
            File.Delete(xEditPath);
        }
    }

    [Fact]
    public async Task PrepareAsync_StalePublication_ThrowsTypedPreflightFailure()
    {
        var xEditPath = await CreateTempFileAsync();
        try
        {
            using var refresh = new RecordingPluginRefreshModule();
            var fresh = CreateFreshPublication(CreatePlugin("Update.esm"));
            refresh.CurrentPublication = fresh with
            {
                Freshness = new PluginRefreshFreshness(false, PluginRefreshStalenessReason.SkipListSettingsChanged)
            };
            var sut = CreateSut(CreateState(xEditPath: xEditPath), refresh);

            var act = () => sut.PrepareAsync(CancellationToken.None);

            var thrown = await act.Should().ThrowAsync<CleaningPreflightException>();
            thrown.Which.Failure.Kind.Should().Be(CleaningPreflightFailureKind.StalePluginRefreshPublication);
        }
        finally
        {
            File.Delete(xEditPath);
        }
    }

    [Fact]
    public async Task PrepareAsync_UsesPublicationSelectionAndSkipListFacts()
    {
        var xEditPath = await CreateTempFileAsync();
        try
        {
            var clean = CreatePlugin("Clean.esp");
            var notSelected = CreatePlugin("NotSelected.esp");
            var skipped = CreatePlugin("Skipped.esp") with { IsInSkipList = true };
            using var refresh = new RecordingPluginRefreshModule();
            refresh.CurrentPublication = CreateFreshPublication(
                RecordingPluginRefreshModule.CreatePublishedRow(clean),
                RecordingPluginRefreshModule.CreatePublishedRow(notSelected, isSelected: false),
                RecordingPluginRefreshModule.CreatePublishedRow(
                    skipped,
                    isVisible: false,
                    isSelected: true,
                    isSkippedByPolicy: true));
            var config = CreateConfig();
            var sut = CreateSut(CreateState(xEditPath: xEditPath), refresh, config: config);

            var result = await sut.PrepareAsync(CancellationToken.None);

            result.PluginRows.Should().Contain(row => row.Plugin.FileName == "Clean.esp" && row.Decision == PreflightDecision.Clean);
            result.PluginRows.Should().Contain(row => row.Plugin.FileName == "NotSelected.esp" && row.SkipReason == PreflightSkipReason.NotSelected);
            result.PluginRows.Should().Contain(row => row.Plugin.FileName == "Skipped.esp" && row.SkipReason == PreflightSkipReason.InSkipList);
            await config.DidNotReceive().GetSkipListAsync(Arg.Any<GameType>(), Arg.Any<GameVariant>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            File.Delete(xEditPath);
        }
    }

    [Fact]
    public async Task PrepareAsync_EmptyPublicationRows_ThrowsNoPluginsLoaded()
    {
        var xEditPath = await CreateTempFileAsync();
        try
        {
            using var refresh = new RecordingPluginRefreshModule();
            refresh.CurrentPublication = RecordingPluginRefreshModule.CreateFreshPublication(rows: []);
            var sut = CreateSut(CreateState(xEditPath: xEditPath), refresh);

            var act = () => sut.PrepareAsync(CancellationToken.None);

            var thrown = await act.Should().ThrowAsync<CleaningPreflightException>();
            thrown.Which.Failure.Kind.Should().Be(CleaningPreflightFailureKind.NoPluginsLoaded);
        }
        finally
        {
            File.Delete(xEditPath);
        }
    }

    [Fact]
    public async Task PrepareAsync_NoSelectedCleanableRows_ThrowsNoPluginsSelected()
    {
        var xEditPath = await CreateTempFileAsync();
        try
        {
            using var refresh = new RecordingPluginRefreshModule();
            refresh.CurrentPublication = CreateFreshPublication(
                RecordingPluginRefreshModule.CreatePublishedRow(CreatePlugin("Deselected.esp"), isSelected: false));
            var sut = CreateSut(CreateState(xEditPath: xEditPath), refresh);

            var act = () => sut.PrepareAsync(CancellationToken.None);

            var thrown = await act.Should().ThrowAsync<CleaningPreflightException>();
            thrown.Which.Failure.Kind.Should().Be(CleaningPreflightFailureKind.NoPluginsSelected);
        }
        finally
        {
            File.Delete(xEditPath);
        }
    }

    [Theory]
    [InlineData(null, CleaningPreflightFailureKind.XEditNotConfigured)]
    [InlineData(@"C:\Missing\SSEEdit.exe", CleaningPreflightFailureKind.XEditNotFound)]
    public async Task PrepareAsync_XEditLaunchReadinessFailure_ThrowsTypedFailure(
        string? xEditPath,
        CleaningPreflightFailureKind expectedKind)
    {
        using var refresh = new RecordingPluginRefreshModule();
        refresh.CurrentPublication = CreateFreshPublication(CreatePlugin("Update.esm"));
        var sut = CreateSut(CreateState(xEditPath: xEditPath), refresh);

        var act = () => sut.PrepareAsync(CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<CleaningPreflightException>();
        thrown.Which.Failure.Kind.Should().Be(expectedKind);
    }

    [Fact]
    public async Task SharedLaunchFailure_XEditNotFound_UsesSameFailureForReadinessAndPreflight()
    {
        using var refresh = new RecordingPluginRefreshModule();
        refresh.CurrentPublication = CreateFreshPublication(CreatePlugin("Update.esm"));
        var state = CreateState(xEditPath: @"C:\Users\Alice\Tools\SSEEdit.exe");
        var stateService = Substitute.For<IStateService>();
        stateService.CurrentState.Returns(state);
        var readiness = new CleaningCommandReadiness(refresh, stateService);
        var preflight = CreateSut(state, refresh, stateService: stateService);

        var readinessResult = await readiness.EvaluateAsync(CancellationToken.None);
        var act = () => preflight.PrepareAsync(CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<CleaningPreflightException>();
        readinessResult.Failure.Should().NotBeNull();
        thrown.Which.Failure.Should().BeEquivalentTo(readinessResult.Failure);
        readinessResult.Failure!.SafeMessage.Should()
            .Be("xEdit Path (SSEEdit.exe) is missing. Choose the correct xEdit executable in Settings.");
    }

    [Fact]
    public async Task PrepareAsync_DirectLoadOrderWithoutPath_ThrowsLoadOrderNotConfigured()
    {
        var xEditPath = await CreateTempFileAsync();
        try
        {
            var configuration = RecordingPluginRefreshModule.CreateConfiguration(xEditPath: xEditPath);
            var plan = RecordingPluginRefreshModule.CreateDiscoveryPlan(
                mode: PluginRefreshDiscoveryMode.DirectLoadOrderFile,
                configuration: configuration,
                loadOrderPath: null);
            using var refresh = new RecordingPluginRefreshModule();
            refresh.CurrentPublication = RecordingPluginRefreshModule.CreateFreshPublication(
                discoveryPlan: plan,
                configuration: configuration);
            var sut = CreateSut(CreateState(xEditPath: xEditPath), refresh);

            var act = () => sut.PrepareAsync(CancellationToken.None);

            var thrown = await act.Should().ThrowAsync<CleaningPreflightException>();
            thrown.Which.Failure.Kind.Should().Be(CleaningPreflightFailureKind.LoadOrderNotConfigured);
        }
        finally
        {
            File.Delete(xEditPath);
        }
    }

    [Fact]
    public async Task PrepareAsync_DirectLoadOrderWithMissingFile_ThrowsLoadOrderNotFound()
    {
        var xEditPath = await CreateTempFileAsync();
        try
        {
            var loadOrderPath = @"C:\Missing\plugins.txt";
            var configuration = RecordingPluginRefreshModule.CreateConfiguration(
                loadOrderPath: loadOrderPath,
                xEditPath: xEditPath);
            var plan = RecordingPluginRefreshModule.CreateDiscoveryPlan(
                mode: PluginRefreshDiscoveryMode.DirectLoadOrderFile,
                configuration: configuration,
                loadOrderPath: loadOrderPath);
            using var refresh = new RecordingPluginRefreshModule();
            refresh.CurrentPublication = RecordingPluginRefreshModule.CreateFreshPublication(
                discoveryPlan: plan,
                configuration: configuration);
            var sut = CreateSut(CreateState(xEditPath: xEditPath, loadOrderPath: loadOrderPath), refresh);

            var act = () => sut.PrepareAsync(CancellationToken.None);

            var thrown = await act.Should().ThrowAsync<CleaningPreflightException>();
            thrown.Which.Failure.Kind.Should().Be(CleaningPreflightFailureKind.LoadOrderNotFound);
        }
        finally
        {
            File.Delete(xEditPath);
        }
    }

    [Fact]
    public async Task PrepareAsync_Mo2PublicationWithInvalidExecutable_ThrowsTypedMo2Failure()
    {
        var xEditPath = await CreateTempFileAsync();
        var mo2Path = await CreateTempMo2ExecutableAsync();
        var loadOrderPath = await CreateTempFileAsync();
        var instanceDirectory = Directory.CreateTempSubdirectory();
        try
        {
            using var refresh = new RecordingPluginRefreshModule();
            refresh.CurrentPublication = CreateMo2Publication(
                xEditPath,
                mo2Path,
                instanceDirectory.FullName,
                loadOrderPath,
                selectedProfile: "Default");
            var mo2Validation = Substitute.For<IMo2ValidationService>();
            mo2Validation.ValidateMo2ExecutableAsync(mo2Path).Returns(false);
            var sut = CreateSut(
                CreateState(xEditPath: xEditPath, mo2Mode: true, mo2Path: mo2Path, mo2Profile: "Default"),
                refresh,
                mo2Validation: mo2Validation);

            var act = () => sut.PrepareAsync(CancellationToken.None);

            var thrown = await act.Should().ThrowAsync<CleaningPreflightException>();
            thrown.Which.Failure.Kind.Should().Be(CleaningPreflightFailureKind.Mo2NotFound);
            await mo2Validation.Received(1).ValidateMo2ExecutableAsync(mo2Path);
        }
        finally
        {
            File.Delete(xEditPath);
            DeleteTempMo2Executable(mo2Path);
            File.Delete(loadOrderPath);
            instanceDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task PrepareAsync_FlushFailure_ThrowsConfigPersistenceFailureException()
    {
        var xEditPath = await CreateTempFileAsync();
        try
        {
            var failure = CreateFlushFailure();
            var config = CreateConfig();
            config.FlushPendingSavesAsync(Arg.Any<CancellationToken>())
                .Returns(CreateFlushResult(ConfigPersistenceStatusKind.Failed, failure));
            using var refresh = new RecordingPluginRefreshModule();
            refresh.CurrentPublication = CreateFreshPublication(CreatePlugin("Update.esm"));
            var sut = CreateSut(CreateState(xEditPath: xEditPath), refresh, config: config);

            var act = () => sut.PrepareAsync(CancellationToken.None);

            var thrown = await act.Should().ThrowAsync<ConfigPersistenceFailureException>();
            thrown.Which.Failure.Should().BeSameAs(failure);
            thrown.Which.Message.Should().Be(failure.SafeSummary);
        }
        finally
        {
            File.Delete(xEditPath);
        }
    }

    [Fact]
    public async Task PrepareAsync_FlushFailure_DoesNotInvokeDownstreamCollaborators()
    {
        var xEditPath = await CreateTempFileAsync();
        try
        {
            var failure = CreateFlushFailure();
            var config = CreateConfig();
            config.FlushPendingSavesAsync(Arg.Any<CancellationToken>())
                .Returns(CreateFlushResult(ConfigPersistenceStatusKind.Failed, failure));
            var validation = Substitute.For<IPluginValidationService>();
            var mo2Validation = Substitute.For<IMo2ValidationService>();
            using var refresh = new RecordingPluginRefreshModule();
            refresh.CurrentPublication = CreateFreshPublication(CreatePlugin("Update.esm"));
            var sut = CreateSut(
                CreateState(xEditPath: xEditPath),
                refresh,
                config: config,
                validation: validation,
                mo2Validation: mo2Validation);

            var act = () => sut.PrepareAsync(CancellationToken.None);

            await act.Should().ThrowAsync<ConfigPersistenceFailureException>();
            validation.DidNotReceive().ValidatePluginFile(Arg.Any<PluginInfo>());
            await mo2Validation.DidNotReceiveWithAnyArgs().ValidateMo2ExecutableAsync(null!);
            await config.DidNotReceiveWithAnyArgs().LoadUserConfigAsync();
            await config.Received(1).FlushPendingSavesAsync(Arg.Any<CancellationToken>());
        }
        finally
        {
            File.Delete(xEditPath);
        }
    }

    [Theory]
    [InlineData(ConfigPersistenceStatusKind.Success)]
    [InlineData(ConfigPersistenceStatusKind.NoOp)]
    public async Task PrepareAsync_SuccessfulFlush_ProceedsToPublicationPreflight(ConfigPersistenceStatusKind flushStatus)
    {
        var xEditPath = await CreateTempFileAsync();
        try
        {
            var config = CreateConfig();
            config.FlushPendingSavesAsync(Arg.Any<CancellationToken>())
                .Returns(CreateFlushResult(flushStatus));
            var validation = Substitute.For<IPluginValidationService>();
            validation.ValidatePluginFile(Arg.Any<PluginInfo>()).Returns(PluginWarningKind.None);
            using var refresh = new RecordingPluginRefreshModule();
            refresh.CurrentPublication = CreateFreshPublication(CreatePlugin("Update.esm"));
            var sut = CreateSut(CreateState(xEditPath: xEditPath), refresh, config: config, validation: validation);

            var plan = await sut.PrepareAsync(CancellationToken.None);

            plan.PluginRows.Should().ContainSingle(row => row.Decision == PreflightDecision.Clean);
            await config.Received(1).FlushPendingSavesAsync(Arg.Any<CancellationToken>());
            validation.Received(1).ValidatePluginFile(Arg.Any<PluginInfo>());
        }
        finally
        {
            File.Delete(xEditPath);
        }
    }

    [Fact]
    public async Task PrepareAsync_FlushFailure_LogsErrorWithSafeSummary()
    {
        var xEditPath = await CreateTempFileAsync();
        try
        {
            var failure = CreateFlushFailure();
            var config = CreateConfig();
            config.FlushPendingSavesAsync(Arg.Any<CancellationToken>())
                .Returns(CreateFlushResult(ConfigPersistenceStatusKind.Failed, failure));
            var logger = Substitute.For<ILoggingService>();
            using var refresh = new RecordingPluginRefreshModule();
            refresh.CurrentPublication = CreateFreshPublication(CreatePlugin("Update.esm"));
            var sut = CreateSut(CreateState(xEditPath: xEditPath), refresh, config: config, logger: logger);

            var act = () => sut.PrepareAsync(CancellationToken.None);

            await act.Should().ThrowAsync<ConfigPersistenceFailureException>();
            logger.Received(1).Error(
                null,
                Arg.Any<string>(),
                Arg.Is<object[]>(args => ContainsSafeSummary(args)));
        }
        finally
        {
            File.Delete(xEditPath);
        }
    }

    private static CleaningPreflight CreateSut(
        AppState state,
        IPluginRefreshModule refresh,
        IConfigurationService? config = null,
        IPluginValidationService? validation = null,
        IMo2ValidationService? mo2Validation = null,
        IStateService? stateService = null,
        ILoggingService? logger = null)
    {
        stateService ??= Substitute.For<IStateService>();
        stateService.CurrentState.Returns(state);
        if (validation is null)
        {
            validation = Substitute.For<IPluginValidationService>();
            validation.ValidatePluginFile(Arg.Any<PluginInfo>()).Returns(PluginWarningKind.None);
        }

        return new CleaningPreflight(
            config ?? CreateConfig(),
            validation,
            refresh,
            mo2Validation ?? Substitute.For<IMo2ValidationService>(),
            stateService,
            logger ?? Substitute.For<ILoggingService>());
    }

    private static IConfigurationService CreateConfig(UserConfiguration? userConfig = null)
    {
        var config = Substitute.For<IConfigurationService>();
        config.FlushPendingSavesAsync(Arg.Any<CancellationToken>())
            .Returns(CreateFlushResult(ConfigPersistenceStatusKind.NoOp));
        config.LoadUserConfigAsync(Arg.Any<CancellationToken>())
            .Returns(userConfig ?? new UserConfiguration());
        return config;
    }

    private static AppState CreateState(
        string? xEditPath,
        bool mo2Mode = false,
        string? mo2Path = null,
        string? mo2Profile = null,
        string? loadOrderPath = null) =>
        new()
        {
            CurrentGameType = GameType.SkyrimSe,
            XEditExecutablePath = xEditPath,
            LoadOrderPath = loadOrderPath,
            Mo2ExecutablePath = mo2Path,
            Mo2Profile = mo2Profile,
            Mo2ModeEnabled = mo2Mode,
            CleaningTimeout = 300
        };

    private static PluginRefreshPublication CreateFreshPublication(params PluginInfo[] plugins) =>
        CreateFreshPublication(plugins.Select(plugin => RecordingPluginRefreshModule.CreatePublishedRow(plugin)).ToArray());

    private static PluginRefreshPublication CreateFreshPublication(params PluginRefreshPublishedRow[] rows) =>
        RecordingPluginRefreshModule.CreateFreshPublication(rows: rows);

    private static PluginRefreshPublication CreateMo2Publication(
        string xEditPath,
        string mo2Path,
        string instancePath,
        string loadOrderPath,
        string? selectedProfile)
    {
        var configuration = RecordingPluginRefreshModule.CreateConfiguration(
            xEditPath: xEditPath,
            mo2Path: mo2Path,
            mo2ModeEnabled: true,
            mo2InstancePath: instancePath,
            selectedProfile: selectedProfile);
        var plan = RecordingPluginRefreshModule.CreateDiscoveryPlan(
            mode: PluginRefreshDiscoveryMode.Mo2LoadOrderFile,
            configuration: configuration,
            mo2LoadOrderPath: loadOrderPath);
        return RecordingPluginRefreshModule.CreateFreshPublication(
            discoveryPlan: plan,
            configuration: configuration);
    }

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

    private static async Task<string> CreateTempFileAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"AutoQAC-{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(path, string.Empty);
        return path;
    }

    private static async Task<string> CreateTempMo2ExecutableAsync()
    {
        var directory = Directory.CreateTempSubdirectory("AutoQAC-MO2-");
        var path = Path.Combine(directory.FullName, "ModOrganizer.exe");
        await File.WriteAllTextAsync(path, string.Empty);
        return path;
    }

    private static void DeleteTempMo2Executable(string path)
    {
        File.Delete(path);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
