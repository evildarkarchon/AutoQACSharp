using System.Reactive.Linq;
using System.Reactive.Subjects;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Services.Cleaning;
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
    /// <summary>A rejected disk write must leave the unchanged accepted discovery usable, including rollback notifications.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedSettingsSave_PreservesAcceptedPublication(bool throws)
    {
        using var fixture = new Fixture();
        var initial = await fixture.Refresh.RefreshForSettingsAsync(GameType.SkyrimSe);
        var accepted = await fixture.Refresh.GetCurrentPublicationAsync();
        accepted.Freshness.IsFresh.Should().BeTrue();
        var original = fixture.Saved.Copy();
        fixture.Config.FlushPendingSavesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            fixture.RestoreConfiguration(original);
            if (throws) throw new IOException("The settings file is unavailable.");
            return Task.FromResult(new ConfigPersistenceResult(ConfigPersistenceStatusKind.Failed,
                ConfigPersistenceOperationKind.Flush, 1,
                new ConfigPersistenceFailure(ConfigPersistenceOperationKind.Flush,
                    ConfigPersistenceFailureKind.WriteFailed, "Could not save settings.", null, 1)));
        });

        var result = await fixture.Settings.ExecuteAsync(new DiscoverySettingsIntent.SetDisableSkipLists(true));
        var current = await fixture.Refresh.GetCurrentPublicationAsync();

        result.Status.Should().Be(DiscoverySettingsChangeStatus.SaveFailed);
        current.Freshness.IsFresh.Should().BeTrue();
        current.Generation.Should().Be(initial.Snapshot!.Generation);
        current.Rows.Select(row => row.Key).Should().Equal(accepted.Rows.Select(row => row.Key));
    }

    /// <summary>A canceled caller still leaves a saved discovery choice with a matching background publication.</summary>
    [Fact]
    public async Task CancellationDuringFlush_RepublishesSavedDiscoveryChoice()
    {
        using var fixture = new Fixture();
        var initial = await fixture.Refresh.RefreshForSettingsAsync(GameType.SkyrimSe);
        var flushEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishFlush = new TaskCompletionSource<ConfigPersistenceResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Config.FlushPendingSavesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            flushEntered.TrySetResult();
            return finishFlush.Task;
        });
        var replacementPublished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = fixture.Refresh.Snapshots.Subscribe(snapshot =>
        {
            if (snapshot.Generation > initial.Snapshot!.Generation && snapshot.Activity.IsIssueApproximationRefreshRunning)
                replacementPublished.TrySetResult();
        });
        using var cancellation = new CancellationTokenSource();

        var change = fixture.Settings.ExecuteAsync(new DiscoverySettingsIntent.SetDisableSkipLists(true), cancellation.Token);
        await flushEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();
        finishFlush.SetResult(new ConfigPersistenceResult(ConfigPersistenceStatusKind.Success,
            ConfigPersistenceOperationKind.Flush, 1, null));

        var result = await change.WaitAsync(TimeSpan.FromSeconds(5));
        result.Status.Should().Be(DiscoverySettingsChangeStatus.Canceled);
        result.SettingsSaved.Should().BeTrue();
        fixture.Saved.Settings.DisableSkipLists.Should().BeTrue();
        await replacementPublished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var publication = await fixture.Refresh.GetCurrentPublicationAsync();
        publication.Freshness.IsFresh.Should().BeTrue();
        publication.DiscoveryPlan!.DisableSkipLists.Should().BeTrue();
        publication.Rows.Should().ContainSingle();
    }

    /// <summary>Withdrawing acceptance after a save must not cancel the publication needed to restore freshness.</summary>
    [Fact]
    public async Task CancellationDuringDiscovery_CompletesCallerButPublishesSavedChoice()
    {
        using var fixture = new Fixture();
        var initial = await fixture.Refresh.RefreshForSettingsAsync(GameType.SkyrimSe);
        var loadingEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishLoading = new TaskCompletionSource<PluginLoadingResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Loading.TryGetPluginsAsync(GameType.SkyrimSe, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                loadingEntered.TrySetResult();
                return finishLoading.Task;
            });
        var replacementPublished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = fixture.Refresh.Snapshots.Subscribe(snapshot =>
        {
            if (snapshot.Generation > initial.Snapshot!.Generation && snapshot.Activity.IsIssueApproximationRefreshRunning)
                replacementPublished.TrySetResult();
        });
        using var cancellation = new CancellationTokenSource();

        var change = fixture.Settings.ExecuteAsync(new DiscoverySettingsIntent.SetDisableSkipLists(true), cancellation.Token);
        await loadingEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();
        var result = await change.WaitAsync(TimeSpan.FromSeconds(5));
        result.Status.Should().Be(DiscoverySettingsChangeStatus.Canceled);
        result.SettingsSaved.Should().BeTrue();
        finishLoading.SetResult(new PluginLoadingResult
        {
            Status = PluginLoadingStatus.Success,
            DataFolder = Path.GetTempPath(),
            Plugins = [new PluginInfo
            {
                FileName = "Chosen.esp",
                FullPath = Path.Combine(Path.GetTempPath(), "Chosen.esp"),
                DetectedGameType = GameType.SkyrimSe
            }]
        });

        await replacementPublished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var publication = await fixture.Refresh.GetCurrentPublicationAsync();
        publication.Freshness.IsFresh.Should().BeTrue();
        publication.DiscoveryPlan!.DisableSkipLists.Should().BeTrue();
        publication.Rows.Should().ContainSingle();
    }

    /// <summary>Existing case-insensitive game names retain their game and rows when another discovery setting changes.</summary>
    [Fact]
    public async Task LowercasePersistedGame_SettingChangeKeepsGameAndPublishesRows()
    {
        using var fixture = new Fixture();
        fixture.Saved.SelectedGame = "skyrimse";

        var result = await fixture.Settings.ExecuteAsync(new DiscoverySettingsIntent.SetDisableSkipLists(true));
        var publication = await fixture.Refresh.GetCurrentPublicationAsync();

        result.Status.Should().Be(DiscoverySettingsChangeStatus.Accepted);
        fixture.State.CurrentState.CurrentGameType.Should().Be(GameType.SkyrimSe);
        publication.GameType.Should().Be(GameType.SkyrimSe);
        publication.Rows.Should().ContainSingle();
        publication.Freshness.IsFresh.Should().BeTrue();
    }

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
            var admission = new CleaningAdmission();
            var coordinator = new ConfigPersistenceCoordinator(store, logger, admission, TimeSpan.Zero);
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
    [Theory]
    [InlineData("FalloutNewVegas")]
    [InlineData("falloutnewvegas")]
    public async Task DialogLoadOrderChange_ReplacesExistingSelectedGameOverride(string selectedGame)
    {
        var original = Path.GetTempFileName();
        var replacement = Path.GetTempFileName();
        try
        {
            using var fixture = new Fixture();
            fixture.Saved.SelectedGame = selectedGame;
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

    /// <summary>
    ///     A Cleaning reservation that drains an admitted save rejects the follow-up refresh operation, so no
    ///     discovery runs and the saved change completes as canceled.
    /// </summary>
    [Fact]
    public async Task CleaningReservedDuringSave_RejectsRefreshOperationAndCancelsChange()
    {
        using var fixture = new Fixture();
        var saveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Config.SaveUserConfigAsync(Arg.Any<UserConfiguration>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                saveStarted.TrySetResult();
                await continueSave.Task;
                fixture.CompleteSave(call.Arg<UserConfiguration>()!);
            });
        var change = fixture.Settings.ExecuteAsync(new DiscoverySettingsIntent.SetDisableSkipLists(true));
        await saveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var cleaning = fixture.Admission.EnterCleaningAsync();
        cleaning.IsCompleted.Should().BeFalse("the admitted save still holds the settings lease");

        continueSave.TrySetResult();
        using var cleaningLease = await cleaning.WaitAsync(TimeSpan.FromSeconds(5));
        var result = await change.WaitAsync(TimeSpan.FromSeconds(5));

        result.Status.Should().Be(DiscoverySettingsChangeStatus.Canceled);
        result.SettingsSaved.Should().BeTrue();
        fixture.Admission.CurrentRefresh.Should().BeNull("admission rejected the refresh operation");
        await fixture.Loading.DidNotReceiveWithAnyArgs().TryGetPluginsAsync(default, default, default);
    }

    /// <summary>Cleaning admission waits for a settings refresh it canceled to finish unwinding its discovery.</summary>
    [Fact]
    public async Task CleaningReservedDuringSettingsRefresh_WaitsForCanceledOperationToUnwind()
    {
        using var fixture = new Fixture();
        var loadingEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowUnwind = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Loading.TryGetPluginsAsync(GameType.SkyrimSe, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var token = call.Arg<CancellationToken>();
                loadingEntered.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    cancellationObserved.TrySetResult();
                }

                // Model an importer that keeps unwinding after it observes cancellation.
                await allowUnwind.Task;
                return new PluginLoadingResult { Status = PluginLoadingStatus.Success, Plugins = [] };
            });
        var change = fixture.Settings.ExecuteAsync(new DiscoverySettingsIntent.SetDisableSkipLists(true));
        await loadingEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var operation = fixture.Admission.CurrentRefresh!;
        Task<IDisposable>? cleaning = null;

        try
        {
            cleaning = fixture.Admission.EnterCleaningAsync();
            await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // The importer cannot unwind until released, so the operation cannot be disposed and the drain cannot end.
            operation.IsCurrent.Should().BeFalse();
            operation.Unwound.IsCompleted.Should().BeFalse();
            cleaning.IsCompleted.Should().BeFalse(
                "Cleaning admission must not complete while the canceled settings refresh is still unwinding");
        }
        finally
        {
            allowUnwind.TrySetResult();
            if (cleaning is not null)
            {
                using var cleaningLease = await cleaning.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        (await change.WaitAsync(TimeSpan.FromSeconds(5))).Status.Should().Be(DiscoverySettingsChangeStatus.Canceled);
        operation.Unwound.IsCompleted.Should().BeTrue();
    }

    /// <summary>
    ///     A newer change whose save fails must not report an older saved change as unrefreshable: the older choice is
    ///     still durable, so it is accepted from a publication of the durable settings.
    /// </summary>
    [Fact]
    public async Task NewerSaveFailure_AcceptsOlderSavedChangeFromDurableSettings()
    {
        using var fixture = new Fixture();
        var loadingEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishFirstLoad = new TaskCompletionSource<PluginLoadingResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loads = 0;
        fixture.Loading.TryGetPluginsAsync(GameType.SkyrimSe, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (Interlocked.Increment(ref loads) == 1)
                {
                    // Hold the older change's discovery so the newer change is admitted while it is in flight.
                    loadingEntered.TrySetResult();
                    return finishFirstLoad.Task;
                }

                return Task.FromResult(new PluginLoadingResult
                {
                    Status = PluginLoadingStatus.Success,
                    DataFolder = call.ArgAt<string?>(1),
                    Plugins = [new PluginInfo { FileName = "Chosen.esp", FullPath = Path.Combine(Path.GetTempPath(), "Chosen.esp"), DetectedGameType = GameType.SkyrimSe }]
                });
            });
        var older = fixture.Settings.ExecuteAsync(new DiscoverySettingsIntent.SetDisableSkipLists(true));
        await loadingEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var durable = fixture.Saved.Copy();
        fixture.Config.FlushPendingSavesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            fixture.RestoreConfiguration(durable);
            return Task.FromResult(new ConfigPersistenceResult(ConfigPersistenceStatusKind.Failed,
                ConfigPersistenceOperationKind.Flush, 2,
                new ConfigPersistenceFailure(ConfigPersistenceOperationKind.Flush,
                    ConfigPersistenceFailureKind.WriteFailed, "Could not save settings.", null, 2)));
        });

        try
        {
            var newer = await fixture.Settings.ExecuteAsync(new DiscoverySettingsIntent.SetMo2Mode(true))
                .WaitAsync(TimeSpan.FromSeconds(5));
            var olderResult = await older.WaitAsync(TimeSpan.FromSeconds(5));
            var publication = await fixture.Refresh.GetCurrentPublicationAsync();

            newer.Status.Should().Be(DiscoverySettingsChangeStatus.SaveFailed);
            olderResult.Status.Should().Be(DiscoverySettingsChangeStatus.Accepted);
            olderResult.SettingsSaved.Should().BeTrue();
            publication.Freshness.IsFresh.Should().BeTrue();
            publication.DiscoveryPlan!.DisableSkipLists.Should().BeTrue();
            publication.Configuration.Mo2ModeEnabled.Should().BeFalse("the newer choice was never saved");
        }
        finally
        {
            finishFirstLoad.TrySetResult(new PluginLoadingResult { Status = PluginLoadingStatus.Success, Plugins = [] });
        }
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
        public CleaningAdmission Admission { get; } = new();
        public UserConfiguration Saved { get; private set; } = new() { SelectedGame = "SkyrimSe" };
        public TaskCompletionSource ApproximationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource ReleaseApproximation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public PluginRefreshModule Refresh { get; }
        public DiscoverySettingsModule Settings { get; }

        /// <summary>Models the persistence coordinator restoring and publishing its last durable configuration.</summary>
        public void RestoreConfiguration(UserConfiguration configuration)
        {
            Saved = configuration.Copy();
            ConfigurationChanged.OnNext(Saved.Copy());
        }

        /// <summary>Records a durable write; the coordinator reports it through the save result, not a change notification.</summary>
        public void CompleteSave(UserConfiguration configuration)
        {
            Saved = configuration.Copy();
        }

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
            // Settings and Refresh share one admission, as production wiring does.
            var store = new PluginRefreshPublicationStore(new PluginRefreshAppStateMirror(State),
                new PluginRefreshCommandAvailabilityPolicy(Admission), planner.GetAffordance(GameType.SkyrimSe, false));
            Refresh = new PluginRefreshModule(planner, approximation, State,
                new SkipListPolicy(Config, Substitute.For<IGameDetectionService>()), store, Admission,
                configurationService: Config);
            Settings = new DiscoverySettingsModule(Config, State, Refresh, Admission);
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
