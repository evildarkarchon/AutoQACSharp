using AutoQAC.Models;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;
using FluentAssertions;

namespace AutoQAC.Tests.Services;

public sealed class PluginRefreshPublicationTests
{
    [Fact]
    public void StatusChanged_ShouldEmitNamedPublicationStatuses()
    {
        var stateService = new StateService();
        var sut = new StateServicePluginRefreshPublication(stateService);
        var statuses = new List<PluginRefreshStatus>();
        using var subscription = sut.StatusChanged.Subscribe(statuses.Add);
        using var scope = sut.BeginRefresh();

        scope.PublishLoadingPlugins();
        scope.PublishApproximationUnavailable();
        sut.PublishSelectPlugins();
        sut.PublishManualCancellation();
        scope.BeginFullApproximationRefresh([Target("Full.esp")]).PublishCompleted();
        scope.BeginSelectedApproximationRefresh(GameType.SkyrimSe, [Target("Selected.esp")]).PublishCompleted();

        statuses.Select(status => status.Kind).Should().ContainInOrder(
            PluginRefreshStatusKind.LoadingPlugins,
            PluginRefreshStatusKind.ApproximationUnavailable,
            PluginRefreshStatusKind.SelectPlugins,
            PluginRefreshStatusKind.Canceled,
            PluginRefreshStatusKind.FullRefreshCompleted,
            PluginRefreshStatusKind.SelectedRefreshCompleted);
    }

    [Fact]
    public void StaleScopes_ShouldNotMutateStateOrEmitStatus()
    {
        var stateService = new StateService();
        var sut = new StateServicePluginRefreshPublication(stateService);
        var statuses = new List<PluginRefreshStatus>();
        using var subscription = sut.StatusChanged.Subscribe(statuses.Add);
        using var staleScope = sut.BeginRefresh();
        using var currentScope = sut.BeginRefresh();

        staleScope.PublishLoadingPlugins();
        staleScope.PublishPluginRows([Plugin("Stale.esp")]);
        staleScope.PublishNoGameSelected();

        stateService.CurrentState.PluginsToClean.Should().BeEmpty();
        stateService.CurrentState.CurrentGameType.Should().Be(GameType.Unknown);
        statuses.Should().BeEmpty();
        currentScope.IsVisible.Should().BeTrue();
    }

    [Fact]
    public void CanceledScopes_ShouldNotMutateStateOrEmitStatus()
    {
        var stateService = new StateService();
        var sut = new StateServicePluginRefreshPublication(stateService);
        var statuses = new List<PluginRefreshStatus>();
        using var subscription = sut.StatusChanged.Subscribe(statuses.Add);
        using var cts = new CancellationTokenSource();
        using var scope = sut.BeginRefresh(cts.Token);

        cts.Cancel();
        scope.PublishLoadingPlugins();
        scope.PublishPluginRows([Plugin("Canceled.esp")]);

        stateService.CurrentState.PluginsToClean.Should().BeEmpty();
        statuses.Should().BeEmpty();
    }

    [Fact]
    public void PublishNoGameSelected_ShouldClearRowsSetUnknownAndEmitIdleStatus()
    {
        var stateService = new StateService();
        stateService.UpdateState(state => state with { CurrentGameType = GameType.SkyrimSe });
        stateService.SetPluginsToClean([Plugin("Old.esp")]);
        var sut = new StateServicePluginRefreshPublication(stateService);
        var statuses = new List<PluginRefreshStatus>();
        using var subscription = sut.StatusChanged.Subscribe(statuses.Add);
        using var scope = sut.BeginRefresh();

        scope.PublishNoGameSelected();

        stateService.CurrentState.CurrentGameType.Should().Be(GameType.Unknown);
        stateService.CurrentState.PluginsToClean.Should().BeEmpty();
        statuses.Should().ContainSingle(status =>
            status.Kind == PluginRefreshStatusKind.Idle && status.Message == "No game selected");
    }

    [Fact]
    public void PublishConfiguration_ShouldUpdateRuntimeConfigurationFields()
    {
        var stateService = new StateService();
        var sut = new StateServicePluginRefreshPublication(stateService);
        using var scope = sut.BeginRefresh();

        scope.PublishConfiguration(
            new PluginRefreshProjection(GameType.Fallout4, LoadOrderPath: @"C:\Fallout4\plugins.txt"),
            new PluginRefreshPublicationSnapshot(
                @"C:\Fallout4\plugins.txt",
                @"C:\MO2\ModOrganizer.exe",
                @"C:\xEdit\FO4Edit.exe",
                "Default",
                Mo2ModeEnabled: false,
                CleaningTimeout: 123));

        stateService.CurrentState.CurrentGameType.Should().Be(GameType.Fallout4);
        stateService.CurrentState.LoadOrderPath.Should().Be(@"C:\Fallout4\plugins.txt");
        stateService.CurrentState.Mo2ExecutablePath.Should().Be(@"C:\MO2\ModOrganizer.exe");
        stateService.CurrentState.XEditExecutablePath.Should().Be(@"C:\xEdit\FO4Edit.exe");
        stateService.CurrentState.Mo2Profile.Should().Be("Default");
        stateService.CurrentState.Mo2ModeEnabled.Should().BeFalse();
        stateService.CurrentState.CleaningTimeout.Should().Be(123);
    }

    [Fact]
    public void PublishPluginRows_ShouldUseStateServicePruningBehavior()
    {
        var stateService = new StateService();
        stateService.UpdateExcludedPlugins(_ => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Game\Data\Keep.esp",
            @"C:\Game\Data\Stale.esp"
        });
        var sut = new StateServicePluginRefreshPublication(stateService);
        using var scope = sut.BeginRefresh();

        scope.PublishPluginRows([Plugin("Keep.esp", @"C:\Game\Data\Keep.esp")]);

        stateService.CurrentState.ExcludedPluginPaths.Should().Equal(@"C:\Game\Data\Keep.esp");
    }

    [Fact]
    public void BeginSelectedApproximationRefresh_ShouldMarkSelectedRowsPendingAndPreserveUnrelatedRows()
    {
        var originalApproximation = PluginIssueApproximation.Available(9, 8, 7);
        var stateService = new StateService();
        stateService.SetPluginsToClean([
            Plugin("Selected.esp", approximation: PluginIssueApproximation.Available(1, 1, 1)),
            Plugin("Unrelated.esp", approximation: originalApproximation)
        ]);
        var sut = new StateServicePluginRefreshPublication(stateService);
        using var scope = sut.BeginRefresh();

        scope.BeginSelectedApproximationRefresh(GameType.SkyrimSe, [Target("Selected.esp")]);

        stateService.CurrentState.PluginsToClean.Should().Contain(plugin =>
            plugin.FileName == "Selected.esp" && plugin.Approximation.Status == PluginIssueApproximationStatus.Pending);
        stateService.CurrentState.PluginsToClean.Should().Contain(plugin =>
            plugin.FileName == "Unrelated.esp" && plugin.Approximation == originalApproximation);
    }

    [Fact]
    public void BeginSelectedApproximationRefresh_ShouldMaterializePendingRowsWhenVisibleListIsEmpty()
    {
        var stateService = new StateService();
        var sut = new StateServicePluginRefreshPublication(stateService);
        using var scope = sut.BeginRefresh();

        scope.BeginSelectedApproximationRefresh(GameType.SkyrimSe, [Target("Selected.esp")]);

        stateService.CurrentState.PluginsToClean.Should().ContainSingle(plugin =>
            plugin.FileName == "Selected.esp" &&
            plugin.DetectedGameType == GameType.SkyrimSe &&
            plugin.Approximation.Status == PluginIssueApproximationStatus.Pending);
    }

    [Fact]
    public void PublishResult_ShouldPreferFullPathWhenBothSidesHaveUsablePaths()
    {
        var stateService = new StateService();
        stateService.SetPluginsToClean([
            Plugin("Duplicate.esp", @"C:\A\Duplicate.esp", approximation: PluginIssueApproximation.Pending),
            Plugin("Duplicate.esp", @"C:\B\Duplicate.esp", approximation: PluginIssueApproximation.Pending)
        ]);
        var sut = new StateServicePluginRefreshPublication(stateService);
        using var scope = sut.BeginRefresh();
        var publication = scope.BeginFullApproximationRefresh([Target("Duplicate.esp", @"C:\B\Duplicate.esp")]);

        publication.PublishResult(Result("Duplicate.esp", @"C:\B\Duplicate.esp"));

        stateService.CurrentState.PluginsToClean.Should().Contain(plugin =>
            plugin.FullPath == @"C:\A\Duplicate.esp" && plugin.Approximation.Status == PluginIssueApproximationStatus.Pending);
        stateService.CurrentState.PluginsToClean.Should().Contain(plugin =>
            plugin.FullPath == @"C:\B\Duplicate.esp" && plugin.Approximation.Status == PluginIssueApproximationStatus.Available);
    }

    [Fact]
    public void PublishResult_ShouldFallBackToFileNameWhenEitherSideLacksUsableFullPath()
    {
        var stateService = new StateService();
        stateService.SetPluginsToClean([Plugin("Fallback.esp", string.Empty, approximation: PluginIssueApproximation.Pending)]);
        var sut = new StateServicePluginRefreshPublication(stateService);
        using var scope = sut.BeginRefresh();
        var publication = scope.BeginFullApproximationRefresh([Target("Fallback.esp", string.Empty)]);

        publication.PublishResult(Result("Fallback.esp", @"C:\Game\Data\Fallback.esp"));

        stateService.CurrentState.PluginsToClean.Should().ContainSingle(plugin =>
            plugin.FileName == "Fallback.esp" && plugin.Approximation.Status == PluginIssueApproximationStatus.Available);
    }

    [Fact]
    public void PublishResult_ShouldIgnoreNonTargetResults()
    {
        var stateService = new StateService();
        stateService.SetPluginsToClean([
            Plugin("Target.esp", approximation: PluginIssueApproximation.Pending),
            Plugin("Other.esp", approximation: PluginIssueApproximation.Pending)
        ]);
        var sut = new StateServicePluginRefreshPublication(stateService);
        var statuses = new List<PluginRefreshStatus>();
        using var subscription = sut.StatusChanged.Subscribe(statuses.Add);
        using var scope = sut.BeginRefresh();
        var publication = scope.BeginFullApproximationRefresh([Target("Target.esp")]);

        publication.PublishResult(Result("Other.esp"));

        stateService.CurrentState.PluginsToClean.Should().OnlyContain(plugin =>
            plugin.Approximation.Status == PluginIssueApproximationStatus.Pending);
        statuses.Should().NotContain(status => status.Kind == PluginRefreshStatusKind.AnalyzingSelected);
    }

    [Fact]
    public void ApproximationPublication_ShouldCountOnlyMatchedVisibleResultsForProgressAndCompletion()
    {
        var stateService = new StateService();
        stateService.SetPluginsToClean([Plugin("Target.esp", approximation: PluginIssueApproximation.Pending)]);
        var sut = new StateServicePluginRefreshPublication(stateService);
        var statuses = new List<PluginRefreshStatus>();
        using var subscription = sut.StatusChanged.Subscribe(statuses.Add);
        using var scope = sut.BeginRefresh();
        var publication = scope.BeginSelectedApproximationRefresh(GameType.SkyrimSe,
            [Target("Target.esp"), Target("Missing.esp")]);

        publication.PublishResult(Result("Other.esp"));
        publication.PublishResult(Result("Missing.esp"));
        publication.PublishResult(Result("Target.esp"));
        publication.PublishCompleted();

        statuses.Should().ContainSingle(status =>
            status.Kind == PluginRefreshStatusKind.AnalyzingSelected &&
            status.Current == 1 &&
            status.Total == 2);
        statuses.Should().ContainSingle(status =>
            status.Kind == PluginRefreshStatusKind.SelectedRefreshCompleted &&
            status.UpdatedCount == 1);
    }

    [Fact]
    public void PublishApproximationFailure_ShouldMarkTargetsUnavailableAndEmitFailureStatus()
    {
        var stateService = new StateService();
        stateService.SetPluginsToClean([
            Plugin("Target.esp", approximation: PluginIssueApproximation.Pending),
            Plugin("Other.esp", approximation: PluginIssueApproximation.Pending)
        ]);
        var sut = new StateServicePluginRefreshPublication(stateService);
        var statuses = new List<PluginRefreshStatus>();
        using var subscription = sut.StatusChanged.Subscribe(statuses.Add);
        using var scope = sut.BeginRefresh();

        scope.PublishApproximationFailure([Target("Target.esp")]);

        stateService.CurrentState.PluginsToClean.Should().Contain(plugin =>
            plugin.FileName == "Target.esp" && plugin.Approximation.Status == PluginIssueApproximationStatus.Unavailable);
        stateService.CurrentState.PluginsToClean.Should().Contain(plugin =>
            plugin.FileName == "Other.esp" && plugin.Approximation.Status == PluginIssueApproximationStatus.Pending);
        statuses.Should().ContainSingle(status =>
            status.Kind == PluginRefreshStatusKind.Idle && status.Message == "Approximation refresh failed.");
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

    private static PluginRefreshTarget Target(string fileName, string? fullPath = null) =>
        new(fileName, fullPath ?? $@"C:\Game\Data\{fileName}");

    private static PluginIssueApproximationResult Result(string fileName, string? fullPath = null) =>
        new()
        {
            FileName = fileName,
            FullPath = fullPath ?? $@"C:\Game\Data\{fileName}",
            Approximation = PluginIssueApproximation.Available(1, 2, 3)
        };
}
