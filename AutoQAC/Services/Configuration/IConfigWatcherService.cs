using System;

namespace AutoQAC.Services.Configuration;

/// <summary>
/// Watches the user configuration file for external changes (e.g., manual YAML edits)
/// and triggers a reload when the content has actually changed.
/// </summary>
public interface IConfigWatcherService : IDisposable
{
    /// <summary>
    /// Begins FileSystemWatcher monitoring of the config directory.
    /// </summary>
    void StartWatching();

    /// <summary>
    /// Stops FileSystemWatcher monitoring.
    /// </summary>
    void StopWatching();
}
