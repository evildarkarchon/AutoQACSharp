using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using Avalonia.Controls.ApplicationLifetimes;
using ReactiveUI;

namespace AutoQAC.ViewModels.MainWindow;

/// <summary>
/// Manages cleaning commands (start/stop/preview), validation errors,
/// status text during cleaning, and pre-clean validation.
/// </summary>
public sealed class CleaningCommandsViewModel : ViewModelBase, IDisposable
{
    private readonly IStateService _stateService;
    private readonly ICleaningOrchestrator _orchestrator;
    private readonly IConfigurationService _configService;
    private readonly ILoggingService _logger;
    private readonly IMessageDialogService _messageDialog;
    private readonly CompositeDisposable _disposables = new();

    // Interaction references passed from parent
    private readonly Interaction<Unit, Unit> _showProgressInteraction;
    private readonly Interaction<List<DryRunResult>, Unit> _showPreviewInteraction;
    private readonly Interaction<Unit, bool> _showSettingsInteraction;
    private readonly Interaction<Unit, bool> _showSkipListInteraction;
    private readonly Interaction<Unit, Unit> _showRestoreInteraction;

    private string _statusText = "Ready";

    public string StatusText
    {
        get => _statusText;
        set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    private ObservableCollection<ValidationError> _validationErrors = new();

    public ObservableCollection<ValidationError> ValidationErrors
    {
        get => _validationErrors;
        set => this.RaiseAndSetIfChanged(ref _validationErrors, value);
    }

    private bool _hasValidationErrors;

    public bool HasValidationErrors
    {
        get => _hasValidationErrors;
        set => this.RaiseAndSetIfChanged(ref _hasValidationErrors, value);
    }

    private readonly ObservableAsPropertyHelper<bool> _isCleaning;
    public bool IsCleaning => _isCleaning.Value;

    private readonly ObservableAsPropertyHelper<bool> _canStartCleaning;
    public bool CanStartCleaning => _canStartCleaning.Value;

    // Commands
    public ReactiveCommand<Unit, Unit> StartCleaningCommand { get; }
    public ReactiveCommand<Unit, Unit> StopCleaningCommand { get; }
    public ReactiveCommand<Unit, Unit> PreviewCommand { get; }
    public ReactiveCommand<Unit, Unit> ExitCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowAboutCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowSettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowSkipListCommand { get; }
    public ReactiveCommand<Unit, Unit> RestoreBackupsCommand { get; }
    public ReactiveCommand<Unit, Unit> DismissValidationCommand { get; }

    public CleaningCommandsViewModel(
        IStateService stateService,
        ICleaningOrchestrator orchestrator,
        IConfigurationService configService,
        ILoggingService logger,
        IMessageDialogService messageDialog,
        Interaction<Unit, Unit> showProgressInteraction,
        Interaction<List<DryRunResult>, Unit> showPreviewInteraction,
        Interaction<Unit, bool> showSettingsInteraction,
        Interaction<Unit, bool> showSkipListInteraction,
        Interaction<Unit, Unit> showRestoreInteraction)
    {
        _stateService = stateService;
        _orchestrator = orchestrator;
        _configService = configService;
        _logger = logger;
        _messageDialog = messageDialog;
        _showProgressInteraction = showProgressInteraction;
        _showPreviewInteraction = showPreviewInteraction;
        _showSettingsInteraction = showSettingsInteraction;
        _showSkipListInteraction = showSkipListInteraction;
        _showRestoreInteraction = showRestoreInteraction;

        // IsCleaning OAPH from state
        _isCleaning = _stateService.StateChanged
            .Select(s => s.IsCleaning)
            .ToProperty(this, x => x.IsCleaning);
        _disposables.Add(_isCleaning);

        // Define canStart observable from state only (no cross-VM dependencies)
        var hasPlugins = _stateService.StateChanged
            .Select(s => s.PluginsToClean.Count > 0);

        var hasXEditPath = _stateService.StateChanged
            .Select(s => !string.IsNullOrEmpty(s.XEditExecutablePath));

        var canStart = hasPlugins.CombineLatest(hasXEditPath,
            this.WhenAnyValue(x => x.IsCleaning),
            (hasP, hasXEdit, isCleaning) =>
                hasP &&
                hasXEdit &&
                !isCleaning);

        _canStartCleaning = canStart
            .ToProperty(this, x => x.CanStartCleaning);
        _disposables.Add(_canStartCleaning);

        StartCleaningCommand = ReactiveCommand.CreateFromTask(
            StartCleaningAsync,
            canStart);

        PreviewCommand = ReactiveCommand.CreateFromTask(
            RunPreviewAsync,
            canStart);

        StopCleaningCommand = ReactiveCommand.CreateFromTask(
            HandleStopAsync,
            this.WhenAnyValue(x => x.IsCleaning));

        ExitCommand = ReactiveCommand.Create(Exit);
        ShowAboutCommand = ReactiveCommand.Create(ShowAbout);
        ShowSettingsCommand = ReactiveCommand.CreateFromTask(ShowSettingsAsync);

        // Skip list command - disabled during cleaning
        var canShowSkipList = this.WhenAnyValue(x => x.IsCleaning)
            .Select(cleaning => !cleaning);
        ShowSkipListCommand = ReactiveCommand.CreateFromTask(ShowSkipListAsync, canShowSkipList);

        // Restore backups command - disabled during cleaning
        var canShowRestore = this.WhenAnyValue(x => x.IsCleaning)
            .Select(cleaning => !cleaning);
        RestoreBackupsCommand = ReactiveCommand.CreateFromTask(ShowRestoreAsync, canShowRestore);

        DismissValidationCommand = ReactiveCommand.Create(() =>
        {
            ValidationErrors.Clear();
            HasValidationErrors = false;
        });
    }

    /// <summary>
    /// Updates the status text during cleaning state changes. Called by parent on state changes.
    /// </summary>
    public void OnStateChanged(AppState state)
    {
        if (state.IsCleaning)
        {
            StatusText = $"Cleaning: {state.CurrentPlugin} ({state.Progress}/{state.TotalPlugins})";
        }
    }

    private async Task StartCleaningAsync()
    {
        // Clear previous validation errors
        ValidationErrors.Clear();
        HasValidationErrors = false;

        // Run pre-clean validation
        var errors = ValidatePreClean();
        if (errors.Count > 0)
        {
            foreach (var error in errors)
                ValidationErrors.Add(error);
            HasValidationErrors = true;
            return; // Do not start cleaning
        }

        try
        {
            // Show progress window (non-modal)
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

    private async Task RunPreviewAsync()
    {
        // Clear previous validation errors (same as StartCleaningAsync)
        ValidationErrors.Clear();
        HasValidationErrors = false;

        // Run pre-clean validation (same as StartCleaningAsync)
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

            // Show preview in progress window via interaction
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

    private List<ValidationError> ValidatePreClean()
    {
        var errors = new List<ValidationError>();
        var state = _stateService.CurrentState;

        // xEdit validation
        if (string.IsNullOrEmpty(state.XEditExecutablePath))
        {
            errors.Add(new ValidationError(
                "xEdit not configured",
                "xEdit executable path is not set.",
                "Go to Edit > Settings and set the xEdit Path to your xEdit executable (SSEEdit.exe, FO4Edit.exe, etc.)."));
        }
        else if (!System.IO.File.Exists(state.XEditExecutablePath))
        {
            errors.Add(new ValidationError(
                "xEdit not found",
                $"xEdit not found at: {state.XEditExecutablePath}",
                "Go to Edit > Settings and update the xEdit Path to the correct location."));
        }

        // Load order / plugins validation
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
            var selectedCount = state.PluginsToClean.Count(p => p.IsSelected && !p.IsInSkipList);
            if (selectedCount == 0)
            {
                errors.Add(new ValidationError(
                    "No plugins selected",
                    "All plugins are either deselected or in the skip list.",
                    "Select at least one plugin to clean, or check your skip list settings."));
            }
        }

        // MO2 validation (only if MO2 mode enabled)
        if (state.Mo2ModeEnabled)
        {
            if (string.IsNullOrEmpty(state.Mo2ExecutablePath))
            {
                errors.Add(new ValidationError(
                    "MO2 not configured",
                    "MO2 mode is enabled but no MO2 executable path is set.",
                    "Go to Edit > Settings and set the MO2 Path, or disable MO2 mode if not using Mod Organizer 2."));
            }
            else if (!System.IO.File.Exists(state.Mo2ExecutablePath))
            {
                errors.Add(new ValidationError(
                    "MO2 not found",
                    $"MO2 executable not found at: {state.Mo2ExecutablePath}",
                    "Check the MO2 executable path in Edit > Settings, or disable MO2 mode."));
            }
        }

        return errors;
    }

    /// <summary>
    /// Handles timeout retry prompts for plugin cleaning.
    /// </summary>
    private async Task<bool> HandleTimeoutRetryAsync(string pluginName, int timeoutSeconds, int attemptNumber)
    {
        var message = $"Cleaning of '{pluginName}' timed out after {timeoutSeconds} seconds.\n\n" +
                      $"Attempt {attemptNumber} of 3 failed.\n\n" +
                      "Would you like to retry cleaning this plugin?";

        var details = "Possible causes:\n" +
                      "- The plugin is very large\n" +
                      "- xEdit is processing slowly\n" +
                      "- The system is under heavy load\n\n" +
                      "You can increase the timeout in Edit > Settings if plugins regularly time out.";

        return await _messageDialog.ShowRetryAsync("Plugin Timeout", message, details);
    }

    /// <summary>
    /// Handles backup failure prompts during plugin cleaning.
    /// </summary>
    private async Task<BackupFailureChoice> HandleBackupFailureAsync(string pluginName, string errorMessage)
    {
        return await _messageDialog.ShowBackupFailureDialogAsync(pluginName, errorMessage);
    }

    /// <summary>
    /// Shows the backup restore browser window.
    /// </summary>
    private async Task ShowRestoreAsync()
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

    private async Task HandleStopAsync()
    {
        StatusText = "Stopping...";
        await _orchestrator.StopCleaningAsync();

        // Check if grace period expired (Path A: user waited patiently)
        if (_orchestrator.LastTerminationResult == TerminationResult.GracePeriodExpired)
        {
            // TODO(01-02): Replace this with a user confirmation dialog
            // For now, auto-escalate to force kill since the prompt UI
            // will be added with the "Stopping..." spinner in a future plan.
            await _orchestrator.ForceStopCleaningAsync();
        }
    }

    private void Exit()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private void ShowAbout()
    {
        // TODO: Show about dialog
        StatusText = "AutoQAC Sharp v1.0";
    }

    private async Task ShowSettingsAsync()
    {
        try
        {
            var result = await _showSettingsInteraction.Handle(Unit.Default);

            if (result)
            {
                // Settings were saved - reload configuration into state
                var config = await _configService.LoadUserConfigAsync();

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

    private async Task ShowSkipListAsync()
    {
        try
        {
            var result = await _showSkipListInteraction.Handle(Unit.Default);

            if (result)
            {
                StatusText = "Skip list saved";
                _logger.Information("Skip list updated from skip list dialog");

                // Note: Skip list change triggers a refresh via ConfigService.SkipListChanged
                // which ConfigurationViewModel subscribes to. No need to refresh here.
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to show or process skip list dialog");
            StatusText = "Error opening skip list";
        }
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
