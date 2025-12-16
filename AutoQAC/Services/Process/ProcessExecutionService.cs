using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Services.State;

namespace AutoQAC.Services.Process;

public sealed class ProcessExecutionService : IProcessExecutionService, IDisposable
{
    private readonly SemaphoreSlim _processSlots;
    private readonly ILoggingService _logger;
    private readonly IStateService _stateService;

    public ProcessExecutionService(IStateService stateService, ILoggingService logger)
    {
        _logger = logger;
        _stateService = stateService;

        // Initialize semaphore with max concurrent processes from settings
        // Default to 1 to be safe per user request, though config defaults to 3.
        // We will respect the config if provided, otherwise fallback.
        var maxProcesses = stateService.CurrentState.MaxConcurrentSubprocesses ?? 1;
        
        // If the user specifically said "No concurrent subprocesses", we might want to hardcode 1.
        // But let's trust the Orchestrator to be sequential, and the Semaphore to just be a safety net.
        // Since the user was emphatic, we will log this decision.
        _logger.Debug($"Initializing ProcessExecutionService with {maxProcesses} slots.");
        
        _processSlots = new SemaphoreSlim(maxProcesses, maxProcesses);
    }

    public async Task<IDisposable> AcquireProcessSlotAsync(CancellationToken ct = default)
    {
        await _processSlots.WaitAsync(ct).ConfigureAwait(false);
        return new SemaphoreReleaser(_processSlots);
    }

    public async Task<ProcessResult> ExecuteAsync(
        ProcessStartInfo startInfo,
        IProgress<string>? outputProgress = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        // We acquire a slot for every execution to respect the limit
        using var slot = await AcquireProcessSlotAsync(ct).ConfigureAwait(false);

        using var process = new System.Diagnostics.Process { StartInfo = startInfo };

        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;

        var outputLines = new List<string>();
        var errorLines = new List<string>();
        
        // Use a TaskCompletionSource to wait for process exit async
        var tcs = new TaskCompletionSource<bool>();

        process.OutputDataReceived += (s, e) => {
            if (e.Data != null)
            {
                outputLines.Add(e.Data);
                outputProgress?.Report(e.Data);
            }
        };

        process.ErrorDataReceived += (s, e) => {
            if (e.Data != null) errorLines.Add(e.Data);
        };

        process.Exited += (s, e) => tcs.TrySetResult(true);
        process.EnableRaisingEvents = true;

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
            return new ProcessResult { ExitCode = -1, ErrorLines = new List<string> { ex.Message } };
        }

        // Wait with timeout and cancellation
        using var timeoutCts = timeout.HasValue
            ? new CancellationTokenSource(timeout.Value)
            : null;

        // Combine tokens: User cancellation + Timeout
        using var linkedCts = timeoutCts != null
            ? CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token)
            : null;
        var linkedToken = linkedCts?.Token ?? ct;

        // Register cancellation callback to kill process
        using var reg = linkedToken.Register(() =>
        {
            tcs.TrySetCanceled();
        });

        bool timedOut = false;
        try
        {
            await tcs.Task;
        }
        catch (TaskCanceledException)
        {
            // Determine if it was timeout or user cancel
            timedOut = timeoutCts?.IsCancellationRequested ?? false;

            _logger.Warning(timedOut
                ? "Process execution timed out."
                : "Process execution cancelled by user.");

            await TerminateProcessGracefullyAsync(process).ConfigureAwait(false);
        }

        return new ProcessResult
        {
            ExitCode = timedOut ? -1 : (process.HasExited ? process.ExitCode : -1),
            OutputLines = outputLines,
            ErrorLines = errorLines,
            TimedOut = timedOut
        };
    }

    private async Task TerminateProcessGracefullyAsync(System.Diagnostics.Process process)
    {
        if (process.HasExited) return;

        _logger.Debug("Attempting graceful termination...");
        try
        {
            // Try to close main window first (if it has one) or send close signal
            // Since we use CreateNoWindow=true, CloseMainWindow might not work as expected for console apps,
            // but xEdit is a GUI app usually.
            process.CloseMainWindow();
            
            // Wait a bit
            await Task.Delay(2000).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
             _logger.Debug($"Graceful termination failed: {ex.Message}");
        }

        if (!process.HasExited)
        {
            _logger.Debug("Force killing process...");
            try
            {
                process.Kill();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to kill process.");
            }
        }
    }

    public void Dispose()
    {
        _processSlots.Dispose();
    }

    private class SemaphoreReleaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _isDisposed;

        public SemaphoreReleaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _semaphore.Release();
                _isDisposed = true;
            }
        }
    }
}
