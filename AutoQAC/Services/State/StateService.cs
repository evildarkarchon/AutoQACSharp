using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using AutoQAC.Models;

namespace AutoQAC.Services.State;

public sealed class StateService : IStateService, IDisposable
{
    private readonly Lock _lock = new();
    private readonly BehaviorSubject<AppState> _stateSubject = new(new AppState());
    private readonly Subject<(string plugin, CleaningStatus status)> _pluginProcessedSubject = new();
    private readonly Subject<CleaningSessionResult> _cleaningCompletedSubject = new();

    private readonly List<PluginCleaningResult> _currentSessionResults = new();
    private DateTime _cleaningStartTime;
    private CleaningSessionResult? _lastSessionResult;

    public CleaningSessionResult? LastSessionResult => _lastSessionResult;

    public AppState CurrentState
    {
        get
        {
            lock (_lock)
            {
                return _stateSubject.Value;
            }
        }
    }

    public IObservable<AppState> StateChanged => _stateSubject.AsObservable();

    public IObservable<bool> ConfigurationValidChanged => 
        _stateSubject.Select(s => s is { IsLoadOrderConfigured: true, IsXEditConfigured: true })
                     .DistinctUntilChanged();

    public IObservable<(int current, int total)> ProgressChanged =>
        _stateSubject.Select(s => (s.Progress, s.TotalPlugins))
                     .DistinctUntilChanged();

    public IObservable<(string plugin, CleaningStatus status)> PluginProcessed =>
        _pluginProcessedSubject.AsObservable();

    public IObservable<CleaningSessionResult> CleaningCompleted =>
        _cleaningCompletedSubject.AsObservable();

    public void UpdateState(Func<AppState, AppState> updateFunc)
    {
        lock (_lock)
        {
            var newState = updateFunc(_stateSubject.Value);
            _stateSubject.OnNext(newState);
        }
    }

    public void UpdateConfigurationPaths(string? loadOrder, string? mo2, string? xEdit)
    {
        UpdateState(s => s with
        {
            LoadOrderPath = loadOrder,
            Mo2ExecutablePath = mo2,
            XEditExecutablePath = xEdit
        });
    }

    public void SetPluginsToClean(List<PluginInfo> plugins)
    {
        UpdateState(s => s with
        {
            PluginsToClean = [..plugins]
        });
    }

    public void StartCleaning(List<PluginInfo> plugins)
    {
        lock (_lock)
        {
            _currentSessionResults.Clear();
            _cleaningStartTime = DateTime.Now;
        }

        UpdateState(s => s with
        {
            IsCleaning = true,
            PluginsToClean = [..plugins],
            Progress = 0,
            TotalPlugins = plugins.Count,
            CleanedPlugins = new HashSet<string>(),
            FailedPlugins = new HashSet<string>(),
            SkippedPlugins = new HashSet<string>()
        });
    }

    public void FinishCleaning()
    {
        UpdateState(s => s with
        {
            IsCleaning = false,
            CurrentPlugin = null,
            CurrentOperation = null
        });
    }

    public void AddDetailedCleaningResult(PluginCleaningResult result)
    {
        lock (_lock)
        {
            _currentSessionResults.Add(result);
        }

        // Also update the basic status tracking
        AddCleaningResult(result.PluginName, result.Status);
    }

    public void FinishCleaningWithResults(CleaningSessionResult sessionResult)
    {
        lock (_lock)
        {
            _lastSessionResult = sessionResult;
        }

        UpdateState(s => s with
        {
            IsCleaning = false,
            CurrentPlugin = null,
            CurrentOperation = null
        });

        _cleaningCompletedSubject.OnNext(sessionResult);
    }

    public void AddCleaningResult(string plugin, CleaningStatus status)
    {
        UpdateState(s =>
        {
            var cleaned = new HashSet<string>(s.CleanedPlugins);
            var failed = new HashSet<string>(s.FailedPlugins);
            var skipped = new HashSet<string>(s.SkippedPlugins);

            switch (status)
            {
                case CleaningStatus.Cleaned:
                    cleaned.Add(plugin);
                    break;
                case CleaningStatus.Failed:
                    failed.Add(plugin);
                    break;
                case CleaningStatus.Skipped:
                    skipped.Add(plugin);
                    break;
            }

            return s with
            {
                CleanedPlugins = cleaned,
                FailedPlugins = failed,
                SkippedPlugins = skipped,
                Progress = s.Progress + 1
            };
        });

        _pluginProcessedSubject.OnNext((plugin, status));
    }

    public void UpdateProgress(int current, int total)
    {
        UpdateState(s => s with
        {
            Progress = current,
            TotalPlugins = total
        });
    }

    public void Dispose()
    {
        _stateSubject.Dispose();
        _pluginProcessedSubject.Dispose();
        _cleaningCompletedSubject.Dispose();
    }
}
