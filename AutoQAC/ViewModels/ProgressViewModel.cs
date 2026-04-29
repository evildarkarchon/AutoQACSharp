using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoQAC.ViewModels;

public sealed partial class ProgressViewModel : ViewModelBase, IDisposable
{
    private readonly IStateService _stateService;
    private readonly ICleaningOrchestrator _orchestrator;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly List<IDisposable> _subscriptions = new();

    private bool _wasPreviouslyCleaning;
    private bool _hangWarningDismissed;

    [ObservableProperty]
    private string? _currentPlugin;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private int _progress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private int _total;

    [ObservableProperty]
    private int _cleanedCount;

    [ObservableProperty]
    private int _skippedCount;

    [ObservableProperty]
    private int _failedCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool _isCleaning;

    [ObservableProperty]
    private int _currentItmCount;

    [ObservableProperty]
    private int _currentUdrCount;

    [ObservableProperty]
    private int _currentNavCount;

    [ObservableProperty]
    private bool _hasCurrentPluginStats;

    public ObservableCollection<PluginCleaningResult> CompletedPlugins { get; } = new();

    [ObservableProperty]
    private bool _isShowingResults;

    [ObservableProperty]
    private CleaningSessionResult? _sessionResult;

    [ObservableProperty]
    private bool _wasCancelled;

    [ObservableProperty]
    private string _sessionSummaryText = string.Empty;

    [ObservableProperty]
    private int _totalItmCount;

    [ObservableProperty]
    private int _totalUdrCount;

    [ObservableProperty]
    private int _totalNavCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool _isTerminating;

    [ObservableProperty]
    private bool _isHangWarningVisible;

    [ObservableProperty]
    private bool _isPreviewMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveOperationLabel))]
    [NotifyPropertyChangedFor(nameof(IsBackupOperationActive))]
    [NotifyPropertyChangedFor(nameof(IsBackupCancelVisible))]
    [NotifyPropertyChangedFor(nameof(IsCleanupCancelVisible))]
    [NotifyPropertyChangedFor(nameof(BackupOperationProgressText))]
    [NotifyCanExecuteChangedFor(nameof(CancelBackupOperationCommand))]
    private BackupOperationState? _backupOperation;

    public ObservableCollection<DryRunResult> DryRunResults { get; } = new();

    public string PreviewDisclaimer => "Preview only -- does not detect ITMs/UDRs (requires xEdit)";

    [ObservableProperty]
    private int _willCleanCount;

    [ObservableProperty]
    private int _willSkipCount;

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

    /// <summary>Event raised when the window should close.</summary>
    public event EventHandler? CloseRequested;

    public ProgressViewModel(IStateService stateService, ICleaningOrchestrator orchestrator, IUiDispatcher uiDispatcher)
    {
        _stateService = stateService;
        _orchestrator = orchestrator;
        _uiDispatcher = uiDispatcher;

        _subscriptions.Add(_stateService.StateChanged.Subscribe(
            new CallbackObserver<AppState>(state => _uiDispatcher.Post(() => OnStateChanged(state)))));

        _subscriptions.Add(_stateService.DetailedPluginResult.Subscribe(
            new CallbackObserver<PluginCleaningResult>(result => _uiDispatcher.Post(() => OnDetailedResult(result)))));

        _subscriptions.Add(_stateService.CleaningCompleted.Subscribe(
            new CallbackObserver<CleaningSessionResult>(session => _uiDispatcher.Post(() => OnCleaningCompleted(session)))));

        _subscriptions.Add(_orchestrator.HangDetected.Subscribe(
            new CallbackObserver<bool>(isHung => _uiDispatcher.Post(() => OnHangDetected(isHung)))));

        _subscriptions.Add(_stateService.IsTerminatingChanged.Subscribe(
            new CallbackObserver<bool>(isTerminating => _uiDispatcher.Post(() => IsTerminating = isTerminating))));

        OnStateChanged(_stateService.CurrentState);
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
    private async System.Threading.Tasks.Task StopAsync() => await _orchestrator.StopCleaningAsync();

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void DismissHangWarning()
    {
        IsHangWarningVisible = false;
        _hangWarningDismissed = true;
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task KillHungProcessAsync()
    {
        IsHangWarningVisible = false;
        await _orchestrator.ForceStopCleaningAsync();
    }

    /// <summary>
    /// Requests cancellation of the active backup or retention file operation without using the xEdit Stop path.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCancelBackupOperation))]
    private async System.Threading.Tasks.Task CancelBackupOperationAsync() => await _orchestrator.CancelBackupOperationAsync();

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
            ? $"{countText} — {FormatBytes(operation.BytesCopied)} / {FormatBytes(operation.TotalBytes.Value)}"
            : countText;
    }

    /// <summary>
    /// Converts byte counts to the decimal display convention used by Phase 7 copy progress text.
    /// </summary>
    /// <param name="bytes">The byte count reported by the backup copy operation.</param>
    /// <returns>A one-decimal megabyte string suitable for UI binding.</returns>
    private static string FormatBytes(long bytes)
    {
        const double BytesPerMegabyte = 1024.0 * 1024.0;
        return $"{bytes / BytesPerMegabyte:0.0} MB";
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
