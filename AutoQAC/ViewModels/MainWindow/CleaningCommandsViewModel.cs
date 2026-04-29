using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using AutoQAC.Services.UI.Interactions;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoQAC.ViewModels.MainWindow;

/// <summary>
/// Manages cleaning commands (start/stop/preview), validation errors,
/// status text during cleaning, and pre-clean validation.
/// </summary>
public sealed partial class CleaningCommandsViewModel : ViewModelBase, IDisposable
{
    private readonly IConfigurationService _configService;
    private readonly ILoggingService _logger;
    private readonly IMessageDialogService _messageDialog;
    private readonly ICleaningOrchestrator _orchestrator;
    private readonly IPluginLoadingService _pluginLoadingService;
    private readonly IStateService _stateService;
    private readonly IUiDispatcher _uiDispatcher;

    private readonly Interaction<Unit, Unit> _showProgressInteraction;
    private readonly Interaction<List<DryRunResult>, Unit> _showPreviewInteraction;
    private readonly Interaction<Unit, bool> _showSettingsInteraction;
    private readonly Interaction<Unit, bool> _showSkipListInteraction;
    private readonly Interaction<Unit, Unit> _showRestoreInteraction;
    private readonly Interaction<Unit, Unit> _showAboutInteraction;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private ObservableCollection<ValidationError> _validationErrors = new();

    [ObservableProperty]
    private bool _hasValidationErrors;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopCleaningCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowSkipListCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreBackupsCommand))]
    private bool _isCleaning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCleaningCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    private bool _canStartCleaning;

    public CleaningCommandsViewModel(
        IStateService stateService,
        ICleaningOrchestrator orchestrator,
        IConfigurationService configService,
        IPluginLoadingService pluginLoadingService,
        ILoggingService logger,
        IMessageDialogService messageDialog,
        IUiDispatcher uiDispatcher,
        Interaction<Unit, Unit> showProgressInteraction,
        Interaction<List<DryRunResult>, Unit> showPreviewInteraction,
        Interaction<Unit, bool> showSettingsInteraction,
        Interaction<Unit, bool> showSkipListInteraction,
        Interaction<Unit, Unit> showRestoreInteraction,
        Interaction<Unit, Unit> showAboutInteraction)
    {
        _stateService = stateService;
        _orchestrator = orchestrator;
        _configService = configService;
        _pluginLoadingService = pluginLoadingService;
        _logger = logger;
        _messageDialog = messageDialog;
        _uiDispatcher = uiDispatcher;
        _showProgressInteraction = showProgressInteraction;
        _showPreviewInteraction = showPreviewInteraction;
        _showSettingsInteraction = showSettingsInteraction;
        _showSkipListInteraction = showSkipListInteraction;
        _showRestoreInteraction = showRestoreInteraction;
        _showAboutInteraction = showAboutInteraction;
    }

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
                           !string.IsNullOrEmpty(state.XEditExecutablePath) &&
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
            _ = _showProgressInteraction.Handle(Unit.Default);

            StatusText = "Cleaning started...";
            await _orchestrator.StartCleaningAsync(HandleTimeoutRetryAsync, HandleBackupFailureAsync);
            StatusText = "Cleaning completed.";
        }
        catch (InvalidOperationException ex)
        {
            _logger.Error(ex, "Configuration validation failed before cleaning");
            ValidationErrors.Clear();
            ValidationErrors.Add(new ValidationError(
                "Configuration error",
                ex.Message,
                "Check your configuration in Edit > Settings."));
            HasValidationErrors = true;
            StatusText = "Configuration error";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            _logger.Error(ex, "StartCleaningAsync failed");
            await _messageDialog.ShowErrorAsync(
                "Cleaning Failed",
                "An error occurred during the cleaning process.",
                $"Error: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}");
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
            var results = await _orchestrator.RunDryRunAsync();

            _ = _showPreviewInteraction.Handle(results);

            StatusText = "Preview complete";
        }
        catch (InvalidOperationException ex)
        {
            _logger.Error(ex, "Configuration validation failed before preview");
            ValidationErrors.Clear();
            ValidationErrors.Add(new ValidationError(
                "Configuration error",
                ex.Message,
                "Check your configuration in Edit > Settings."));
            HasValidationErrors = true;
            StatusText = "Configuration error";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            _logger.Error(ex, "RunPreviewAsync failed");
            await _messageDialog.ShowErrorAsync(
                "Preview Failed",
                "An error occurred while running the preview.",
                $"Error: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}");
        }
    }

    private bool CanStop() => IsCleaning;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopCleaningAsync()
    {
        StatusText = "Stopping...";
        var stopResult = await _orchestrator.StopCleaningAsync();
        var terminationResult = stopResult.TerminationResult ?? _orchestrator.LastTerminationResult;

        if (terminationResult == TerminationResult.GracePeriodExpired)
        {
            // The existing dialog service exposes Yes/No buttons; Yes maps to Force Terminate and No maps to Leave Running.
            var confirmed = await _messageDialog.ShowConfirmAsync(
                "Force Terminate xEdit?",
                "xEdit did not exit after the stop request. Force terminating can interrupt any remaining file or log writes. Do you want AutoQAC to force terminate xEdit now?");

            if (confirmed)
            {
                var forceResult = await _orchestrator.ForceStopCleaningAsync();
                if (forceResult.TerminationResult == TerminationResult.ForceKillFailed)
                {
                    await _messageDialog.ShowErrorAsync(
                        "Could Not Force Terminate xEdit",
                        "AutoQAC could not force terminate xEdit. xEdit may still be running; close it manually or check the log for details before starting another cleaning session.");
                }
            }
            else
            {
                _orchestrator.MarkLeftRunningByUser();
                StatusText = "Cleaning stopped; xEdit left running.";
                await _messageDialog.ShowWarningAsync(
                    "Cleaning Stopped",
                    "AutoQAC stopped the cleaning session. xEdit was left running by your choice; close it manually when it is safe.");
            }
        }
    }

    [RelayCommand]
    private void Exit()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    [RelayCommand]
    private async Task ShowAboutAsync()
    {
        try
        {
            await _showAboutInteraction.Handle(Unit.Default);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to show about dialog");
            StatusText = "Error opening about dialog";
        }
    }

    [RelayCommand]
    private async Task ShowSettingsAsync()
    {
        try
        {
            var result = await _showSettingsInteraction.Handle(Unit.Default);

            if (result)
            {
                var config = await _configService.LoadUserConfigAsync();

                _stateService.UpdateConfigurationPaths(
                    config.LoadOrder.File,
                    config.ModOrganizer.Binary,
                    config.XEdit.Binary);
                _stateService.UpdateState(s => s with
                {
                    Mo2ModeEnabled = config.Settings.Mo2Mode,
                    CleaningTimeout = config.Settings.CleaningTimeout
                });

                StatusText = "Settings saved";
                _logger.Information("Settings updated from settings dialog");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to show or process settings dialog");
            StatusText = "Error opening settings";
        }
    }

    private bool CanShowSkipList() => !IsCleaning;

    [RelayCommand(CanExecute = nameof(CanShowSkipList))]
    private async Task ShowSkipListAsync()
    {
        try
        {
            var result = await _showSkipListInteraction.Handle(Unit.Default);

            if (result)
            {
                StatusText = "Skip list saved";
                _logger.Information("Skip list updated from skip list dialog");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to show or process skip list dialog");
            StatusText = "Error opening skip list";
        }
    }

    private bool CanRestoreBackups() => !IsCleaning;

    [RelayCommand(CanExecute = nameof(CanRestoreBackups))]
    private async Task RestoreBackupsAsync()
    {
        try
        {
            await _showRestoreInteraction.Handle(Unit.Default);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to show restore window");
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
        var state = _stateService.CurrentState;

        if (string.IsNullOrEmpty(state.XEditExecutablePath))
        {
            errors.Add(new ValidationError(
                "xEdit not configured",
                "xEdit executable path is not set.",
                "Go to Edit > Settings and set the xEdit Path to your xEdit executable (SSEEdit.exe, FO4Edit.exe, etc.)."));
        }
        else if (!File.Exists(state.XEditExecutablePath))
        {
            errors.Add(new ValidationError(
                "xEdit not found",
                $"xEdit not found at: {state.XEditExecutablePath}",
                "Go to Edit > Settings and update the xEdit Path to the correct location."));
        }

        var requiresLoadOrder = state.CurrentGameType != GameType.Unknown &&
                                !_pluginLoadingService.IsGameSupportedByMutagen(state.CurrentGameType);

        if (requiresLoadOrder)
        {
            if (string.IsNullOrWhiteSpace(state.LoadOrderPath))
            {
                errors.Add(new ValidationError(
                    "Load order not configured",
                    $"{state.CurrentGameType} requires a load order file (plugins.txt/loadorder.txt).",
                    "Set the load order path in the main window under Configuration > Load Order File."));
            }
            else if (!File.Exists(state.LoadOrderPath))
            {
                errors.Add(new ValidationError(
                    "Load order not found",
                    $"Load order file not found at: {state.LoadOrderPath}",
                    "Set a valid per-game load order path in the main window under Configuration > Load Order File."));
            }
        }

        var hasPlugins = state.PluginsToClean.Count > 0;
        if (!hasPlugins)
        {
            errors.Add(new ValidationError(
                "No plugins loaded",
                "No plugins are available for cleaning.",
                new StringBuilder().Append("Select a game from the dropdown, or browse for a load order file.")
                    .ToString()));
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

        if (!state.Mo2ModeEnabled) return errors;
        if (string.IsNullOrEmpty(state.Mo2ExecutablePath))
        {
            errors.Add(new ValidationError(
                "MO2 not configured",
                "MO2 mode is enabled but no MO2 executable path is set.",
                "Go to Edit > Settings and set the MO2 Path, or disable MO2 mode if not using Mod Organizer 2."));
        }
        else if (!File.Exists(state.Mo2ExecutablePath))
        {
            errors.Add(new ValidationError(
                "MO2 not found",
                $"MO2 executable not found at: {state.Mo2ExecutablePath}",
                "Check the MO2 executable path in Edit > Settings, or disable MO2 mode."));
        }

        return errors;
    }

    private async Task<bool> HandleTimeoutRetryAsync(string pluginName, int timeoutSeconds, int attemptNumber)
    {
        var message = $"Cleaning of '{pluginName}' timed out after {timeoutSeconds} seconds.\n\n" +
                      $"Attempt {attemptNumber} of 3 failed.\n\n" +
                      "Would you like to retry cleaning this plugin?";

        const string details = "Possible causes:\n" +
                               "- The plugin is very large\n" +
                               "- xEdit is processing slowly\n" +
                               "- The system is under heavy load\n\n" +
                               "You can increase the timeout in Edit > Settings if plugins regularly time out.";

        return await _messageDialog.ShowRetryAsync("Plugin Timeout", message, details);
    }

    private async Task<BackupFailureChoice> HandleBackupFailureAsync(string pluginName, string errorMessage) =>
        await _messageDialog.ShowBackupFailureDialogAsync(pluginName, errorMessage);

    public void Dispose()
    {
    }
}
