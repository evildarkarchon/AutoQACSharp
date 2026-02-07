using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using AutoQAC.ViewModels.MainWindow;
using ReactiveUI;

namespace AutoQAC.ViewModels;

/// <summary>
/// Slim orchestrator that composes Configuration, PluginList, and CleaningCommands
/// sub-ViewModels. Owns Interactions (registered in MainWindow.axaml.cs code-behind)
/// and mediates cross-VM state changes.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    // Sub-ViewModels
    public ConfigurationViewModel Configuration { get; }
    public PluginListViewModel PluginList { get; }
    public CleaningCommandsViewModel Commands { get; }

    // Interactions -- registered in MainWindow.axaml.cs
    public Interaction<Unit, Unit> ShowProgressInteraction { get; } = new();
    public Interaction<List<DryRunResult>, Unit> ShowPreviewInteraction { get; } = new();
    public Interaction<CleaningSessionResult, Unit> ShowCleaningResultsInteraction { get; } = new();
    public Interaction<Unit, bool> ShowSettingsInteraction { get; } = new();
    public Interaction<Unit, bool> ShowSkipListInteraction { get; } = new();
    public Interaction<Unit, Unit> ShowRestoreInteraction { get; } = new();

    public MainWindowViewModel(
        IConfigurationService configService,
        IStateService stateService,
        ICleaningOrchestrator orchestrator,
        ILoggingService logger,
        IFileDialogService fileDialog,
        IMessageDialogService messageDialog,
        IPluginValidationService pluginService,
        IPluginLoadingService pluginLoadingService)
    {
        // Create sub-ViewModels
        Configuration = new ConfigurationViewModel(
            configService, stateService, logger, fileDialog,
            messageDialog, pluginService, pluginLoadingService);

        PluginList = new PluginListViewModel(stateService);

        Commands = new CleaningCommandsViewModel(
            stateService, orchestrator, configService, logger,
            messageDialog,
            ShowProgressInteraction, ShowPreviewInteraction,
            ShowSettingsInteraction, ShowSkipListInteraction,
            ShowRestoreInteraction);

        // Subscribe to state changes and dispatch to sub-VMs
        var stateSubscription = stateService.StateChanged
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(OnStateChanged);
        _disposables.Add(stateSubscription);

        // Initialize from current state
        OnStateChanged(stateService.CurrentState);

        // Kick off async initialization
        _ = Configuration.InitializeAsync();
    }

    private void OnStateChanged(AppState state)
    {
        Configuration.OnStateChanged(state);
        PluginList.OnStateChanged(state);
        Commands.OnStateChanged(state);
    }

    /// <summary>
    /// Shows a non-modal migration warning banner in the main window.
    /// Delegates to ConfigurationViewModel.
    /// </summary>
    public void ShowMigrationWarning(string message)
    {
        Configuration.ShowMigrationWarning(message);
    }

    public void Dispose()
    {
        _disposables.Dispose();
        Configuration.Dispose();
        PluginList.Dispose();
        Commands.Dispose();
    }
}
