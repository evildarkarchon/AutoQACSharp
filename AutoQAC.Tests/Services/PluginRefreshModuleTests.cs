using System.Reactive.Linq;
using System.Reactive.Subjects;
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

namespace AutoQAC.Tests.Services;

public sealed class PluginRefreshModuleTests
{
    /// <summary>
    /// Verifies direct full refresh reuses accepted targets and publishes deterministic incremental progress.
    /// </summary>
    [Fact]
    public async Task RefreshGame_DirectMode_UsesAcceptedTargetsAndPublishesOrderedProgress()
    {
        var stateService = CreateStateWithRows(
            Plugin("Selected.esp"),
            Plugin("Completed.esp"),
            Plugin("NotStarted.esp"));
        stateService.UpdateExcludedPlugins(_ => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Game\Data\NotStarted.esp"
        });
        var configurationService = CreateConfigurationServiceWithSkipList(
            GameType.SkyrimSe,
            ["Completed.esp"]);
        var approximationModule = new ResultPluginIssueApproximationModule();
        using var sut = CreateModule(
            stateService,
            configurationService: configurationService,
            approximationModule: approximationModule);
        var snapshots = new List<PluginRefreshSnapshot>();
        using var subscription = sut.Snapshots.Subscribe(snapshots.Add);

        var final = await sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));
        var publication = await sut.GetCurrentPublicationAsync();

        var request = approximationModule.Requests.Should().ContainSingle().Subject;
        var source = request.Source.Should()
            .BeOfType<PluginIssueApproximationModuleSource.ResolvedLoadOrder>()
            .Subject;
        source.BaseDataFolder.Should().Be(@"C:\Game\Data");
        source.Rows.Should().HaveCount(3);
        for (var index = 0; index < source.Rows.Count; index++)
        {
            source.Rows[index].Should().BeSameAs(publication.Rows[index].Key);
        }

        request.Targets.Should().HaveCount(2);
        request.Targets[0].Should().BeSameAs(
            publication.Rows.Single(row => row.Plugin.FileName == "Selected.esp").Key);
        request.Targets[1].Should().BeSameAs(
            publication.Rows.Single(row => row.Plugin.FileName == "NotStarted.esp").Key);
        publication.Rows.Should().Contain(row =>
            row.Plugin.FileName == "Completed.esp" &&
            row.IsSkippedByPolicy &&
            !row.IsVisible &&
            row.Plugin.Approximation.Status == PluginIssueApproximationStatus.Unavailable);
        publication.Rows.Should().Contain(row =>
            row.Plugin.FileName == "NotStarted.esp" &&
            !row.IsSelected &&
            row.Plugin.Approximation.Status == PluginIssueApproximationStatus.Available);
        publication.Rows.Should().NotContain(row =>
            row.Plugin.Approximation.Status == PluginIssueApproximationStatus.Pending);
        stateService.CurrentState.PluginsToClean.Should().Equal(
            publication.Rows.Select(row => row.Plugin),
            "the accepted keyed results must be mirrored into AppState compatibility rows");
        snapshots
            .Where(snapshot => snapshot.StatusText.StartsWith("Analyzing ", StringComparison.Ordinal))
            .Select(snapshot => snapshot.StatusText)
            .Should()
            .Equal(
                "Analyzing 0 of 2 plugins.",
                "Analyzing 1 of 2 plugins.",
                "Analyzing 2 of 2 plugins.");
        final.StatusText.Should().Be("Refreshed 2 plugin approximations.");
    }

    /// <summary>
    /// Verifies a module result assembled from parts of two targets cannot correlate to either publication row.
    /// </summary>
    [Fact]
    public async Task RefreshGame_WhenModuleReturnsMixedTargetIdentity_IgnoresResultAndFinalizesTargetsUnavailable()
    {
        var stateService = new StateService();
        var approximationModule = new ResultPluginIssueApproximationModule(
            (request, onResult, _) =>
            {
                onResult(new PluginIssueApproximationModuleResult(
                    new PluginRefreshRowKey(
                        request.Targets[0].FileName,
                        request.Targets[1].FullPath),
                    PluginIssueApproximation.Available(9, 9, 9)));
                return Task.CompletedTask;
            });
        using var sut = CreateModule(stateService, approximationModule: approximationModule);
        var snapshots = new List<PluginRefreshSnapshot>();
        using var subscription = sut.Snapshots.Subscribe(snapshots.Add);

        var final = await sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));
        var publication = await sut.GetCurrentPublicationAsync();

        publication.Rows.Should().OnlyContain(row =>
            row.Plugin.Approximation.Status == PluginIssueApproximationStatus.Unavailable);
        stateService.CurrentState.PluginsToClean.Should().OnlyContain(plugin =>
            plugin.Approximation.Status == PluginIssueApproximationStatus.Unavailable);
        snapshots
            .Where(snapshot => snapshot.StatusText.StartsWith("Analyzing ", StringComparison.Ordinal))
            .Select(snapshot => snapshot.StatusText)
            .Should()
            .Equal("Analyzing 0 of 3 plugins.");
        final.StatusText.Should().Be("Refreshed 0 plugin approximations.");
    }

    /// <summary>
    /// Verifies MO2 full refresh retains every accepted conflict-winning row as resolved dependency context.
    /// </summary>
    [Fact]
    public async Task RefreshGame_Mo2Mode_UsesCompleteAcceptedResolvedSource()
    {
        var stateService = new StateService();
        var configurationService = CreateConfigurationServiceWithSkipList(
            GameType.SkyrimSe,
            ["Hidden.esm"]);
        var configuration = new PluginRefreshConfigurationProjection(
            LoadOrderPath: null,
            GameDataFolder: @"C:\Skyrim\Data",
            HasGameDataFolderOverride: true,
            XEditPath: null,
            Mo2Path: @"C:\MO2\ModOrganizer.exe",
            Mo2ModeEnabled: true,
            Mo2InstancePath: @"C:\MO2",
            IsMo2InstanceOverride: true,
            IsMo2InstanceValid: true,
            AvailableProfiles: ["Default"],
            SelectedProfile: "Default",
            CleaningTimeout: 300);
        var plan = new PluginRefreshDiscoveryPlan(
            GameType.SkyrimSe,
            PluginRefreshDiscoveryMode.Mo2LoadOrderFile,
            configuration,
            DisableSkipLists: false,
            CanAttemptIssueApproximation: true,
            DataFolderPath: @"C:\Skyrim\Data",
            LoadOrderPath: null,
            Mo2LoadOrderPath: @"C:\MO2\profiles\Default\loadorder.txt",
            Mo2PathMap: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Mo2BaseDataFolder: @"C:\Skyrim\Data");
        var loadedRows = new[]
        {
            Plugin("First.esm", @"C:\MO2\mods\WinnerA\First.esm"),
            Plugin("Hidden.esm", @"C:\MO2\mods\WinnerB\Hidden.esm"),
            Plugin("Last.esp", @"C:\MO2\overwrite\Last.esp")
        };
        var approximationModule = new ResultPluginIssueApproximationModule();
        using var sut = CreateModule(
            stateService,
            configurationService: configurationService,
            approximationModule: approximationModule,
            discoveryPlanner: CreateReadyDiscoveryPlanner(plan, loadedRows));

        await sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));
        var publication = await sut.GetCurrentPublicationAsync();

        var request = approximationModule.Requests.Should().ContainSingle().Subject;
        var source = request.Source.Should()
            .BeOfType<PluginIssueApproximationModuleSource.ResolvedLoadOrder>()
            .Subject;
        source.BaseDataFolder.Should().Be(@"C:\Skyrim\Data");
        source.Rows.Should().HaveCount(3);
        for (var index = 0; index < source.Rows.Count; index++)
        {
            source.Rows[index].Should().BeSameAs(publication.Rows[index].Key);
        }

        request.Targets.Should().HaveCount(2);
        request.Targets[0].Should().BeSameAs(publication.Rows[0].Key);
        request.Targets[1].Should().BeSameAs(publication.Rows[2].Key);
        source.Rows.Select(row => row.FullPath).Should().Equal(
            @"C:\MO2\mods\WinnerA\First.esm",
            @"C:\MO2\mods\WinnerB\Hidden.esm",
            @"C:\MO2\overwrite\Last.esp");
        publication.Rows.Should().NotContain(row =>
            row.Plugin.Approximation.Status == PluginIssueApproximationStatus.Pending);
    }

    /// <summary>
    /// Verifies an operation failure preserves completed results and terminalizes every unfinished target.
    /// </summary>
    [Fact]
    public async Task RefreshGame_WhenApproximationOperationFails_PreservesCompletedAndFinalizesUnfinished()
    {
        var stateService = new StateService();
        var approximationModule = new ResultPluginIssueApproximationModule(
            (request, onResult, _) =>
            {
                onResult(new PluginIssueApproximationModuleResult(
                    request.Targets[0],
                    PluginIssueApproximation.Available(7, 8, 9)));
                throw new InvalidOperationException("Synthetic approximation failure");
            });
        using var sut = CreateModule(stateService, approximationModule: approximationModule);

        var final = await sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));
        var publication = await sut.GetCurrentPublicationAsync();

        publication.Rows.Should().Contain(row =>
            row.Plugin.FileName == "Selected.esp" &&
            row.Plugin.Approximation == PluginIssueApproximation.Available(7, 8, 9));
        publication.Rows
            .Where(row => row.Plugin.FileName != "Selected.esp")
            .Should()
            .OnlyContain(row => row.Plugin.Approximation.Status == PluginIssueApproximationStatus.Unavailable);
        publication.Rows.Should().NotContain(row =>
            row.Plugin.Approximation.Status == PluginIssueApproximationStatus.Pending);
        final.Activity.Should().Be(new PluginRefreshActivity(false, false));
        final.StatusText.Should().Be("Approximation refresh failed.");
    }

    /// <summary>
    /// Verifies initial-refresh cancellation preserves completed results and rejects late callback publication.
    /// </summary>
    [Fact]
    public async Task RefreshGame_WhenInitialApproximationCanceled_PreservesCompletedAndFinalizesUnstarted()
    {
        var stateService = new StateService();
        var configurationService = CreateConfigurationServiceWithSkipList(
            GameType.SkyrimSe,
            ["Completed.esp"]);
        var firstResultPublished = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Action<PluginIssueApproximationModuleResult>? capturedCallback = null;
        PluginIssueApproximationModuleRequest? capturedRequest = null;
        var approximationModule = new ResultPluginIssueApproximationModule(
            async (request, onResult, ct) =>
            {
                capturedRequest = request;
                capturedCallback = onResult;
                onResult(new PluginIssueApproximationModuleResult(
                    request.Targets[0],
                    PluginIssueApproximation.Available(4, 5, 6)));
                firstResultPublished.TrySetResult();
                await Task.Delay(TimeSpan.FromMinutes(5), ct);
            });
        using var sut = CreateModule(
            stateService,
            configurationService: configurationService,
            approximationModule: approximationModule);

        var refresh = sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));
        await firstResultPublished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var canceled = await sut.ExecuteAsync(new PluginRefreshIntent.Cancel(PluginRefreshCancelReason.Manual));
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));

        capturedCallback!.Invoke(new PluginIssueApproximationModuleResult(
            capturedRequest!.Targets[1],
            PluginIssueApproximation.Available(99, 99, 99)));
        var publication = await sut.GetCurrentPublicationAsync();

        publication.Rows.Should().Contain(row =>
            row.Plugin.FileName == "Selected.esp" &&
            row.Plugin.Approximation == PluginIssueApproximation.Available(4, 5, 6));
        publication.Rows
            .Where(row => row.Plugin.FileName != "Selected.esp")
            .Should()
            .OnlyContain(row => row.Plugin.Approximation.Status == PluginIssueApproximationStatus.Unavailable);
        publication.Rows.Should().NotContain(row =>
            row.Plugin.Approximation.Status == PluginIssueApproximationStatus.Pending);
        canceled.Activity.Should().Be(new PluginRefreshActivity(false, false));
        canceled.StatusText.Should().Be("Approximation refresh canceled.");
    }

    /// <summary>
    /// Verifies a superseded generation cannot publish a late authoritative result.
    /// </summary>
    [Fact]
    public async Task RefreshGame_WhenSuperseded_IgnoresLateAuthoritativeResult()
    {
        var stateService = new StateService();
        var firstResultPublished = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Action<PluginIssueApproximationModuleResult>? firstCallback = null;
        PluginIssueApproximationModuleRequest? firstRequest = null;
        var invocation = 0;
        var approximationModule = new ResultPluginIssueApproximationModule(
            async (request, onResult, ct) =>
            {
                if (Interlocked.Increment(ref invocation) == 1)
                {
                    firstRequest = request;
                    firstCallback = onResult;
                    onResult(new PluginIssueApproximationModuleResult(
                        request.Targets[0],
                        PluginIssueApproximation.Available(1, 1, 1)));
                    firstResultPublished.TrySetResult();
                    await Task.Delay(TimeSpan.FromMinutes(5), ct);
                    return;
                }

                foreach (var target in request.Targets)
                {
                    onResult(new PluginIssueApproximationModuleResult(
                        target,
                        PluginIssueApproximation.Available(9, 9, 9)));
                }
            });
        using var sut = CreateModule(stateService, approximationModule: approximationModule);

        var firstRefresh = sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));
        await firstResultPublished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));
        await firstRefresh.WaitAsync(TimeSpan.FromSeconds(5));

        firstCallback!.Invoke(new PluginIssueApproximationModuleResult(
            firstRequest!.Targets[1],
            PluginIssueApproximation.Available(99, 99, 99)));
        var publication = await sut.GetCurrentPublicationAsync();

        publication.Rows.Should().OnlyContain(row =>
            row.Plugin.Approximation == PluginIssueApproximation.Available(9, 9, 9));
        publication.Rows.Should().NotContain(row =>
            row.Plugin.Approximation.Status == PluginIssueApproximationStatus.Pending);
        publication.StatusText.Should().Be("Refreshed 3 plugin approximations.");
    }

    [Fact]
    public async Task RefreshGame_EmitsSnapshotsAndWritesFullRowsToAppState()
    {
        var stateService = new StateService();
        using var sut = CreateModule(stateService);
        var snapshots = new List<PluginRefreshSnapshot>();
        using var subscription = sut.Snapshots.Subscribe(snapshots.Add);

        var final = await sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));

        snapshots.Should().Contain(snapshot => snapshot.Activity.IsPluginRefreshRunning);
        snapshots.Should().Contain(snapshot => snapshot.Rows.Count > 0);
        final.Activity.IsPluginRefreshRunning.Should().BeFalse();
        final.Activity.IsIssueApproximationRefreshRunning.Should().BeFalse();
        final.StatusText.Should().StartWith("Refreshed ");
        stateService.CurrentState.PluginsToClean.Should().NotBeEmpty(
            "Cleaning session compatibility still reads full rows from AppState");
    }

    [Fact]
    public async Task RefreshGame_HidesSkipListRowsButKeepsThemInAppState()
    {
        var stateService = new StateService();
        var configurationService = CreateConfigurationServiceWithSkipList(
            GameType.SkyrimSe,
            ["Completed.esp"]);
        using var sut = CreateModule(stateService, configurationService: configurationService);

        var final = await sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));

        final.Rows.Should().NotContain(row => row.FileName == "Completed.esp");
        stateService.CurrentState.PluginsToClean.Should().Contain(plugin =>
            plugin.FileName == "Completed.esp" && plugin.IsInSkipList);
    }

    [Fact]
    public async Task GetCurrentPublicationAsync_AfterRefresh_PublishesPlanAndFullRows()
    {
        var stateService = new StateService();
        var configurationService = CreateConfigurationServiceWithSkipList(
            GameType.SkyrimSe,
            ["Completed.esp"]);
        using var sut = CreateModule(stateService, configurationService: configurationService);

        var snapshot = await sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));
        var publication = await sut.GetCurrentPublicationAsync();

        publication.Freshness.IsFresh.Should().BeTrue();
        publication.DiscoveryPlan.Should().NotBeNull();
        publication.Rows.Should().Contain(row =>
            row.Plugin.FileName == "Completed.esp" &&
            !row.IsVisible &&
            row.IsSkippedByPolicy);
        publication.VisibleRows.Select(row => row.FileName)
            .Should().Equal(snapshot.Rows.Select(row => row.FileName));
    }

    [Fact]
    public async Task GetCurrentPublicationAsync_DelegatesFreshnessCheckWithCurrentAppStateContext()
    {
        var stateService = new StateService();
        var configurationService = CreateConfigurationService();
        var plan = CreateWiringPlan();
        var tokenSourcePlanner = new PluginRefreshDiscoveryPlanner(
            configurationService,
            new TestPluginLoadingService(),
            Substitute.For<IMo2InstanceService>());
        var freshnessToken = await tokenSourcePlanner.CreateFreshnessTokenAsync(plan);
        var expectedFreshness = new PluginRefreshFreshness(false, PluginRefreshStalenessReason.LoadOrderPathChanged);
        var discoveryPlanner = Substitute.For<IPluginRefreshDiscoveryPlanner>();
        discoveryPlanner.GetAffordance(Arg.Any<GameType>(), Arg.Any<bool>())
            .Returns(call => new PluginRefreshGameAffordance(call.ArgAt<GameType>(0), true, false, false));
        discoveryPlanner.CreatePlanAsync(Arg.Any<PluginRefreshDiscoveryPlanRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PluginRefreshDiscoveryPlanResult(PluginRefreshDiscoveryPlanStatus.Ready, plan, plan.Configuration));
        discoveryPlanner.CreateFreshnessTokenAsync(plan, Arg.Any<CancellationToken>())
            .Returns(freshnessToken);
        discoveryPlanner.LoadPluginsAsync(plan, Arg.Any<CancellationToken>())
            .Returns(new PluginRefreshDiscoveredPlugins(plan, [Plugin("Selected.esp")], null));
        discoveryPlanner.CheckFreshnessAsync(
                freshnessToken,
                Arg.Any<PluginRefreshDiscoveryFreshnessContext>(),
                Arg.Any<CancellationToken>())
            .Returns(expectedFreshness);
        using var sut = CreateModule(
            stateService,
            configurationService: configurationService,
            discoveryPlanner: discoveryPlanner);

        await sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));
        stateService.UpdateState(state => state with
        {
            CurrentGameType = GameType.SkyrimSe,
            Mo2ModeEnabled = false,
            LoadOrderPath = @"C:\Changed\plugins.txt",
            Mo2Profile = "Survival"
        });

        var publication = await sut.GetCurrentPublicationAsync();

        publication.Freshness.Should().Be(expectedFreshness);
        await discoveryPlanner.Received().CheckFreshnessAsync(
            freshnessToken,
            Arg.Is<PluginRefreshDiscoveryFreshnessContext>(context =>
                context.CurrentGameType == GameType.SkyrimSe &&
                !context.Mo2ModeEnabled &&
                context.LoadOrderPath == @"C:\Changed\plugins.txt" &&
                context.Mo2Profile == "Survival"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DiscoveryAffectingStateChange_EmitsSnapshotWithoutRefreshingRows()
    {
        var stateService = new StateService();
        var configurationService = CreateConfigurationService();
        var plan = CreateWiringPlan();
        var tokenSourcePlanner = new PluginRefreshDiscoveryPlanner(
            configurationService,
            new TestPluginLoadingService(),
            Substitute.For<IMo2InstanceService>());
        var freshnessToken = await tokenSourcePlanner.CreateFreshnessTokenAsync(plan);
        var discoveryPlanner = Substitute.For<IPluginRefreshDiscoveryPlanner>();
        discoveryPlanner.GetAffordance(Arg.Any<GameType>(), Arg.Any<bool>())
            .Returns(call => new PluginRefreshGameAffordance(call.ArgAt<GameType>(0), true, false, false));
        discoveryPlanner.CreatePlanAsync(Arg.Any<PluginRefreshDiscoveryPlanRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PluginRefreshDiscoveryPlanResult(PluginRefreshDiscoveryPlanStatus.Ready, plan, plan.Configuration));
        discoveryPlanner.CreateFreshnessTokenAsync(plan, Arg.Any<CancellationToken>())
            .Returns(freshnessToken);
        discoveryPlanner.LoadPluginsAsync(plan, Arg.Any<CancellationToken>())
            .Returns(new PluginRefreshDiscoveredPlugins(plan, [Plugin("Selected.esp")], null));
        discoveryPlanner.CheckFreshnessAsync(
                freshnessToken,
                Arg.Any<PluginRefreshDiscoveryFreshnessContext>(),
                Arg.Any<CancellationToken>())
            .Returns(new PluginRefreshFreshness(false, PluginRefreshStalenessReason.LoadOrderPathChanged));
        using var sut = CreateModule(
            stateService,
            configurationService: configurationService,
            discoveryPlanner: discoveryPlanner);
        await sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));
        var rowCountBefore = stateService.CurrentState.PluginsToClean.Count;
        var freshnessNotification = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ignoredCurrent = false;
        using var subscription = sut.Snapshots.Subscribe(_ =>
        {
            if (!ignoredCurrent)
            {
                ignoredCurrent = true;
                return;
            }

            freshnessNotification.TrySetResult();
        });

        stateService.UpdateState(state => state with { LoadOrderPath = @"C:\Changed\plugins.txt" });

        await freshnessNotification.Task.WaitAsync(TimeSpan.FromSeconds(5));
        stateService.CurrentState.PluginsToClean.Should().HaveCount(rowCountBefore,
            "staleness publication must not auto-refresh or clear rows");
        var publication = await sut.GetCurrentPublicationAsync();
        publication.Freshness.StalenessReason.Should().Be(PluginRefreshStalenessReason.LoadOrderPathChanged);
    }

    [Fact]
    public async Task UserConfigurationChanged_EmitsSnapshotWithoutRefreshingRows()
    {
        var stateService = new StateService();
        var configurationService = CreateConfigurationService();
        var userConfigurationChanged = new Subject<UserConfiguration>();
        configurationService.UserConfigurationChanged.Returns(userConfigurationChanged);
        var plan = CreateWiringPlan();
        var tokenSourcePlanner = new PluginRefreshDiscoveryPlanner(
            configurationService,
            new TestPluginLoadingService(),
            Substitute.For<IMo2InstanceService>());
        var freshnessToken = await tokenSourcePlanner.CreateFreshnessTokenAsync(plan);
        var discoveryPlanner = CreateStaleDiscoveryPlanner(
            plan,
            freshnessToken,
            PluginRefreshStalenessReason.SkipListSettingsChanged);
        using var sut = CreateModule(
            stateService,
            configurationService: configurationService,
            discoveryPlanner: discoveryPlanner);
        await sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));
        var rowCountBefore = stateService.CurrentState.PluginsToClean.Count;
        var freshnessNotification = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ignoredCurrent = false;
        using var subscription = sut.Snapshots.Subscribe(_ =>
        {
            if (!ignoredCurrent)
            {
                ignoredCurrent = true;
                return;
            }

            freshnessNotification.TrySetResult();
        });

        userConfigurationChanged.OnNext(new UserConfiguration());

        await freshnessNotification.Task.WaitAsync(TimeSpan.FromSeconds(5));
        stateService.CurrentState.PluginsToClean.Should().HaveCount(rowCountBefore,
            "configuration staleness publication must not auto-refresh or clear rows");
        var publication = await sut.GetCurrentPublicationAsync();
        publication.Freshness.StalenessReason.Should().Be(PluginRefreshStalenessReason.SkipListSettingsChanged);
        await discoveryPlanner.Received().CheckFreshnessAsync(
            freshnessToken,
            Arg.Any<PluginRefreshDiscoveryFreshnessContext>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SkipListChanged_EmitsSnapshotWithoutRefreshingRows()
    {
        var stateService = new StateService();
        var configurationService = CreateConfigurationService();
        var skipListChanged = new Subject<GameType>();
        configurationService.SkipListChanged.Returns(skipListChanged);
        var plan = CreateWiringPlan();
        var tokenSourcePlanner = new PluginRefreshDiscoveryPlanner(
            configurationService,
            new TestPluginLoadingService(),
            Substitute.For<IMo2InstanceService>());
        var freshnessToken = await tokenSourcePlanner.CreateFreshnessTokenAsync(plan);
        var discoveryPlanner = CreateStaleDiscoveryPlanner(
            plan,
            freshnessToken,
            PluginRefreshStalenessReason.SkipListSettingsChanged);
        using var sut = CreateModule(
            stateService,
            configurationService: configurationService,
            discoveryPlanner: discoveryPlanner);
        await sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));
        var rowCountBefore = stateService.CurrentState.PluginsToClean.Count;
        var freshnessNotification = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ignoredCurrent = false;
        using var subscription = sut.Snapshots.Subscribe(_ =>
        {
            if (!ignoredCurrent)
            {
                ignoredCurrent = true;
                return;
            }

            freshnessNotification.TrySetResult();
        });

        skipListChanged.OnNext(GameType.SkyrimSe);

        await freshnessNotification.Task.WaitAsync(TimeSpan.FromSeconds(5));
        stateService.CurrentState.PluginsToClean.Should().HaveCount(rowCountBefore,
            "Skip list staleness publication must not auto-refresh or clear rows");
        var publication = await sut.GetCurrentPublicationAsync();
        publication.Freshness.StalenessReason.Should().Be(PluginRefreshStalenessReason.SkipListSettingsChanged);
        await discoveryPlanner.Received().CheckFreshnessAsync(
            freshnessToken,
            Arg.Any<PluginRefreshDiscoveryFreshnessContext>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCurrentPublicationAsync_WhenNoPlanAccepted_ReturnsMissingFreshness()
    {
        var stateService = new StateService();
        using var sut = CreateModule(stateService);

        var publication = await sut.GetCurrentPublicationAsync();

        publication.Freshness.Should().Be(PluginRefreshFreshness.Missing);
    }

    [Fact]
    public async Task RefreshGame_WhenSameListRefreshes_PreservesPublicationSelection()
    {
        var stateService = new StateService();
        using var sut = CreateModule(stateService);
        var loaded = await sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));
        var selected = loaded.Rows.Single(row => row.FileName == "Selected.esp");
        await sut.ExecuteAsync(new PluginRefreshIntent.ChangeSelection(
            new PluginSelectionChange.SetOne(selected.Key, IsSelected: false)));

        await sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));
        var publication = await sut.GetCurrentPublicationAsync();

        publication.Rows.Should().Contain(row =>
            row.Plugin.FileName == "Selected.esp" && !row.IsSelected);
        publication.VisibleRows.Should().Contain(row =>
            row.FileName == "Selected.esp" && !row.IsSelected);
        stateService.CurrentState.ExcludedPluginPaths.Should().Contain(selected.FullPath);
    }

    /// <summary>
    /// Verifies selected reanalysis rejects a missing accepted publication before planning or mutation.
    /// </summary>
    [Fact]
    public async Task RefreshSelectedIssueApproximations_WhenPublicationMissing_RequiresFullRefreshWithoutPlanningOrAnalysis()
    {
        var stateService = CreateStateWithRows(
            Plugin("Selected.esp", approximation: PluginIssueApproximation.Available(3, 2, 1)));
        var plan = CreateWiringPlan() with
        {
            CanAttemptIssueApproximation = true,
            DataFolderPath = @"C:\Game\Data"
        };
        var discoveryPlanner = CreateReadyDiscoveryPlanner(plan, [Plugin("Selected.esp")]);
        var approximationModule = new ResultPluginIssueApproximationModule();
        using var sut = CreateModule(
            stateService,
            approximationModule: approximationModule,
            discoveryPlanner: discoveryPlanner);
        var snapshots = new List<PluginRefreshSnapshot>();
        using var subscription = sut.Snapshots.Subscribe(snapshots.Add);

        var final = await sut.ExecuteAsync(new PluginRefreshIntent.RefreshSelectedIssueApproximations());

        final.StatusText.Should().Be(
            "Run a full Plugin refresh before refreshing selected approximations.");
        final.Activity.Should().Be(new PluginRefreshActivity(false, false));
        final.Rows.Should().ContainSingle(row =>
            row.FileName == "Selected.esp" &&
            row.Approximation == PluginIssueApproximation.Available(3, 2, 1));
        snapshots.Should().NotContain(snapshot =>
            snapshot.Rows.Any(row => row.Approximation.Status == PluginIssueApproximationStatus.Pending));
        approximationModule.Requests.Should().BeEmpty();
        await discoveryPlanner.DidNotReceive()
            .CreatePlanAsync(
                Arg.Any<PluginRefreshDiscoveryPlanRequest>(),
                Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies a stale accepted publication is rejected before any selected row becomes Pending.
    /// </summary>
    [Fact]
    public async Task RefreshSelectedIssueApproximations_WhenPublicationStale_RejectsBeforePending()
    {
        var stateService = new StateService();
        var plan = CreateWiringPlan() with
        {
            CanAttemptIssueApproximation = true,
            DataFolderPath = @"C:\Game\Data"
        };
        var discoveryPlanner = CreateReadyDiscoveryPlanner(
            plan,
            [
                Plugin("First.esp"),
                Plugin("Second.esp")
            ]);
        var approximationModule = new ResultPluginIssueApproximationModule();
        using var sut = CreateModule(
            stateService,
            approximationModule: approximationModule,
            discoveryPlanner: discoveryPlanner);

        await sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));
        var accepted = await sut.GetCurrentPublicationAsync();
        approximationModule.Requests.Clear();
        discoveryPlanner.ClearReceivedCalls();
        discoveryPlanner.CheckFreshnessAsync(
                Arg.Any<PluginRefreshDiscoveryFreshnessToken>(),
                Arg.Any<PluginRefreshDiscoveryFreshnessContext>(),
                Arg.Any<CancellationToken>())
            .Returns(new PluginRefreshFreshness(
                false,
                PluginRefreshStalenessReason.LoadOrderPathChanged));
        var snapshots = new List<PluginRefreshSnapshot>();
        using var subscription = sut.Snapshots.Subscribe(snapshots.Add);

        var final = await sut.ExecuteAsync(new PluginRefreshIntent.RefreshSelectedIssueApproximations());
        var publication = await sut.GetCurrentPublicationAsync();

        final.StatusText.Should().Be(
            "Run a full Plugin refresh before refreshing selected approximations.");
        publication.Rows.Select(row => row.Plugin.Approximation)
            .Should()
            .Equal(accepted.Rows.Select(row => row.Plugin.Approximation));
        snapshots.Should().NotContain(snapshot =>
            snapshot.Rows.Any(row => row.Approximation.Status == PluginIssueApproximationStatus.Pending));
        approximationModule.Requests.Should().BeEmpty();
        await discoveryPlanner.Received()
            .CheckFreshnessAsync(
                Arg.Any<PluginRefreshDiscoveryFreshnessToken>(),
                Arg.Any<PluginRefreshDiscoveryFreshnessContext>(),
                Arg.Any<CancellationToken>());
        await discoveryPlanner.DidNotReceive()
            .CreatePlanAsync(
                Arg.Any<PluginRefreshDiscoveryPlanRequest>(),
                Arg.Any<CancellationToken>());
        await discoveryPlanner.DidNotReceive()
            .CreateFreshnessTokenAsync(
                Arg.Any<PluginRefreshDiscoveryPlan>(),
                Arg.Any<CancellationToken>());
        await discoveryPlanner.DidNotReceive()
            .LoadPluginsAsync(
                Arg.Any<PluginRefreshDiscoveryPlan>(),
                Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies a settings change during freshness validation invalidates the selected operation's lease.
    /// </summary>
    [Fact]
    public async Task RefreshSelectedIssueApproximations_WhenSettingsChangeDuringFreshnessCheck_DoesNotStartAnalysis()
    {
        var stateService = new StateService();
        var plan = CreateWiringPlan() with
        {
            CanAttemptIssueApproximation = true,
            DataFolderPath = @"C:\Game\Data"
        };
        var discoveryPlanner = CreateReadyDiscoveryPlanner(plan, [Plugin("Selected.esp")]);
        var freshnessCheckStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFreshnessCheck = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        discoveryPlanner.CheckFreshnessAsync(
                Arg.Any<PluginRefreshDiscoveryFreshnessToken>(),
                Arg.Any<PluginRefreshDiscoveryFreshnessContext>(),
                Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                freshnessCheckStarted.TrySetResult();
                await releaseFreshnessCheck.Task;
                return PluginRefreshFreshness.Fresh;
            });
        var approximationModule = new ResultPluginIssueApproximationModule();
        using var sut = CreateModule(
            stateService,
            approximationModule: approximationModule,
            discoveryPlanner: discoveryPlanner);

        await sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));
        approximationModule.Requests.Clear();
        var snapshots = new List<PluginRefreshSnapshot>();
        using var subscription = sut.Snapshots.Subscribe(snapshots.Add);
        var selectedRefresh = sut.ExecuteAsync(
            new PluginRefreshIntent.RefreshSelectedIssueApproximations());
        await freshnessCheckStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        stateService.UpdateState(state => state with { LoadOrderPath = @"C:\Changed\plugins.txt" });
        releaseFreshnessCheck.TrySetResult();
        var final = await selectedRefresh.WaitAsync(TimeSpan.FromSeconds(5));

        final.StatusText.Should().Be(
            "Run a full Plugin refresh before refreshing selected approximations.");
        approximationModule.Requests.Should().BeEmpty();
        snapshots.Should().NotContain(snapshot =>
            snapshot.Rows.Any(row => row.Approximation.Status == PluginIssueApproximationStatus.Pending));
    }

    /// <summary>
    /// Verifies selected reanalysis reuses complete accepted MO2 rows and ordered selected identities.
    /// </summary>
    [Fact]
    public async Task RefreshSelectedIssueApproximations_UsesAcceptedPublicationRowsAndOrderedSelectedKeys()
    {
        var stateService = new StateService();
        var configurationService = CreateConfigurationServiceWithSkipList(
            GameType.SkyrimSe,
            ["Hidden.esm"]);
        var configuration = new PluginRefreshConfigurationProjection(
            LoadOrderPath: null,
            GameDataFolder: @"C:\Skyrim\Data",
            HasGameDataFolderOverride: true,
            XEditPath: null,
            Mo2Path: @"C:\MO2\ModOrganizer.exe",
            Mo2ModeEnabled: true,
            Mo2InstancePath: @"C:\MO2",
            IsMo2InstanceOverride: true,
            IsMo2InstanceValid: true,
            AvailableProfiles: ["Default"],
            SelectedProfile: "Default",
            CleaningTimeout: 300);
        var plan = new PluginRefreshDiscoveryPlan(
            GameType.SkyrimSe,
            PluginRefreshDiscoveryMode.Mo2LoadOrderFile,
            configuration,
            DisableSkipLists: false,
            CanAttemptIssueApproximation: true,
            DataFolderPath: @"C:\Skyrim\Data",
            LoadOrderPath: null,
            Mo2LoadOrderPath: @"C:\MO2\profiles\Default\loadorder.txt",
            Mo2PathMap: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Mo2BaseDataFolder: @"C:\Skyrim\Data");
        var loadedRows = new[]
        {
            Plugin("First.esm", @"C:\MO2\mods\WinnerA\First.esm"),
            Plugin("Hidden.esm", @"C:\MO2\mods\WinnerB\Hidden.esm"),
            Plugin("Unselected.esp", @"C:\MO2\mods\WinnerC\Unselected.esp"),
            Plugin("Last.esp", @"C:\MO2\overwrite\Last.esp")
        };
        var approximationModule = new ResultPluginIssueApproximationModule();
        using var sut = CreateModule(
            stateService,
            configurationService: configurationService,
            approximationModule: approximationModule,
            discoveryPlanner: CreateReadyDiscoveryPlanner(plan, loadedRows));

        await sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));
        var accepted = await sut.GetCurrentPublicationAsync();
        var unselected = accepted.Rows.Single(row => row.Plugin.FileName == "Unselected.esp");

        await sut.ExecuteAsync(new PluginRefreshIntent.ChangeSelection(
            new PluginSelectionChange.SetOne(unselected.Key, false)));
        approximationModule.Requests.Clear();
        var snapshots = new List<PluginRefreshSnapshot>();
        using var subscription = sut.Snapshots.Subscribe(snapshots.Add);

        var final = await sut.ExecuteAsync(new PluginRefreshIntent.RefreshSelectedIssueApproximations());
        var publication = await sut.GetCurrentPublicationAsync();

        var request = approximationModule.Requests.Should().ContainSingle().Subject;
        request.GameType.Should().Be(GameType.SkyrimSe);
        var source = request.Source.Should()
            .BeOfType<PluginIssueApproximationModuleSource.ResolvedLoadOrder>()
            .Subject;
        source.BaseDataFolder.Should().Be(@"C:\Skyrim\Data");
        source.Rows.Should().HaveCount(4);
        for (var index = 0; index < source.Rows.Count; index++)
        {
            source.Rows[index].Should().BeSameAs(accepted.Rows[index].Key);
        }

        request.Targets.Should().HaveCount(2);
        request.Targets[0].Should().BeSameAs(accepted.Rows[0].Key);
        request.Targets[1].Should().BeSameAs(accepted.Rows[3].Key);
        source.Rows.Select(row => row.FullPath).Should().Equal(
            @"C:\MO2\mods\WinnerA\First.esm",
            @"C:\MO2\mods\WinnerB\Hidden.esm",
            @"C:\MO2\mods\WinnerC\Unselected.esp",
            @"C:\MO2\overwrite\Last.esp");
        publication.Rows.Should().Contain(row =>
            row.Plugin.FileName == "Hidden.esm" &&
            row.IsSkippedByPolicy &&
            !row.IsVisible);
        publication.Rows.Should().Contain(row =>
            row.Plugin.FileName == "Unselected.esp" &&
            !row.IsSelected);
        publication.Rows.Should().NotContain(row =>
            row.Plugin.Approximation.Status == PluginIssueApproximationStatus.Pending);
        snapshots
            .Where(snapshot => snapshot.StatusText.StartsWith("Analyzing ", StringComparison.Ordinal))
            .Select(snapshot => snapshot.StatusText)
            .Should()
            .Equal(
                "Analyzing 0 of 2 selected plugins.",
                "Analyzing 1 of 2 selected plugins.",
                "Analyzing 2 of 2 selected plugins.");
        final.StatusText.Should().Be("Updated 2 selected plugin approximations.");
    }

    /// <summary>
    /// Verifies an accepted publication with no selected visible rows does not invoke analysis.
    /// </summary>
    [Fact]
    public async Task RefreshSelectedIssueApproximations_WhenSelectionEmpty_DoesNotAnalyze()
    {
        var stateService = new StateService();
        var approximationModule = new ResultPluginIssueApproximationModule();
        using var sut = CreateModule(stateService, approximationModule: approximationModule);

        await sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));
        await sut.ExecuteAsync(new PluginRefreshIntent.ChangeSelection(new PluginSelectionChange.DeselectAllVisible()));
        approximationModule.Requests.Clear();
        var final = await sut.ExecuteAsync(new PluginRefreshIntent.RefreshSelectedIssueApproximations());

        final.StatusText.Should().Be("Select plugins to refresh.");
        final.Activity.IsIssueApproximationRefreshRunning.Should().BeFalse();
        approximationModule.Requests.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies selected operation failure preserves completed results and terminalizes unfinished targets.
    /// </summary>
    [Fact]
    public async Task RefreshSelectedIssueApproximations_WhenAnalysisFails_PreservesCompletedAndFinalizesUnfinished()
    {
        var stateService = new StateService();
        var invocation = 0;
        var approximationModule = new ResultPluginIssueApproximationModule(
            (request, onResult, _) =>
            {
                if (Interlocked.Increment(ref invocation) == 1)
                {
                    foreach (var target in request.Targets)
                    {
                        onResult(new PluginIssueApproximationModuleResult(
                            target,
                            PluginIssueApproximation.Available(1, 1, 1)));
                    }

                    return Task.CompletedTask;
                }

                onResult(new PluginIssueApproximationModuleResult(
                    request.Targets[0],
                    PluginIssueApproximation.Available(7, 8, 9)));
                throw new InvalidOperationException("Synthetic selected approximation failure");
            });
        using var sut = CreateModule(stateService, approximationModule: approximationModule);

        await sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));
        var final = await sut.ExecuteAsync(new PluginRefreshIntent.RefreshSelectedIssueApproximations());
        var publication = await sut.GetCurrentPublicationAsync();

        publication.Rows.Should().Contain(row =>
            row.Plugin.FileName == "Selected.esp" &&
            row.Plugin.Approximation == PluginIssueApproximation.Available(7, 8, 9));
        publication.Rows
            .Where(row => row.Plugin.FileName != "Selected.esp")
            .Should()
            .OnlyContain(row =>
                row.Plugin.Approximation.Status == PluginIssueApproximationStatus.Unavailable);
        publication.Rows.Should().NotContain(row =>
            row.Plugin.Approximation.Status == PluginIssueApproximationStatus.Pending);
        final.Activity.Should().Be(new PluginRefreshActivity(false, false));
        final.StatusText.Should().Be("Approximation refresh failed.");
    }

    /// <summary>
    /// Verifies normal completion cannot leave targets Pending when a module omits callbacks.
    /// </summary>
    [Fact]
    public async Task RefreshSelectedIssueApproximations_WhenAnalysisCompletesWithoutEveryResult_FinalizesPendingTargets()
    {
        var stateService = new StateService();
        var invocation = 0;
        var approximationModule = new ResultPluginIssueApproximationModule(
            (request, onResult, _) =>
            {
                if (Interlocked.Increment(ref invocation) == 1)
                {
                    foreach (var target in request.Targets)
                    {
                        onResult(new PluginIssueApproximationModuleResult(
                            target,
                            PluginIssueApproximation.Available(1, 1, 1)));
                    }
                }

                return Task.CompletedTask;
            });
        using var sut = CreateModule(stateService, approximationModule: approximationModule);

        await sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));
        var final = await sut.ExecuteAsync(new PluginRefreshIntent.RefreshSelectedIssueApproximations());
        var publication = await sut.GetCurrentPublicationAsync();

        publication.Rows.Should().OnlyContain(row =>
            row.Plugin.Approximation.Status == PluginIssueApproximationStatus.Unavailable);
        publication.Rows.Should().NotContain(row =>
            row.Plugin.Approximation.Status == PluginIssueApproximationStatus.Pending);
        final.Activity.Should().Be(new PluginRefreshActivity(false, false));
        final.StatusText.Should().Be("Updated 0 selected plugin approximations.");
    }

    [Fact]
    public async Task PluginSelection_DoesNotBleedAcrossPluginListReplacementWhenFileNamesCollide()
    {
        var stateService = new StateService();
        stateService.UpdateExcludedPlugins(_ => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\OldGame\Data\Selected.esp"
        });
        using var sut = CreateModule(stateService);

        var final = await sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));

        final.Rows.Should().Contain(row => row.FileName == "Selected.esp" && row.IsSelected);
        stateService.CurrentState.ExcludedPluginPaths.Should().BeEmpty(
            "StateService.SetPluginsToClean pruning prevents stale path exclusions from leaking into replacement rows");
    }

    /// <summary>
    /// Verifies manual cancellation preserves completed work and restores each unstarted prior estimate.
    /// </summary>
    [Fact]
    public async Task ManualCancellation_PreservesCompletedResultsAndPublishesCanceledSnapshot()
    {
        var stateService = new StateService();
        var selectedResultPublished = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Action<PluginIssueApproximationModuleResult>? capturedCallback = null;
        PluginIssueApproximationModuleRequest? selectedRequest = null;
        var invocation = 0;
        var approximationModule = new ResultPluginIssueApproximationModule(
            async (request, onResult, ct) =>
            {
                if (Interlocked.Increment(ref invocation) == 1)
                {
                    for (var index = 0; index < request.Targets.Count; index++)
                    {
                        onResult(new PluginIssueApproximationModuleResult(
                            request.Targets[index],
                            PluginIssueApproximation.Available(index + 1, index + 1, index + 1)));
                    }

                    return;
                }

                selectedRequest = request;
                capturedCallback = onResult;
                onResult(new PluginIssueApproximationModuleResult(
                    request.Targets[0],
                    PluginIssueApproximation.Available(9, 9, 9)));
                selectedResultPublished.TrySetResult();
                await Task.Delay(TimeSpan.FromMinutes(5), ct);
            });
        using var sut = CreateModule(stateService, approximationModule: approximationModule);

        await sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));

        var refresh = sut.ExecuteAsync(new PluginRefreshIntent.RefreshSelectedIssueApproximations());
        await selectedResultPublished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var canceled = await sut.ExecuteAsync(new PluginRefreshIntent.Cancel(PluginRefreshCancelReason.Manual));
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));

        capturedCallback!.Invoke(new PluginIssueApproximationModuleResult(
            selectedRequest!.Targets[1],
            PluginIssueApproximation.Available(99, 99, 99)));
        var publication = await sut.GetCurrentPublicationAsync();

        publication.Rows[0].Plugin.Approximation.Should().Be(PluginIssueApproximation.Available(9, 9, 9));
        publication.Rows[1].Plugin.Approximation.Should().Be(PluginIssueApproximation.Available(2, 2, 2));
        publication.Rows[2].Plugin.Approximation.Should().Be(PluginIssueApproximation.Available(3, 3, 3));
        publication.Rows.Should().NotContain(row =>
            row.Plugin.Approximation.Status == PluginIssueApproximationStatus.Pending);
        canceled.Activity.Should().Be(new PluginRefreshActivity(false, false));
        canceled.StatusText.Should().Be("Approximation refresh canceled.");
    }

    /// <summary>
    /// Verifies service disposal restores unstarted selected targets before publication teardown.
    /// </summary>
    [Fact]
    public async Task DisposalDuringSelectedIssueApproximation_RestoresUnstartedPriorApproximations()
    {
        var stateService = new StateService();
        var selectedResultPublished = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Action<PluginIssueApproximationModuleResult>? capturedCallback = null;
        PluginIssueApproximationModuleRequest? selectedRequest = null;
        var invocation = 0;
        var approximationModule = new ResultPluginIssueApproximationModule(
            async (request, onResult, ct) =>
            {
                if (Interlocked.Increment(ref invocation) == 1)
                {
                    for (var index = 0; index < request.Targets.Count; index++)
                    {
                        onResult(new PluginIssueApproximationModuleResult(
                            request.Targets[index],
                            PluginIssueApproximation.Available(index + 1, index + 1, index + 1)));
                    }

                    return;
                }

                selectedRequest = request;
                capturedCallback = onResult;
                onResult(new PluginIssueApproximationModuleResult(
                    request.Targets[0],
                    PluginIssueApproximation.Available(9, 9, 9)));
                selectedResultPublished.TrySetResult();
                await Task.Delay(TimeSpan.FromMinutes(5), ct);
            });
        var sut = CreateModule(stateService, approximationModule: approximationModule);

        await sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));
        var refresh = sut.ExecuteAsync(new PluginRefreshIntent.RefreshSelectedIssueApproximations());
        await selectedResultPublished.Task.WaitAsync(TimeSpan.FromSeconds(5));

        sut.Dispose();
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));
        capturedCallback!.Invoke(new PluginIssueApproximationModuleResult(
            selectedRequest!.Targets[1],
            PluginIssueApproximation.Available(99, 99, 99)));

        stateService.CurrentState.PluginsToClean[0].Approximation
            .Should().Be(PluginIssueApproximation.Available(9, 9, 9));
        stateService.CurrentState.PluginsToClean[1].Approximation
            .Should().Be(PluginIssueApproximation.Available(2, 2, 2));
        stateService.CurrentState.PluginsToClean[2].Approximation
            .Should().Be(PluginIssueApproximation.Available(3, 3, 3));
        stateService.CurrentState.PluginsToClean.Should().NotContain(plugin =>
            plugin.Approximation.Status == PluginIssueApproximationStatus.Pending);
    }

    /// <summary>
    /// Verifies supersession rejects late keyed callbacks from the replaced selected generation.
    /// </summary>
    [Fact]
    public async Task RefreshSelectedIssueApproximations_WhenSuperseded_IgnoresLateKeyedResult()
    {
        var stateService = new StateService();
        var firstSelectedResultPublished = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Action<PluginIssueApproximationModuleResult>? staleCallback = null;
        PluginIssueApproximationModuleRequest? staleRequest = null;
        var invocation = 0;
        var approximationModule = new ResultPluginIssueApproximationModule(
            async (request, onResult, ct) =>
            {
                var call = Interlocked.Increment(ref invocation);
                if (call == 2)
                {
                    staleRequest = request;
                    staleCallback = onResult;
                    onResult(new PluginIssueApproximationModuleResult(
                        request.Targets[0],
                        PluginIssueApproximation.Available(4, 4, 4)));
                    firstSelectedResultPublished.TrySetResult();
                    await Task.Delay(TimeSpan.FromMinutes(5), ct);
                    return;
                }

                var approximation = call == 1
                    ? PluginIssueApproximation.Available(1, 1, 1)
                    : PluginIssueApproximation.Available(9, 9, 9);
                foreach (var target in request.Targets)
                {
                    onResult(new PluginIssueApproximationModuleResult(target, approximation));
                }
            });
        using var sut = CreateModule(stateService, approximationModule: approximationModule);

        await sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));
        var staleRefresh = sut.ExecuteAsync(new PluginRefreshIntent.RefreshSelectedIssueApproximations());
        await firstSelectedResultPublished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var replacement = await sut.ExecuteAsync(new PluginRefreshIntent.RefreshSelectedIssueApproximations());
        await staleRefresh.WaitAsync(TimeSpan.FromSeconds(5));

        staleCallback!.Invoke(new PluginIssueApproximationModuleResult(
            staleRequest!.Targets[1],
            PluginIssueApproximation.Available(99, 99, 99)));
        var publication = await sut.GetCurrentPublicationAsync();

        publication.Rows.Should().OnlyContain(row =>
            row.Plugin.Approximation == PluginIssueApproximation.Available(9, 9, 9));
        publication.Rows.Should().NotContain(row =>
            row.Plugin.Approximation.Status == PluginIssueApproximationStatus.Pending);
        replacement.Activity.Should().Be(new PluginRefreshActivity(false, false));
        replacement.StatusText.Should().Be("Updated 3 selected plugin approximations.");
    }

    [Fact]
    public async Task CleaningStartedCancellation_StopsActiveWorkWithoutCanceledStatusText()
    {
        var stateService = new StateService();
        var loadingService = new DelayedPluginLoadingService();
        using var sut = CreateModule(stateService, pluginLoadingService: loadingService);
        var snapshots = new List<PluginRefreshSnapshot>();
        using var subscription = sut.Snapshots.Subscribe(snapshots.Add);

        var refresh = sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));
        await loadingService.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await sut.ExecuteAsync(new PluginRefreshIntent.Cancel(PluginRefreshCancelReason.CleaningStarted));
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));

        snapshots.Should().NotContain(snapshot => snapshot.StatusText == "Approximation refresh canceled.");
    }

    [Fact]
    public async Task SupersededRefresh_CannotPublishStaleRows()
    {
        var stateService = new StateService();
        var loadingService = new DelayedPluginLoadingService(delayOnlyFirstCall: true);
        using var sut = CreateModule(stateService, pluginLoadingService: loadingService);

        var firstRefresh = sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));
        await loadingService.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.Fallout4));
        await firstRefresh.WaitAsync(TimeSpan.FromSeconds(5));

        stateService.CurrentState.PluginsToClean.Should().OnlyContain(plugin =>
            plugin.DetectedGameType == GameType.Fallout4);
    }

    [Fact]
    public async Task RefreshGame_DifferentGame_ClearsAppStateRowsBeforeDiscoveryCompletes()
    {
        var stateService = CreateStateWithRows(Plugin("OldSkyrim.esp"));
        var loadingService = new DelayedPluginLoadingService();
        using var sut = CreateModule(stateService, pluginLoadingService: loadingService);

        var refresh = sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.Fallout4));
        await loadingService.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        stateService.CurrentState.CurrentGameType.Should().Be(GameType.Fallout4);
        stateService.CurrentState.PluginsToClean.Should().BeEmpty(
            "Start/Preview must not see the previous game's rows while the new game is still loading");

        await sut.ExecuteAsync(new PluginRefreshIntent.Cancel(PluginRefreshCancelReason.Manual));
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RecoverableApproximationFailure_MarksTargetsUnavailableAndPublishesNonRunningSnapshot()
    {
        var stateService = new StateService();
        using var sut = CreateModule(
            stateService,
            approximationModule: new ResultPluginIssueApproximationModule(
                (_, _, _) => throw new InvalidOperationException("Synthetic approximation failure")));

        var final = await sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));

        stateService.CurrentState.PluginsToClean.Should().OnlyContain(plugin =>
            plugin.Approximation.Status == PluginIssueApproximationStatus.Unavailable);
        final.Activity.IsPluginRefreshRunning.Should().BeFalse();
        final.Activity.IsIssueApproximationRefreshRunning.Should().BeFalse();
        final.StatusText.Should().Be("Approximation refresh failed.");
    }

    [Fact]
    public async Task RecoverableApproximationFailure_PreservesCompletedResultsAndMarksPendingTargetsUnavailable()
    {
        var stateService = new StateService();
        var approximationModule = new ResultPluginIssueApproximationModule(
            (request, onResult, _) =>
            {
                var completed = request.Targets.Single(target => target.FileName == "Completed.esp");
                onResult(new PluginIssueApproximationModuleResult(
                    completed,
                    PluginIssueApproximation.Available(1, 2, 3)));
                throw new InvalidOperationException("Synthetic approximation failure");
            });
        using var sut = CreateModule(stateService, approximationModule: approximationModule);

        var final = await sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));

        stateService.CurrentState.PluginsToClean.Should().Contain(plugin =>
            plugin.FileName == "Completed.esp" &&
            plugin.Approximation.Status == PluginIssueApproximationStatus.Available &&
            plugin.Approximation.ItmCount == 1);
        stateService.CurrentState.PluginsToClean.Should().Contain(plugin =>
            plugin.FileName == "Selected.esp" &&
            plugin.Approximation.Status == PluginIssueApproximationStatus.Unavailable);
        stateService.CurrentState.PluginsToClean.Should().Contain(plugin =>
            plugin.FileName == "NotStarted.esp" &&
            plugin.Approximation.Status == PluginIssueApproximationStatus.Unavailable);
        final.Activity.IsIssueApproximationRefreshRunning.Should().BeFalse();
        final.StatusText.Should().Be("Approximation refresh failed.");
    }

    [Fact]
    public async Task RefreshGame_WhenEnderalVariantDetected_RequestsEnderalSkipList()
    {
        var stateService = new StateService();
        var gameDetectionService = Substitute.For<IGameDetectionService>();
        gameDetectionService
            .DetectVariant(GameType.SkyrimSe, Arg.Any<IReadOnlyList<string>>())
            .Returns(GameVariant.Enderal);
        var configurationService = CreateConfigurationServiceWithSkipList(
            GameType.SkyrimSe,
            GameVariant.Enderal,
            ["Completed.esp"]);
        using var sut = CreateModule(
            stateService,
            configurationService: configurationService,
            gameDetectionService: gameDetectionService);

        await sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));

        await configurationService.Received(1)
            .GetSkipListAsync(GameType.SkyrimSe, GameVariant.Enderal, Arg.Any<CancellationToken>());
    }

    private static PluginRefreshModule CreateModule(
        IStateService stateService,
        IConfigurationService? configurationService = null,
        IPluginLoadingService? pluginLoadingService = null,
        IPluginIssueApproximationModule? approximationModule = null,
        IGameDetectionService? gameDetectionService = null,
        IPluginRefreshDiscoveryPlanner? discoveryPlanner = null)
    {
        configurationService ??= CreateConfigurationService();
        pluginLoadingService ??= new TestPluginLoadingService();
        gameDetectionService ??= CreateDefaultGameDetectionService();
        discoveryPlanner ??= new PluginRefreshDiscoveryPlanner(
            configurationService,
            pluginLoadingService,
            Substitute.For<IMo2InstanceService>());
        var initialConfiguration = PluginRefreshAppStateMirror.CreateConfigurationProjection(stateService.CurrentState);
        var publicationStore = new PluginRefreshPublicationStore(
            new PluginRefreshAppStateMirror(stateService),
            new PluginRefreshCommandAvailabilityPolicy(),
            discoveryPlanner.GetAffordance(
                stateService.CurrentState.CurrentGameType,
                initialConfiguration.Mo2ModeEnabled));
        return new PluginRefreshModule(
            discoveryPlanner,
            approximationModule ?? new ResultPluginIssueApproximationModule(),
            stateService,
            new SkipListPolicy(configurationService, gameDetectionService),
            publicationStore,
            configurationService: configurationService);
    }

    private static StateService CreateStateWithRows(params PluginInfo[] rows)
    {
        var stateService = new StateService();
        stateService.UpdateState(state => state with { CurrentGameType = GameType.SkyrimSe });
        stateService.SetPluginsToClean(rows.ToList());
        return stateService;
    }

    private static IConfigurationService CreateConfigurationService(bool disableSkipLists = false)
    {
        var configurationService = Substitute.For<IConfigurationService>();
        configurationService.UserConfigurationChanged.Returns(Observable.Never<UserConfiguration>());
        configurationService.SkipListChanged.Returns(Observable.Never<GameType>());
        configurationService.LoadUserConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new UserConfiguration
            {
                Settings = new AutoQacSettings { DisableSkipLists = disableSkipLists }
            });
        configurationService.GetGameDataFolderOverrideAsync(Arg.Any<GameType>(), Arg.Any<CancellationToken>())
            .Returns(@"C:\Game\Data");
        configurationService.GetGameLoadOrderOverrideAsync(Arg.Any<GameType>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        configurationService.GetSkipListAsync(
                Arg.Any<GameType>(),
                Arg.Any<GameVariant>(),
                Arg.Any<CancellationToken>())
            .Returns([]);
        return configurationService;
    }

    private static IConfigurationService CreateConfigurationServiceWithSkipList(
        GameType gameType,
        IReadOnlyList<string> skipList,
        bool disableSkipLists = false) =>
        CreateConfigurationServiceWithSkipList(gameType, GameVariant.None, skipList, disableSkipLists);

    private static IConfigurationService CreateConfigurationServiceWithSkipList(
        GameType gameType,
        GameVariant variant,
        IReadOnlyList<string> skipList,
        bool disableSkipLists = false)
    {
        var configurationService = CreateConfigurationService(disableSkipLists);
        configurationService
            .GetSkipListAsync(gameType, variant, Arg.Any<CancellationToken>())
            .Returns(skipList.ToList());
        return configurationService;
    }

    private static IGameDetectionService CreateDefaultGameDetectionService()
    {
        var gameDetectionService = Substitute.For<IGameDetectionService>();
        gameDetectionService
            .DetectVariant(Arg.Any<GameType>(), Arg.Any<IReadOnlyList<string>>())
            .Returns(GameVariant.None);
        return gameDetectionService;
    }

    private static PluginRefreshDiscoveryPlan CreateWiringPlan()
    {
        var configuration = new PluginRefreshConfigurationProjection(
            LoadOrderPath: @"C:\SkyrimSe\plugins.txt",
            GameDataFolder: @"C:\SkyrimSe\Data",
            HasGameDataFolderOverride: true,
            XEditPath: null,
            Mo2Path: null,
            Mo2ModeEnabled: false,
            Mo2InstancePath: null,
            IsMo2InstanceOverride: false,
            IsMo2InstanceValid: null,
            AvailableProfiles: [],
            SelectedProfile: null,
            CleaningTimeout: 300);

        return new PluginRefreshDiscoveryPlan(
            GameType.SkyrimSe,
            PluginRefreshDiscoveryMode.DirectLoadOrderFile,
            configuration,
            DisableSkipLists: false,
            CanAttemptIssueApproximation: false,
            DataFolderPath: @"C:\SkyrimSe\Data",
            LoadOrderPath: @"C:\SkyrimSe\plugins.txt",
            Mo2LoadOrderPath: null,
            Mo2PathMap: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Mo2BaseDataFolder: null);
    }

    private static IPluginRefreshDiscoveryPlanner CreateStaleDiscoveryPlanner(
        PluginRefreshDiscoveryPlan plan,
        PluginRefreshDiscoveryFreshnessToken freshnessToken,
        PluginRefreshStalenessReason stalenessReason)
    {
        var discoveryPlanner = Substitute.For<IPluginRefreshDiscoveryPlanner>();
        discoveryPlanner.GetAffordance(Arg.Any<GameType>(), Arg.Any<bool>())
            .Returns(call => new PluginRefreshGameAffordance(call.ArgAt<GameType>(0), true, false, false));
        discoveryPlanner.CreatePlanAsync(Arg.Any<PluginRefreshDiscoveryPlanRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PluginRefreshDiscoveryPlanResult(PluginRefreshDiscoveryPlanStatus.Ready, plan, plan.Configuration));
        discoveryPlanner.CreateFreshnessTokenAsync(plan, Arg.Any<CancellationToken>())
            .Returns(freshnessToken);
        discoveryPlanner.LoadPluginsAsync(plan, Arg.Any<CancellationToken>())
            .Returns(new PluginRefreshDiscoveredPlugins(plan, [Plugin("Selected.esp")], null));
        discoveryPlanner.CheckFreshnessAsync(
                freshnessToken,
                Arg.Any<PluginRefreshDiscoveryFreshnessContext>(),
                Arg.Any<CancellationToken>())
            .Returns(new PluginRefreshFreshness(false, stalenessReason));
        return discoveryPlanner;
    }

    private static IPluginRefreshDiscoveryPlanner CreateReadyDiscoveryPlanner(
        PluginRefreshDiscoveryPlan plan,
        IReadOnlyList<PluginInfo> plugins)
    {
        var freshnessToken = new PluginRefreshDiscoveryFreshnessToken(
            plan.GameType,
            plan.Configuration.Mo2ModeEnabled,
            plan.Configuration.Mo2Path,
            plan.LoadOrderPath,
            plan.DataFolderPath,
            plan.Configuration.Mo2InstancePath,
            plan.Configuration.SelectedProfile,
            plan.DisableSkipLists,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase));
        var discoveryPlanner = Substitute.For<IPluginRefreshDiscoveryPlanner>();
        discoveryPlanner.GetAffordance(Arg.Any<GameType>(), Arg.Any<bool>())
            .Returns(call => new PluginRefreshGameAffordance(call.ArgAt<GameType>(0), true, false, true));
        discoveryPlanner.CreatePlanAsync(
                Arg.Any<PluginRefreshDiscoveryPlanRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(new PluginRefreshDiscoveryPlanResult(
                PluginRefreshDiscoveryPlanStatus.Ready,
                plan,
                plan.Configuration));
        discoveryPlanner.CreateFreshnessTokenAsync(plan, Arg.Any<CancellationToken>())
            .Returns(freshnessToken);
        discoveryPlanner.LoadPluginsAsync(plan, Arg.Any<CancellationToken>())
            .Returns(new PluginRefreshDiscoveredPlugins(plan, plugins, null));
        discoveryPlanner.CheckFreshnessAsync(
                freshnessToken,
                Arg.Any<PluginRefreshDiscoveryFreshnessContext>(),
                Arg.Any<CancellationToken>())
            .Returns(PluginRefreshFreshness.Fresh);
        return discoveryPlanner;
    }

    private static PluginInfo Plugin(
        string fileName,
        string? fullPath = null,
        PluginIssueApproximation? approximation = null) =>
        new()
        {
            FileName = fileName,
            FullPath = fullPath ?? $@"C:\Game\Data\{fileName}",
            DetectedGameType = GameType.SkyrimSe,
            Approximation = approximation ?? PluginIssueApproximation.Unavailable
        };

    private sealed class TestPluginLoadingService : IPluginLoadingService
    {
        public Task<List<PluginInfo>> GetPluginsAsync(
            GameType gameType,
            string? customDataFolder = null,
            CancellationToken ct = default) =>
            Task.FromResult(CreatePlugins(gameType, customDataFolder).ToList());

        public Task<PluginLoadingResult> TryGetPluginsAsync(
            GameType gameType,
            string? customDataFolder = null,
            CancellationToken ct = default) =>
            Task.FromResult(new PluginLoadingResult
            {
                Status = PluginLoadingStatus.Success,
                Plugins = CreatePlugins(gameType, customDataFolder),
                DataFolder = customDataFolder
            });

        public Task<List<PluginInfo>> GetPluginsFromFileAsync(
            string loadOrderPath,
            string? dataFolderPath = null,
            CancellationToken ct = default) =>
            Task.FromResult(CreatePlugins(GameType.FalloutNewVegas, dataFolderPath).ToList());

        public string? GetGameDataFolder(GameType gameType, string? customDataFolderOverride = null) =>
            customDataFolderOverride ?? $@"C:\{gameType}\Data";

        public string? GetDefaultLoadOrderPath(GameType gameType) =>
            $@"C:\{gameType}\plugins.txt";

        private static IReadOnlyList<PluginInfo> CreatePlugins(GameType gameType, string? dataFolder)
        {
            var root = dataFolder ?? $@"C:\{gameType}\Data";
            return
            [
                new PluginInfo { FileName = "Selected.esp", FullPath = $@"{root}\Selected.esp", DetectedGameType = gameType },
                new PluginInfo { FileName = "Completed.esp", FullPath = $@"{root}\Completed.esp", DetectedGameType = gameType },
                new PluginInfo { FileName = "NotStarted.esp", FullPath = $@"{root}\NotStarted.esp", DetectedGameType = gameType }
            ];
        }
    }

    private sealed class DelayedPluginLoadingService : IPluginLoadingService
    {
        private readonly bool _delayOnlyFirstCall;
        private int _tryGetCallCount;

        public DelayedPluginLoadingService(bool delayOnlyFirstCall = false)
        {
            _delayOnlyFirstCall = delayOnlyFirstCall;
        }

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<List<PluginInfo>> GetPluginsAsync(
            GameType gameType,
            string? customDataFolder = null,
            CancellationToken ct = default) =>
            Task.FromResult(CreatePlugins(gameType, customDataFolder).ToList());

        public async Task<PluginLoadingResult> TryGetPluginsAsync(
            GameType gameType,
            string? customDataFolder = null,
            CancellationToken ct = default)
        {
            var callNumber = Interlocked.Increment(ref _tryGetCallCount);
            if (!_delayOnlyFirstCall || callNumber == 1)
            {
                Started.TrySetResult();
                await Task.Delay(TimeSpan.FromMinutes(5), ct);
            }

            return new PluginLoadingResult
            {
                Status = PluginLoadingStatus.Success,
                Plugins = CreatePlugins(gameType, customDataFolder),
                DataFolder = customDataFolder
            };
        }

        public Task<List<PluginInfo>> GetPluginsFromFileAsync(
            string loadOrderPath,
            string? dataFolderPath = null,
            CancellationToken ct = default) =>
            Task.FromResult(CreatePlugins(GameType.FalloutNewVegas, dataFolderPath).ToList());

        public string? GetGameDataFolder(GameType gameType, string? customDataFolderOverride = null) =>
            customDataFolderOverride ?? $@"C:\{gameType}\Data";

        public string? GetDefaultLoadOrderPath(GameType gameType) =>
            $@"C:\{gameType}\plugins.txt";

        private static IReadOnlyList<PluginInfo> CreatePlugins(GameType gameType, string? dataFolder)
        {
            var root = dataFolder ?? $@"C:\{gameType}\Data";
            return
            [
                new PluginInfo { FileName = "Completed.esp", FullPath = $@"{root}\Completed.esp", DetectedGameType = gameType },
                new PluginInfo { FileName = "NotStarted.esp", FullPath = $@"{root}\NotStarted.esp", DetectedGameType = gameType }
            ];
        }
    }

    private sealed class ResultPluginIssueApproximationModule : IPluginIssueApproximationModule
    {
        private readonly Func<
            PluginIssueApproximationModuleRequest,
            Action<PluginIssueApproximationModuleResult>,
            CancellationToken,
            Task>? _handler;

        public ResultPluginIssueApproximationModule(
            Func<
                PluginIssueApproximationModuleRequest,
                Action<PluginIssueApproximationModuleResult>,
                CancellationToken,
                Task>? handler = null)
        {
            _handler = handler;
        }

        public List<PluginIssueApproximationModuleRequest> Requests { get; } = [];

        /// <inheritdoc />
        public Task AnalyzeAsync(
            PluginIssueApproximationModuleRequest request,
            Action<PluginIssueApproximationModuleResult> onResult,
            CancellationToken ct = default)
        {
            Requests.Add(request);
            if (_handler is not null)
            {
                return _handler(request, onResult, ct);
            }

            foreach (var target in request.Targets)
            {
                ct.ThrowIfCancellationRequested();
                onResult(new PluginIssueApproximationModuleResult(
                    target,
                    PluginIssueApproximation.Available(1, 2, 3)));
            }

            return Task.CompletedTask;
        }
    }

}
