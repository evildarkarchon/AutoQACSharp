using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoQAC.ViewModels.MainWindow;

/// <summary>
/// Manages the plugin collection and select/deselect all commands for the main
/// window plugin list. Receives state via <see cref="OnStateChanged"/> from the
/// parent VM (which marshals onto the UI thread via <c>IUiDispatcher</c>).
/// </summary>
public sealed partial class PluginListViewModel : ViewModelBase, IDisposable
{
    private readonly IStateService _stateService;
    private readonly IPluginRefreshCoordinator _pluginRefreshCoordinator;
    private readonly IPluginRefreshCapabilityPolicy _refreshCapabilityPolicy;
    private readonly IDisposable _pluginRefreshStatusSubscription;

    public ObservableCollection<PluginListItem> PluginsToClean { get; } = new();

    [ObservableProperty]
    private PluginListItem? _selectedPlugin;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SelectAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeselectAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshSelectedApproximationsCommand))]
    private bool _hasPlugins;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SelectAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeselectAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshSelectedApproximationsCommand))]
    private bool _isCleaning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRefreshApproximations))]
    [NotifyCanExecuteChangedFor(nameof(RefreshSelectedApproximationsCommand))]
    private GameType _currentGameType = GameType.Unknown;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshSelectedApproximationsCommand))]
    private bool _hasSelectedVisiblePlugin;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelApproximationRefreshCommand))]
    private bool _isApproximationRefreshRunning;

    public bool CanRefreshApproximations =>
        CurrentGameType != GameType.Unknown && _refreshCapabilityPolicy.SupportsIssueApproximation(CurrentGameType);

    public PluginListViewModel(
        IStateService stateService,
        IPluginRefreshCoordinator? pluginRefreshCoordinator = null,
        IPluginRefreshCapabilityPolicy? refreshCapabilityPolicy = null)
    {
        _stateService = stateService;
        _pluginRefreshCoordinator = pluginRefreshCoordinator ?? NoOpPluginRefreshCoordinator.Instance;
        _refreshCapabilityPolicy = refreshCapabilityPolicy ?? NoApproximationRefreshCapabilityPolicy.Instance;
        _pluginRefreshStatusSubscription = _pluginRefreshCoordinator.StatusChanged.Subscribe(
            new CallbackObserver<PluginRefreshStatus>(OnPluginRefreshStatusChanged));
        // No subscription here — the parent VM dispatches OnStateChanged on the UI thread.
        // Initial pull from current state so commands reflect reality before first change event.
        var initial = stateService.CurrentState;
        HasPlugins = initial.PluginsToClean.Count > 0;
        IsCleaning = initial.IsCleaning;
        CurrentGameType = initial.CurrentGameType;
    }

    private bool CanSelectPlugins() => HasPlugins && !IsCleaning;

    private bool CanRefreshSelectedApproximations() =>
        HasPlugins && !IsCleaning && HasSelectedVisiblePlugin && CanRefreshApproximations;

    private bool CanCancelApproximationRefresh() => IsApproximationRefreshRunning;

    [RelayCommand(CanExecute = nameof(CanSelectPlugins))]
    private void SelectAll()
    {
        // Drop every visible plugin's path from the excluded set (they all become selected).
        var visiblePaths = PluginsToClean.Select(p => p.FullPath).ToList();
        _stateService.UpdateExcludedPlugins(current =>
        {
            if (current.Count == 0)
            {
                return current;
            }

            var next = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
            foreach (var path in visiblePaths)
            {
                next.Remove(path);
            }

            return next.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        });
    }

    [RelayCommand(CanExecute = nameof(CanSelectPlugins))]
    private void DeselectAll()
    {
        // Add every visible plugin's path to the excluded set (they all become deselected).
        var visiblePaths = PluginsToClean.Select(p => p.FullPath).ToList();
        _stateService.UpdateExcludedPlugins(current =>
        {
            var next = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
            foreach (var path in visiblePaths)
            {
                next.Add(path);
            }

            return next.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// Requests a targeted issue-approximation refresh for the currently checked visible plugin rows.
    /// The target list is snapshotted before awaiting the coordinator so later checkbox changes do not mutate active work.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRefreshSelectedApproximations))]
    private async Task RefreshSelectedApproximationsAsync()
    {
        var targets = PluginsToClean
            .Where(plugin => plugin.IsSelected)
            .Select(plugin => new PluginRefreshTarget(plugin.FileName, plugin.FullPath))
            .ToList();

        if (targets.Count == 0)
        {
            await _pluginRefreshCoordinator.RefreshSelectedApproximationsAsync(
                new PluginRefreshRequest(CurrentGameType),
                targets).ConfigureAwait(false);
            return;
        }

        IsApproximationRefreshRunning = true;
        try
        {
            await _pluginRefreshCoordinator.RefreshSelectedApproximationsAsync(
                new PluginRefreshRequest(CurrentGameType),
                targets).ConfigureAwait(false);
        }
        finally
        {
            IsApproximationRefreshRunning = false;
        }
    }

    /// <summary>
    /// Cancels any active approximation refresh without prompting the user.
    /// Completed row results are left intact by the coordinator's cancellation policy.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCancelApproximationRefresh))]
    private void CancelApproximationRefresh()
    {
        _pluginRefreshCoordinator.CancelActiveRefresh(PluginRefreshCancelReason.Manual);
    }

    /// <summary>
    /// Updates the plugin list and gating flags from application state. Called by parent
    /// on state changes (already on the UI thread).
    /// </summary>
    public void OnStateChanged(AppState state)
    {
        IsCleaning = state.IsCleaning;
        HasPlugins = state.PluginsToClean.Count > 0;
        CurrentGameType = state.CurrentGameType;

        var displayPlugins = state.PluginsToClean.Where(p => !p.IsInSkipList).ToList();
        var excluded = state.ExcludedPluginPaths;

        // 1. Sync each visible row position-by-position. Reuse the existing wrapper when
        //    the underlying plugin matches by full path so the row's IsSelected stays in
        //    sync without firing SelectionToggled back into state.
        for (var i = 0; i < displayPlugins.Count; i++)
        {
            var nextPlugin = displayPlugins[i];
            var isSelected = !excluded.Contains(nextPlugin.FullPath);

            if (i >= PluginsToClean.Count)
            {
                PluginsToClean.Add(CreateItem(nextPlugin, isSelected));
                continue;
            }

            var existing = PluginsToClean[i];
            if (IsSamePlugin(existing.Info, nextPlugin))
            {
                if (!ReferenceEquals(existing.Info, nextPlugin))
                {
                    existing.UpdateInfo(nextPlugin);
                }

                existing.SetSelectedFromState(isSelected);
                continue;
            }

            // Different plugin at this slot — replace, but detach the old subscription first.
            DetachItem(existing);
            if (SelectedPlugin is not null && IsSamePlugin(SelectedPlugin.Info, existing.Info))
            {
                SelectedPlugin = null;
            }

            PluginsToClean[i] = CreateItem(nextPlugin, isSelected);
        }

        // 2. Trim trailing rows that no longer exist in state.
        while (PluginsToClean.Count > displayPlugins.Count)
        {
            var removed = PluginsToClean[^1];
            DetachItem(removed);
            if (SelectedPlugin is not null && IsSamePlugin(SelectedPlugin.Info, removed.Info))
            {
                SelectedPlugin = null;
            }

            PluginsToClean.RemoveAt(PluginsToClean.Count - 1);
        }

        RecomputeSelectedVisiblePluginFlag();
    }

    private PluginListItem CreateItem(PluginInfo info, bool isSelected)
    {
        var item = new PluginListItem(info, isSelected);
        item.SelectionToggled += OnRowSelectionToggled;
        return item;
    }

    private void DetachItem(PluginListItem item)
    {
        item.SelectionToggled -= OnRowSelectionToggled;
    }

    private void OnRowSelectionToggled(PluginListItem item, bool isSelected)
    {
        var fullPath = item.FullPath;
        _stateService.UpdateExcludedPlugins(current =>
        {
            var alreadyExcluded = current.Contains(fullPath);
            if (isSelected ? !alreadyExcluded : alreadyExcluded)
            {
                return current;
            }

            var next = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
            if (isSelected)
            {
                next.Remove(fullPath);
            }
            else
            {
                next.Add(fullPath);
            }

            return next.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        });
        RecomputeSelectedVisiblePluginFlag();
    }

    private void RecomputeSelectedVisiblePluginFlag()
    {
        HasSelectedVisiblePlugin = PluginsToClean.Any(plugin => plugin.IsSelected);
    }

    private void OnPluginRefreshStatusChanged(PluginRefreshStatus status)
    {
        IsApproximationRefreshRunning = status.Kind is
            PluginRefreshStatusKind.LoadingPlugins or
            PluginRefreshStatusKind.AnalyzingSelected;
    }

    private static bool IsSamePlugin(PluginInfo left, PluginInfo right) =>
        string.Equals(left.FullPath, right.FullPath, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(left.FileName, right.FileName, StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        _pluginRefreshStatusSubscription.Dispose();
        foreach (var item in PluginsToClean)
        {
            DetachItem(item);
        }
    }

    private sealed class NoOpPluginRefreshCoordinator : IPluginRefreshCoordinator
    {
        public static NoOpPluginRefreshCoordinator Instance { get; } = new();

        public IObservable<PluginRefreshStatus> StatusChanged => System.Reactive.Linq.Observable.Never<PluginRefreshStatus>();

        public Task RefreshForGameAsync(PluginRefreshRequest request, CancellationToken ct = default) => Task.CompletedTask;

        public Task RefreshSelectedApproximationsAsync(
            PluginRefreshRequest request,
            IReadOnlyList<PluginRefreshTarget> selectedTargets,
            CancellationToken ct = default) => Task.CompletedTask;

        public void CancelActiveRefresh(PluginRefreshCancelReason reason)
        {
        }
    }

    private sealed class NoApproximationRefreshCapabilityPolicy : IPluginRefreshCapabilityPolicy
    {
        public static NoApproximationRefreshCapabilityPolicy Instance { get; } = new();

        public bool SupportsPluginLoading(GameType gameType) => gameType != GameType.Unknown;

        public bool SupportsIssueApproximation(GameType gameType) => false;

        public bool RequiresLoadOrderFile(GameType gameType) => false;
    }
}
