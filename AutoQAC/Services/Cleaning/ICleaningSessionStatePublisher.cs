using System.Collections.Generic;
using AutoQAC.Models;

namespace AutoQAC.Services.Cleaning;

/// <summary>
/// Publishes Cleaning session state changes while hiding the concrete application-state store.
/// </summary>
public interface ICleaningSessionStatePublisher
{
    /// <summary>Publishes the detected game for a real Cleaning session.</summary>
    void PublishDetectedGame(GameType gameType);

    /// <summary>Publishes the start of a real Cleaning session.</summary>
    void PublishStarted(IReadOnlyList<PluginInfo> pluginsToClean);

    /// <summary>Publishes the plugin currently being processed.</summary>
    void PublishCurrentPlugin(string pluginName);

    /// <summary>Publishes a completed or skipped per-plugin result.</summary>
    void PublishPluginResult(PluginCleaningResult result);

    /// <summary>Publishes that a plugin was skipped by user decision.</summary>
    void PublishSkippedPlugin(string pluginName);

    /// <summary>Publishes whether xEdit termination is currently in progress.</summary>
    void PublishTerminating(bool isTerminating);

    /// <summary>Publishes the final result for a real Cleaning session.</summary>
    void PublishCompleted(CleaningSessionResult sessionResult);
}
