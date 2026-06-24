using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Models.Diagnostics;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using AutoQAC.Services.UI.Interactions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoQAC.ViewModels.MainWindow;

/// <summary>
/// Manages cleaning commands (start/stop/preview), validation errors,
/// status text during cleaning, and pre-clean validation.
/// </summary>
public sealed partial class CleaningCommandsViewModel(
    IStateService stateService,
    ICleaningOrchestrator orchestrator,
    IConfigurationService configService,
    IPluginLoadingService pluginLoadingService,
    IPluginRefreshCoordinator? pluginRefreshCoordinator,
    ILoggingService logger,
    IMessageDialogService messageDialog,
    IAppLifetime appLifetime,
    Interaction<Unit, Unit> showProgressInteraction,
    Interaction<List<DryRunResult>, Unit> showPreviewInteraction,
    Interaction<Unit, bool> showSettingsInteraction,
    Interaction<Unit, bool> showSkipListInteraction,
    Interaction<Unit, Unit> showRestoreInteraction,
    Interaction<Unit, Unit> showAboutInteraction)
    : ViewModelBase, IDisposable
{
    private readonly IPluginRefreshCoordinator _pluginRefreshCoordinator =
        pluginRefreshCoordinator ?? NoOpPluginRefreshCoordinator.Instance;

    [ObservableProperty] public partial string StatusText { get; set; } = "Ready";

    [ObservableProperty] public partial ObservableCollection<ValidationError> ValidationErrors { get; set; } = [];

    [ObservableProperty] public partial bool HasValidationErrors { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopCleaningCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowSkipListCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreBackupsCommand))]
    public partial bool IsCleaning { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCleaningCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    public partial bool CanStartCleaning { get; set; }

    /// <summary>
    /// Updates VM state from application state. Called by the parent VM when
    /// <c>IStateService.StateChanged</c> fires; the parent has already marshaled
    /// onto the UI thread via <c>IUiDispatcher</c>, so we just apply directly here.
    /// </summary>
    public void OnStateChanged(AppState state) => ApplyState(state);

    private void ApplyState(AppState state)
    {
        IsCleaning = state.IsCleaning;
        CanStartCleaning = state.PluginsToClean.Count > 0 &&
                           !string.IsNullOrWhiteSpace(state.XEditExecutablePath) &&
                           !state.IsCleaning;

        if (state.IsCleaning)
        {
            StatusText = $"Cleaning: {state.CurrentPlugin} ({state.Progress}/{state.TotalPlugins})";
        }
    }

    private bool CanStart() => CanStartCleaning;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartCleaningAsync()
    {
        ValidationErrors.Clear();
        HasValidationErrors = false;

        var errors = ValidatePreClean();
        if (errors.Count > 0)
        {
            foreach (var error in errors)
                ValidationErrors.Add(error);
            HasValidationErrors = true;
            return;
        }

        try
        {
            _pluginRefreshCoordinator.CancelActiveRefresh(PluginRefreshCancelReason.CleaningStarted);
            await showProgressInteraction.Handle(Unit.Default);

            StatusText = "Cleaning started...";
            await orchestrator.StartCleaningAsync(HandleTimeoutRetryAsync, HandleBackupFailureAsync);
            StatusText = "Cleaning completed.";
        }
        catch (InvalidOperationException ex)
        {
            logger.Error(ex, "Configuration validation failed before cleaning");
            var message = DiagnosticTextFormatter.OperationFailed("Configuration validation");
            ValidationErrors.Clear();
            ValidationErrors.Add(new ValidationError(
                "Configuration error",
                message,
                "Check your configuration in Edit > Settings."));
            HasValidationErrors = true;
            StatusText = "Configuration error";
        }
        catch (Exception ex)
        {
            logger.Error(ex, "StartCleaningAsync failed");
            var message = DiagnosticTextFormatter.OperationFailed("Cleaning");
            StatusText = message;
            await messageDialog.ShowErrorAsync(
                "Cleaning Failed",
                message,
                DiagnosticTextFormatter.LatestLogDetails);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task PreviewAsync()
    {
        ValidationErrors.Clear();
        HasValidationErrors = false;

        var errors = ValidatePreClean();
        if (errors.Count > 0)
        {
            foreach (var error in errors)
                ValidationErrors.Add(error);
            HasValidationErrors = true;
            return;
        }

        try
        {
            StatusText = "Running preview...";
            var results = await orchestrator.RunDryRunAsync();

            await showPreviewInteraction.Handle(results);

            StatusText = "Preview complete";
        }
        catch (InvalidOperationException ex)
        {
            logger.Error(ex, "Configuration validation failed before preview");
            var message = DiagnosticTextFormatter.OperationFailed("Configuration validation");
            ValidationErrors.Clear();
            ValidationErrors.Add(new ValidationError(
                "Configuration error",
                message,
                "Check your configuration in Edit > Settings."));
            HasValidationErrors = true;
            StatusText = "Configuration error";
        }
        catch (Exception ex)
        {
            logger.Error(ex, "RunPreviewAsync failed");
            var message = DiagnosticTextFormatter.OperationFailed("Preview");
            StatusText = message;
            await messageDialog.ShowErrorAsync(
                "Preview Failed",
                message,
                DiagnosticTextFormatter.LatestLogDetails);
        }
    }

    private bool CanStop() => IsCleaning;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopCleaningAsync()
    {
        try
        {
            StatusText = "Stopping...";
            var stopResult = await orchestrator.StopCleaningAsync();
            var terminationResult = stopResult.TerminationResult ?? orchestrator.LastTerminationResult;

            if (terminationResult == TerminationResult.GracePeriodExpired)
            {
                var choice = await messageDialog.ShowChoiceAsync(StopTerminationDialogContent.ConfirmationTitle,
                    StopTerminationDialogContent.ConfirmationMessage,
                    StopTerminationDialogContent.ForceTerminateButton,
                    StopTerminationDialogContent.LeaveRunningButton);

                if (choice == MessageDialogResult.Yes)
                {
                    var forceResult = await orchestrator.ForceStopCleaningAsync();
                    if (forceResult.TerminationResult == TerminationResult.ForceKillFailed)
                    {
                        await ShowStopFailureDialogSafelyAsync();
                    }
                }
                else
                {
                    orchestrator.MarkLeftRunningByUser();
                    StatusText = "Cleaning stopped; xEdit left running.";
                    await ShowLeftRunningWarningSafelyAsync();
                }
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "StopCleaningAsync failed");
            StatusText = StopTerminationDialogContent.ForceFailureTitle;
            await ShowStopFailureDialogSafelyAsync();
        }
    }

    /// <summary>
    /// Shows the left-running acknowledgement without converting dialog failures into force-termination failures.
    /// </summary>
    private async Task ShowLeftRunningWarningSafelyAsync()
    {
        try
        {
            await messageDialog.ShowWarningAsync(
                StopTerminationDialogContent.LeftRunningTitle,
                StopTerminationDialogContent.LeftRunningMessage);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to show left-running stop warning dialog");
        }
    }

    /// <summary>
    /// Shows the shared force-failure dialog without letting dialog-service failures escape the stop command.
    /// </summary>
    private async Task ShowStopFailureDialogSafelyAsync()
    {
        try
        {
            await messageDialog.ShowErrorAsync(
                StopTerminationDialogContent.ForceFailureTitle,
                StopTerminationDialogContent.ForceFailureMessage);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to show stop failure dialog");
        }
    }

    [RelayCommand]
    private void Exit()
    {
        appLifetime.Shutdown();
    }

    [RelayCommand]
    private async Task ShowAboutAsync()
    {
        try
        {
            await showAboutInteraction.Handle(Unit.Default);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to show about dialog");
            StatusText = "Error opening about dialog";
        }
    }

    [RelayCommand]
    private async Task ShowSettingsAsync()
    {
        try
        {
            var result = await showSettingsInteraction.Handle(Unit.Default);

            if (result)
            {
                var config = await configService.LoadUserConfigAsync();

                stateService.UpdateConfigurationPaths(
                    config.LoadOrder.File,
                    config.ModOrganizer.Binary,
                    config.XEdit.Binary);
                stateService.UpdateState(s => s with
                {
                    Mo2ModeEnabled = config.Settings.Mo2Mode,
                    CleaningTimeout = config.Settings.CleaningTimeout
                });

                StatusText = "Settings saved";
                logger.Information("Settings updated from settings dialog");
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to show or process settings dialog");
            StatusText = "Error opening settings";
        }
    }

    private bool CanShowSkipList() => !IsCleaning;

    [RelayCommand(CanExecute = nameof(CanShowSkipList))]
    private async Task ShowSkipListAsync()
    {
        try
        {
            var result = await showSkipListInteraction.Handle(Unit.Default);

            if (result)
            {
                StatusText = "Skip list saved";
                logger.Information("Skip list updated from skip list dialog");
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to show or process skip list dialog");
            StatusText = "Error opening skip list";
        }
    }

    private bool CanRestoreBackups() => !IsCleaning;

    [RelayCommand(CanExecute = nameof(CanRestoreBackups))]
    private async Task RestoreBackupsAsync()
    {
        try
        {
            await showRestoreInteraction.Handle(Unit.Default);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to show restore window");
            StatusText = "Error opening restore window";
        }
    }

    [RelayCommand]
    private void DismissValidation()
    {
        ValidationErrors.Clear();
        HasValidationErrors = false;
    }

    private List<ValidationError> ValidatePreClean()
    {
        var errors = new List<ValidationError>();
        var state = stateService.CurrentState;

        if (string.IsNullOrWhiteSpace(state.XEditExecutablePath))
        {
            var message = MissingXEditMessage(state.XEditExecutablePath);
            errors.Add(new ValidationError(
                "xEdit not configured",
                message,
                "Choose the correct xEdit executable in Settings."));
        }
        else if (!File.Exists(state.XEditExecutablePath))
        {
            var message = MissingXEditMessage(state.XEditExecutablePath);
            errors.Add(new ValidationError(
                "xEdit not found",
                message,
                "Choose the correct xEdit executable in Settings."));
        }

        var requiresLoadOrder = state.CurrentGameType != GameType.Unknown &&
                                !state.Mo2ModeEnabled &&
                                !pluginLoadingService.IsGameSupportedByMutagen(state.CurrentGameType);

        if (requiresLoadOrder)
        {
            if (string.IsNullOrWhiteSpace(state.LoadOrderPath))
            {
                var message = MissingLoadOrderMessage(state.LoadOrderPath);
                errors.Add(new ValidationError(
                    "Load order not configured",
                    message,
                    "Choose the current plugins.txt or loadorder.txt file."));
            }
            else if (!File.Exists(state.LoadOrderPath))
            {
                var message = MissingLoadOrderMessage(state.LoadOrderPath);
                errors.Add(new ValidationError(
                    "Load order not found",
                    message,
                    "Choose the current plugins.txt or loadorder.txt file."));
            }
        }

        if (state.Mo2ModeEnabled)
        {
            if (string.IsNullOrWhiteSpace(state.Mo2ExecutablePath))
            {
                var message = MissingMo2Message(state.Mo2ExecutablePath);
                errors.Add(new ValidationError(
                    "MO2 not configured",
                    message,
                    "Choose ModOrganizer.exe or disable MO2 Mode."));
            }
            else if (!File.Exists(state.Mo2ExecutablePath))
            {
                var message = MissingMo2Message(state.Mo2ExecutablePath);
                errors.Add(new ValidationError(
                    "MO2 not found",
                    message,
                    "Choose ModOrganizer.exe or disable MO2 Mode."));
            }

            if (string.IsNullOrWhiteSpace(state.Mo2Profile))
            {
                errors.Add(new ValidationError(
                    "MO2 profile not selected",
                    "MO2 mode is enabled but no MO2 profile is selected.",
                    "Select a game and MO2 profile before cleaning."));
            }
        }

        var hasPlugins = state.PluginsToClean.Count > 0;
        if (!hasPlugins)
        {
            errors.Add(new ValidationError(
                "No plugins loaded",
                "No plugins are available for cleaning.",
                "Select a game from the dropdown, or browse for a load order file."));
        }
        else
        {
            var excluded = state.ExcludedPluginPaths;
            var selectedCount = state.PluginsToClean
                .Count(p => !p.IsInSkipList && !excluded.Contains(p.FullPath));
            if (selectedCount == 0)
            {
                errors.Add(new ValidationError(
                    "No plugins selected",
                    "All plugins are either deselected or in the skip list.",
                    "Select at least one plugin to clean, or check your skip list settings."));
            }
        }

        return errors;
    }

    private static string MissingXEditMessage(string? path) =>
        $"{DiagnosticTextFormatter.SafeFileIdentifier("xEdit Path", path, "xEdit executable")} is missing. Choose the correct xEdit executable in Settings.";

    private static string MissingMo2Message(string? path) =>
        $"{DiagnosticTextFormatter.SafeFileIdentifier("MO2 Path", path, "ModOrganizer.exe")} is missing. Choose ModOrganizer.exe or disable MO2 Mode.";

    private static string MissingLoadOrderMessage(string? path) =>
        $"{DiagnosticTextFormatter.SafeFileIdentifier("Load Order File", path, "load order file")} is missing. Choose the current plugins.txt or loadorder.txt file.";

    private async Task<bool> HandleTimeoutRetryAsync(string pluginName, int timeoutSeconds, int attemptNumber)
    {
        var safePluginName = DiagnosticTextFormatter.SafePluginName(pluginName);
        var message = $"Cleaning of '{safePluginName}' timed out after {timeoutSeconds} seconds.\n\n" +
                      $"Attempt {attemptNumber} of 3 failed.\n\n" +
                      "Would you like to retry cleaning this plugin?";

        const string details = "Possible causes:\n" +
                               "- The plugin is very large\n" +
                               "- xEdit is processing slowly\n" +
                               "- The system is under heavy load\n\n" +
                               "You can increase the timeout in Edit > Settings if plugins regularly time out.";

        return await messageDialog.ShowRetryAsync("Plugin Timeout", message, details);
    }

    private async Task<BackupFailureChoice> HandleBackupFailureAsync(string pluginName, string errorMessage)
    {
        var safePluginName = DiagnosticTextFormatter.SafePluginName(pluginName);
        var safeErrorMessage = DiagnosticTextFormatter.SafeFailureSummary(
            errorMessage,
            DiagnosticTextFormatter.CleaningFailedForPlugin(pluginName));

        return await messageDialog.ShowBackupFailureDialogAsync(safePluginName, safeErrorMessage);
    }

    public void Dispose()
    {
    }

    private sealed class NoOpPluginRefreshCoordinator : IPluginRefreshCoordinator
    {
        public static NoOpPluginRefreshCoordinator Instance { get; } = new();

        public IObservable<PluginRefreshStatus> StatusChanged =>
            System.Reactive.Linq.Observable.Never<PluginRefreshStatus>();

        public Task RefreshForGameAsync(PluginRefreshRequest request, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task RefreshSelectedApproximationsAsync(
            PluginRefreshRequest request,
            IReadOnlyList<PluginRefreshTarget> selectedTargets,
            CancellationToken ct = default) => Task.CompletedTask;

        public void CancelActiveRefresh(PluginRefreshCancelReason reason)
        {
        }
    }
}
