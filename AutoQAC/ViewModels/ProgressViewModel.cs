using System;
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

    private string _logOutput = string.Empty;
    public string LogOutput
    {
        get => _logOutput;
        set => this.RaiseAndSetIfChanged(ref _logOutput, value);
    }

    private bool _isCleaning;
    public bool IsCleaning
    {
        get => _isCleaning;
        set => this.RaiseAndSetIfChanged(ref _isCleaning, value);
    }

    public ReactiveCommand<Unit, Unit> StopCommand { get; }
    
    /// <summary>
    /// Event raised when the window should close.
    /// </summary>
    public event EventHandler? CloseRequested;
    
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    private readonly ObservableAsPropertyHelper<string> _progressText;
    public string ProgressText => _progressText.Value;

    public ProgressViewModel(IStateService stateService, ICleaningOrchestrator orchestrator)
    {
        _stateService = stateService;
        _orchestrator = orchestrator;

        // StopCommand is only enabled when cleaning is in progress
        var canStop = this.WhenAnyValue(x => x.IsCleaning);
        StopCommand = ReactiveCommand.CreateFromTask(() => _orchestrator.StopCleaningAsync(), canStop);
        
        // CloseCommand raises an event to request window closure
        CloseCommand = ReactiveCommand.Create(() => CloseRequested?.Invoke(this, EventArgs.Empty));

        // Subscribe to state changes
        var stateSubscription = _stateService.StateChanged
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(OnStateChanged);
        _disposables.Add(stateSubscription);

        var pluginProcessedSubscription = _stateService.PluginProcessed
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(OnPluginProcessed);
        _disposables.Add(pluginProcessedSubscription);

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
        IsCleaning = state.IsCleaning;
        CurrentPlugin = state.CurrentPlugin;
        Progress = state.Progress;
        Total = state.TotalPlugins;
        CleanedCount = state.CleanedPlugins.Count;
        SkippedCount = state.SkippedPlugins.Count;
        FailedCount = state.FailedPlugins.Count;
    }

    private void OnPluginProcessed((string plugin, CleaningStatus status) args)
    {
        LogOutput += $"[{DateTime.Now:HH:mm:ss}] {args.plugin}: {args.status}\n";
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
