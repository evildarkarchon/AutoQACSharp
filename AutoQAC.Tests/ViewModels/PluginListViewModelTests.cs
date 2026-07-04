using System.Reactive.Subjects;
using AutoQAC.Models;
using AutoQAC.Services.Plugin;
using AutoQAC.ViewModels.MainWindow;
using FluentAssertions;

namespace AutoQAC.Tests.ViewModels;

public sealed class PluginListViewModelTests
{
    [Fact]
    public void OnPluginRefreshSnapshot_ShouldApplyRowsAndCommandAvailability()
    {
        var module = new RecordingPluginRefreshModule();
        var vm = new PluginListViewModel(module);
        try
        {
            vm.OnPluginRefreshSnapshot(Snapshot(
                rows: [Row("A.esp", isSelected: true)],
                commands: new PluginRefreshCommandAvailability(
                    CanSelectAll: true,
                    CanDeselectAll: true,
                    CanRefreshSelectedIssueApproximations: true,
                    CanCancelRefresh: false)));

            vm.PluginsToClean.Should().ContainSingle(item =>
                item.FileName == "A.esp" && item.IsSelected);
            vm.SelectAllCommand.CanExecute(null).Should().BeTrue();
            vm.DeselectAllCommand.CanExecute(null).Should().BeTrue();
            vm.RefreshSelectedApproximationsCommand.CanExecute(null).Should().BeTrue();
            vm.CancelApproximationRefreshCommand.CanExecute(null).Should().BeFalse();
        }
        finally
        {
            vm.Dispose();
            module.Dispose();
        }
    }

    [Fact]
    public void OnPluginRefreshSnapshot_ShouldUseSnapshotRunningFlags()
    {
        var module = new RecordingPluginRefreshModule();
        var vm = new PluginListViewModel(module);
        try
        {
            vm.OnPluginRefreshSnapshot(Snapshot(
                rows: [Row("A.esp")],
                activity: new PluginRefreshActivity(
                    IsPluginRefreshRunning: false,
                    IsIssueApproximationRefreshRunning: true),
                commands: new PluginRefreshCommandAvailability(false, false, false, true)));

            vm.IsApproximationRefreshRunning.Should().BeTrue();
            vm.CancelApproximationRefreshCommand.CanExecute(null).Should().BeTrue();

            vm.OnPluginRefreshSnapshot(Snapshot(
                rows: [Row("A.esp")],
                activity: new PluginRefreshActivity(false, false),
                commands: new PluginRefreshCommandAvailability(true, true, true, false)));

            vm.IsApproximationRefreshRunning.Should().BeFalse();
            vm.CancelApproximationRefreshCommand.CanExecute(null).Should().BeFalse();
        }
        finally
        {
            vm.Dispose();
            module.Dispose();
        }
    }

    [Fact]
    public async Task SelectionCommands_ShouldSendSelectionIntents()
    {
        var module = new RecordingPluginRefreshModule();
        var vm = new PluginListViewModel(module);
        try
        {
            vm.OnPluginRefreshSnapshot(Snapshot(
                rows: [Row("A.esp")],
                commands: new PluginRefreshCommandAvailability(true, true, false, false)));

            await vm.SelectAllCommand.ExecuteAsync(null);
            await vm.DeselectAllCommand.ExecuteAsync(null);

            var changes = module.Intents.OfType<PluginRefreshIntent.ChangeSelection>()
                .Select(intent => intent.Change)
                .ToList();
            changes.Should().HaveCount(2);
            changes[0].Should().BeOfType<PluginSelectionChange.SelectAllVisible>();
            changes[1].Should().BeOfType<PluginSelectionChange.DeselectAllVisible>();
        }
        finally
        {
            vm.Dispose();
            module.Dispose();
        }
    }

    [Fact]
    public void RowToggle_ShouldSendSetOneIntent()
    {
        var module = new RecordingPluginRefreshModule();
        var vm = new PluginListViewModel(module);
        try
        {
            vm.OnPluginRefreshSnapshot(Snapshot(rows: [Row("A.esp", @"C:\Game\Data\A.esp", true)]));

            vm.PluginsToClean.Single().IsSelected = false;

            var setOne = module.Intents.OfType<PluginRefreshIntent.ChangeSelection>()
                .Select(intent => intent.Change)
                .OfType<PluginSelectionChange.SetOne>()
                .Should().ContainSingle().Which;
            setOne.Row.FileName.Should().Be("A.esp");
            setOne.Row.FullPath.Should().Be(@"C:\Game\Data\A.esp");
            setOne.IsSelected.Should().BeFalse();
        }
        finally
        {
            vm.Dispose();
            module.Dispose();
        }
    }

    [Fact]
    public async Task RefreshSelectedApproximationsCommand_ShouldSendTargetlessRefreshIntent()
    {
        var module = new RecordingPluginRefreshModule();
        var vm = new PluginListViewModel(module);
        try
        {
            vm.OnPluginRefreshSnapshot(Snapshot(
                rows: [Row("A.esp")],
                commands: new PluginRefreshCommandAvailability(false, false, true, false)));

            await vm.RefreshSelectedApproximationsCommand.ExecuteAsync(null);

            module.Intents.OfType<PluginRefreshIntent.RefreshSelectedIssueApproximations>()
                .Should().ContainSingle();
        }
        finally
        {
            vm.Dispose();
            module.Dispose();
        }
    }

    [Fact]
    public async Task CancelApproximationRefreshCommand_ShouldSendManualCancelIntent()
    {
        var module = new RecordingPluginRefreshModule();
        var vm = new PluginListViewModel(module);
        try
        {
            vm.OnPluginRefreshSnapshot(Snapshot(
                rows: [Row("A.esp")],
                commands: new PluginRefreshCommandAvailability(false, false, false, true)));

            await vm.CancelApproximationRefreshCommand.ExecuteAsync(null);

            module.Intents.OfType<PluginRefreshIntent.Cancel>()
                .Should().ContainSingle(cancel => cancel.Reason == PluginRefreshCancelReason.Manual);
        }
        finally
        {
            vm.Dispose();
            module.Dispose();
        }
    }

    [Fact]
    public void SnapshotRowUpdates_ShouldPreserveSelectionFromSnapshotAfterApproximationMerge()
    {
        var module = new RecordingPluginRefreshModule();
        var vm = new PluginListViewModel(module);
        try
        {
            vm.OnPluginRefreshSnapshot(Snapshot(rows: [Row("A.esp", isSelected: false)]));

            vm.OnPluginRefreshSnapshot(Snapshot(rows:
            [
                Row("A.esp", isSelected: false, approximation: PluginIssueApproximation.Available(3, 1, 0))
            ]));

            vm.PluginsToClean.Should().ContainSingle(item =>
                item.FileName == "A.esp" &&
                !item.IsSelected &&
                item.Approximation.Status == PluginIssueApproximationStatus.Available);
        }
        finally
        {
            vm.Dispose();
            module.Dispose();
        }
    }

    private static PluginRefreshSnapshot Snapshot(
        IReadOnlyList<PluginRefreshRow>? rows = null,
        PluginRefreshActivity? activity = null,
        PluginRefreshCommandAvailability? commands = null,
        string statusText = "Ready") =>
        new(
            Generation: 1,
            GameType: GameType.SkyrimSe,
            Rows: rows ?? [],
            Configuration: new PluginRefreshConfigurationProjection(
                LoadOrderPath: null,
                GameDataFolder: @"C:\Game\Data",
                HasGameDataFolderOverride: false,
                XEditPath: null,
                Mo2Path: null,
                Mo2ModeEnabled: false,
                Mo2InstancePath: null,
                IsMo2InstanceOverride: false,
                IsMo2InstanceValid: null,
                AvailableProfiles: [],
                SelectedProfile: null,
                CleaningTimeout: 300),
            Activity: activity ?? new PluginRefreshActivity(false, false),
            Commands: commands ?? new PluginRefreshCommandAvailability(true, true, true, false),
            StatusText: statusText);

    private static PluginRefreshRow Row(
        string fileName,
        string? fullPath = null,
        bool isSelected = true,
        PluginIssueApproximation? approximation = null) =>
        new(
            fileName,
            fullPath ?? $@"C:\Game\Data\{fileName}",
            GameType.SkyrimSe,
            isSelected,
            IsInSkipList: false,
            approximation ?? PluginIssueApproximation.Unavailable);

    private sealed class RecordingPluginRefreshModule : IPluginRefreshModule, IDisposable
    {
        private readonly Subject<PluginRefreshSnapshot> _snapshots = new();
        private PluginRefreshSnapshot _lastSnapshot = Snapshot();

        public List<PluginRefreshIntent> Intents { get; } = [];

        public IObservable<PluginRefreshSnapshot> Snapshots => _snapshots;

        public Task<PluginRefreshSnapshot> ExecuteAsync(
            PluginRefreshIntent intent,
            CancellationToken cancellationToken = default)
        {
            Intents.Add(intent);
            return Task.FromResult(_lastSnapshot);
        }

        public Task<PluginRefreshPublication> GetCurrentPublicationAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PluginRefreshPublication(
                _lastSnapshot.Generation,
                _lastSnapshot.GameType,
                DiscoveryPlan: null,
                _lastSnapshot.Configuration,
                PluginRefreshFreshness.Missing,
                Rows: [],
                _lastSnapshot.Rows,
                _lastSnapshot.Activity,
                _lastSnapshot.Commands,
                _lastSnapshot.StatusText));

        public void Publish(PluginRefreshSnapshot snapshot)
        {
            _lastSnapshot = snapshot;
            _snapshots.OnNext(snapshot);
        }

        public void Dispose() => _snapshots.Dispose();
    }
}
