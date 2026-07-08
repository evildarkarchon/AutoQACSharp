using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    ICleaningSession cleaningSession,
    IConfigurationService configService,
    ICleaningCommandReadiness cleaningCommandReadiness,
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
    private CancellationTokenSource? _readinessCts;
    private int _readinessRequestId;
    private CleaningPreflightFailureKind? _currentReadinessFailureKind;

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
    public void OnStateChanged(AppState state)
    {
        ApplyState(state);
        ScheduleReadinessRefresh(projectFailure: true);
    }

    /// <summary>
    /// Re-evaluates command readiness when Plugin refresh publication facts may have changed.
    /// </summary>
    public void OnPluginRefreshSnapshot(PluginRefreshSnapshot snapshot)
    {
        _ = snapshot;
        ScheduleReadinessRefresh(projectFailure: true);
    }

    private void ApplyState(AppState state)
    {
        IsCleaning = state.IsCleaning;
        if (state.IsCleaning)
        {
            CanStartCleaning = false;
        }

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

        if (!await ValidatePreCleanAsync().ConfigureAwait(true))
        {
            return;
        }

        try
        {
            await pluginRefreshModule.ExecuteAsync(
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

        if (!await ValidatePreCleanAsync().ConfigureAwait(true))
        {
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

    private async Task<bool> ValidatePreCleanAsync()
    {
        var readiness = await cleaningCommandReadiness.EvaluateAsync().ConfigureAwait(true);
        ApplyReadiness(readiness, projectFailure: true);
        return readiness.CanStartOrPreview;
    }

    private void ScheduleReadinessRefresh(bool projectFailure)
    {
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _readinessCts, cts);
        previous?.Cancel();
        previous?.Dispose();

        var requestId = Interlocked.Increment(ref _readinessRequestId);
        _ = UpdateReadinessAsync(requestId, projectFailure, cts.Token);
    }

    private async Task UpdateReadinessAsync(
        int requestId,
        bool projectFailure,
        CancellationToken ct)
    {
        try
        {
            var readiness = await cleaningCommandReadiness.EvaluateAsync(ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested || requestId != Volatile.Read(ref _readinessRequestId))
            {
                return;
            }

            ApplyReadiness(readiness, projectFailure);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to evaluate Cleaning command readiness");
            CanStartCleaning = false;
        }
    }

    private void ApplyReadiness(CleaningCommandReadinessResult readiness, bool projectFailure)
    {
        CanStartCleaning = readiness.CanStartOrPreview;
        if (readiness.CanStartOrPreview)
        {
            ClearReadinessValidationIfCurrent();
            return;
        }

        if (projectFailure && readiness.Failure is not null && !IsCleaning)
        {
            ProjectReadinessFailure(readiness.Failure);
        }
    }

    private void ProjectReadinessFailure(CleaningPreflightFailure failure)
    {
        ProjectPreflightFailure(failure);
        _currentReadinessFailureKind = failure.Kind;
    }

    private void ClearReadinessValidationIfCurrent()
    {
        if (_currentReadinessFailureKind is null)
        {
            return;
        }

        ValidationErrors.Clear();
        HasValidationErrors = false;
        _currentReadinessFailureKind = null;
    }

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
                ("Plugins need refresh",
                    "Refresh plugins after changing game, load order, MO2, or skip list settings."),
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
        var cts = Interlocked.Exchange(ref _readinessCts, null);
        cts?.Cancel();
        cts?.Dispose();
    }
}
