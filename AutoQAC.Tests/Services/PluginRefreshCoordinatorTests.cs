using AutoQAC.Models;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;
using FluentAssertions;
using NSubstitute;

namespace AutoQAC.Tests.Services;

public sealed class PluginRefreshCoordinatorTests
{
    [Fact]
    public async Task RefreshForGameAsync_PublishesRowsBeforeApproximationResults()
    {
        var stateService = new StateService();
        var sut = CreateCoordinator(stateService);
        var request = new PluginRefreshRequest(GameType.SkyrimSe, @"C:\Game\Data");

        await sut.RefreshForGameAsync(request, CancellationToken.None);

        stateService.CurrentState.PluginsToClean.Should().NotBeEmpty(
            "rows must be published before the coordinator starts approximation analysis");
        stateService.CurrentState.PluginsToClean.Should().OnlyContain(plugin =>
            plugin.Approximation.Status == PluginIssueApproximationStatus.Pending ||
            plugin.Approximation.Status == PluginIssueApproximationStatus.Available);
    }

    [Fact]
    public async Task RefreshSelectedApproximationsAsync_UsesSnapshotAndDoesNotAnalyzeDeselectedRows()
    {
        var stateService = new StateService();
        var sut = CreateCoordinator(stateService);
        var request = new PluginRefreshRequest(GameType.SkyrimSe, @"C:\Game\Data");
        var selectedTargets = new List<PluginRefreshTarget>
        {
            new("Selected.esp", @"C:\Game\Data\Selected.esp")
        };

        await sut.RefreshSelectedApproximationsAsync(request, selectedTargets, CancellationToken.None);
        selectedTargets.Add(new PluginRefreshTarget("LateSelection.esp", @"C:\Game\Data\LateSelection.esp"));

        stateService.CurrentState.PluginsToClean.Should().Contain(plugin =>
            plugin.FileName == "Selected.esp" && plugin.Approximation.Status == PluginIssueApproximationStatus.Available);
        stateService.CurrentState.PluginsToClean.Should().NotContain(plugin =>
            plugin.FileName == "LateSelection.esp" && plugin.Approximation.Status == PluginIssueApproximationStatus.Available,
            "selection changes after command start must not mutate the active refresh target snapshot");
    }

    [Fact]
    public async Task RefreshSelectedApproximationsAsync_ShouldPreserveNonSelectedPluginRows()
    {
        var stateService = new StateService();
        var sut = CreateCoordinator(stateService);
        var request = new PluginRefreshRequest(GameType.SkyrimSe, @"C:\Game\Data");
        var originalUnselectedApproximation = PluginIssueApproximation.Available(9, 8, 7);
        stateService.SetPluginsToClean([
            new PluginInfo
            {
                FileName = "Selected.esp",
                FullPath = @"C:\Game\Data\Selected.esp",
                DetectedGameType = GameType.SkyrimSe,
                Approximation = PluginIssueApproximation.Available(1, 1, 1)
            },
            new PluginInfo
            {
                FileName = "Unselected.esp",
                FullPath = @"C:\Game\Data\Unselected.esp",
                DetectedGameType = GameType.SkyrimSe,
                Approximation = originalUnselectedApproximation
            }
        ]);

        await sut.RefreshSelectedApproximationsAsync(
            request,
            [new PluginRefreshTarget("Selected.esp", @"C:\Game\Data\Selected.esp")],
            CancellationToken.None);

        stateService.CurrentState.PluginsToClean.Should().HaveCount(2,
            "targeted approximation refresh must update selected rows in place rather than replacing the visible list");
        stateService.CurrentState.PluginsToClean.Should().Contain(plugin =>
            plugin.FileName == "Selected.esp" && plugin.Approximation.Status == PluginIssueApproximationStatus.Available);
        stateService.CurrentState.PluginsToClean.Should().Contain(plugin =>
            plugin.FileName == "Unselected.esp" && plugin.Approximation == originalUnselectedApproximation,
            "non-selected rows must remain visible with their previous approximation");
    }

    [Fact]
    public async Task RefreshForGameAsync_WhenDisableSkipListsTrue_ShouldNotMarkSkipListedPluginsAsInSkipList()
    {
        var stateService = new StateService();
        var configurationService = CreateConfigurationServiceWithSkipList(
            GameType.SkyrimSe,
            ["Completed.esp"]);
        var sut = CreateCoordinator(stateService, configurationService);
        var request = new PluginRefreshRequest(
            GameType.SkyrimSe,
            @"C:\Game\Data",
            DisableSkipLists: true);

        await sut.RefreshForGameAsync(request, CancellationToken.None);

        stateService.CurrentState.PluginsToClean
            .Should().Contain(plugin =>
                plugin.FileName == "Completed.esp" && !plugin.IsInSkipList,
                "Disable Skip Lists must override the configured skip list during refresh");
    }

    [Fact]
    public async Task RefreshForGameAsync_WhenDisableSkipListsFalse_ShouldMarkConfiguredSkipListPluginsAsInSkipList()
    {
        var stateService = new StateService();
        var configurationService = CreateConfigurationServiceWithSkipList(
            GameType.SkyrimSe,
            ["Completed.esp"]);
        var sut = CreateCoordinator(stateService, configurationService);
        var request = new PluginRefreshRequest(
            GameType.SkyrimSe,
            @"C:\Game\Data",
            DisableSkipLists: false);

        await sut.RefreshForGameAsync(request, CancellationToken.None);

        stateService.CurrentState.PluginsToClean
            .Should().Contain(plugin =>
                plugin.FileName == "Completed.esp" && plugin.IsInSkipList,
                "default behavior must continue to honor the configured skip list");
    }

    [Fact]
    public async Task RefreshSelectedApproximationsAsync_WhenNoTargets_PublishesSelectPluginsStatus()
    {
        var stateService = new StateService();
        var sut = CreateCoordinator(stateService);
        var statuses = new List<PluginRefreshStatus>();
        using var subscription = sut.StatusChanged.Subscribe(statuses.Add);

        await sut.RefreshSelectedApproximationsAsync(
            new PluginRefreshRequest(GameType.SkyrimSe, @"C:\Game\Data"),
            [],
            CancellationToken.None);

        statuses.Should().Contain(status =>
            status.Kind == PluginRefreshStatusKind.SelectPlugins &&
            status.ToDisplayText() == "Select plugins to refresh.");
        stateService.CurrentState.PluginsToClean.Should().BeEmpty("empty selected refresh does not analyze or idle-fill rows");
    }

    [Fact]
    public async Task RefreshForGameAsync_WhenSuperseded_DoesNotPublishStaleApproximation()
    {
        var stateService = new StateService();
        var sut = CreateCoordinator(stateService);

        var firstRefresh = sut.RefreshForGameAsync(new PluginRefreshRequest(GameType.SkyrimSe, @"C:\OldGame\Data"), CancellationToken.None);
        await sut.RefreshForGameAsync(new PluginRefreshRequest(GameType.Fallout4, @"C:\NewGame\Data"), CancellationToken.None);
        await firstRefresh;

        stateService.CurrentState.PluginsToClean.Should().OnlyContain(plugin =>
            plugin.DetectedGameType == GameType.Fallout4,
            "superseded generations must not publish stale approximation callbacks into the replacement row set");
    }

    [Fact]
    public async Task CancelActiveRefresh_ManualCancelKeepsCompletedResultsAndPublishesCanceledStatus()
    {
        var stateService = new StateService();
        var sut = CreateCoordinator(stateService);
        var statuses = new List<PluginRefreshStatus>();
        using var subscription = sut.StatusChanged.Subscribe(statuses.Add);

        var refresh = sut.RefreshSelectedApproximationsAsync(
            new PluginRefreshRequest(GameType.SkyrimSe, @"C:\Game\Data"),
            [
                new PluginRefreshTarget("Completed.esp", @"C:\Game\Data\Completed.esp"),
                new PluginRefreshTarget("NotStarted.esp", @"C:\Game\Data\NotStarted.esp")
            ],
            CancellationToken.None);

        sut.CancelActiveRefresh(PluginRefreshCancelReason.Manual);
        await refresh;

        stateService.CurrentState.PluginsToClean.Should().Contain(plugin =>
            plugin.FileName == "Completed.esp" && plugin.Approximation.Status == PluginIssueApproximationStatus.Available);
        stateService.CurrentState.PluginsToClean.Should().Contain(plugin =>
            plugin.FileName == "NotStarted.esp" && plugin.Approximation.Status != PluginIssueApproximationStatus.Available);
        statuses.Should().Contain(status =>
            status.Kind == PluginRefreshStatusKind.Canceled &&
            status.ToDisplayText() == "Approximation refresh canceled.");
    }

    private static PluginRefreshCoordinator CreateCoordinator(IStateService stateService)
    {
        return new PluginRefreshCoordinator(
            new TestPluginLoadingService(),
            new TestPluginIssueApproximationService(),
            stateService,
            new TestPluginRefreshCapabilityPolicy());
    }

    private static PluginRefreshCoordinator CreateCoordinator(
        IStateService stateService,
        IConfigurationService configurationService) =>
        new(
            new TestPluginLoadingService(),
            new TestPluginIssueApproximationService(),
            stateService,
            new TestPluginRefreshCapabilityPolicy(),
            configurationService);

    private static IConfigurationService CreateConfigurationServiceWithSkipList(
        GameType gameType,
        IReadOnlyList<string> skipList)
    {
        var configurationService = Substitute.For<IConfigurationService>();
        // GetSkipListAsync has an optional GameVariant parameter; matching it explicitly keeps
        // NSubstitute bound to the same call shape used by the coordinator's named ct argument.
        configurationService
            .GetSkipListAsync(gameType, GameVariant.None, Arg.Any<CancellationToken>())
            .Returns(skipList.ToList());
        configurationService
            .GetGameLoadOrderOverrideAsync(gameType, Arg.Any<CancellationToken>())
            .Returns((string?)null);
        return configurationService;
    }

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

        public bool IsGameSupportedByMutagen(GameType gameType) =>
            gameType is GameType.SkyrimSe or GameType.Fallout4;

        public IReadOnlyList<GameType> GetAvailableGames() =>
            [GameType.SkyrimSe, GameType.Fallout4, GameType.FalloutNewVegas];

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

    private sealed class TestPluginIssueApproximationService : IPluginIssueApproximationService
    {
        public async Task<IReadOnlyList<PluginIssueApproximationResult>> GetApproximationsAsync(
            GameType gameType,
            string dataFolder,
            Action<PluginIssueApproximationResult>? onApproximationReady = null,
            CancellationToken ct = default)
        {
            var results = new List<PluginIssueApproximationResult>
            {
                CreateResult(dataFolder, "Completed.esp"),
                CreateResult(dataFolder, "Selected.esp")
            };

            foreach (var result in results)
            {
                ct.ThrowIfCancellationRequested();
                onApproximationReady?.Invoke(result);
                await Task.Delay(25, ct);
            }

            ct.ThrowIfCancellationRequested();
            var notStarted = CreateResult(dataFolder, "NotStarted.esp");
            results.Add(notStarted);
            onApproximationReady?.Invoke(notStarted);
            return results;
        }

        private static PluginIssueApproximationResult CreateResult(string dataFolder, string fileName) =>
            new()
            {
                FileName = fileName,
                FullPath = $@"{dataFolder}\{fileName}",
                Approximation = PluginIssueApproximation.Available(1, 2, 3)
            };
    }

    private sealed class TestPluginRefreshCapabilityPolicy : IPluginRefreshCapabilityPolicy
    {
        public bool SupportsPluginLoading(GameType gameType) => gameType != GameType.Unknown;

        public bool SupportsIssueApproximation(GameType gameType) =>
            gameType is GameType.SkyrimSe or GameType.Fallout4;

        public bool RequiresLoadOrderFile(GameType gameType) => gameType == GameType.FalloutNewVegas;
    }
}
