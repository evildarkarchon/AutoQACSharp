using System;

namespace AutoQAC.Models;

/// <summary>
/// Record for PID file entries stored in autoqac-pids.json.
/// Used for orphan detection: on startup or before a cleaning run,
/// we check if any tracked processes are still running and kill them.
/// </summary>
public sealed record TrackedProcess
{
    /// <summary>Process ID of the xEdit instance.</summary>
    public int Pid { get; init; }

    /// <summary>When the process was started (for PID reuse validation).</summary>
    public DateTime StartTime { get; init; }

    /// <summary>Name of the plugin being cleaned when this process was launched.</summary>
    public string PluginName { get; init; } = string.Empty;
}
