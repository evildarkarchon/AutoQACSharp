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
    void SetPluginsToClean(List<PluginInfo> plugins);
    void StartCleaning(List<PluginInfo> plugins);
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

    /// <summary>
    /// Emits a detailed per-plugin result each time a plugin finishes cleaning.
    /// Subscribers receive ITM/UDR/Nav stats for live counter badges.
    /// </summary>
    IObservable<PluginCleaningResult> DetailedPluginResult { get; }

    /// <summary>
    /// Observable that emits true when stop/termination is in progress.
    /// Foundation for the locked "UI blocking during stop" user decision.
    /// The actual UI spinner will be implemented in a future plan.
    /// </summary>
    IObservable<bool> IsTerminatingChanged { get; }

    /// <summary>
    /// Sets whether termination is currently in progress.
    /// </summary>
    void SetTerminating(bool isTerminating);
}
