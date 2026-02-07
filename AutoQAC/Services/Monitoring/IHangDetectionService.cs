using System;

namespace AutoQAC.Services.Monitoring;

/// <summary>
/// Monitors a process for hang detection by polling CPU usage.
/// Emits true when the process appears hung (near-zero CPU for extended duration),
/// and false when the process resumes activity.
/// </summary>
public interface IHangDetectionService
{
    /// <summary>
    /// Start monitoring a process for hang detection.
    /// Emits true when the process appears hung, false when it resumes.
    /// The observable completes when the process exits.
    /// </summary>
    /// <param name="process">The process to monitor.</param>
    /// <returns>An observable that emits hang state changes.</returns>
    IObservable<bool> MonitorProcess(System.Diagnostics.Process process);
}
