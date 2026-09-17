using System.Reactive.Linq;
using System.Reactive.Subjects;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Tests.TestInfrastructure;
using YamlDotNet.Serialization;
using AutoQAC.Models;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameCapability;
using AutoQAC.Services.GameDetection;
using AutoQAC.Services.MO2;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;
using FluentAssertions;
using NSubstitute;

namespace AutoQAC.Tests.Services.Configuration;

/// <summary>Exercises settings acceptance through real discovery planning and authoritative publication.</summary>
public sealed class DiscoverySettingsPublicationIntegrationTests
{
    /// <summary>The first settings mutation must initialize the real facade before admission can defer external reloads.</summary>
    [Fact]
    public async Task ColdConfiguration_FirstMutationPreservesExistingDiskSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AutoQAC-ColdSettings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var logger = Substitute.For<ILoggingService>();
            var store = new UserConfigFileStore(logger, directory);
            var existing = new UserConfiguration();
            existing.Settings.CleaningTimeout = 777;
            existing.XEdit.Binary = @"C:\Tools\ExistingXEdit.exe";
            await store.WriteAsync(existing, CancellationToken.None);
            using var state = new StateService();
            var admission = new DiscoverySettingsAdmission();
            var coordinator = new ConfigPersistenceCoordinator(store, state, logger, TimeSpan.Zero, admission);
            await using var configuration = new ConfigurationService(coordinator, logger, directory);
            using var refresh = new RecordingPluginRefreshModule();
            using var settings = new DiscoverySettingsModule(configuration, state, refresh, admission);

            var result = await settings.ExecuteAsync(new DiscoverySettingsIntent.SetDisableSkipLists(true))
                .WaitAsync(TimeSpan.FromSeconds(5));
            var saved = new DeserializerBuilder().Build().Deserialize<UserConfiguration>(
                (await store.ReadAsync(CancellationToken.None)).Content!);

            result.Status.Should().Be(DiscoverySettingsChangeStatus.Accepted);
            saved.Settings.DisableSkipLists.Should().BeTrue();
            saved.Settings.CleaningTimeout.Should().Be(777);
            saved.XEdit.Binary.Should().Be(@"C:\Tools\ExistingXEdit.exe");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>A dialog load-order edit replaces the selected game's override, so accepted rows reflect the new choice.</summary>
    [Fact]
    public async Task DialogLoadOrderChange_ReplacesExistingSelectedGameOverride()
    {
        var original = Path.GetTempFileName();
        var replacement = Path.GetTempFileName();
        try
        {
            using var fixture = new Fixture();
            fixture.Saved.SelectedGame = "FalloutNewVegas";
            fixture.Saved.LoadOrder.File = original;
            fixture.Saved.LoadOrderFileOverrides["FNV"] = original;
            fixture.State.UpdateState(state => state with { CurrentGameType = GameType.FalloutNewVegas });
            fixture.Config.GetGameLoadOrderOverrideAsync(GameType.FalloutNewVegas, Arg.Any<CancellationToken>())
                .Returns(_ => fixture.Saved.LoadOrderFileOverrides.GetValueOrDefault("FNV"));
            fixture.Loading.GetPluginsFromFileAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(call => new List<PluginInfo>
                {
                    new() { FileName = call.ArgAt<string>(0) == replacement ? "New.esp" : "Old.esp",
                        FullPath = Path.Combine(Path.GetTempPath(), "Chosen.esp"), DetectedGameType = GameType.FalloutNewVegas }
                });
            var baseline = fixture.Saved.Copy();
            var edited = baseline.Copy();
            edited.LoadOrder.File = replacement;

            var result = await fixture.Settings.ExecuteAsync(new DiscoverySettingsIntent.ApplySettings(baseline, edited));
            var publication = await fixture.Refresh.GetCurrentPublicationAsync();

            result.Status.Should().Be(DiscoverySettingsChangeStatus.Accepted);
            publication.DiscoveryPlan!.LoadOrderPath.Should().Be(replacement);
            publication.Rows.Single().Plugin.FileName.Should().Be("New.esp");
        }
        finally
        {
            File.Delete(original);
            File.Delete(replacement);
        }
    }

    /// <summary>The saved folder must feed the accepted rows while estimates remain independent work.</summary>
    [Fact]
    public async Task FolderChoice_AcceptsMatchingSavedPlanBeforeApproximationFinishes()
    {
        using var fixture = new Fixture();
        var folder = Path.GetTempPath();
        var result = await fixture.Settings.ExecuteAsync(
            new DiscoverySettingsIntent.SetGameDataFolderOverride(GameType.SkyrimSe, folder))
            .WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.ApproximationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var publication = await fixture.Refresh.GetCurrentPublicationAsync();

        result.Status.Should().Be(DiscoverySettingsChangeStatus.Accepted);
        fixture.Saved.GameDataFolderOverrides["SSE"].Should().Be(folder);
        publication.DiscoveryPlan!.DataFolderPath.Should().Be(folder);
        publication.Rows.Single().Plugin.FullPath.Should().Be(Path.Combine(folder, "Chosen.esp"));
        publication.Activity.IsIssueApproximationRefreshRunning.Should().BeTrue();
        publication.Freshness.IsFresh.Should().BeTrue();
    }

    /// <summary>An external change during discovery cannot be accepted under the former saved settings.</summary>
    [Fact]
    public async Task ExternalChange_DuringDiscovery_PreventsOutdatedAcceptance()
    {
        using var fixture = new Fixture();
        var loading = new TaskCompletionSource<PluginLoadingResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Loading.TryGetPluginsAsync(GameType.SkyrimSe, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { entered.TrySetResult(); return loading.Task; });
        var operation = fixture.Settings.ExecuteAsync(new DiscoverySettingsIntent.SetDisableSkipLists(true));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Saved.Settings.DisableSkipLists = false;
        fixture.ConfigurationChanged.OnNext(fixture.Saved.Copy());
        loading.SetResult(new PluginLoadingResult { Status = PluginLoadingStatus.Success, Plugins = [] });

        (await operation.WaitAsync(TimeSpan.FromSeconds(5))).Status.Should().Be(DiscoverySettingsChangeStatus.Superseded);
        (await fixture.Refresh.GetCurrentPublicationAsync()).Freshness.IsFresh.Should().BeFalse();
    }

    /// <summary>A loader failure preserves the saved choice but cannot establish accepted empty rows.</summary>
    [Fact]
    public async Task DiscoveryFailure_PreservesSavedChoiceAndLeavesCleaningUnavailable()
    {
        using var fixture = new Fixture();
        fixture.Loading.TryGetPluginsAsync(GameType.SkyrimSe, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new PluginLoadingResult { Status = PluginLoadingStatus.Failed, Plugins = [] });
        var result = await fixture.Settings.ExecuteAsync(new DiscoverySettingsIntent.SetDisableSkipLists(true));
        result.Status.Should().Be(DiscoverySettingsChangeStatus.RefreshFailed);
        fixture.Saved.Settings.DisableSkipLists.Should().BeTrue();
        (await fixture.Refresh.GetCurrentPublicationAsync()).Freshness.IsFresh.Should().BeFalse();
    }

    private sealed class Fixture : IDisposable
    {
        public IConfigurationService Config { get; } = Substitute.For<IConfigurationService>();
        public IPluginLoadingService Loading { get; } = Substitute.For<IPluginLoadingService>();
        public Subject<UserConfiguration> ConfigurationChanged { get; } = new();
        public StateService State { get; } = new();
        public UserConfiguration Saved { get; private set; } = new() { SelectedGame = "SkyrimSe" };
        public TaskCompletionSource ApproximationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource ReleaseApproximation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public PluginRefreshModule Refresh { get; }
        public DiscoverySettingsModule Settings { get; }

        /// <summary>Only filesystem discovery, persistence, and estimation are controlled; both coordinating modules are real.</summary>
        public Fixture()
        {
            State.UpdateState(state => state with { CurrentGameType = GameType.SkyrimSe });
            Config.UserConfigurationChanged.Returns(ConfigurationChanged);
            Config.SkipListChanged.Returns(Observable.Never<GameType>());
            Config.LoadUserConfigAsync(Arg.Any<CancellationToken>()).Returns(_ => Saved.Copy());
            Config.SaveUserConfigAsync(Arg.Any<UserConfiguration>(), Arg.Any<CancellationToken>())
                .Returns(call => { Saved = call.Arg<UserConfiguration>().Copy(); return Task.CompletedTask; });
            Config.GetGameDataFolderOverrideAsync(GameType.SkyrimSe, Arg.Any<CancellationToken>())
                .Returns(_ => Saved.GameDataFolderOverrides.GetValueOrDefault("SSE"));
            Config.SetGameDataFolderOverrideAsync(GameType.SkyrimSe, Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call => { Saved.GameDataFolderOverrides["SSE"] = call.ArgAt<string>(1); return Task.CompletedTask; });
            Config.GetSkipListAsync(Arg.Any<GameType>(), Arg.Any<GameVariant>(), Arg.Any<CancellationToken>()).Returns([]);
            Config.FlushPendingSavesAsync(Arg.Any<CancellationToken>()).Returns(new ConfigPersistenceResult(
                ConfigPersistenceStatusKind.Success, ConfigPersistenceOperationKind.Flush, 1, null));
            Loading.GetGameDataFolder(GameType.SkyrimSe, Arg.Any<string?>()).Returns(call => call.ArgAt<string?>(1) ?? Path.GetTempPath());
            Loading.TryGetPluginsAsync(GameType.SkyrimSe, Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(call => new PluginLoadingResult
                {
                    Status = PluginLoadingStatus.Success,
                    DataFolder = call.ArgAt<string?>(1),
                    Plugins = [new PluginInfo { FileName = "Chosen.esp", FullPath = Path.Combine(call.ArgAt<string>(1), "Chosen.esp"), DetectedGameType = GameType.SkyrimSe }]
                });
            var planner = new PluginRefreshDiscoveryPlanner(Config, Loading, Substitute.For<IMo2InstanceService>());
            var approximation = Substitute.For<IPluginIssueApproximationModule>();
            approximation.AnalyzeAsync(Arg.Any<PluginIssueApproximationModuleRequest>(),
                    Arg.Any<Action<PluginIssueApproximationModuleResult>>(), Arg.Any<CancellationToken>())
                .Returns(async call =>
                {
                    ApproximationStarted.TrySetResult();
                    await ReleaseApproximation.Task.WaitAsync(call.Arg<CancellationToken>());
                });
            var store = new PluginRefreshPublicationStore(new PluginRefreshAppStateMirror(State),
                new PluginRefreshCommandAvailabilityPolicy(), planner.GetAffordance(GameType.SkyrimSe, false));
            Refresh = new PluginRefreshModule(planner, approximation, State,
                new SkipListPolicy(Config, Substitute.For<IGameDetectionService>()), store, configurationService: Config);
            Settings = new DiscoverySettingsModule(Config, State, Refresh);
        }

        /// <summary>Cancels module-owned approximation and releases fixture resources.</summary>
        public void Dispose()
        {
            Settings.Dispose();
            Refresh.Dispose();
            ReleaseApproximation.TrySetResult();
            State.Dispose();
            ConfigurationChanged.Dispose();
        }
    }
}
