using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
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
        var admission = new CleaningAdmission();
        using var sut = CreateStore(stateService, admission: admission);
        var plan = CreatePlan();
        sut.PublishAcceptedPublication(Begin(admission), GameType.SkyrimSe, plan, CreateFreshnessToken(),
            plan.Configuration, [Published("Old.esp", isSelected: false)], new(false, false), "Old", Affordance());

        var result = sut.PublishMissingPublicationFromState(Begin(admission), GameType.SkyrimSe,
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
        var admission = new CleaningAdmission();
        using var sut = CreateStore(stateService, admission: admission);
        var plan = CreatePlan();
        sut.PublishAcceptedPublication(Begin(admission), GameType.SkyrimSe, plan, CreateFreshnessToken(),
            plan.Configuration, [Published("Old.esp")], new(false, false), "Old", Affordance());
        var failing = Begin(admission);
        RefreshOperation? winner = null;
        using var subscription = stateService.StateChanged.Subscribe(state =>
        {
            if (winner is not null || state.PluginsToClean.Count != 0) return;
            winner = Begin(admission);
            sut.PublishAcceptedPublication(winner, GameType.SkyrimSe, plan, CreateFreshnessToken(),
                plan.Configuration, [Published("Winner.esp", isSelected: false)], new(false, false), "Winner", Affordance());
        });

        var result = sut.PublishMissingPublicationFromState(failing, GameType.SkyrimSe,
            plan.Configuration, new(false, false), "Discovery failed", Affordance());

        winner.Should().NotBeNull();
        result.Generation.Should().Be(winner!.Id);
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
        var admission = new CleaningAdmission();
        using var sut = CreateStore(stateService, admission: admission);
        var plan = CreatePlan();
        var obsolete = Begin(admission);
        var winner = Begin(admission);
        sut.PublishAcceptedPublication(winner, GameType.SkyrimSe, plan, CreateFreshnessToken(),
            plan.Configuration, [Published("Winner.esp", isSelected: false)], new(false, false), "Winner", Affordance());
        if (invalidate) Fence(admission, sut);
        var before = sut.GetFreshnessInspection();
        var snapshot = sut.GetCurrentSnapshot();
        var notifications = new List<PluginRefreshSnapshot>();
        using var subscription = sut.Snapshots.Subscribe(notifications.Add);

        var result = sut.PublishMissingPublicationFromState(invalidate ? winner : obsolete, GameType.SkyrimSe,
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
        var admission = new CleaningAdmission();
        using var sut = CreateStore(stateService, admission: admission);
        var plan = CreatePlan();
        var obsolete = Begin(admission);
        var winner = Begin(admission);
        var snapshot = sut.PublishAcceptedPublication(winner, GameType.SkyrimSe, plan, CreateFreshnessToken(),
            plan.Configuration, [Published("Winner.esp", isSelected: false)], new(false, false), "Winner", Affordance());
        if (invalidate) Fence(admission, sut);

        var result = sut.PublishAcceptedPublication(invalidate ? winner : obsolete, GameType.SkyrimSe, plan,
            CreateFreshnessToken(), plan.Configuration, [Published("Obsolete.esp")], new(false, false), "Obsolete",
            Affordance());

        result.Should().BeSameAs(snapshot);
        sut.GetFreshnessInspection().Publication.Freshness.IsFresh.Should().Be(!invalidate,
            "a fenced operation cannot restore the revoked freshness lease");
        stateService.CurrentState.PluginsToClean.Should().ContainSingle(row => row.FileName == "Winner.esp");
        stateService.CurrentState.ExcludedPluginPaths.Should().Equal(@"C:\Data\Winner.esp");
    }

    /// <summary>
    ///     Once supersession finalizes an operation's initial tail, nothing that operation still has in flight can
    ///     commit: not a late result, not its own cancellation cleanup, and not a delayed publication.
    /// </summary>
    [Fact]
    public void SupersededOperation_AfterFinalization_CannotCommit()
    {
        var stateService = new StateService();
        var admission = new CleaningAdmission();
        using var sut = CreateStore(stateService, admission: admission);
        var plan = CreatePlan();
        var superseded = Begin(admission);
        sut.PublishAcceptedPublication(superseded, GameType.SkyrimSe, plan, CreateFreshnessToken(),
            plan.Configuration,
            [
                Published("Done.esp", approximation: PluginIssueApproximation.Available(1, 1, 1)),
                Published("Pending.esp", approximation: PluginIssueApproximation.Pending)
            ],
            new(true, true), "Analyzing 1 of 2 plugins.", Affordance());
        var pendingKey = new PluginRefreshRowKey("Pending.esp", @"C:\Data\Pending.esp");
        var successor = admission.TryBeginRefresh(CancellationToken.None, out var replaced);
        successor.Should().NotBeNull();
        replaced.Should().BeSameAs(superseded);

        sut.TryFinalizeSupersededOperation(superseded, "Superseded", Affordance(), out var finalized)
            .Should().BeTrue();
        var notifications = new List<PluginRefreshSnapshot>();
        using var subscription = sut.Snapshots.Subscribe(notifications.Add);

        sut.TryPublishInitialApproximationResult(superseded,
                PluginRefreshPublicationRows.CreateTargetLookup([pendingKey]),
                new(pendingKey, PluginIssueApproximation.Available(9, 9, 9)), "Late", Affordance())
            .Should().BeFalse();
        sut.TryFinalizeInitialApproximation(superseded, "Own cleanup", Affordance(), out _).Should().BeFalse();
        sut.TryFinalizeSupersededOperation(superseded, "Again", Affordance(), out _).Should().BeFalse();
        sut.PublishAcceptedPublication(superseded, GameType.SkyrimSe, plan, CreateFreshnessToken(),
            plan.Configuration, [Published("Late.esp")], new(false, false), "Late", Affordance());

        finalized.Activity.Should().Be(new PluginRefreshActivity(false, false));
        sut.GetCurrentSnapshot().Should().BeSameAs(finalized);
        notifications.Should().ContainSingle("only the subscription replay is observed after finalization");
        sut.GetFreshnessInspection().Publication.Rows.Select(row => row.Plugin.Approximation).Should().Equal(
            PluginIssueApproximation.Available(1, 1, 1), PluginIssueApproximation.Unavailable);
        stateService.CurrentState.PluginsToClean.Select(plugin => plugin.Approximation).Should().Equal(
            PluginIssueApproximation.Available(1, 1, 1), PluginIssueApproximation.Unavailable);
    }

    /// <summary>A settings fence leaves no current operation and still finalizes the fenced operation's Pending rows.</summary>
    [Fact]
    public void Fence_WithNoSuccessor_FinalizesPendingRows()
    {
        var stateService = new StateService();
        var admission = new CleaningAdmission();
        using var sut = CreateStore(stateService, admission: admission);
        var plan = CreatePlan();
        var fenced = Begin(admission);
        sut.PublishAcceptedPublication(fenced, GameType.SkyrimSe, plan, CreateFreshnessToken(),
            plan.Configuration, [Published("Pending.esp", approximation: PluginIssueApproximation.Pending)],
            new(true, true), "Analyzing 0 of 1 plugins.", Affordance());

        admission.SupersedeRefresh().Should().BeSameAs(fenced);
        var finalizedRows = sut.TryFinalizeSupersededOperation(fenced, "Approximation refresh canceled.",
            Affordance(), out var finalized);

        admission.CurrentRefresh.Should().BeNull("a fence has no successor");
        fenced.IsCurrent.Should().BeFalse();
        fenced.Token.IsCancellationRequested.Should().BeTrue();
        finalizedRows.Should().BeTrue();
        finalized.StatusText.Should().Be("Approximation refresh canceled.");
        finalized.Activity.Should().Be(new PluginRefreshActivity(false, false));
        sut.GetFreshnessInspection().Publication.Rows.Should().ContainSingle()
            .Which.Plugin.Approximation.Should().Be(PluginIssueApproximation.Unavailable);
        stateService.CurrentState.PluginsToClean.Should().ContainSingle()
            .Which.Approximation.Should().Be(PluginIssueApproximation.Unavailable);
    }

    /// <summary>
    ///     A canceled operation that was not superseded can no longer commit results, but its own cancellation
    ///     cleanup may still finalize the estimates it owns.
    /// </summary>
    [Fact]
    public void CanceledOperation_CannotCommitResultsButCanFinalizeItsTail()
    {
        var stateService = new StateService();
        var admission = new CleaningAdmission();
        using var sut = CreateStore(stateService, admission: admission);
        var plan = CreatePlan();
        using var caller = new CancellationTokenSource();
        var operation = admission.TryBeginRefresh(caller.Token, out _)!;
        var key = new PluginRefreshRowKey("Pending.esp", @"C:\Data\Pending.esp");
        sut.PublishAcceptedPublication(operation, GameType.SkyrimSe, plan, CreateFreshnessToken(),
            plan.Configuration, [Published("Pending.esp", approximation: PluginIssueApproximation.Pending)],
            new(true, true), "Analyzing 0 of 1 plugins.", Affordance());

        caller.Cancel();

        operation.IsCurrent.Should().BeFalse();
        operation.IsSuperseded.Should().BeFalse();
        sut.TryPublishInitialApproximationResult(operation, PluginRefreshPublicationRows.CreateTargetLookup([key]),
                new(key, PluginIssueApproximation.Available(9, 9, 9)), "Late", Affordance())
            .Should().BeFalse();
        sut.TryFinalizeInitialApproximation(operation, "Canceled", Affordance(), out _).Should().BeTrue();
        sut.GetFreshnessInspection().Publication.Rows.Should().ContainSingle()
            .Which.Plugin.Approximation.Should().Be(PluginIssueApproximation.Unavailable);
    }

    /// <summary>An approximation result and the concurrent user selection must both survive publication.</summary>
    [Fact]
    public void ApplySelectionChange_WhenApproximationReplacesRows_ShouldApplySelectionToLatestPublication()
    {
        var stateService = Substitute.For<IStateService>();
        var backingState = new StateService();
        stateService.CurrentState.Returns(_ => backingState.CurrentState);
        stateService.When(service => service.SetPluginsToClean(Arg.Any<List<PluginInfo>>()))
            .Do(call => backingState.SetPluginsToClean(call.Arg<List<PluginInfo>>()!));
        stateService.When(service => service.UpdateExcludedPlugins(Arg.Any<Func<IReadOnlySet<string>, IReadOnlySet<string>>>()))
            .Do(call => backingState.UpdateExcludedPlugins(call.Arg<Func<IReadOnlySet<string>, IReadOnlySet<string>>>()!));
        var admission = new CleaningAdmission();
        using var sut = new PluginRefreshPublicationStore(new PluginRefreshAppStateMirror(stateService),
            new PluginRefreshCommandAvailabilityPolicy(admission), Affordance());
        var plan = CreatePlan();
        var target = new PluginRefreshRowKey("Target.esp", @"C:\Data\Target.esp");
        var operation = Begin(admission);
        sut.PublishAcceptedPublication(operation, GameType.SkyrimSe, plan, CreateFreshnessToken(),
            plan.Configuration, [Published("Target.esp", approximation: PluginIssueApproximation.Pending)],
            new(true, true), "Analyzing", Affordance());
        // Land the approximation commit once, between the selection's read and its compare-and-swap.
        sut.BeforeSelectionCommit = () =>
        {
            sut.BeforeSelectionCommit = null;
            sut.TryPublishInitialApproximationResult(operation,
                PluginRefreshPublicationRows.CreateTargetLookup([target]),
                new(target, PluginIssueApproximation.Available(3, 2, 1)), "Result arrived", Affordance());
        };

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
        var admission = new CleaningAdmission();
        using var sut = CreateStore(stateService, admission: admission);
        var plan = CreatePlan();
        var token = CreateFreshnessToken();

        var snapshot = sut.PublishAcceptedPublication(
            Begin(admission),
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
        var admission = new CleaningAdmission();
        using var sut = CreateStore(stateService, admission: admission);
        var plan = CreatePlan();

        sut.PublishAcceptedPublication(
            Begin(admission),
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
        var admission = new CleaningAdmission();
        using var sut = CreateStore(stateService, admission: admission);
        var plan = CreatePlan();
        sut.PublishAcceptedPublication(
            Begin(admission),
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
        var admission = new CleaningAdmission();
        using var sut = CreateStore(stateService, admission: admission);
        var plan = CreatePlan();
        var token = CreateFreshnessToken();
        sut.PublishAcceptedPublication(
            Begin(admission),
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

    /// <summary>A freshness verdict observed before a newer settings change cannot replace the publication's freshness.</summary>
    [Fact]
    public void PublishFreshnessIfCurrent_WhenSettingsObservationAdvanced_ShouldRejectVerdict()
    {
        var stateService = new StateService();
        var admission = new CleaningAdmission();
        using var sut = CreateStore(stateService, admission: admission);
        var plan = CreatePlan();
        var token = CreateFreshnessToken();
        sut.PublishAcceptedPublication(Begin(admission), GameType.SkyrimSe, plan, token, plan.Configuration,
            [Published("Selected.esp")], new(false, false), "Loaded", Affordance());
        var version = new PluginRefreshFreshnessVersion();
        var observation = version.Observe();
        version.Advance();

        var accepted = sut.PublishFreshnessIfCurrent(sut.GetFreshnessInspection().Publication, token,
            new PluginRefreshFreshness(false, PluginRefreshStalenessReason.LoadOrderPathChanged), observation);

        accepted.Should().BeFalse();
        observation.IsCurrent.Should().BeFalse();
        sut.GetFreshnessInspection().Publication.Freshness.Should().Be(PluginRefreshFreshness.Fresh);
    }

    /// <summary>A Cleaning admission reservation disables row commands and is reported in the snapshot.</summary>
    [Fact]
    public async Task PublishCommandAvailabilityIfChanged_WhenCleaningReserved_ShouldDisableCommandsWithoutReplacingRows()
    {
        var stateService = new StateService();
        var admission = new CleaningAdmission();
        using var sut = CreateStore(stateService, admission: admission);
        var plan = CreatePlan(true);
        var operation = Begin(admission);
        sut.PublishAcceptedPublication(
            operation,
            GameType.SkyrimSe,
            plan,
            CreateFreshnessToken(),
            plan.Configuration,
            [Published("Selected.esp")],
            new PluginRefreshActivity(false, false),
            "Loaded",
            Affordance());
        // The publishing operation has unwound; otherwise Cleaning admission would wait to drain it.
        operation.Dispose();
        var before = sut.GetCurrentSnapshot();

        before.Commands.CanSelectAll.Should().BeTrue();
        before.Commands.CanRefreshSelectedIssueApproximations.Should().BeTrue();
        before.Commands.IsCleaningReserved.Should().BeFalse();
        using var cleaning = await admission.EnterCleaningAsync();
        var commandInspection = sut.GetCommandAvailabilityInspection();
        sut.PublishCommandAvailabilityIfChanged(
            commandInspection,
            Affordance(),
            Affordance());
        var during = sut.GetCurrentSnapshot();

        during.Commands.IsCleaningReserved.Should().BeTrue();
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
        var admission = new CleaningAdmission();
        using var sut = CreateStore(stateService, admission: admission);
        var plan = CreatePlan();
        var operation = Begin(admission);
        sut.PublishAcceptedPublication(
            operation,
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
        var targets = sut.GetCurrentSnapshot().Rows
            .Where(row => row.IsSelected)
            .Select(row => row.Key)
            .ToList();
        var targetLookup = PluginRefreshPublicationRows.CreateTargetLookup(targets);

        var target = targets.Single(key => key.FileName == "Target.esp");
        var matched = sut.TryPublishInitialApproximationResult(
            operation,
            targetLookup,
            new PluginIssueApproximationModuleResult(
                target,
                PluginIssueApproximation.Available(3, 2, 1)),
            "Analyzing 1 of 2 plugins.",
            Affordance());
        var nonTargetMatched = sut.TryPublishInitialApproximationResult(
            operation,
            targetLookup,
            new PluginIssueApproximationModuleResult(
                new PluginRefreshRowKey("Other.esp", @"C:\Data\Other.esp"),
                PluginIssueApproximation.Available(9, 9, 9)),
            "Analyzing 2 of 2 plugins.",
            Affordance());

        targets.Select(key => key.FileName).Should().Equal("Target.esp", "PendingOnly.esp");
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
            operation,
            "Refreshed 1 plugin approximations.",
            Affordance(),
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

    /// <summary>Begins an operation that supersedes whichever operation the admission currently has.</summary>
    private static RefreshOperation Begin(CleaningAdmission admission)
    {
        return admission.TryBeginRefresh(CancellationToken.None, out _)
               ?? throw new InvalidOperationException("Cleaning unexpectedly reserved admission.");
    }

    /// <summary>Fences refresh work the way a Discovery settings change does: supersede, then revoke freshness.</summary>
    private static void Fence(CleaningAdmission admission, PluginRefreshPublicationStore store)
    {
        admission.SupersedeRefresh();
        store.InvalidatePublication();
    }

    private static PluginRefreshPublicationStore CreateStore(
        StateService stateService,
        bool canAttemptIssueApproximation = true,
        CleaningAdmission? admission = null)
    {
        return new PluginRefreshPublicationStore(
            new PluginRefreshAppStateMirror(stateService),
            new PluginRefreshCommandAvailabilityPolicy(admission ?? new CleaningAdmission()),
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
