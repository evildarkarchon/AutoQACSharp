using AutoQAC.Models;
using AutoQAC.Services.GameCapability;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;
using FluentAssertions;
using NSubstitute;

namespace AutoQAC.Tests.Services;

public sealed class PluginRefreshPublicationStoreTests
{
    /// <summary>Publishing discovery failure must clear both compatibility rows and their selection exclusions.</summary>
    [Fact]
    public void PublishMissingPublicationFromState_WhenCurrent_ShouldClearCompatibilityRows()
    {
        var stateService = new StateService();
        using var sut = CreateStore(stateService);
        var plan = CreatePlan();
        sut.PublishAcceptedPublication(1, GameType.SkyrimSe, plan, CreateFreshnessToken(),
            plan.Configuration, [Published("Old.esp", isSelected: false)], new(false, false), "Old", Affordance());

        var result = sut.PublishMissingPublicationFromState(2, GameType.SkyrimSe,
            plan.Configuration, new(false, false), "Discovery failed", Affordance());

        result.Rows.Should().BeEmpty();
        stateService.CurrentState.PluginsToClean.Should().BeEmpty();
        stateService.CurrentState.ExcludedPluginPaths.Should().BeEmpty();
        sut.GetFreshnessInspection().Publication.Freshness.Should().Be(PluginRefreshFreshness.Missing);
    }

    /// <summary>Synchronous state observers can supersede a failure clear before its snapshot is committed.</summary>
    [Fact]
    public void PublishMissingPublicationFromState_WhenSupersededDuringClear_ShouldPreserveWinningPublication()
    {
        var stateService = new StateService();
        using var sut = CreateStore(stateService);
        var plan = CreatePlan();
        sut.PublishAcceptedPublication(1, GameType.SkyrimSe, plan, CreateFreshnessToken(),
            plan.Configuration, [Published("Old.esp")], new(false, false), "Old", Affordance());
        var supersede = true;
        using var subscription = stateService.StateChanged.Subscribe(state =>
        {
            if (!supersede || state.PluginsToClean.Count != 0) return;
            supersede = false;
            sut.PublishAcceptedPublication(3, GameType.SkyrimSe, plan, CreateFreshnessToken(),
                plan.Configuration, [Published("Winner.esp", isSelected: false)], new(false, false), "Winner", Affordance());
        });

        var result = sut.PublishMissingPublicationFromState(2, GameType.SkyrimSe,
            plan.Configuration, new(false, false), "Discovery failed", Affordance());

        result.Generation.Should().Be(3);
        result.Rows.Should().ContainSingle(row => row.FileName == "Winner.esp");
        stateService.CurrentState.PluginsToClean.Should().ContainSingle(row => row.FileName == "Winner.esp");
        stateService.CurrentState.ExcludedPluginPaths.Should().Equal(@"C:\Data\Winner.esp");
        sut.GetFreshnessInspection().Publication.Freshness.Should().Be(PluginRefreshFreshness.Fresh);
    }

    /// <summary>Obsolete failure paths must preserve the winning publication and its freshness lease.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublishMissingPublicationFromState_WhenSuperseded_ShouldPreserveCurrentPublication(bool invalidate)
    {
        var stateService = new StateService();
        using var sut = CreateStore(stateService);
        var plan = CreatePlan();
        sut.PublishAcceptedPublication(2, GameType.SkyrimSe, plan, CreateFreshnessToken(),
            plan.Configuration, [Published("Winner.esp", isSelected: false)], new(false, false), "Winner", Affordance());
        if (invalidate) sut.InvalidatePublication(3);
        var before = sut.GetFreshnessInspection();
        var snapshot = sut.GetCurrentSnapshot();
        var notifications = new List<PluginRefreshSnapshot>();
        using var subscription = sut.Snapshots.Subscribe(notifications.Add);

        var result = sut.PublishMissingPublicationFromState(invalidate ? 2 : 1, GameType.SkyrimSe,
            plan.Configuration, new(false, false), "Obsolete failure", Affordance());

        result.Should().BeSameAs(snapshot);
        sut.GetCurrentSnapshot().Should().BeSameAs(snapshot);
        sut.GetFreshnessInspection().Should().BeEquivalentTo(before);
        stateService.CurrentState.PluginsToClean.Should().ContainSingle(row => row.FileName == "Winner.esp");
        stateService.CurrentState.ExcludedPluginPaths.Should().Equal(@"C:\Data\Winner.esp");
        notifications.Should().ContainSingle();
    }

    /// <summary>Rejected discovery must not overwrite compatibility rows or selection exclusions.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublishAcceptedPublication_WhenSuperseded_ShouldPreserveCompatibilityRows(bool invalidate)
    {
        var stateService = new StateService();
        using var sut = CreateStore(stateService);
        var plan = CreatePlan();
        var snapshot = sut.PublishAcceptedPublication(2, GameType.SkyrimSe, plan, CreateFreshnessToken(),
            plan.Configuration, [Published("Winner.esp", isSelected: false)], new(false, false), "Winner", Affordance());
        if (invalidate) sut.InvalidatePublication(3);

        var result = sut.PublishAcceptedPublication(invalidate ? 2 : 1, GameType.SkyrimSe, plan, CreateFreshnessToken(),
            plan.Configuration, [Published("Obsolete.esp")], new(false, false), "Obsolete", Affordance());

        result.Should().BeSameAs(snapshot);
        stateService.CurrentState.PluginsToClean.Should().ContainSingle(row => row.FileName == "Winner.esp");
        stateService.CurrentState.ExcludedPluginPaths.Should().Equal(@"C:\Data\Winner.esp");
    }

    /// <summary>An approximation result and the concurrent user selection must both survive publication.</summary>
    [Fact]
    public void ApplySelectionChange_WhenApproximationReplacesRows_ShouldApplySelectionToLatestPublication()
    {
        var stateService = Substitute.For<IStateService>();
        var backingState = new StateService();
        Action? beforeStateRead = null;
        stateService.CurrentState.Returns(_ =>
        {
            var callback = beforeStateRead;
            beforeStateRead = null;
            callback?.Invoke();
            return backingState.CurrentState;
        });
        stateService.When(service => service.SetPluginsToClean(Arg.Any<List<PluginInfo>>()))
            .Do(call => backingState.SetPluginsToClean(call.Arg<List<PluginInfo>>()!));
        stateService.When(service => service.UpdateExcludedPlugins(Arg.Any<Func<IReadOnlySet<string>, IReadOnlySet<string>>>()))
            .Do(call => backingState.UpdateExcludedPlugins(call.Arg<Func<IReadOnlySet<string>, IReadOnlySet<string>>>()!));
        using var sut = new PluginRefreshPublicationStore(new PluginRefreshAppStateMirror(stateService),
            new PluginRefreshCommandAvailabilityPolicy(), Affordance());
        var plan = CreatePlan();
        var target = new PluginRefreshRowKey("Target.esp", @"C:\Data\Target.esp");
        sut.PublishAcceptedPublication(1, GameType.SkyrimSe, plan, CreateFreshnessToken(),
            plan.Configuration, [Published("Target.esp", approximation: PluginIssueApproximation.Pending)],
            new(true, true), "Analyzing", Affordance());
        beforeStateRead = () => sut.TryPublishInitialApproximationResult(1,
            PluginRefreshPublicationRows.CreateTargetLookup([target]),
            new(target, PluginIssueApproximation.Available(3, 2, 1)), "Result arrived", Affordance(), () => true);

        sut.ApplySelectionChange(new PluginSelectionChange.SetOne(target, false), Affordance());

        sut.GetFreshnessInspection().Publication.Rows.Single().Plugin.Approximation
            .Should().Be(PluginIssueApproximation.Available(3, 2, 1));
        sut.GetCurrentSnapshot().Rows.Single().Approximation
            .Should().Be(PluginIssueApproximation.Available(3, 2, 1));
        sut.GetCurrentSnapshot().Rows.Single().IsSelected.Should().BeFalse();
        backingState.CurrentState.PluginsToClean.Single().Approximation
            .Should().Be(PluginIssueApproximation.Available(3, 2, 1));
    }

    [Fact]
    public void PublishAcceptedPublication_ShouldMirrorFullRowsAndPublishVisibleSnapshot()
    {
        var stateService = new StateService();
        using var sut = CreateStore(stateService);
        var plan = CreatePlan();
        var token = CreateFreshnessToken();

        var snapshot = sut.PublishAcceptedPublication(
            1,
            GameType.SkyrimSe,
            plan,
            token,
            plan.Configuration,
            [
                Published("Visible.esp"),
                Published("Hidden.esp", false, isSkippedByPolicy: true)
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
            1,
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
            1,
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
            1,
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
        var plan = CreatePlan(true);
        sut.PublishAcceptedPublication(
            1,
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
            1,
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
            1,
            targetLookup,
            new PluginIssueApproximationModuleResult(
                target,
                PluginIssueApproximation.Available(3, 2, 1)),
            "Analyzing 1 of 2 plugins.",
            Affordance(),
            () => true);
        var nonTargetMatched = sut.TryPublishInitialApproximationResult(
            1,
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
            1,
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
        bool canAttemptIssueApproximation = true)
    {
        return new PluginRefreshPublicationStore(
            new PluginRefreshAppStateMirror(stateService),
            new PluginRefreshCommandAvailabilityPolicy(),
            new PluginRefreshGameAffordance(
                stateService.CurrentState.CurrentGameType,
                true,
                false,
                canAttemptIssueApproximation &&
                stateService.CurrentState.CurrentGameType != GameType.Unknown));
    }

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
            @"C:\SkyrimSe\plugins.txt",
            @"C:\SkyrimSe\Data",
            true,
            null,
            null,
            false,
            null,
            false,
            null,
            [],
            null,
            300);

        return new PluginRefreshDiscoveryPlan(
            GameType.SkyrimSe,
            PluginRefreshDiscoveryMode.DirectLoadOrderFile,
            configuration,
            false,
            canAttemptIssueApproximation,
            @"C:\SkyrimSe\Data",
            @"C:\SkyrimSe\plugins.txt",
            null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            null);
    }

    private static PluginRefreshDiscoveryFreshnessToken CreateFreshnessToken()
    {
        return new PluginRefreshDiscoveryFreshnessToken(
            GameType.SkyrimSe,
            false,
            null,
            @"C:\SkyrimSe\plugins.txt",
            null,
            null,
            null,
            false,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase));
    }

    private static PluginRefreshGameAffordance Affordance(
        GameType gameType = GameType.SkyrimSe,
        bool canAttemptIssueApproximation = true)
    {
        return new PluginRefreshGameAffordance(
            gameType,
            true,
            false,
            canAttemptIssueApproximation && gameType != GameType.Unknown);
    }

    private static PluginRefreshPublishedRow Published(
        string fileName,
        bool isVisible = true,
        bool isSelected = true,
        bool isSkippedByPolicy = false,
        PluginIssueApproximation? approximation = null)
    {
        var plugin = Plugin(fileName, approximation) with
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
        PluginIssueApproximation? approximation = null)
    {
        return new PluginInfo
        {
            FileName = fileName,
            FullPath = $@"C:\Data\{fileName}",
            DetectedGameType = GameType.SkyrimSe,
            Approximation = approximation ?? PluginIssueApproximation.Unavailable
        };
    }
}
