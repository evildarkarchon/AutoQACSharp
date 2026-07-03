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
    public async Task ChangeSelection_UpdatesSnapshotsAndAppStateExclusions()
    {
        var stateService = new StateService();
        using var sut = CreateModule(stateService);
        var loaded = await sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));
        var selected = loaded.Rows.Single(row => row.FileName == "Selected.esp");

        var deselected = await sut.ExecuteAsync(new PluginRefreshIntent.ChangeSelection(
            new PluginSelectionChange.SetOne(selected.Key, IsSelected: false)));

        deselected.Rows.Should().Contain(row => row.FileName == "Selected.esp" && !row.IsSelected);
        stateService.CurrentState.ExcludedPluginPaths.Should().Contain(selected.FullPath);

        var reselected = await sut.ExecuteAsync(new PluginRefreshIntent.ChangeSelection(
            new PluginSelectionChange.SelectAllVisible()));

        reselected.Rows.Should().OnlyContain(row => row.IsSelected);
        stateService.CurrentState.ExcludedPluginPaths.Should().BeEmpty();
    }

    [Fact]
    public async Task RefreshSelectedIssueApproximations_DerivesTargetsFromCurrentSelection()
    {
        var stateService = CreateStateWithRows(
            Plugin("Selected.esp", approximation: PluginIssueApproximation.Pending),
            Plugin("Unselected.esp", approximation: PluginIssueApproximation.Pending));
        var approximationService = new ResultIssueApproximationService([
            Result("Selected.esp"),
            Result("Unselected.esp")
        ]);
        using var sut = CreateModule(stateService, approximationService: approximationService);
        var unselected = stateService.CurrentState.PluginsToClean.Single(plugin => plugin.FileName == "Unselected.esp");

        await sut.ExecuteAsync(new PluginRefreshIntent.ChangeSelection(
            new PluginSelectionChange.SetOne(new PluginRefreshRowKey(unselected.FileName, unselected.FullPath), false)));
        var final = await sut.ExecuteAsync(new PluginRefreshIntent.RefreshSelectedIssueApproximations());

        approximationService.CallCount.Should().Be(1);
        stateService.CurrentState.PluginsToClean.Should().Contain(plugin =>
            plugin.FileName == "Selected.esp" && plugin.Approximation.Status == PluginIssueApproximationStatus.Available);
        stateService.CurrentState.PluginsToClean.Should().Contain(plugin =>
            plugin.FileName == "Unselected.esp" && plugin.Approximation.Status == PluginIssueApproximationStatus.Pending,
            "non-selected callback results must be ignored even when the adapter reports them");
        final.Rows.Should().Contain(row => row.FileName == "Unselected.esp" && !row.IsSelected);
    }

    [Fact]
    public async Task RefreshSelectedIssueApproximations_WhenSelectionEmpty_DoesNotAnalyze()
    {
        var stateService = CreateStateWithRows(Plugin("Selected.esp"));
        var approximationService = new ResultIssueApproximationService([Result("Selected.esp")]);
        using var sut = CreateModule(stateService, approximationService: approximationService);

        await sut.ExecuteAsync(new PluginRefreshIntent.ChangeSelection(new PluginSelectionChange.DeselectAllVisible()));
        var final = await sut.ExecuteAsync(new PluginRefreshIntent.RefreshSelectedIssueApproximations());

        final.StatusText.Should().Be("Select plugins to refresh.");
        final.Activity.IsIssueApproximationRefreshRunning.Should().BeFalse();
        approximationService.CallCount.Should().Be(0);
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

    [Fact]
    public async Task ApproximationMerge_PrefersFullPathWhenBothSidesHaveUsablePaths()
    {
        var stateService = CreateStateWithRows(
            Plugin("Duplicate.esp", @"C:\A\Duplicate.esp", PluginIssueApproximation.Pending),
            Plugin("Duplicate.esp", @"C:\B\Duplicate.esp", PluginIssueApproximation.Pending));
        using var sut = CreateModule(stateService, approximationService: new ResultIssueApproximationService([
            Result("Duplicate.esp", @"C:\B\Duplicate.esp")
        ]));

        await sut.ExecuteAsync(new PluginRefreshIntent.RefreshSelectedIssueApproximations());

        stateService.CurrentState.PluginsToClean.Should().Contain(plugin =>
            plugin.FullPath == @"C:\A\Duplicate.esp" && plugin.Approximation.Status == PluginIssueApproximationStatus.Pending);
        stateService.CurrentState.PluginsToClean.Should().Contain(plugin =>
            plugin.FullPath == @"C:\B\Duplicate.esp" && plugin.Approximation.Status == PluginIssueApproximationStatus.Available);
    }

    [Fact]
    public async Task ApproximationMerge_FallsBackToFileNameWhenUsablePathIsMissing()
    {
        var stateService = CreateStateWithRows(
            Plugin("Fallback.esp", string.Empty, PluginIssueApproximation.Pending));
        using var sut = CreateModule(stateService, approximationService: new ResultIssueApproximationService([
            Result("Fallback.esp", @"C:\Game\Data\Fallback.esp")
        ]));

        await sut.ExecuteAsync(new PluginRefreshIntent.RefreshSelectedIssueApproximations());

        stateService.CurrentState.PluginsToClean.Should().ContainSingle(plugin =>
            plugin.FileName == "Fallback.esp" && plugin.Approximation.Status == PluginIssueApproximationStatus.Available);
    }

    [Fact]
    public async Task ApproximationMerge_IgnoresNonTargetResults()
    {
        var stateService = CreateStateWithRows(
            Plugin("Target.esp", approximation: PluginIssueApproximation.Pending));
        using var sut = CreateModule(stateService, approximationService: new ResultIssueApproximationService([
            Result("Other.esp")
        ]));

        await sut.ExecuteAsync(new PluginRefreshIntent.RefreshSelectedIssueApproximations());

        stateService.CurrentState.PluginsToClean.Should().ContainSingle(plugin =>
            plugin.FileName == "Target.esp" && plugin.Approximation.Status == PluginIssueApproximationStatus.Pending);
    }

    [Fact]
    public async Task ManualCancellation_PreservesCompletedResultsAndPublishesCanceledSnapshot()
    {
        var stateService = CreateStateWithRows(
            Plugin("Completed.esp", approximation: PluginIssueApproximation.Pending),
            Plugin("NotStarted.esp", approximation: PluginIssueApproximation.Pending));
        var approximationService = new ResultIssueApproximationService([
            Result("Completed.esp"),
            Result("NotStarted.esp")
        ], delayBetweenResults: true);
        using var sut = CreateModule(stateService, approximationService: approximationService);

        var refresh = sut.ExecuteAsync(new PluginRefreshIntent.RefreshSelectedIssueApproximations());
        await approximationService.FirstResultPublished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var canceled = await sut.ExecuteAsync(new PluginRefreshIntent.Cancel(PluginRefreshCancelReason.Manual));
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));

        stateService.CurrentState.PluginsToClean.Should().Contain(plugin =>
            plugin.FileName == "Completed.esp" && plugin.Approximation.Status == PluginIssueApproximationStatus.Available);
        stateService.CurrentState.PluginsToClean.Should().Contain(plugin =>
            plugin.FileName == "NotStarted.esp" && plugin.Approximation.Status != PluginIssueApproximationStatus.Available);
        canceled.StatusText.Should().Be("Approximation refresh canceled.");
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
            approximationService: new ThrowingPluginIssueApproximationService());

        var final = await sut.ExecuteAsync(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));

        stateService.CurrentState.PluginsToClean.Should().OnlyContain(plugin =>
            plugin.Approximation.Status == PluginIssueApproximationStatus.Unavailable);
        final.Activity.IsPluginRefreshRunning.Should().BeFalse();
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
        IPluginIssueApproximationService? approximationService = null,
        IGameDetectionService? gameDetectionService = null)
    {
        configurationService ??= CreateConfigurationService();
        pluginLoadingService ??= new TestPluginLoadingService();
        gameDetectionService ??= CreateDefaultGameDetectionService();
        var discoveryPlanner = new PluginRefreshDiscoveryPlanner(
            configurationService,
            pluginLoadingService,
            Substitute.For<IMo2InstanceService>(),
            new GameCapabilityProvider());
        return new PluginRefreshModule(
            discoveryPlanner,
            approximationService ?? new ResultIssueApproximationService(CreateDefaultResults()),
            stateService,
            new SkipListPolicy(configurationService, gameDetectionService));
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

    private static IReadOnlyList<PluginIssueApproximationResult> CreateDefaultResults() =>
    [
        Result("Completed.esp"),
        Result("Selected.esp"),
        Result("NotStarted.esp")
    ];

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

    private static PluginIssueApproximationResult Result(string fileName, string? fullPath = null) =>
        new()
        {
            FileName = fileName,
            FullPath = fullPath ?? $@"C:\Game\Data\{fileName}",
            Approximation = PluginIssueApproximation.Available(1, 2, 3)
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

    private sealed class ResultIssueApproximationService : IPluginIssueApproximationService
    {
        private readonly IReadOnlyList<PluginIssueApproximationResult> _results;
        private readonly bool _delayBetweenResults;

        public ResultIssueApproximationService(
            IReadOnlyList<PluginIssueApproximationResult> results,
            bool delayBetweenResults = false)
        {
            _results = results;
            _delayBetweenResults = delayBetweenResults;
        }

        public int CallCount { get; private set; }

        public TaskCompletionSource FirstResultPublished { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<PluginIssueApproximationResult>> GetApproximationsAsync(
            PluginIssueApproximationRequest request,
            Action<PluginIssueApproximationResult>? onApproximationReady = null,
            CancellationToken ct = default)
        {
            CallCount++;
            foreach (var result in _results)
            {
                ct.ThrowIfCancellationRequested();
                onApproximationReady?.Invoke(result);
                FirstResultPublished.TrySetResult();
                if (_delayBetweenResults)
                {
                    await Task.Delay(TimeSpan.FromMinutes(5), ct);
                }
            }

            return _results;
        }
    }

    private sealed class ThrowingPluginIssueApproximationService : IPluginIssueApproximationService
    {
        public Task<IReadOnlyList<PluginIssueApproximationResult>> GetApproximationsAsync(
            PluginIssueApproximationRequest request,
            Action<PluginIssueApproximationResult>? onApproximationReady = null,
            CancellationToken ct = default) =>
            throw new InvalidOperationException("Synthetic approximation failure");
    }
}
