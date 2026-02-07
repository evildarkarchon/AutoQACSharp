using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.State;
using ReactiveUI;

namespace AutoQAC.ViewModels;

public sealed class ProgressViewModel : ViewModelBase, IDisposable
{
    private readonly IStateService _stateService;
    private readonly ICleaningOrchestrator _orchestrator;
    private readonly CompositeDisposable _disposables = new();

    private string? _currentPlugin;
    public string? CurrentPlugin
    {
        get => _currentPlugin;
        set => this.RaiseAndSetIfChanged(ref _currentPlugin, value);
    }

    private int _progress;
    public int Progress
    {
        get => _progress;
        set => this.RaiseAndSetIfChanged(ref _progress, value);
    }

    private int _total;
    public int Total
    {
        get => _total;
        set => this.RaiseAndSetIfChanged(ref _total, value);
    }

    private int _cleanedCount;
    public int CleanedCount
    {
        get => _cleanedCount;
        set => this.RaiseAndSetIfChanged(ref _cleanedCount, value);
    }

    private int _skippedCount;
    public int SkippedCount
    {
        get => _skippedCount;
        set => this.RaiseAndSetIfChanged(ref _skippedCount, value);
    }

    private int _failedCount;
    public int FailedCount
    {
        get => _failedCount;
        set => this.RaiseAndSetIfChanged(ref _failedCount, value);
    }

    private bool _isCleaning;
    public bool IsCleaning
    {
        get => _isCleaning;
        set => this.RaiseAndSetIfChanged(ref _isCleaning, value);
    }

    // Per-plugin live counter badges
    private int _currentItmCount;
    public int CurrentItmCount
    {
        get => _currentItmCount;
        set => this.RaiseAndSetIfChanged(ref _currentItmCount, value);
    }

    private int _currentUdrCount;
    public int CurrentUdrCount
    {
        get => _currentUdrCount;
        set => this.RaiseAndSetIfChanged(ref _currentUdrCount, value);
    }

    private int _currentNavCount;
    public int CurrentNavCount
    {
        get => _currentNavCount;
        set => this.RaiseAndSetIfChanged(ref _currentNavCount, value);
    }

    private bool _hasCurrentPluginStats;
    public bool HasCurrentPluginStats
    {
        get => _hasCurrentPluginStats;
        set => this.RaiseAndSetIfChanged(ref _hasCurrentPluginStats, value);
    }

    // Accumulated completed plugins
    public ObservableCollection<PluginCleaningResult> CompletedPlugins { get; } = new();

    // Results summary mode
    private bool _isShowingResults;
    public bool IsShowingResults
    {
        get => _isShowingResults;
        set => this.RaiseAndSetIfChanged(ref _isShowingResults, value);
    }

    private CleaningSessionResult? _sessionResult;
    public CleaningSessionResult? SessionResult
    {
        get => _sessionResult;
        set => this.RaiseAndSetIfChanged(ref _sessionResult, value);
    }

    private bool _wasCancelled;
    public bool WasCancelled
    {
        get => _wasCancelled;
        set => this.RaiseAndSetIfChanged(ref _wasCancelled, value);
    }

    private string _sessionSummaryText = string.Empty;
    public string SessionSummaryText
    {
        get => _sessionSummaryText;
        set => this.RaiseAndSetIfChanged(ref _sessionSummaryText, value);
    }

    // Session-wide totals for results summary
    private int _totalItmCount;
    public int TotalItmCount
    {
        get => _totalItmCount;
        set => this.RaiseAndSetIfChanged(ref _totalItmCount, value);
    }

    private int _totalUdrCount;
    public int TotalUdrCount
    {
        get => _totalUdrCount;
        set => this.RaiseAndSetIfChanged(ref _totalUdrCount, value);
    }

    private int _totalNavCount;
    public int TotalNavCount
    {
        get => _totalNavCount;
        set => this.RaiseAndSetIfChanged(ref _totalNavCount, value);
    }

    // Termination in progress (stopping spinner)
    private bool _isTerminating;
    public bool IsTerminating
    {
        get => _isTerminating;
        set => this.RaiseAndSetIfChanged(ref _isTerminating, value);
    }

    // Hang detection warning
    private bool _isHangWarningVisible;
    public bool IsHangWarningVisible
    {
        get => _isHangWarningVisible;
        set => this.RaiseAndSetIfChanged(ref _isHangWarningVisible, value);
    }

    /// <summary>
    /// True when the user has dismissed the warning via "Wait" -- prevents the warning
    /// from reappearing until the next hang detection cycle (process resumes then hangs again).
    /// </summary>
    private bool _hangWarningDismissed;

    public ReactiveCommand<Unit, Unit> DismissHangWarningCommand { get; }
    public ReactiveCommand<Unit, Unit> KillHungProcessCommand { get; }

    // Dry-run preview mode
    private bool _isPreviewMode;
    public bool IsPreviewMode
    {
        get => _isPreviewMode;
        set => this.RaiseAndSetIfChanged(ref _isPreviewMode, value);
    }

    public ObservableCollection<DryRunResult> DryRunResults { get; } = new();

    public string PreviewDisclaimer => "Preview only -- does not detect ITMs/UDRs (requires xEdit)";

    private int _willCleanCount;
    public int WillCleanCount
    {
        get => _willCleanCount;
        set => this.RaiseAndSetIfChanged(ref _willCleanCount, value);
    }

    private int _willSkipCount;
    public int WillSkipCount
    {
        get => _willSkipCount;
        set => this.RaiseAndSetIfChanged(ref _willSkipCount, value);
    }

    /// <summary>
    /// Loads dry-run preview results into the ViewModel.
    /// Sets IsPreviewMode to true and populates the DryRunResults collection.
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

    public ReactiveCommand<Unit, Unit> StopCommand { get; }

    /// <summary>
    /// Event raised when the window should close.
    /// </summary>
    public event EventHandler? CloseRequested;

    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    private readonly ObservableAsPropertyHelper<string> _progressText;
    public string ProgressText => _progressText.Value;

    /// <summary>
    /// Track whether we were previously in a cleaning state to detect session start.
    /// </summary>
    private bool _wasPreviouslyCleaning;

    public ProgressViewModel(IStateService stateService, ICleaningOrchestrator orchestrator)
    {
        _stateService = stateService;
        _orchestrator = orchestrator;

        // StopCommand is only enabled when cleaning is in progress and not already terminating
        var canStop = this.WhenAnyValue(x => x.IsCleaning, x => x.IsTerminating,
            (cleaning, terminating) => cleaning && !terminating);
        StopCommand = ReactiveCommand.CreateFromTask(() => _orchestrator.StopCleaningAsync(), canStop);

        // CloseCommand raises an event to request window closure
        CloseCommand = ReactiveCommand.Create(() => CloseRequested?.Invoke(this, EventArgs.Empty));

        // Hang detection commands
        DismissHangWarningCommand = ReactiveCommand.Create(() =>
        {
            IsHangWarningVisible = false;
            _hangWarningDismissed = true;
        });

        KillHungProcessCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            IsHangWarningVisible = false;
            await _orchestrator.ForceStopCleaningAsync();
        });

        // StateChanged subscription with throttling for large plugin counts.
        // ObserveOn(MainThreadScheduler) naturally coalesces rapid updates between UI frames.
        // DistinctUntilChanged on IsCleaning ensures session start/stop transitions are never missed.
        var stateChanged = _stateService.StateChanged
            .ObserveOn(RxApp.MainThreadScheduler);

        var stateSubscription = stateChanged.Subscribe(OnStateChanged);
        _disposables.Add(stateSubscription);

        // Subscribe to detailed per-plugin results (NOT throttled -- fires once per plugin)
        var detailedResultSubscription = _stateService.DetailedPluginResult
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(OnDetailedResult);
        _disposables.Add(detailedResultSubscription);

        // Subscribe to cleaning completed for results summary mode
        var cleaningCompletedSubscription = _stateService.CleaningCompleted
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(OnCleaningCompleted);
        _disposables.Add(cleaningCompletedSubscription);

        // Subscribe to hang detection from the orchestrator
        var hangSubscription = _orchestrator.HangDetected
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(OnHangDetected);
        _disposables.Add(hangSubscription);

        // Subscribe to termination state for stopping spinner
        var terminatingSubscription = _stateService.IsTerminatingChanged
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(isTerminating => IsTerminating = isTerminating);
        _disposables.Add(terminatingSubscription);

        // Computed progress text
        _progressText = this.WhenAnyValue(
            x => x.Progress,
            x => x.Total,
            (current, total) => total > 0
                ? $"{current} / {total} ({current * 100 / total}%)"
                : "0 / 0 (0%)")
            .ToProperty(this, x => x.ProgressText);
        _disposables.Add(_progressText);

        // Initialize from current state
        OnStateChanged(_stateService.CurrentState);
    }

    private void OnStateChanged(AppState state)
    {
        // Detect new cleaning session start: IsCleaning transitions from false to true
        if (state.IsCleaning && !_wasPreviouslyCleaning)
        {
            ResetForNewSession();
        }
        _wasPreviouslyCleaning = state.IsCleaning;

        IsCleaning = state.IsCleaning;
        CurrentPlugin = state.CurrentPlugin;
        Progress = state.Progress;
        Total = state.TotalPlugins;
        CleanedCount = state.CleanedPlugins.Count;
        SkippedCount = state.SkippedPlugins.Count;
        FailedCount = state.FailedPlugins.Count;
    }

    private void OnDetailedResult(PluginCleaningResult result)
    {
        // Add to accumulated completed plugins list
        CompletedPlugins.Add(result);

        // Update per-plugin counter badges from latest completed plugin's stats
        CurrentItmCount = result.ItemsRemoved;
        CurrentUdrCount = result.ItemsUndeleted;
        CurrentNavCount = result.Statistics?.PartialFormsCreated ?? 0;
        HasCurrentPluginStats = result.Statistics != null;

        // Update running session-wide totals
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

        // Use the session's own summary text
        SessionSummaryText = session.SessionSummary;

        // Calculate total counts from session results for accuracy
        TotalItmCount = session.TotalItemsRemoved;
        TotalUdrCount = session.TotalItemsUndeleted;
        TotalNavCount = session.TotalPartialFormsCreated;
    }

    private void OnHangDetected(bool isHung)
    {
        if (isHung)
        {
            // Only show warning if user hasn't dismissed it for this cycle
            if (!_hangWarningDismissed)
            {
                IsHangWarningVisible = true;
            }
        }
        else
        {
            // Process resumed or plugin changed -- auto-dismiss warning and reset dismissed flag
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
        _hangWarningDismissed = false;
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
