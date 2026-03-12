using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using AutoQAC.Models;
using AutoQAC.Services.State;
using ReactiveUI;

namespace AutoQAC.ViewModels.MainWindow;

/// <summary>
/// Manages the plugin collection, select/deselect all commands, and
/// skip list subscription for the main window plugin list.
/// </summary>
public sealed class PluginListViewModel : ViewModelBase, IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    public ObservableCollection<PluginInfo> PluginsToClean
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = new();

    public PluginInfo? SelectedPlugin
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    // Commands
    public ReactiveCommand<Unit, Unit> SelectAllCommand { get; }
    public ReactiveCommand<Unit, Unit> DeselectAllCommand { get; }

    public PluginListViewModel(IStateService stateService)
    {
        var stateChanged = stateService.StateChanged
            .ObserveOn(RxApp.MainThreadScheduler);

        // Define observables for command enablement
        var hasPlugins = stateChanged
            .Select(s => s.PluginsToClean.Count > 0);

        var isCleaning = stateChanged
            .Select(s => s.IsCleaning);

        // Plugin selection commands - disabled during cleaning, enabled when plugins exist
        var canSelectPlugins = hasPlugins.CombineLatest(
            isCleaning,
            (hasP, cleaning) => hasP && !cleaning);
        SelectAllCommand = ReactiveCommand.Create(SelectAllPlugins, canSelectPlugins);
        DeselectAllCommand = ReactiveCommand.Create(DeselectAllPlugins, canSelectPlugins);
        _disposables.Add(SelectAllCommand);
        _disposables.Add(DeselectAllCommand);
    }

    /// <summary>
    /// Updates the plugin list from application state. Called by parent on state changes.
    /// Filters out skipped plugins from display.
    /// </summary>
    public void OnStateChanged(AppState state)
    {
        var displayPlugins = state.PluginsToClean.Where(p => !p.IsInSkipList).ToList();

        var index = 0;
        while (index < displayPlugins.Count)
        {
            var nextPlugin = displayPlugins[index];

            if (index >= PluginsToClean.Count)
            {
                PluginsToClean.Add(nextPlugin);
                index++;
                continue;
            }

            if (PluginsToClean[index] == nextPlugin)
            {
                index++;
                continue;
            }

            PluginsToClean[index] = nextPlugin;
            if (SelectedPlugin is not null && IsSamePlugin(SelectedPlugin, nextPlugin))
            {
                SelectedPlugin = nextPlugin;
            }

            index++;
        }

        while (PluginsToClean.Count > displayPlugins.Count)
        {
            var removedPlugin = PluginsToClean[^1];
            if (SelectedPlugin is not null && IsSamePlugin(SelectedPlugin, removedPlugin))
            {
                SelectedPlugin = null;
            }

            PluginsToClean.RemoveAt(PluginsToClean.Count - 1);
        }
    }

    private static bool IsSamePlugin(PluginInfo left, PluginInfo right)
    {
        return string.Equals(left.FullPath, right.FullPath, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(left.FileName, right.FileName, StringComparison.OrdinalIgnoreCase);
    }

    private void SelectAllPlugins()
    {
        foreach (var plugin in PluginsToClean)
        {
            plugin.IsSelected = true;
        }
    }

    private void DeselectAllPlugins()
    {
        foreach (var plugin in PluginsToClean)
        {
            plugin.IsSelected = false;
        }
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
