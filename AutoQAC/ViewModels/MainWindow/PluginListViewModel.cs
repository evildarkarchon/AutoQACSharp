using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AutoQAC.Models;
using AutoQAC.Services.Plugin;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoQAC.ViewModels.MainWindow;

/// <summary>
///     Manages the plugin collection and plugin-refresh commands from module snapshots.
/// </summary>
public sealed partial class PluginListViewModel(IPluginRefreshModule pluginRefreshModule) : ViewModelBase, IDisposable
{
    private readonly IPluginRefreshModule _pluginRefreshModule = pluginRefreshModule;
    private PluginRefreshCommandAvailability _publishedCommands =
        new(false, false, false, false);
    private bool _cleaningReserved;

    public ObservableCollection<PluginListItem> PluginsToClean { get; } = [];

    [ObservableProperty] public partial PluginListItem? SelectedPlugin { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SelectAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeselectAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshSelectedApproximationsCommand))]
    public partial bool HasPlugins { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SelectAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeselectAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshSelectedApproximationsCommand))]
    public partial bool IsCleaning { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRefreshApproximations))]
    [NotifyCanExecuteChangedFor(nameof(RefreshSelectedApproximationsCommand))]
    public partial GameType CurrentGameType { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshSelectedApproximationsCommand))]
    public partial bool HasSelectedVisiblePlugin { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelApproximationRefreshCommand))]
    public partial bool IsApproximationRefreshRunning { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SelectAllCommand))]
    [NotifyPropertyChangedFor(nameof(CanChangePluginSelection))]
    public partial bool CanSelectAllPlugins { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeselectAllCommand))]
    public partial bool CanDeselectAllPlugins { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRefreshApproximations))]
    [NotifyCanExecuteChangedFor(nameof(RefreshSelectedApproximationsCommand))]
    public partial bool CanRefreshSelectedIssueApproximations { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelApproximationRefreshCommand))]
    public partial bool CanCancelRefresh { get; set; }

    public bool CanRefreshApproximations => CanRefreshSelectedIssueApproximations;

    /// <summary>Whether visible Plugin selection may be changed under the current publication and cleaning admission.</summary>
    public bool CanChangePluginSelection => CanSelectAllPlugins;

    public void Dispose()
    {
        foreach (var item in PluginsToClean) DetachItem(item);
    }

    /// <summary>Projects cleaning startup admission immediately, before AppState publishes active cleaning.</summary>
    public void OnCleaningAdmissionChanged(bool reserved)
    {
        _cleaningReserved = reserved;
        ApplyCommandAvailability();
    }

    private bool CanSelectAll()
    {
        return CanSelectAllPlugins;
    }

    private bool CanDeselectAll()
    {
        return CanDeselectAllPlugins;
    }

    private bool CanRefreshSelectedApproximations()
    {
        return CanRefreshSelectedIssueApproximations;
    }

    private bool CanCancelApproximationRefresh()
    {
        return CanCancelRefresh;
    }

    [RelayCommand(CanExecute = nameof(CanSelectAll))]
    private Task SelectAllAsync()
    {
        return _pluginRefreshModule.ExecuteAsync(
            new PluginRefreshIntent.ChangeSelection(new PluginSelectionChange.SelectAllVisible()));
    }

    [RelayCommand(CanExecute = nameof(CanDeselectAll))]
    private Task DeselectAllAsync()
    {
        return _pluginRefreshModule.ExecuteAsync(
            new PluginRefreshIntent.ChangeSelection(new PluginSelectionChange.DeselectAllVisible()));
    }

    /// <summary>
    ///     Requests a targeted issue-approximation refresh for currently selected visible rows.
    ///     The module materializes the selected target snapshot when the intent is accepted.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRefreshSelectedApproximations))]
    private Task RefreshSelectedApproximationsAsync()
    {
        return _pluginRefreshModule.ExecuteAsync(new PluginRefreshIntent.RefreshSelectedIssueApproximations());
    }

    /// <summary>
    ///     Cancels active Plugin refresh work without prompting the user.
    ///     Completed row results are left intact by the module's cancellation policy.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCancelApproximationRefresh))]
    private Task CancelApproximationRefreshAsync()
    {
        return _pluginRefreshModule.ExecuteAsync(new PluginRefreshIntent.Cancel(PluginRefreshCancelReason.Manual));
    }

    /// <summary>
    ///     Applies the whole Plugin refresh snapshot published by the module.
    /// </summary>
    /// <param name="snapshot">Current visible Plugin refresh snapshot.</param>
    public void OnPluginRefreshSnapshot(PluginRefreshSnapshot snapshot)
    {
        HasPlugins = snapshot.Rows.Count > 0;
        CurrentGameType = snapshot.GameType;
        HasSelectedVisiblePlugin = snapshot.Rows.Any(row => row.IsSelected);
        IsApproximationRefreshRunning =
            snapshot.Activity.IsIssueApproximationRefreshRunning || snapshot.Commands.CanCancelRefresh;
        _publishedCommands = snapshot.Commands;
        ApplyCommandAvailability();

        SyncRows(snapshot.Rows);
    }

    private void ApplyCommandAvailability()
    {
        IsCleaning = _cleaningReserved || (!_publishedCommands.CanSelectAll && HasPlugins);
        CanSelectAllPlugins = !_cleaningReserved && _publishedCommands.CanSelectAll;
        CanDeselectAllPlugins = !_cleaningReserved && _publishedCommands.CanDeselectAll;
        CanRefreshSelectedIssueApproximations =
            !_cleaningReserved && _publishedCommands.CanRefreshSelectedIssueApproximations;
        CanCancelRefresh = _publishedCommands.CanCancelRefresh;
    }

    private void SyncRows(IReadOnlyList<PluginRefreshRow> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var nextRow = rows[i];

            if (i >= PluginsToClean.Count)
            {
                PluginsToClean.Add(CreateItem(nextRow));
                continue;
            }

            var existing = PluginsToClean[i];
            if (IsSamePlugin(existing.Info, nextRow))
            {
                if (!ReferenceEquals(existing.Info, nextRow)) existing.UpdateInfo(nextRow);

                existing.SetSelectedFromState(nextRow.IsSelected);
                continue;
            }

            DetachItem(existing);
            if (SelectedPlugin is not null && IsSamePlugin(SelectedPlugin.Info, existing.Info)) SelectedPlugin = null;

            PluginsToClean[i] = CreateItem(nextRow);
        }

        while (PluginsToClean.Count > rows.Count)
        {
            var removed = PluginsToClean[^1];
            DetachItem(removed);
            if (SelectedPlugin is not null && IsSamePlugin(SelectedPlugin.Info, removed.Info)) SelectedPlugin = null;

            PluginsToClean.RemoveAt(PluginsToClean.Count - 1);
        }
    }

    private PluginListItem CreateItem(PluginRefreshRow row)
    {
        var item = new PluginListItem(row, row.IsSelected);
        item.SelectionToggled += OnRowSelectionToggled;
        return item;
    }

    private void DetachItem(PluginListItem item)
    {
        item.SelectionToggled -= OnRowSelectionToggled;
    }

    private void OnRowSelectionToggled(PluginListItem item, bool isSelected)
    {
        if (!CanChangePluginSelection)
        {
            // A checkbox can finish its binding update after admission closes; restore the authoritative publication value.
            item.SetSelectedFromState(item.Info.IsSelected);
            return;
        }

        _ = _pluginRefreshModule.ExecuteAsync(
            new PluginRefreshIntent.ChangeSelection(
                new PluginSelectionChange.SetOne(item.Key, isSelected)));
    }

    private static bool IsSamePlugin(PluginRefreshRow left, PluginRefreshRow right)
    {
        return string.Equals(left.FullPath, right.FullPath, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(left.FileName, right.FileName, StringComparison.OrdinalIgnoreCase);
    }
}
