using System;
using System.Collections.Generic;
using AutoQAC.Models;

namespace AutoQAC.Services.State;

public interface IStateService
{
    // Current state
    AppState CurrentState { get; }

    // State updates
    void UpdateState(Func<AppState, AppState> updateFunc);
    void UpdateConfigurationPaths(string? loadOrder, string? mo2, string? xEdit);
    void SetPluginsToClean(List<string> plugins);
    void StartCleaning(List<string> plugins);
    void FinishCleaning();
    void AddCleaningResult(string plugin, CleaningStatus status);
    void UpdateProgress(int current, int total);

    // Reactive observables
    IObservable<AppState> StateChanged { get; }
    IObservable<bool> ConfigurationValidChanged { get; }
    IObservable<(int current, int total)> ProgressChanged { get; }
    IObservable<(string plugin, CleaningStatus status)> PluginProcessed { get; }
}
