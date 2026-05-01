using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;

namespace AutoQAC.Services.Process;

public sealed class ProcessExecutionService(
    ILoggingService logger,
    IPidStore pidStore,
    IProcessSessionIdProvider sessionIdProvider,
    IProcessExitWaiter? processExitWaiter = null)
    : IProcessExecutionService, IDisposable
{
    private readonly SemaphoreSlim _processSlots = new(1, 1);
    private readonly IProcessExitWaiter _processExitWaiter = processExitWaiter ?? new ProcessExitWaiter();

    /// <summary>
    /// Known xEdit process name fragments for orphan detection.
    /// </summary>
    private static readonly string[] XEditProcessNames =
        ["sseedit", "fo4edit", "fo3edit", "fnvedit", "tes5vredit", "xedit", "fo76edit", "tes4edit"];

    private const int GracePeriodMs = 2500;
    private enum ProcessStopReason
    {
        Timeout,
        UserRequestedStop
    }

    // Hardcoded to 1: xEdit enforces single-instance via file locking

    public async Task<ProcessResult> ExecuteAsync(
        ProcessStartInfo startInfo,
        TimeSpan? timeout = null,
        CancellationToken ct = default,
        Action<System.Diagnostics.Process>? onProcessStarted = null,
        string? pluginName = null)
    {
        // Acquire a slot -- release in finally block to prevent deadlock
        await _processSlots.WaitAsync(ct).ConfigureAwait(false);
        var slotAcquired = true;

        try
        {
            var fileName = startInfo.FileName;
            var arguments = GetArgumentSummary(startInfo);
            var argumentCount = GetArgumentCount(startInfo);
            var workingDirectory = startInfo.WorkingDirectory;

            var processStartInfo = CloneStartInfoForLaunch(startInfo, fileName, workingDirectory);

            using var process = new System.Diagnostics.Process();
            process.StartInfo = processStartInfo;

            logger.Debug(
                "Starting external process for {Operation}: status={Status}, argumentCount={ArgumentCount}",
                "ExternalProcess",
                "Starting",
                argumentCount);

            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                logger.Error(
                    ex,
                    "Failed to start external process for {Operation}: status={Status}, reason={Reason}, argumentCount={ArgumentCount}",
                    "ExternalProcess",
                    "Failed",
                    "StartFailed",
                    argumentCount);
                return new ProcessResult { ExitCode = -1 };
            }

            // Track PID after successful start
            var processId = process.Id;
            logger.Information(
                "Started external process for {Operation}: status={Status}, processId={ProcessId}, argumentCount={ArgumentCount}",
                "ExternalProcess",
                "Started",
                processId,
                argumentCount);
            try
            {
                await TrackProcessAsync(process, pluginName ?? arguments, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.Warning("[Orphan] Failed to track process PID {Pid}: {Error}", processId, ex.Message);
            }

            // Notify caller of the started process (for CleaningOrchestrator to hold a reference)
            onProcessStarted?.Invoke(process);

            // Wait with timeout and cancellation
            using var timeoutCts = timeout.HasValue
                ? new CancellationTokenSource(timeout.Value)
                : null;

            using var linkedCts = timeoutCts != null
                ? CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token)
                : null;
            var linkedToken = linkedCts?.Token ?? ct;

            var timedOut = false;
            TerminationResult? terminationResult = null;
            try
            {
                // Use WaitForExitAsync instead of TCS+Exited event (known .NET bug with Kill(true))
                await _processExitWaiter.WaitForExitAsync(process, linkedToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                timedOut = timeoutCts?.IsCancellationRequested ?? false;
                var stopReason = timeoutCts?.IsCancellationRequested == true
                    ? ProcessStopReason.Timeout
                    : ProcessStopReason.UserRequestedStop;

                logger.Warning(timedOut
                    ? "Process execution timed out."
                    : "Process execution cancelled by user.");

                // Attempt graceful termination first
                var result = await TerminateProcessAsync(process, forceKill: false, CancellationToken.None)
                    .ConfigureAwait(false);
                terminationResult = result;

                if (stopReason == ProcessStopReason.Timeout && result == TerminationResult.GracePeriodExpired)
                {
                    // Timeout is an automated safety boundary, so it may escalate without a user prompt.
                    terminationResult = await TerminateProcessAsync(process, forceKill: true, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                // Untrack PID after process exits (or is killed)
                try
                {
                    if (!ShouldPreservePidEvidence(terminationResult))
                    {
                        await UntrackProcessAsync(processId, CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    logger.Warning("[Orphan] Failed to untrack process PID {Pid}: {Error}", processId, ex.Message);
                }
            }

            return new ProcessResult
            {
                ExitCode = timedOut ? -1 : (process.HasExited ? process.ExitCode : -1),
                TimedOut = timedOut,
                TerminationResult = terminationResult
            };
        }
        finally
        {
            if (slotAcquired)
            {
                _processSlots.Release();
            }
        }
    }

    /// <summary>
    /// Clones caller-supplied process start settings while preserving the mutually exclusive argument API in use.
    /// </summary>
    private static ProcessStartInfo CloneStartInfoForLaunch(
        ProcessStartInfo startInfo,
        string fileName,
        string workingDirectory)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = startInfo.CreateNoWindow,
            RedirectStandardInput = startInfo.RedirectStandardInput,
            RedirectStandardOutput = startInfo.RedirectStandardOutput,
            RedirectStandardError = startInfo.RedirectStandardError,
            StandardInputEncoding = startInfo.StandardInputEncoding,
            StandardOutputEncoding = startInfo.StandardOutputEncoding,
            StandardErrorEncoding = startInfo.StandardErrorEncoding
        };

        if (startInfo.ArgumentList.Count > 0)
        {
            foreach (var argument in startInfo.ArgumentList)
            {
                processStartInfo.ArgumentList.Add(argument);
            }
        }
        else
        {
            processStartInfo.Arguments = startInfo.Arguments;
        }

        return processStartInfo;
    }

    /// <summary>
    /// Returns debug-safe argument context without treating legacy Arguments as the only launch source.
    /// </summary>
    private static string GetArgumentSummary(ProcessStartInfo startInfo) =>
        startInfo.ArgumentList.Count > 0
            ? $"{startInfo.ArgumentList.Count} argument-list entries"
            : startInfo.Arguments;

    /// <summary>
    /// Counts launch arguments without exposing their raw values in process-start diagnostics.
    /// </summary>
    private static int GetArgumentCount(ProcessStartInfo startInfo) =>
        startInfo.ArgumentList.Count > 0
            ? startInfo.ArgumentList.Count
            : string.IsNullOrWhiteSpace(startInfo.Arguments) ? 0 : 1;

    public async Task<TerminationResult> TerminateProcessAsync(
        System.Diagnostics.Process process,
        bool forceKill = false,
        CancellationToken ct = default)
    {
        try
        {
            if (process.HasExited)
                return TerminationResult.AlreadyExited;
        }
        catch (InvalidOperationException)
        {
            // Process object may be in an invalid state
            return TerminationResult.AlreadyExited;
        }

        if (forceKill)
        {
            logger.Information("[Termination] Force killing process tree (PID: {Pid})", process.Id);
            try
            {
                process.Kill(entireProcessTree: true);
                await _processExitWaiter.WaitForExitAsync(process, ct).ConfigureAwait(false);
                logger.Information("[Termination] Process tree killed successfully (PID: {Pid})", process.Id);
                return TerminationResult.ForceKilled;
            }
            catch (InvalidOperationException)
            {
                logger.Debug("[Termination] Process already exited before Kill could execute");
                return TerminationResult.AlreadyExited;
            }
            catch (Win32Exception ex)
            {
                logger.Error(ex, "[Termination] Failed to kill process tree (PID: {Pid})", process.Id);
                return TerminationResult.ForceKillFailed;
            }
            catch (NotSupportedException ex)
            {
                logger.Error(ex, "[Termination] Force kill is not supported for process tree (PID: {Pid})", process.Id);
                return TerminationResult.ForceKillFailed;
            }
            catch (AggregateException ex)
            {
                logger.Error(ex, "[Termination] Force kill failed while waiting for process tree exit (PID: {Pid})", process.Id);
                return TerminationResult.ForceKillFailed;
            }
            catch (OperationCanceledException ex)
            {
                logger.Error(ex, "[Termination] Force kill wait was canceled for process tree (PID: {Pid})", process.Id);
                return TerminationResult.ForceKillFailed;
            }
        }

        // Graceful path: try CloseMainWindow
        logger.Information("[Termination] Attempting graceful termination (PID: {Pid})", process.Id);
        bool closeResult;
        try
        {
            closeResult = process.CloseMainWindow();
        }
        catch (InvalidOperationException)
        {
            return TerminationResult.AlreadyExited;
        }

        if (!closeResult)
        {
            // CloseMainWindow returned false -- process may not have a visible main window.
            // Skip the grace period, caller should escalate.
            logger.Debug("[Termination] CloseMainWindow returned false (no window) -- returning GracePeriodExpired for escalation");
            return TerminationResult.GracePeriodExpired;
        }

        // Wait the grace period (2.5 seconds) for the process to exit
        using var graceCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        graceCts.CancelAfter(TimeSpan.FromMilliseconds(GracePeriodMs));

        try
        {
            await _processExitWaiter.WaitForExitAsync(process, graceCts.Token).ConfigureAwait(false);
            logger.Information("[Termination] Process exited gracefully (PID: {Pid})", process.Id);
            return TerminationResult.GracefulExit;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Grace period expired, process still running
            logger.Information("[Termination] Grace period expired, process still running (PID: {Pid})", process.Id);
            return TerminationResult.GracePeriodExpired;
        }
    }

    #region PID Tracking

    public async Task TrackProcessAsync(System.Diagnostics.Process process, string pluginName, CancellationToken ct = default)
    {
        DateTime startTime;
        try
        {
            startTime = process.StartTime;
        }
        catch
        {
            startTime = DateTime.Now;
        }

        await pidStore.UpdateAsync(existing => existing.Append(new TrackedProcess
        {
            Pid = process.Id,
            StartTime = startTime,
            PluginName = pluginName,
            SessionId = sessionIdProvider.CurrentSessionId
        }).ToList(), ct).ConfigureAwait(false);
        logger.Debug("[Orphan] Tracking process PID {Pid} for plugin {Plugin}", process.Id, pluginName);
    }

    public async Task UntrackProcessAsync(int pid, CancellationToken ct = default)
    {
        await pidStore.UpdateAsync(tracked => tracked.Where(t => t.Pid != pid).ToList(), ct)
            .ConfigureAwait(false);
        logger.Debug("[Orphan] Untracked process PID {Pid}", pid);
    }

    public async Task CleanOrphanedProcessesAsync(CancellationToken ct = default)
    {
        var tracked = await pidStore.LoadAsync(ct).ConfigureAwait(false);

        if (tracked.Count == 0)
            return;

        logger.Information("[Orphan] Checking {Count} tracked processes for orphans", tracked.Count);

        var retained = new List<TrackedProcess>();

        foreach (var entry in tracked)
        {
            var isCurrentSession = string.Equals(entry.SessionId, sessionIdProvider.CurrentSessionId, StringComparison.Ordinal);
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(entry.Pid);

                if (isCurrentSession)
                {
                    retained.Add(entry);
                    continue;
                }

                if (IsXEditProcess(process, entry.StartTime))
                {
                    logger.Information("[Orphan] Detected orphaned xEdit process (PID: {Pid}, Plugin: {Plugin})", entry.Pid, entry.PluginName);
                    try
                    {
                        process.Kill(entireProcessTree: true);
                        await _processExitWaiter.WaitForExitAsync(process, ct).ConfigureAwait(false);
                        logger.Information("[Orphan] Killed orphaned process (PID: {Pid})", entry.Pid);
                    }
                    catch (InvalidOperationException)
                    {
                        logger.Debug("[Orphan] Orphaned process already exited (PID: {Pid})", entry.Pid);
                    }
                    catch (Win32Exception ex)
                    {
                        logger.Error(ex, "[Orphan] Failed to kill orphaned process (PID: {Pid})", entry.Pid);
                    }
                }
                else
                {
                    logger.Debug("[Orphan] PID {Pid} is not an xEdit process (name: {Name}) -- skipping", entry.Pid, process.ProcessName);
                }
            }
            catch (ArgumentException)
            {
                logger.Debug("[Orphan] Process no longer exists (PID: {Pid})", entry.Pid);
            }
            catch (InvalidOperationException)
            {
                logger.Debug("[Orphan] Cannot access process (PID: {Pid}) -- access denied or exited", entry.Pid);
            }
        }

        await pidStore.UpdateAsync(_ => retained, ct).ConfigureAwait(false);
        logger.Information("[Orphan] Cleared stale PID file entries: {Count}", tracked.Count - retained.Count);
    }

    private static bool ShouldPreservePidEvidence(TerminationResult? result) =>
        result is TerminationResult.GracePeriodExpired or TerminationResult.ForceKillFailed or TerminationResult.LeftRunningByUser;

    /// <summary>
    /// Verify a process is actually xEdit, not a recycled PID.
    /// Checks process name and start time proximity.
    /// </summary>
    private static bool IsXEditProcess(System.Diagnostics.Process process, DateTime trackedStartTime)
    {
        try
        {
            var name = process.ProcessName.ToLowerInvariant();
            var isXEdit = XEditProcessNames.Any(name.Contains);

            if (!isXEdit)
                return false;

            // Verify start time is within 5 seconds of what we tracked
            // (accounts for clock drift and process startup delay)
            var startTimeMatch = Math.Abs((process.StartTime - trackedStartTime).TotalSeconds) < 5;
            return startTimeMatch;
        }
        catch
        {
            // Access denied or process already exited
            return false;
        }
    }

    #endregion

    public void Dispose()
    {
        _processSlots.Dispose();
    }

}
