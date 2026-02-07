using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.State;

namespace AutoQAC.Services.Process;

public sealed class ProcessExecutionService : IProcessExecutionService, IDisposable
{
    private readonly SemaphoreSlim _processSlots;
    private readonly ILoggingService _logger;
    private readonly IStateService _stateService;

    /// <summary>
    /// Known xEdit process name fragments for orphan detection.
    /// </summary>
    private static readonly string[] XEditProcessNames =
        ["sseedit", "fo4edit", "fo3edit", "fnvedit", "tes5vredit", "xedit", "fo76edit", "tes4edit"];

    private const string PidFileName = "autoqac-pids.json";
    private const int GracePeriodMs = 2500;
    private const int SemaphoreTimeoutMs = 60_000;
    private const int SemaphoreWarningMs = 10_000;

    public ProcessExecutionService(IStateService stateService, ILoggingService logger)
    {
        _logger = logger;
        _stateService = stateService;

        var maxProcesses = stateService.CurrentState.MaxConcurrentSubprocesses ?? 1;
        _logger.Debug($"Initializing ProcessExecutionService with {maxProcesses} slots.");
        _processSlots = new SemaphoreSlim(maxProcesses, maxProcesses);
    }

    public async Task<IDisposable> AcquireProcessSlotAsync(CancellationToken ct = default)
    {
        // Start the actual wait
        var slotTask = _processSlots.WaitAsync(ct);

        // Race against a 10-second warning delay
        var warningDelay = Task.Delay(SemaphoreWarningMs, ct);
        var firstCompleted = await Task.WhenAny(slotTask, warningDelay).ConfigureAwait(false);

        if (firstCompleted == warningDelay && !slotTask.IsCompleted)
        {
            _logger.Warning("[Termination] Semaphore slot acquisition has been waiting for 10s -- possible contention");

            // Continue waiting up to the full 60s timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(SemaphoreTimeoutMs - SemaphoreWarningMs);

            try
            {
                await slotTask.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.Warning("[Termination] Semaphore slot acquisition timed out after 60s -- possible deadlock");
                throw new TimeoutException("Process slot acquisition timed out after 60 seconds. This may indicate a deadlock in process management.");
            }
        }
        else
        {
            // slotTask completed within 10s (or was cancelled), just await to propagate exceptions
            await slotTask.ConfigureAwait(false);
        }

        return new SemaphoreReleaser(_processSlots);
    }

    public async Task<ProcessResult> ExecuteAsync(
        ProcessStartInfo startInfo,
        IProgress<string>? outputProgress = null,
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
            var arguments = startInfo.Arguments;
            var workingDirectory = startInfo.WorkingDirectory;

            var processStartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = false
            };

            using var process = new System.Diagnostics.Process();
            process.StartInfo = processStartInfo;

            var outputLines = new List<string>();
            var errorLines = new List<string>();

            process.OutputDataReceived += (s, e) =>
            {
                if (e.Data == null) return;
                outputLines.Add(e.Data);
                outputProgress?.Report(e.Data);
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null) errorLines.Add(e.Data);
            };

            _logger.Debug($"Starting process: {startInfo.FileName} {startInfo.Arguments}");

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to start process: {startInfo.FileName}");
                return new ProcessResult { ExitCode = -1, ErrorLines = [ex.Message] };
            }

            // Track PID after successful start
            var processId = process.Id;
            try
            {
                await TrackProcessAsync(process, pluginName ?? arguments, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warning("[Orphan] Failed to track process PID {Pid}: {Error}", processId, ex.Message);
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

            bool timedOut = false;
            try
            {
                // Use WaitForExitAsync instead of TCS+Exited event (known .NET bug with Kill(true))
                await process.WaitForExitAsync(linkedToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                timedOut = timeoutCts?.IsCancellationRequested ?? false;

                _logger.Warning(timedOut
                    ? "Process execution timed out."
                    : "Process execution cancelled by user.");

                // Attempt graceful termination first
                var result = await TerminateProcessAsync(process, forceKill: false, CancellationToken.None)
                    .ConfigureAwait(false);

                if (result == TerminationResult.GracePeriodExpired)
                {
                    // Grace period expired -- force kill
                    await TerminateProcessAsync(process, forceKill: true, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                // Untrack PID after process exits (or is killed)
                try
                {
                    await UntrackProcessAsync(processId, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Warning("[Orphan] Failed to untrack process PID {Pid}: {Error}", processId, ex.Message);
                }
            }

            return new ProcessResult
            {
                ExitCode = timedOut ? -1 : (process.HasExited ? process.ExitCode : -1),
                OutputLines = outputLines,
                ErrorLines = errorLines,
                TimedOut = timedOut
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
            _logger.Information("[Termination] Force killing process tree (PID: {Pid})", process.Id);
            try
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(ct).ConfigureAwait(false);
                _logger.Information("[Termination] Process tree killed successfully (PID: {Pid})", process.Id);
                return TerminationResult.ForceKilled;
            }
            catch (InvalidOperationException)
            {
                _logger.Debug("[Termination] Process already exited before Kill could execute");
                return TerminationResult.AlreadyExited;
            }
            catch (Win32Exception ex)
            {
                _logger.Error(ex, "[Termination] Failed to kill process tree (PID: {Pid})", process.Id);
                return TerminationResult.ForceKilled; // Best effort -- it may have partially worked
            }
        }

        // Graceful path: try CloseMainWindow
        _logger.Information("[Termination] Attempting graceful termination (PID: {Pid})", process.Id);
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
            _logger.Debug("[Termination] CloseMainWindow returned false (no window) -- returning GracePeriodExpired for escalation");
            return TerminationResult.GracePeriodExpired;
        }

        // Wait the grace period (2.5 seconds) for the process to exit
        using var graceCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        graceCts.CancelAfter(TimeSpan.FromMilliseconds(GracePeriodMs));

        try
        {
            await process.WaitForExitAsync(graceCts.Token).ConfigureAwait(false);
            _logger.Information("[Termination] Process exited gracefully (PID: {Pid})", process.Id);
            return TerminationResult.GracefulExit;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Grace period expired, process still running
            _logger.Information("[Termination] Grace period expired, process still running (PID: {Pid})", process.Id);
            return TerminationResult.GracePeriodExpired;
        }
    }

    #region PID Tracking

    public async Task TrackProcessAsync(System.Diagnostics.Process process, string pluginName, CancellationToken ct = default)
    {
        var pidFilePath = GetPidFilePath();
        var tracked = await LoadTrackedProcessesAsync(pidFilePath, ct).ConfigureAwait(false);

        DateTime startTime;
        try
        {
            startTime = process.StartTime;
        }
        catch
        {
            startTime = DateTime.Now;
        }

        tracked.Add(new TrackedProcess
        {
            Pid = process.Id,
            StartTime = startTime,
            PluginName = pluginName
        });

        await SaveTrackedProcessesAsync(pidFilePath, tracked, ct).ConfigureAwait(false);
        _logger.Debug("[Orphan] Tracking process PID {Pid} for plugin {Plugin}", process.Id, pluginName);
    }

    public async Task UntrackProcessAsync(int pid, CancellationToken ct = default)
    {
        var pidFilePath = GetPidFilePath();

        if (!File.Exists(pidFilePath))
            return;

        var tracked = await LoadTrackedProcessesAsync(pidFilePath, ct).ConfigureAwait(false);
        var updated = tracked.Where(t => t.Pid != pid).ToList();

        if (updated.Count == tracked.Count)
            return; // PID was not in the list

        await SaveTrackedProcessesAsync(pidFilePath, updated, ct).ConfigureAwait(false);
        _logger.Debug("[Orphan] Untracked process PID {Pid}", pid);
    }

    public async Task CleanOrphanedProcessesAsync(CancellationToken ct = default)
    {
        var pidFilePath = GetPidFilePath();

        if (!File.Exists(pidFilePath))
            return;

        var tracked = await LoadTrackedProcessesAsync(pidFilePath, ct).ConfigureAwait(false);

        if (tracked.Count == 0)
            return;

        _logger.Information("[Orphan] Checking {Count} tracked processes for orphans", tracked.Count);

        foreach (var entry in tracked)
        {
            try
            {
                var process = System.Diagnostics.Process.GetProcessById(entry.Pid);

                if (IsXEditProcess(process, entry.StartTime))
                {
                    _logger.Information("[Orphan] Detected orphaned xEdit process (PID: {Pid}, Plugin: {Plugin})", entry.Pid, entry.PluginName);
                    try
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync(ct).ConfigureAwait(false);
                        _logger.Information("[Orphan] Killed orphaned process (PID: {Pid})", entry.Pid);
                    }
                    catch (InvalidOperationException)
                    {
                        _logger.Debug("[Orphan] Orphaned process already exited (PID: {Pid})", entry.Pid);
                    }
                    catch (Win32Exception ex)
                    {
                        _logger.Error(ex, "[Orphan] Failed to kill orphaned process (PID: {Pid})", entry.Pid);
                    }
                }
                else
                {
                    _logger.Debug("[Orphan] PID {Pid} is not an xEdit process (name: {Name}) -- skipping", entry.Pid, process.ProcessName);
                }
            }
            catch (ArgumentException)
            {
                _logger.Debug("[Orphan] Process no longer exists (PID: {Pid})", entry.Pid);
            }
            catch (InvalidOperationException)
            {
                _logger.Debug("[Orphan] Cannot access process (PID: {Pid}) -- access denied or exited", entry.Pid);
            }
        }

        // Clear the PID file after processing all entries
        await SaveTrackedProcessesAsync(pidFilePath, new List<TrackedProcess>(), ct).ConfigureAwait(false);
        _logger.Information("[Orphan] Cleared stale PID file entries: {Count}", tracked.Count);
    }

    /// <summary>
    /// Verify a process is actually xEdit, not a recycled PID.
    /// Checks process name and start time proximity.
    /// </summary>
    private static bool IsXEditProcess(System.Diagnostics.Process process, DateTime trackedStartTime)
    {
        try
        {
            var name = process.ProcessName.ToLowerInvariant();
            var isXEdit = XEditProcessNames.Any(xEditName => name.Contains(xEditName));

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

    private string GetPidFilePath()
    {
        // Use the same directory resolution as ConfigurationService
        var baseDir = AppContext.BaseDirectory;

#if DEBUG
        var current = new DirectoryInfo(baseDir);
        for (int i = 0; i < 6 && current != null; i++)
        {
            var candidate = Path.Combine(current.FullName, "AutoQAC Data");
            if (Directory.Exists(candidate))
            {
                return Path.Combine(candidate, PidFileName);
            }
            current = current.Parent;
        }
#endif

        var configDir = Path.Combine(baseDir, "AutoQAC Data");
        if (!Directory.Exists(configDir))
        {
            Directory.CreateDirectory(configDir);
        }

        return Path.Combine(configDir, PidFileName);
    }

    private static async Task<List<TrackedProcess>> LoadTrackedProcessesAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
            return new List<TrackedProcess>();

        try
        {
            var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                return new List<TrackedProcess>();

            return JsonSerializer.Deserialize<List<TrackedProcess>>(json) ?? new List<TrackedProcess>();
        }
        catch
        {
            // Corrupted file -- start fresh
            return new List<TrackedProcess>();
        }
    }

    private static async Task SaveTrackedProcessesAsync(string path, List<TrackedProcess> tracked, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(tracked, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, ct).ConfigureAwait(false);
    }

    #endregion

    public void Dispose()
    {
        _processSlots.Dispose();
    }

    private class SemaphoreReleaser(SemaphoreSlim semaphore) : IDisposable
    {
        private bool _isDisposed;

        public void Dispose()
        {
            if (!_isDisposed)
            {
                semaphore.Release();
                _isDisposed = true;
            }
        }
    }
}
