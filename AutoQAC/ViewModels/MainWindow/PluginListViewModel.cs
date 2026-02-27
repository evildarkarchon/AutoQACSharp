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
        // Define observables for command enablement
        var hasPlugins = stateService.StateChanged
            .Select(s => s.PluginsToClean.Count > 0);

        var isCleaning = stateService.StateChanged
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
        // Plugins list update - filter out skipped plugins from display
        var displayPlugins = state.PluginsToClean.Where(p => !p.IsInSkipList).ToList();
        if (PluginsToClean.Count != displayPlugins.Count ||
            !PluginsToClean.SequenceEqual(displayPlugins))
        {
            PluginsToClean.Clear();
            foreach (var p in displayPlugins)
            {
                PluginsToClean.Add(p);
            }
        }
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
