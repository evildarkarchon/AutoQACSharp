using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;

namespace AutoQAC.Services.Process;

public interface IProcessExecutionService
{
    // Execute process with real-time output
    Task<ProcessResult> ExecuteAsync(
        ProcessStartInfo startInfo,
        IProgress<string>? outputProgress = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default,
        Action<System.Diagnostics.Process>? onProcessStarted = null,
        string? pluginName = null);

    /// <summary>
    /// Terminate a process with escalation support.
    /// When forceKill is false: attempts CloseMainWindow with a 2.5s grace period.
    /// When forceKill is true: immediately kills the entire process tree.
    /// </summary>
    Task<TerminationResult> TerminateProcessAsync(
        System.Diagnostics.Process process,
        bool forceKill = false,
        CancellationToken ct = default);

    /// <summary>
    /// Detect and kill orphaned xEdit processes from the PID tracking file.
    /// Called on startup and before each cleaning run.
    /// </summary>
    Task CleanOrphanedProcessesAsync(CancellationToken ct = default);

    /// <summary>
    /// Write a PID entry to the tracking file after a process starts.
    /// </summary>
    Task TrackProcessAsync(System.Diagnostics.Process process, string pluginName, CancellationToken ct = default);

    /// <summary>
    /// Remove a PID entry from the tracking file after a process exits.
    /// </summary>
    Task UntrackProcessAsync(int pid, CancellationToken ct = default);
}

public sealed record ProcessResult
{
    public int ExitCode { get; init; }
    public List<string> OutputLines { get; init; } = new();
    public List<string> ErrorLines { get; init; } = new();
    public bool TimedOut { get; init; }
}
