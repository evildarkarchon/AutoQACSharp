using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameCapability;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using AutoQAC.Services.UI.Interactions;
using AutoQAC.ViewModels.MainWindow;

namespace AutoQAC.ViewModels;

/// <summary>
/// Slim orchestrator that composes Configuration, PluginList, and CleaningCommands
/// sub-ViewModels. Owns Interactions (registered in MainWindow.xaml.cs code-behind)
/// and mediates cross-VM state changes.
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly IDisposable _stateSubscription;
    private readonly IDisposable _pluginRefreshSnapshotSubscription;

    public ConfigurationViewModel Configuration { get; }
    public PluginListViewModel PluginList { get; }
    public CleaningCommandsViewModel Commands { get; }

    public Interaction<ICleaningSession, Unit> ShowProgressInteraction { get; } = new();
    public Interaction<List<DryRunResult>, Unit> ShowPreviewInteraction { get; } = new();
    public Interaction<CleaningSessionResult, Unit> ShowCleaningResultsInteraction { get; } = new();
    public Interaction<Unit, bool> ShowSettingsInteraction { get; } = new();
    public Interaction<Unit, bool> ShowSkipListInteraction { get; } = new();
    public Interaction<Unit, Unit> ShowRestoreInteraction { get; } = new();
    public Interaction<Unit, Unit> ShowAboutInteraction { get; } = new();

    public MainWindowViewModel(
        IConfigurationService configService,
        IStateService stateService,
        ICleaningSession cleaningSession,
        ILoggingService logger,
        IFileDialogService fileDialog,
        IMessageDialogService messageDialog,
        IPluginValidationService pluginService,
        IPluginLoadingService pluginLoadingService,
        IUiDispatcher uiDispatcher,
        IPluginRefreshModule pluginRefreshModule,
        IGameCapabilityProvider gameCapabilityProvider,
        IPluginRefreshDiscoveryPlanner? discoveryPlanner = null,
        IAppLifetime? appLifetime = null)
    {
        Configuration = new ConfigurationViewModel(
            configService, stateService, logger, fileDialog,
            messageDialog, pluginService, pluginLoadingService,
            pluginRefreshModule,
            discoveryPlanner ?? new CapabilityOnlyDiscoveryPlanner(gameCapabilityProvider));

        PluginList = new PluginListViewModel(pluginRefreshModule);

        Commands = new CleaningCommandsViewModel(
            stateService, cleaningSession, configService, gameCapabilityProvider,
            pluginRefreshModule,
            logger, messageDialog, appLifetime ?? NoOpAppLifetime.Instance,
            ShowProgressInteraction, ShowPreviewInteraction,
            ShowSettingsInteraction, ShowSkipListInteraction,
            ShowRestoreInteraction, ShowAboutInteraction);

        _pluginRefreshSnapshotSubscription = pluginRefreshModule.Snapshots.Subscribe(
            new CallbackObserver<PluginRefreshSnapshot>(snapshot =>
                uiDispatcher.Post(() => OnPluginRefreshSnapshot(snapshot))));

        _stateSubscription = stateService.StateChanged.Subscribe(
            new CallbackObserver<AppState>(state => uiDispatcher.Post(() => OnStateChanged(state))));

        OnStateChanged(stateService.CurrentState);

        _ = Configuration.InitializeAsync();
    }

    private void OnStateChanged(AppState state)
    {
        Configuration.OnStateChanged(state);
        Commands.OnStateChanged(state);
    }

    private void OnPluginRefreshSnapshot(PluginRefreshSnapshot snapshot)
    {
        Configuration.OnPluginRefreshSnapshot(snapshot);
        PluginList.OnPluginRefreshSnapshot(snapshot);
    }

    /// <summary>
    /// Shows a non-modal migration warning banner in the main window.
    /// Delegates to ConfigurationViewModel.
    /// </summary>
    public void ShowMigrationWarning(string message) => Configuration.ShowMigrationWarning(message);

    public void Dispose()
    {
        _pluginRefreshSnapshotSubscription.Dispose();
        _stateSubscription.Dispose();
        Configuration.Dispose();
        PluginList.Dispose();
        Commands.Dispose();
    }

    private sealed class NoOpAppLifetime : IAppLifetime
    {
        public static NoOpAppLifetime Instance { get; } = new();

        public void Shutdown()
        {
        }
    }

    private sealed class CapabilityOnlyDiscoveryPlanner(IGameCapabilityProvider gameCapabilityProvider)
        : IPluginRefreshDiscoveryPlanner
    {
        public IReadOnlyList<GameType> GetAvailableGames() => gameCapabilityProvider.GetAvailableGames();

        public PluginRefreshGameAffordance GetAffordance(GameType gameType, bool mo2ModeEnabled)
        {
            var capability = gameCapabilityProvider.Get(gameType);
            return new PluginRefreshGameAffordance(
                gameType,
                capability.SupportsAutomaticPluginDiscovery,
                gameType != GameType.Unknown && !mo2ModeEnabled && capability.RequiresLoadOrderFile,
                capability.SupportsIssueApproximation);
        }

        public Task<PluginRefreshDiscoveryPlanResult> CreatePlanAsync(
            PluginRefreshDiscoveryPlanRequest request,
            CancellationToken ct = default) =>
            throw new InvalidOperationException("Capability-only planner cannot create Plugin refresh discovery plans.");

        public Task<PluginRefreshDiscoveredPlugins> LoadPluginsAsync(
            PluginRefreshDiscoveryPlan plan,
            CancellationToken ct = default) =>
            throw new InvalidOperationException("Capability-only planner cannot load Plugin refresh rows.");
    }

}
