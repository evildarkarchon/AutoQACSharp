using System;
using System.Collections.Generic;
using AutoQAC.Models;

namespace AutoQAC.Services.State;

public interface IStateService
{
    // Current state
    AppState CurrentState { get; }

    /// <summary>
    /// Gets the last cleaning session result, or null if no cleaning has been performed.
    /// </summary>
    CleaningSessionResult? LastSessionResult { get; }

    // State updates
    void UpdateState(Func<AppState, AppState> updateFunc);
    void UpdateConfigurationPaths(string? loadOrder, string? mo2, string? xEdit);
    void SetPluginsToClean(List<string> plugins);
    void StartCleaning(List<string> plugins);
    void FinishCleaning();
    void AddCleaningResult(string plugin, CleaningStatus status);
    void UpdateProgress(int current, int total);

    /// <summary>
    /// Adds a detailed cleaning result for a plugin.
    /// </summary>
    void AddDetailedCleaningResult(PluginCleaningResult result);

    /// <summary>
    /// Finishes cleaning with full session results.
    /// </summary>
    void FinishCleaningWithResults(CleaningSessionResult sessionResult);

    // Reactive observables
    IObservable<AppState> StateChanged { get; }
    IObservable<bool> ConfigurationValidChanged { get; }
    IObservable<(int current, int total)> ProgressChanged { get; }
    IObservable<(string plugin, CleaningStatus status)> PluginProcessed { get; }

    /// <summary>
    /// Emits when a cleaning session completes with full results.
    /// </summary>
    IObservable<CleaningSessionResult> CleaningCompleted { get; }
}
