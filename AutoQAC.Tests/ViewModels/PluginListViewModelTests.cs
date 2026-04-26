using System.Reactive.Linq;
using AutoQAC.Models;
using AutoQAC.Services.State;
using AutoQAC.ViewModels.MainWindow;
using FluentAssertions;
using NSubstitute;

namespace AutoQAC.Tests.ViewModels;

/// <summary>
/// Regression coverage for the selection model. Bug history: <c>IsSelected</c> used
/// to live on the <c>PluginInfo</c> record; batch select/deselect mutated those
/// records in place without raising <c>INotifyPropertyChanged</c>, so the UI never
/// repainted, and rapid background <c>MergePluginApproximation</c> calls created
/// new records via <c>with</c> that overwrote the user's selection. The tests
/// below exercise the new flow: selection lives in <c>AppState.ExcludedPluginPaths</c>,
/// PluginListItem owns the INPC checkbox state, and toggles round-trip through
/// IStateService so they survive plugin record replacement.
/// </summary>
public sealed class PluginListViewModelTests
{
    [Fact]
    public void DeselectAllCommand_ShouldExcludeEveryVisiblePluginInState()
    {
        var stateService = new StateService();
        stateService.SetPluginsToClean([
            new PluginInfo { FileName = "A.esp", FullPath = @"C:\A.esp" },
            new PluginInfo { FileName = "B.esp", FullPath = @"C:\B.esp" }
        ]);

        var vm = new PluginListViewModel(stateService);
        try
        {
            vm.OnStateChanged(stateService.CurrentState);

            vm.DeselectAllCommand.Execute(null);

            stateService.CurrentState.ExcludedPluginPaths.Should()
                .BeEquivalentTo(new[] { @"C:\A.esp", @"C:\B.esp" });
            // Sync state -> VM (the parent dispatches this on the UI thread in production).
            vm.OnStateChanged(stateService.CurrentState);
            vm.PluginsToClean.Should().AllSatisfy(item => item.IsSelected.Should().BeFalse());
        }
        finally
        {
            vm.Dispose();
            stateService.Dispose();
        }
    }

    [Fact]
    public void SelectAllCommand_ShouldClearExclusionsForVisiblePlugins()
    {
        var stateService = new StateService();
        stateService.SetPluginsToClean([
            new PluginInfo { FileName = "A.esp", FullPath = @"C:\A.esp" },
            new PluginInfo { FileName = "B.esp", FullPath = @"C:\B.esp" }
        ]);
        stateService.UpdateExcludedPlugins(_ =>
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\A.esp", @"C:\B.esp" });

        var vm = new PluginListViewModel(stateService);
        try
        {
            vm.OnStateChanged(stateService.CurrentState);
            vm.PluginsToClean.Should().AllSatisfy(item => item.IsSelected.Should().BeFalse());

            vm.SelectAllCommand.Execute(null);

            stateService.CurrentState.ExcludedPluginPaths.Should().BeEmpty();
            vm.OnStateChanged(stateService.CurrentState);
            vm.PluginsToClean.Should().AllSatisfy(item => item.IsSelected.Should().BeTrue());
        }
        finally
        {
            vm.Dispose();
            stateService.Dispose();
        }
    }

    [Fact]
    public void TogglingPluginListItemIsSelected_ShouldUpdateExcludedPluginsInState()
    {
        var stateService = new StateService();
        stateService.SetPluginsToClean([
            new PluginInfo { FileName = "A.esp", FullPath = @"C:\A.esp" }
        ]);

        var vm = new PluginListViewModel(stateService);
        try
        {
            vm.OnStateChanged(stateService.CurrentState);
            var row = vm.PluginsToClean.Single();

            row.IsSelected = false;

            stateService.CurrentState.ExcludedPluginPaths.Should().Contain(@"C:\A.esp");

            row.IsSelected = true;

            stateService.CurrentState.ExcludedPluginPaths.Should().NotContain(@"C:\A.esp");
        }
        finally
        {
            vm.Dispose();
            stateService.Dispose();
        }
    }

    [Fact]
    public void Selection_ShouldSurviveApproximationMerge()
    {
        // Pins down the original bug: rapid MergePluginApproximation calls used to
        // replace PluginInfo records and clobber a user's IsSelected. Selection now
        // lives in AppState.ExcludedPluginPaths, so it must remain stable across
        // approximation updates.
        var stateService = new StateService();
        stateService.SetPluginsToClean([
            new PluginInfo
            {
                FileName = "A.esp",
                FullPath = @"C:\A.esp",
                Approximation = PluginIssueApproximation.Pending
            }
        ]);

        var vm = new PluginListViewModel(stateService);
        try
        {
            vm.OnStateChanged(stateService.CurrentState);
            var row = vm.PluginsToClean.Single();

            // User unchecks the box.
            row.IsSelected = false;
            stateService.CurrentState.ExcludedPluginPaths.Should().Contain(@"C:\A.esp");

            // Simulate a streaming approximation update — replaces the PluginInfo record.
            stateService.MergePluginApproximation(new PluginIssueApproximationResult
            {
                FileName = "A.esp",
                FullPath = @"C:\A.esp",
                Approximation = PluginIssueApproximation.Available(3, 1, 0)
            });

            stateService.CurrentState.ExcludedPluginPaths.Should().Contain(@"C:\A.esp");
            vm.OnStateChanged(stateService.CurrentState);
            vm.PluginsToClean.Single().IsSelected.Should().BeFalse(
                "the user's deselection must survive PluginInfo record replacement");
        }
        finally
        {
            vm.Dispose();
            stateService.Dispose();
        }
    }

    [Fact]
    public void OnStateChanged_ShouldNotEchoSelectionTogglesBackToState()
    {
        // When state pushes a new ExcludedPluginPaths set into the VM, the wrapper's
        // IsSelected updates via SetSelectedFromState, which must NOT re-fire
        // SelectionToggled — otherwise we'd bounce the change back into state and
        // mask race conditions.
        var stateService = Substitute.For<IStateService>();
        stateService.StateChanged.Returns(Observable.Never<AppState>());
        var initialState = new AppState
        {
            PluginsToClean = [new PluginInfo { FileName = "A.esp", FullPath = @"C:\A.esp" }]
        };
        stateService.CurrentState.Returns(initialState);

        var vm = new PluginListViewModel(stateService);
        try
        {
            vm.OnStateChanged(initialState);

            stateService.ClearReceivedCalls();

            var nextState = initialState with
            {
                ExcludedPluginPaths =
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\A.esp" }
            };
            vm.OnStateChanged(nextState);

            vm.PluginsToClean.Single().IsSelected.Should().BeFalse();
            stateService.DidNotReceiveWithAnyArgs()
                .UpdateExcludedPlugins(default!);
        }
        finally
        {
            vm.Dispose();
        }
    }

    /// <summary>
    /// Regression for the Codex-flagged bleed: the user deselects A.esp under one
    /// game, then a different game is loaded that also has a file named A.esp at a
    /// different path. The new row must render selected and the old exclusion must
    /// be pruned by SetPluginsToClean, since exclusions are keyed by full path.
    /// </summary>
    [Fact]
    public void Selection_ShouldNotLeakAcrossPluginListReplacement_WhenFileNamesCollide()
    {
        var stateService = new StateService();
        stateService.SetPluginsToClean([
            new PluginInfo { FileName = "A.esp", FullPath = @"C:\GameA\Data\A.esp" }
        ]);

        var vm = new PluginListViewModel(stateService);
        try
        {
            vm.OnStateChanged(stateService.CurrentState);

            // User deselects A.esp under Game A.
            vm.PluginsToClean.Single().IsSelected = false;
            stateService.CurrentState.ExcludedPluginPaths.Should()
                .Contain(@"C:\GameA\Data\A.esp");

            // Switch to Game B — different load order, same file name at a different path.
            stateService.SetPluginsToClean([
                new PluginInfo { FileName = "A.esp", FullPath = @"C:\GameB\Data\A.esp" }
            ]);
            vm.OnStateChanged(stateService.CurrentState);

            stateService.CurrentState.ExcludedPluginPaths.Should()
                .NotContain(@"C:\GameA\Data\A.esp",
                    "the Game A path is no longer in the visible list and must be pruned");
            stateService.CurrentState.ExcludedPluginPaths.Should()
                .NotContain(@"C:\GameB\Data\A.esp",
                    "the user never deselected the Game B plugin");
            vm.PluginsToClean.Single().IsSelected.Should().BeTrue(
                "the freshly loaded Game B A.esp must default to selected — the Game A deselection must not bleed across");
        }
        finally
        {
            vm.Dispose();
            stateService.Dispose();
        }
    }
}
