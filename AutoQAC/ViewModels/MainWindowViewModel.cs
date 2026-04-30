using System;
using System.Collections.Generic;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using AutoQAC.Services.UI.Interactions;
using AutoQAC.ViewModels.MainWindow;

namespace AutoQAC.ViewModels;

/// <summary>
/// Slim orchestrator that composes Configuration, PluginList, and CleaningCommands
/// sub-ViewModels. Owns Interactions (registered in MainWindow.axaml.cs code-behind)
/// and mediates cross-VM state changes.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IDisposable _stateSubscription;

    public ConfigurationViewModel Configuration { get; }
    public PluginListViewModel PluginList { get; }
    public CleaningCommandsViewModel Commands { get; }

    public Interaction<Unit, Unit> ShowProgressInteraction { get; } = new();
    public Interaction<List<DryRunResult>, Unit> ShowPreviewInteraction { get; } = new();
    public Interaction<CleaningSessionResult, Unit> ShowCleaningResultsInteraction { get; } = new();
    public Interaction<Unit, bool> ShowSettingsInteraction { get; } = new();
    public Interaction<Unit, bool> ShowSkipListInteraction { get; } = new();
    public Interaction<Unit, Unit> ShowRestoreInteraction { get; } = new();
    public Interaction<Unit, Unit> ShowAboutInteraction { get; } = new();

    public MainWindowViewModel(
        IConfigurationService configService,
        IStateService stateService,
        ICleaningOrchestrator orchestrator,
        ILoggingService logger,
        IFileDialogService fileDialog,
        IMessageDialogService messageDialog,
        IPluginValidationService pluginService,
        IPluginLoadingService pluginLoadingService,
        IUiDispatcher uiDispatcher,
        IPluginIssueApproximationService? pluginIssueApproximationService = null,
        IPluginRefreshCoordinator? pluginRefreshCoordinator = null,
        IPluginRefreshCapabilityPolicy? pluginRefreshCapabilityPolicy = null)
    {
        _uiDispatcher = uiDispatcher;

        Configuration = new ConfigurationViewModel(
            configService, stateService, logger, fileDialog,
            messageDialog, pluginService, pluginLoadingService, pluginIssueApproximationService, pluginRefreshCoordinator);

        PluginList = new PluginListViewModel(stateService, pluginRefreshCoordinator, pluginRefreshCapabilityPolicy);

        Commands = new CleaningCommandsViewModel(
            stateService, orchestrator, configService, pluginLoadingService,
            pluginRefreshCoordinator,
            logger, messageDialog, uiDispatcher,
            ShowProgressInteraction, ShowPreviewInteraction,
            ShowSettingsInteraction, ShowSkipListInteraction,
            ShowRestoreInteraction, ShowAboutInteraction);

        _stateSubscription = stateService.StateChanged.Subscribe(
            new CallbackObserver<AppState>(state => _uiDispatcher.Post(() => OnStateChanged(state))));

        OnStateChanged(stateService.CurrentState);

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
    public void ShowMigrationWarning(string message) => Configuration.ShowMigrationWarning(message);

    public void Dispose()
    {
        _stateSubscription.Dispose();
        Configuration.Dispose();
        PluginList.Dispose();
        Commands.Dispose();
    }

}
