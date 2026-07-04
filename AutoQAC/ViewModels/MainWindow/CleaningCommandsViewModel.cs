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
using AutoQAC.Services.GameCapability;
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
    ICleaningSession cleaningSession,
    IConfigurationService configService,
    IGameCapabilityProvider gameCapabilityProvider,
    IPluginRefreshModule pluginRefreshModule,
    ILoggingService logger,
    IMessageDialogService messageDialog,
    IAppLifetime appLifetime,
    Interaction<ICleaningSession, Unit> showProgressInteraction,
    Interaction<List<DryRunResult>, Unit> showPreviewInteraction,
    Interaction<Unit, bool> showSettingsInteraction,
    Interaction<Unit, bool> showSkipListInteraction,
    Interaction<Unit, Unit> showRestoreInteraction,
    Interaction<Unit, Unit> showAboutInteraction)
    : ViewModelBase, IDisposable
{
    private readonly IPluginRefreshModule _pluginRefreshModule = pluginRefreshModule;

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
            {
                ValidationErrors.Add(error);
            }

            HasValidationErrors = true;
            return;
        }

        try
        {
            await _pluginRefreshModule.ExecuteAsync(
                new PluginRefreshIntent.Cancel(PluginRefreshCancelReason.CleaningStarted));
            await showProgressInteraction.Handle(cleaningSession);

            StatusText = "Cleaning started...";
            await cleaningSession.StartAsync();
            StatusText = "Cleaning completed.";
        }
        catch (CleaningPreflightException ex)
        {
            logger.Error(ex, "Cleaning preflight failed before cleaning");
            ProjectPreflightFailure(ex.Failure);
        }
        catch (ConfigPersistenceFailureException ex)
        {
            logger.Error(ex, "Configuration persistence failed before cleaning");
            ProjectPreflightFailure(ToPreflightFailure(ex));
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
            logger.Error(ex, "StartAsync failed");
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
            {
                ValidationErrors.Add(error);
            }

            HasValidationErrors = true;
            return;
        }

        try
        {
            StatusText = "Running preview...";
            var results = await cleaningSession.PreviewAsync();

            await showPreviewInteraction.Handle(results.ToList());

            StatusText = "Preview complete";
        }
        catch (CleaningPreflightException ex)
        {
            logger.Error(ex, "Cleaning preflight failed before preview");
            ProjectPreflightFailure(ex.Failure);
        }
        catch (ConfigPersistenceFailureException ex)
        {
            logger.Error(ex, "Configuration persistence failed before preview");
            ProjectPreflightFailure(ToPreflightFailure(ex));
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
            var result = await cleaningSession.ControlAsync(CleaningSessionControl.RequestStop);
            await ProjectStopControlResultAsync(result);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "StopCleaningAsync failed");
            StatusText = StopTerminationDialogContent.ForceFailureTitle;
            await ShowStopFailureDialogSafelyAsync();
        }
    }

    /// <summary>
    /// Projects the session-owned stop decision outcome without reimplementing stop escalation policy.
    /// </summary>
    private async Task ProjectStopControlResultAsync(CleaningSessionControlResult result)
    {
        switch (result.Status)
        {
            case CleaningSessionControlStatus.LeftRunningByUser:
                StatusText = "Cleaning stopped; xEdit left running.";
                await ShowLeftRunningWarningSafelyAsync();
                break;
            case CleaningSessionControlStatus.ForceKillFailed:
                StatusText = StopTerminationDialogContent.ForceFailureTitle;
                await ShowStopFailureDialogSafelyAsync();
                break;
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
            errors.Add(new ValidationError(
                "xEdit not configured",
                MissingXEditMessage(state.XEditExecutablePath),
                "Choose the correct xEdit executable in Settings."));
        }
        else if (!File.Exists(state.XEditExecutablePath))
        {
            errors.Add(new ValidationError(
                "xEdit not found",
                MissingXEditMessage(state.XEditExecutablePath),
                "Choose the correct xEdit executable in Settings."));
        }

        var requiresLoadOrder = state.CurrentGameType != GameType.Unknown &&
                                !state.Mo2ModeEnabled &&
                                gameCapabilityProvider.Get(state.CurrentGameType).RequiresLoadOrderFile;
        if (requiresLoadOrder)
        {
            if (string.IsNullOrWhiteSpace(state.LoadOrderPath))
            {
                errors.Add(new ValidationError(
                    "Load order not configured",
                    MissingLoadOrderMessage(state.LoadOrderPath),
                    "Choose the current plugins.txt or loadorder.txt file."));
            }
            else if (!File.Exists(state.LoadOrderPath))
            {
                errors.Add(new ValidationError(
                    "Load order not found",
                    MissingLoadOrderMessage(state.LoadOrderPath),
                    "Choose the current plugins.txt or loadorder.txt file."));
            }
        }

        if (state.Mo2ModeEnabled)
        {
            if (string.IsNullOrWhiteSpace(state.Mo2ExecutablePath))
            {
                errors.Add(new ValidationError(
                    "MO2 not configured",
                    MissingMo2Message(state.Mo2ExecutablePath),
                    "Choose ModOrganizer.exe or disable MO2 Mode."));
            }
            else if (!File.Exists(state.Mo2ExecutablePath))
            {
                errors.Add(new ValidationError(
                    "MO2 not found",
                    MissingMo2Message(state.Mo2ExecutablePath),
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

        if (state.PluginsToClean.Count == 0)
        {
            errors.Add(new ValidationError(
                "No plugins loaded",
                "No plugins are available for cleaning.",
                "Select a game from the dropdown, or browse for a load order file."));
        }
        else
        {
            var selectedCount = state.PluginsToClean
                .Count(plugin => !plugin.IsInSkipList && !state.ExcludedPluginPaths.Contains(plugin.FullPath));
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

    private static CleaningPreflightFailure ToPreflightFailure(ConfigPersistenceFailureException ex) =>
        new(
            CleaningPreflightFailureKind.ConfigPersistenceFailed,
            ex.Failure.SafeSummary);

    private void ProjectPreflightFailure(CleaningPreflightFailure failure)
    {
        ValidationErrors.Clear();
        ValidationErrors.Add(ToValidationError(failure));
        HasValidationErrors = true;
        StatusText = "Configuration error";
    }

    private static ValidationError ToValidationError(CleaningPreflightFailure failure)
    {
        var (title, action) = failure.Kind switch
        {
            CleaningPreflightFailureKind.MissingPluginRefreshPublication =>
                ("Plugins not refreshed", "Select a game and refresh plugins before cleaning."),
            CleaningPreflightFailureKind.StalePluginRefreshPublication =>
                ("Plugins need refresh", "Refresh plugins after changing game, load order, MO2, or skip list settings."),
            CleaningPreflightFailureKind.NoGameSelected =>
                ("No game selected", "Select a game before cleaning."),
            CleaningPreflightFailureKind.XEditNotConfigured =>
                ("xEdit not configured", "Choose the correct xEdit executable in Settings."),
            CleaningPreflightFailureKind.XEditNotFound =>
                ("xEdit not found", "Choose the correct xEdit executable in Settings."),
            CleaningPreflightFailureKind.LoadOrderNotConfigured =>
                ("Load order not configured", "Choose the current plugins.txt or loadorder.txt file."),
            CleaningPreflightFailureKind.LoadOrderNotFound =>
                ("Load order not found", "Choose the current plugins.txt or loadorder.txt file."),
            CleaningPreflightFailureKind.Mo2NotConfigured =>
                ("MO2 not configured", "Choose ModOrganizer.exe or disable MO2 Mode."),
            CleaningPreflightFailureKind.Mo2NotFound =>
                ("MO2 not found", "Choose ModOrganizer.exe or disable MO2 Mode."),
            CleaningPreflightFailureKind.Mo2InstanceMissing =>
                ("MO2 instance not found", "Choose the MO2 instance folder or disable MO2 Mode."),
            CleaningPreflightFailureKind.Mo2ProfileMissing =>
                ("MO2 profile not selected", "Select a game and MO2 profile before cleaning."),
            CleaningPreflightFailureKind.Mo2ProfileLoadOrderMissing =>
                ("MO2 profile load order missing", "Select a profile with a valid loadorder.txt before cleaning."),
            CleaningPreflightFailureKind.NoPluginsLoaded =>
                ("No plugins loaded", "Refresh plugins for the selected game."),
            CleaningPreflightFailureKind.NoPluginsSelected =>
                ("No plugins selected", "Select at least one plugin to clean, or check Skip list settings."),
            CleaningPreflightFailureKind.ConfigPersistenceFailed =>
                ("Configuration save failed", "Check the latest configuration save error and try again."),
            _ => ("Configuration error", "Check your configuration in Edit > Settings.")
        };

        return new ValidationError(
            title,
            failure.SafeMessage,
            failure.ActionHint ?? action);
    }

    public void Dispose()
    {
    }
}
