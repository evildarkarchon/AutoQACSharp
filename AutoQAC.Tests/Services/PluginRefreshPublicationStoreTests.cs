using AutoQAC.Models;
using AutoQAC.Services.GameCapability;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;
using FluentAssertions;

namespace AutoQAC.Tests.Services;

public sealed class PluginRefreshPublicationStoreTests
{
    [Fact]
    public void PublishAcceptedPublication_ShouldMirrorFullRowsAndPublishVisibleSnapshot()
    {
        var stateService = new StateService();
        using var sut = CreateStore(stateService);
        var plan = CreatePlan();
        var token = CreateFreshnessToken();

        var snapshot = sut.PublishAcceptedPublication(
            generation: 1,
            GameType.SkyrimSe,
            plan,
            token,
            plan.Configuration,
            [
                Published("Visible.esp"),
                Published("Hidden.esp", isVisible: false, isSkippedByPolicy: true)
            ],
            new PluginRefreshActivity(false, false),
            "Loaded",
            Affordance());

        snapshot.Rows.Should().ContainSingle(row => row.FileName == "Visible.esp");
        stateService.CurrentState.PluginsToClean.Select(plugin => plugin.FileName)
            .Should().Equal("Visible.esp", "Hidden.esp");
        stateService.CurrentState.PluginsToClean.Should().Contain(plugin =>
            plugin.FileName == "Hidden.esp" && plugin.IsInSkipList);
        sut.GetFreshnessInspection().Publication.Rows.Should().HaveCount(2);
    }

    [Fact]
    public void PublishAcceptedPublication_WhenCleaning_ShouldNotMirrorRowsIntoAppState()
    {
        var stateService = new StateService();
        stateService.StartCleaning([Plugin("Cleaning.esp")]);
        using var sut = CreateStore(stateService);
        var plan = CreatePlan();

        sut.PublishAcceptedPublication(
            generation: 1,
            GameType.SkyrimSe,
            plan,
            CreateFreshnessToken(),
            plan.Configuration,
            [Published("New.esp")],
            new PluginRefreshActivity(false, false),
            "Loaded",
            Affordance());

        sut.GetCurrentSnapshot().Rows.Should().ContainSingle(row => row.FileName == "New.esp");
        stateService.CurrentState.PluginsToClean.Should().ContainSingle(plugin =>
            plugin.FileName == "Cleaning.esp");
    }

    [Fact]
    public void ApplySelectionChange_WithAcceptedPublication_ShouldUpdateSnapshotPublicationAndMirror()
    {
        var stateService = new StateService();
        using var sut = CreateStore(stateService);
        var plan = CreatePlan();
        sut.PublishAcceptedPublication(
            generation: 1,
            GameType.SkyrimSe,
            plan,
            CreateFreshnessToken(),
            plan.Configuration,
            [Published("Selected.esp"), Published("Other.esp")],
            new PluginRefreshActivity(false, false),
            "Loaded",
            Affordance());

        var changed = sut.ApplySelectionChange(
            new PluginSelectionChange.SetOne(new PluginRefreshRowKey("Selected.esp", @"C:\Data\Selected.esp"), false),
            Affordance());

        changed.Rows.Should().Contain(row => row.FileName == "Selected.esp" && !row.IsSelected);
        sut.GetFreshnessInspection().Publication.Rows.Should().Contain(row =>
            row.Plugin.FileName == "Selected.esp" && !row.IsSelected);
        stateService.CurrentState.ExcludedPluginPaths.Should().Contain(@"C:\Data\Selected.esp");
    }

    [Fact]
    public void ApplySelectionChange_WhenPublicationMissing_ShouldUseAppStateFallback()
    {
        var stateService = CreateStateWithRows(Plugin("Selected.esp"));
        using var sut = CreateStore(stateService);

        var changed = sut.ApplySelectionChange(
            new PluginSelectionChange.SetOne(new PluginRefreshRowKey("Selected.esp", @"C:\Data\Selected.esp"), false),
            Affordance());

        changed.Rows.Should().ContainSingle(row => row.FileName == "Selected.esp" && !row.IsSelected);
        stateService.CurrentState.ExcludedPluginPaths.Should().Contain(@"C:\Data\Selected.esp");
        sut.GetFreshnessInspection().Publication.Freshness.Should().Be(PluginRefreshFreshness.Missing);
    }

    [Fact]
    public void PublishFreshnessIfCurrent_ShouldEmitOnlyWhenCurrentPublicationFreshnessChanges()
    {
        var stateService = new StateService();
        using var sut = CreateStore(stateService);
        var plan = CreatePlan();
        var token = CreateFreshnessToken();
        sut.PublishAcceptedPublication(
            generation: 1,
            GameType.SkyrimSe,
            plan,
            token,
            plan.Configuration,
            [Published("Selected.esp")],
            new PluginRefreshActivity(false, false),
            "Loaded",
            Affordance());
        var notifications = new List<PluginRefreshSnapshot>();
        using var subscription = sut.Snapshots.Subscribe(notifications.Add);
        var observed = sut.GetFreshnessInspection();

        sut.PublishFreshnessIfCurrent(observed.Publication, token, PluginRefreshFreshness.Fresh);
        sut.PublishFreshnessIfCurrent(
            observed.Publication,
            token,
            new PluginRefreshFreshness(false, PluginRefreshStalenessReason.LoadOrderPathChanged));
        sut.PublishFreshnessIfCurrent(
            observed.Publication,
            token,
            new PluginRefreshFreshness(false, PluginRefreshStalenessReason.LoadOrderPathChanged));

        notifications.Should().HaveCount(2, "the subscription receives the current snapshot plus one freshness change");
        sut.GetFreshnessInspection().Publication.Freshness.StalenessReason
            .Should().Be(PluginRefreshStalenessReason.LoadOrderPathChanged);
    }

    [Fact]
    public void PublishCommandAvailabilityIfChanged_WhenCleaning_ShouldDisableCommandsWithoutReplacingRows()
    {
        var stateService = new StateService();
        using var sut = CreateStore(stateService);
        var plan = CreatePlan(canAttemptIssueApproximation: true);
        sut.PublishAcceptedPublication(
            generation: 1,
            GameType.SkyrimSe,
            plan,
            CreateFreshnessToken(),
            plan.Configuration,
            [Published("Selected.esp")],
            new PluginRefreshActivity(false, false),
            "Loaded",
            Affordance());
        var before = sut.GetCurrentSnapshot();

        before.Commands.CanSelectAll.Should().BeTrue();
        before.Commands.CanRefreshSelectedIssueApproximations.Should().BeTrue();
        stateService.StartCleaning([Plugin("Cleaning.esp")]);
        var commandInspection = sut.GetCommandAvailabilityInspection();
        sut.PublishCommandAvailabilityIfChanged(
            stateService.CurrentState,
            commandInspection,
            Affordance(),
            Affordance());
        var during = sut.GetCurrentSnapshot();

        during.Commands.CanSelectAll.Should().BeFalse();
        during.Commands.CanDeselectAll.Should().BeFalse();
        during.Commands.CanRefreshSelectedIssueApproximations.Should().BeFalse();
        during.Rows.Select(row => row.FileName).Should().Equal(before.Rows.Select(row => row.FileName));
    }

    /// <summary>
    /// Verifies initial keyed results and unfinished-target terminalization commit through the AppState mirror.
    /// </summary>
    [Fact]
    public void InitialApproximationPublication_ShouldMirrorExactResultsAndFinalizeUnfinishedTargets()
    {
        var stateService = new StateService();
        using var sut = CreateStore(stateService);
        var plan = CreatePlan();
        sut.PublishAcceptedPublication(
            generation: 1,
            GameType.SkyrimSe,
            plan,
            CreateFreshnessToken(),
            plan.Configuration,
            [
                Published("Target.esp", approximation: PluginIssueApproximation.Unavailable),
                Published("PendingOnly.esp", approximation: PluginIssueApproximation.Unavailable),
                Published("Other.esp")
            ],
            new PluginRefreshActivity(true, true),
            "Analyzing 0 of 2 plugins.",
            Affordance());
        sut.ApplySelectionChange(
            new PluginSelectionChange.SetOne(new PluginRefreshRowKey("Other.esp", @"C:\Data\Other.esp"), false),
            Affordance());
        var targets = sut.GetSelectedIssueApproximationTargets();
        var targetLookup = PluginRefreshPublicationRows.CreateTargetLookup(targets.Targets);

        var target = targets.Targets.Single(key => key.FileName == "Target.esp");
        var matched = sut.TryPublishInitialApproximationResult(
            generation: 1,
            targetLookup,
            new PluginIssueApproximationModuleResult(
                target,
                PluginIssueApproximation.Available(3, 2, 1)),
            "Analyzing 1 of 2 plugins.",
            Affordance(),
            () => true);
        var nonTargetMatched = sut.TryPublishInitialApproximationResult(
            generation: 1,
            targetLookup,
            new PluginIssueApproximationModuleResult(
                new PluginRefreshRowKey("Other.esp", @"C:\Data\Other.esp"),
                PluginIssueApproximation.Available(9, 9, 9)),
            "Analyzing 2 of 2 plugins.",
            Affordance(),
            () => true);

        targets.Targets.Select(target => target.FileName).Should().Equal("Target.esp", "PendingOnly.esp");
        matched.Should().BeTrue();
        nonTargetMatched.Should().BeFalse();
        sut.GetFreshnessInspection().Publication.Rows.Should().Contain(row =>
            row.Plugin.FileName == "Target.esp" &&
            row.Plugin.Approximation.Status == PluginIssueApproximationStatus.Available &&
            row.Plugin.Approximation.ItmCount == 3);
        stateService.CurrentState.PluginsToClean.Should().Contain(plugin =>
            plugin.FileName == "Target.esp" &&
            plugin.Approximation.Status == PluginIssueApproximationStatus.Available);

        sut.TryFinalizeInitialApproximation(
            generation: 1,
            "Refreshed 1 plugin approximations.",
            Affordance(),
            () => true,
            out _).Should().BeTrue();

        sut.GetFreshnessInspection().Publication.Rows.Should().Contain(row =>
            row.Plugin.FileName == "Target.esp" &&
            row.Plugin.Approximation.Status == PluginIssueApproximationStatus.Available &&
            row.Plugin.Approximation.ItmCount == 3);
        sut.GetFreshnessInspection().Publication.Rows.Should().Contain(row =>
            row.Plugin.FileName == "PendingOnly.esp" &&
            row.Plugin.Approximation.Status == PluginIssueApproximationStatus.Unavailable);
        stateService.CurrentState.PluginsToClean.Should().Contain(plugin =>
            plugin.FileName == "Target.esp" &&
            plugin.Approximation.Status == PluginIssueApproximationStatus.Available);
        stateService.CurrentState.PluginsToClean.Should().Contain(plugin =>
            plugin.FileName == "PendingOnly.esp" &&
            plugin.Approximation.Status == PluginIssueApproximationStatus.Unavailable);
    }

    private static PluginRefreshPublicationStore CreateStore(
        StateService stateService,
        bool canAttemptIssueApproximation = true) =>
        new(
            new PluginRefreshAppStateMirror(stateService),
            new PluginRefreshCommandAvailabilityPolicy(),
            new PluginRefreshGameAffordance(
                stateService.CurrentState.CurrentGameType,
                IsMutagenSupported: true,
                RequiresLoadOrderFile: false,
                CanAttemptIssueApproximation: canAttemptIssueApproximation &&
                                             stateService.CurrentState.CurrentGameType != GameType.Unknown));

    private static StateService CreateStateWithRows(params PluginInfo[] rows)
    {
        var stateService = new StateService();
        stateService.UpdateState(state => state with { CurrentGameType = GameType.SkyrimSe });
        stateService.SetPluginsToClean(rows.ToList());
        return stateService;
    }

    private static PluginRefreshDiscoveryPlan CreatePlan(bool canAttemptIssueApproximation = true)
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
            CanAttemptIssueApproximation: canAttemptIssueApproximation,
            DataFolderPath: @"C:\SkyrimSe\Data",
            LoadOrderPath: @"C:\SkyrimSe\plugins.txt",
            Mo2LoadOrderPath: null,
            Mo2PathMap: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Mo2BaseDataFolder: null);
    }

    private static PluginRefreshDiscoveryFreshnessToken CreateFreshnessToken() =>
        new(
            GameType.SkyrimSe,
            mo2ModeEnabled: false,
            mo2ExecutablePath: null,
            loadOrderPath: @"C:\SkyrimSe\plugins.txt",
            gameDataFolderOverride: null,
            mo2InstancePath: null,
            mo2Profile: null,
            disableSkipLists: false,
            skipLists: new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase));

    private static PluginRefreshGameAffordance Affordance(
        GameType gameType = GameType.SkyrimSe,
        bool canAttemptIssueApproximation = true) =>
        new(
            gameType,
            IsMutagenSupported: true,
            RequiresLoadOrderFile: false,
            CanAttemptIssueApproximation: canAttemptIssueApproximation && gameType != GameType.Unknown);

    private static PluginRefreshPublishedRow Published(
        string fileName,
        bool isVisible = true,
        bool isSelected = true,
        bool isSkippedByPolicy = false,
        PluginIssueApproximation? approximation = null)
    {
        var plugin = Plugin(fileName, approximation: approximation) with
        {
            IsInSkipList = isSkippedByPolicy
        };
        return new PluginRefreshPublishedRow(
            plugin,
            isVisible,
            isSelected,
            isSkippedByPolicy,
            new PluginRefreshRowKey(plugin.FileName, plugin.FullPath));
    }

    private static PluginInfo Plugin(
        string fileName,
        PluginIssueApproximation? approximation = null) =>
        new()
        {
            FileName = fileName,
            FullPath = $@"C:\Data\{fileName}",
            DetectedGameType = GameType.SkyrimSe,
            Approximation = approximation ?? PluginIssueApproximation.Unavailable
        };

}
