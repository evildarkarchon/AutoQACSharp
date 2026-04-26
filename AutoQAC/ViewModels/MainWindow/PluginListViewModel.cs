using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AutoQAC.Models;
using AutoQAC.Services.State;
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

    public ObservableCollection<PluginListItem> PluginsToClean { get; } = new();

    [ObservableProperty]
    private PluginListItem? _selectedPlugin;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SelectAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeselectAllCommand))]
    private bool _hasPlugins;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SelectAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeselectAllCommand))]
    private bool _isCleaning;

    public PluginListViewModel(IStateService stateService)
    {
        _stateService = stateService;
        // No subscription here — the parent VM dispatches OnStateChanged on the UI thread.
        // Initial pull from current state so commands reflect reality before first change event.
        var initial = stateService.CurrentState;
        HasPlugins = initial.PluginsToClean.Count > 0;
        IsCleaning = initial.IsCleaning;
    }

    private bool CanSelectPlugins() => HasPlugins && !IsCleaning;

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
    /// Updates the plugin list and gating flags from application state. Called by parent
    /// on state changes (already on the UI thread).
    /// </summary>
    public void OnStateChanged(AppState state)
    {
        IsCleaning = state.IsCleaning;
        HasPlugins = state.PluginsToClean.Count > 0;

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
    }

    private static bool IsSamePlugin(PluginInfo left, PluginInfo right) =>
        string.Equals(left.FullPath, right.FullPath, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(left.FileName, right.FileName, StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        foreach (var item in PluginsToClean)
        {
            DetachItem(item);
        }
    }
}
