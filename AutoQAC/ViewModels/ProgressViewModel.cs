using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoQAC.ViewModels;

public sealed partial class ProgressViewModel : ViewModelBase, IDisposable
{
    private readonly ICleaningSession _cleaningSession;
    private readonly IMessageDialogService _messageDialog;
    private readonly ILoggingService _logger;
    private readonly List<IDisposable> _subscriptions = [];

    private bool _wasPreviouslyCleaning;
    private bool _hangWarningDismissed;

    [ObservableProperty] public partial string? CurrentPlugin { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    public partial int Progress { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    public partial int Total { get; set; }

    [ObservableProperty] public partial int CleanedCount { get; set; }

    [ObservableProperty] public partial int SkippedCount { get; set; }

    [ObservableProperty] public partial int FailedCount { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    public partial bool IsCleaning { get; set; }

    [ObservableProperty] public partial int CurrentItmCount { get; set; }

    [ObservableProperty] public partial int CurrentUdrCount { get; set; }

    [ObservableProperty] public partial int CurrentNavCount { get; set; }

    [ObservableProperty] public partial bool HasCurrentPluginStats { get; set; }

    public ObservableCollection<PluginCleaningResult> CompletedPlugins { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsResultsSummaryVisible))]
    public partial bool IsShowingResults { get; set; }

    [ObservableProperty] public partial CleaningSessionResult? SessionResult { get; set; }

    [ObservableProperty] public partial bool WasCancelled { get; set; }

    [ObservableProperty] public partial string SessionSummaryText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStopOutcomeWarning))]
    public partial string? StopOutcomeWarningText { get; set; }

    [ObservableProperty] public partial int TotalItmCount { get; set; }

    [ObservableProperty] public partial int TotalUdrCount { get; set; }

    [ObservableProperty] public partial int TotalNavCount { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    public partial bool IsTerminating { get; set; }

    [ObservableProperty] public partial bool IsHangWarningVisible { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsResultsSummaryVisible))]
    public partial bool IsPreviewMode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveOperationLabel))]
    [NotifyPropertyChangedFor(nameof(IsBackupOperationActive))]
    [NotifyPropertyChangedFor(nameof(IsBackupCancelVisible))]
    [NotifyPropertyChangedFor(nameof(IsCleanupCancelVisible))]
    [NotifyPropertyChangedFor(nameof(BackupOperationProgressText))]
    [NotifyCanExecuteChangedFor(nameof(CancelBackupOperationCommand))]
    public partial BackupOperationState? BackupOperation { get; set; }

    public ObservableCollection<DryRunResult> DryRunResults { get; } = [];

    // ReSharper disable once UnusedMember.Global - bound from ProgressWindow.xaml.
    public string PreviewDisclaimer => "Preview only -- does not detect ITMs/UDRs (requires xEdit)";

    [ObservableProperty] public partial int WillCleanCount { get; set; }

    [ObservableProperty] public partial int WillSkipCount { get; set; }

    public string ProgressText => Total > 0
        ? $"{Progress} / {Total} ({Progress * 100 / Total}%)"
        : "0 / 0 (0%)";

    public string ActiveOperationLabel => BackupOperation is { IsActive: true } operation
        ? operation.Label
        : CurrentPlugin is { Length: > 0 } plugin
            ? $"Cleaning: {plugin}"
            : "Cleaning:";

    public bool IsBackupOperationActive => BackupOperation is { IsActive: true };

    public bool IsBackupCancelVisible => BackupOperation is
    {
        IsActive: true,
        CanCancel: true,
        Kind: BackupOperationKind.Backup
    };

    public bool IsCleanupCancelVisible => BackupOperation is
    {
        IsActive: true,
        CanCancel: true,
        Kind: BackupOperationKind.RetentionCleanup
    };

    public string BackupOperationProgressText => FormatBackupOperationProgress(BackupOperation);

    /// <summary>
    /// Gets whether the completed-cleaning summary panel should be visible instead of the active or preview panels.
    /// </summary>
    public bool IsResultsSummaryVisible => IsShowingResults && !IsPreviewMode;

    /// <summary>
    /// Gets whether a persistent stop outcome warning should remain visible in the results summary.
    /// </summary>
    public bool HasStopOutcomeWarning => !string.IsNullOrWhiteSpace(StopOutcomeWarningText);

    /// <summary>Event raised when the window should close.</summary>
    public event EventHandler? CloseRequested;

    public ProgressViewModel(IStateService stateService, ICleaningSession cleaningSession,
        IMessageDialogService messageDialog, ILoggingService logger, IUiDispatcher uiDispatcher)
    {
        _cleaningSession = cleaningSession;
        _messageDialog = messageDialog;
        _logger = logger;

        _subscriptions.Add(stateService.StateChanged.Subscribe(
            new CallbackObserver<AppState>(state => uiDispatcher.Post(() => OnStateChanged(state)))));

        _subscriptions.Add(stateService.DetailedPluginResult.Subscribe(
            new CallbackObserver<PluginCleaningResult>(result => uiDispatcher.Post(() => OnDetailedResult(result)))));

        _subscriptions.Add(stateService.CleaningCompleted.Subscribe(
            new CallbackObserver<CleaningSessionResult>(session =>
                uiDispatcher.Post(() => OnCleaningCompleted(session)))));

        _subscriptions.Add(_cleaningSession.HangDetected.Subscribe(
            new CallbackObserver<bool>(isHung => uiDispatcher.Post(() => OnHangDetected(isHung)))));

        _subscriptions.Add(stateService.IsTerminatingChanged.Subscribe(
            new CallbackObserver<bool>(isTerminating => uiDispatcher.Post(() => IsTerminating = isTerminating))));

        OnStateChanged(stateService.CurrentState);
    }

    /// <summary>
    /// Loads dry-run preview results into the ViewModel. Sets IsPreviewMode true and populates the
    /// DryRunResults collection.
    /// </summary>
    public void LoadDryRunResults(List<DryRunResult> results)
    {
        IsPreviewMode = true;
        DryRunResults.Clear();
        foreach (var result in results)
        {
            DryRunResults.Add(result);
        }

        WillCleanCount = results.Count(r => r.Status == DryRunStatus.WillClean);
        WillSkipCount = results.Count(r => r.Status == DryRunStatus.WillSkip);
        IsShowingResults = true;
    }

    private bool CanStop() => IsCleaning && !IsTerminating;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        try
        {
            var result = await _cleaningSession.ControlAsync(CleaningSessionControl.RequestStop);
            await ReportControlWarningIfNeededAsync(result);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Progress stop command failed");
            await ShowForceFailureDialogSafelyAsync();
        }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void DismissHangWarning()
    {
        IsHangWarningVisible = false;
        _hangWarningDismissed = true;
    }

    [RelayCommand]
    private async Task KillHungProcessAsync()
    {
        try
        {
            IsHangWarningVisible = false;
            var forceResult = await _cleaningSession.ControlAsync(CleaningSessionControl.ForceStop);
            await ReportControlWarningIfNeededAsync(forceResult);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Progress hang force-stop command failed");
            IsHangWarningVisible = false;
            await ShowForceFailureDialogSafelyAsync();
        }
    }

    /// <summary>
    /// Reports control outcomes through the shared safe dialog copy and persistent Progress summary warning.
    /// </summary>
    /// <param name="result">The structured control result returned by the Cleaning session.</param>
    private async Task ReportControlWarningIfNeededAsync(CleaningSessionControlResult result)
    {
        if (result.Status == CleaningSessionControlStatus.LeftRunningByUser)
        {
            StopOutcomeWarningText = StopTerminationDialogContent.LeftRunningMessage;
        }

        if (result.Status == CleaningSessionControlStatus.ForceKillFailed)
        {
            await ShowForceFailureDialogSafelyAsync();
        }
    }

    /// <summary>
    /// Persists the shared force-failure warning and best-effort displays the matching dialog.
    /// </summary>
    private async Task ShowForceFailureDialogSafelyAsync()
    {
        StopOutcomeWarningText = StopTerminationDialogContent.ForceFailureMessage;
        try
        {
            await _messageDialog.ShowErrorAsync(
                StopTerminationDialogContent.ForceFailureTitle,
                StopTerminationDialogContent.ForceFailureMessage);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to show Progress stop failure dialog");
            // The persistent warning remains visible when the modal dialog itself cannot be shown.
        }
    }

    /// <summary>
    /// Requests cancellation of the active backup or retention file operation without using the xEdit Stop path.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCancelBackupOperation))]
    private async Task CancelBackupOperationAsync() =>
        await _cleaningSession.ControlAsync(CleaningSessionControl.CancelBackupOperation);

    private void OnStateChanged(AppState state)
    {
        if (state.IsCleaning && !_wasPreviouslyCleaning)
        {
            ResetForNewSession();
        }

        _wasPreviouslyCleaning = state.IsCleaning;

        IsCleaning = state.IsCleaning;
        CurrentPlugin = state.CurrentPlugin;
        OnPropertyChanged(nameof(ActiveOperationLabel));
        Progress = state.Progress;
        Total = state.TotalPlugins;
        CleanedCount = state.CleanedPlugins.Count;
        SkippedCount = state.SkippedPlugins.Count;
        FailedCount = state.FailedPlugins.Count;
        BackupOperation = state.BackupOperation is { IsActive: true } operation ? operation : null;
    }

    private bool CanCancelBackupOperation() => BackupOperation is { IsActive: true, CanCancel: true };

    /// <summary>
    /// Formats count-only retention progress or byte-aware backup copy progress for the cleaning progress band.
    /// </summary>
    /// <param name="operation">The currently active non-xEdit file operation, or null when no file work is active.</param>
    /// <returns>A concise user-facing progress string; empty when no operation is active.</returns>
    private static string FormatBackupOperationProgress(BackupOperationState? operation)
    {
        if (operation is not { IsActive: true })
        {
            return string.Empty;
        }

        var totalFiles = operation.TotalFiles ?? 0;
        var countText = totalFiles > 0
            ? $"{operation.FilesCompleted} / {totalFiles} files"
            : $"{operation.FilesCompleted} files";

        return operation.TotalBytes is > 0
            ? $"{countText} — {BackupProgressTextFormatter.FormatBytes(operation.BytesCopied)} / {BackupProgressTextFormatter.FormatBytes(operation.TotalBytes.Value)}"
            : countText;
    }

    private void OnDetailedResult(PluginCleaningResult result)
    {
        CompletedPlugins.Add(result);

        CurrentItmCount = result.ItemsRemoved;
        CurrentUdrCount = result.ItemsUndeleted;
        CurrentNavCount = result.Statistics?.PartialFormsCreated ?? 0;
        HasCurrentPluginStats = result.Statistics != null;

        TotalItmCount = CompletedPlugins.Sum(p => p.ItemsRemoved);
        TotalUdrCount = CompletedPlugins.Sum(p => p.ItemsUndeleted);
        TotalNavCount = CompletedPlugins.Sum(p => p.Statistics?.PartialFormsCreated ?? 0);
    }

    private void OnCleaningCompleted(CleaningSessionResult session)
    {
        SessionResult = session;
        IsShowingResults = true;
        WasCancelled = session.WasCancelled;
        IsCleaning = false;

        SessionSummaryText = session.SessionSummary;

        TotalItmCount = session.TotalItemsRemoved;
        TotalUdrCount = session.TotalItemsUndeleted;
        TotalNavCount = session.TotalPartialFormsCreated;
    }

    private void OnHangDetected(bool isHung)
    {
        if (isHung)
        {
            if (!_hangWarningDismissed)
            {
                IsHangWarningVisible = true;
            }
        }
        else
        {
            IsHangWarningVisible = false;
            _hangWarningDismissed = false;
        }
    }

    private void ResetForNewSession()
    {
        CompletedPlugins.Clear();
        CurrentItmCount = 0;
        CurrentUdrCount = 0;
        CurrentNavCount = 0;
        HasCurrentPluginStats = false;
        TotalItmCount = 0;
        TotalUdrCount = 0;
        TotalNavCount = 0;
        IsShowingResults = false;
        SessionResult = null;
        WasCancelled = false;
        SessionSummaryText = string.Empty;
        StopOutcomeWarningText = null;
        IsPreviewMode = false;
        DryRunResults.Clear();
        WillCleanCount = 0;
        WillSkipCount = 0;
        IsTerminating = false;
        IsHangWarningVisible = false;
        BackupOperation = null;
        _hangWarningDismissed = false;
    }

    public void Dispose()
    {
        foreach (var sub in _subscriptions)
        {
            sub.Dispose();
        }

        _subscriptions.Clear();
    }
}
