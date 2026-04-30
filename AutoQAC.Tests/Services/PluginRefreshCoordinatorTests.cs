using AutoQAC.Models;
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
            plugin.Approximation.Status is PluginIssueApproximationStatus.Pending or PluginIssueApproximationStatus.Available);
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
        var loadingService = Substitute.For<IPluginLoadingService>();
        var approximationService = Substitute.For<IPluginIssueApproximationService>();
        var capabilityPolicy = Substitute.For<IPluginRefreshCapabilityPolicy>();

        capabilityPolicy.SupportsPluginLoading(Arg.Any<GameType>()).Returns(true);
        capabilityPolicy.SupportsIssueApproximation(Arg.Any<GameType>()).Returns(true);

        return new PluginRefreshCoordinator(
            loadingService,
            approximationService,
            stateService,
            capabilityPolicy);
    }
}
