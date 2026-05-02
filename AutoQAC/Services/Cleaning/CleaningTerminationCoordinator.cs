using System;
using System.ComponentModel;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Monitoring;
using AutoQAC.Services.Process;
using AutoQAC.Services.State;
using DiagnosticsProcess = System.Diagnostics.Process;

namespace AutoQAC.Services.Cleaning;

/// <summary>
/// Coordinates the active xEdit process reference, hang monitor lifecycle, and two-stage
/// user stop/force-stop policy while preserving the Phase 5 termination invariants.
/// </summary>
public sealed class CleaningTerminationCoordinator : ICleaningTerminationCoordinator, IDisposable
{
    private readonly IProcessExecutionService _processService;
    private readonly IHangDetectionService _hangDetection;
    private readonly IStateService _stateService;
    private readonly ILoggingService _logger;

    // Reactive Subject for hang state — stays in service tier per AGENTS.md (only ViewModels avoid System.Reactive).
    private readonly Subject<bool> _hangDetected = new();
    private readonly object _processLock = new();

    // State owned by this coordinator (lifted from CleaningOrchestrator.cs Phase 5 locks).
    private int _isStopRequested;
    private DiagnosticsProcess? _currentProcess;
    private PendingForceTarget? _pendingForceEscalationTarget;
    private TerminationResult? _lastTerminationResult;
    private IDisposable? _hangMonitorSubscription;

    private sealed record PendingForceTarget(int ProcessId, DateTime StartTime);

    /// <summary>
    /// Creates a termination coordinator using existing process, hang detection, state, and logging services.
    /// </summary>
    /// <param name="processService">Service responsible for graceful and forceful process termination.</param>
    /// <param name="hangDetection">CPU-based hang detection service for the attached process.</param>
    /// <param name="stateService">State service used to publish the user-visible terminating flag.</param>
    /// <param name="logger">Logger for preserving existing termination diagnostics.</param>
    public CleaningTerminationCoordinator(
        IProcessExecutionService processService,
        IHangDetectionService hangDetection,
        IStateService stateService,
        ILoggingService logger)
    {
        _processService = processService;
        _hangDetection = hangDetection;
        _stateService = stateService;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsStopRequested => Volatile.Read(ref _isStopRequested) != 0;

    /// <inheritdoc />
    public TerminationResult? LastTerminationResult => _lastTerminationResult;

    /// <inheritdoc />
    public IObservable<bool> HangDetected => _hangDetected.AsObservable();

    /// <inheritdoc />
    public bool HasActiveProcess
    {
        get
        {
            lock (_processLock)
            {
                return _currentProcess is not null;
            }
        }
    }

    /// <inheritdoc />
    public bool ProcessMayStillBeRunning => MayProcessStillBeRunning(_lastTerminationResult);

    /// <inheritdoc />
    public void AttachProcess(DiagnosticsProcess process)
    {
        lock (_processLock)
        {
            _currentProcess = process;
        }

        StartHangMonitoring(process);
    }

    /// <summary>
    /// Detaches the active cleaning process and hang monitor without discarding an unresolved pending force target.
    /// The pending target is separate ownership for a user-confirmed escalation after <see cref="TerminationResult.GracePeriodExpired" />.
    /// </summary>
    public void DetachProcess()
    {
        // Stop hang monitoring and dismiss any visible warning.
        _hangMonitorSubscription?.Dispose();
        _hangMonitorSubscription = null;
        _hangDetected.OnNext(false);

        // Clear only active cleaning semantics; an unresolved pending force target may still need confirmation.
        lock (_processLock)
        {
            _currentProcess = null;
        }
    }

    /// <inheritdoc />
    public async Task<StopCleaningResult> StopAsync()
    {
        if (Interlocked.Exchange(ref _isStopRequested, 1) == 1)
        {
            // Path B: Second click during grace period -- immediate force kill, no prompt
            _logger.Information("[Termination] Second stop requested -- escalating to force kill");
            return await ForceStopAsync().ConfigureAwait(false);
        }

        _stateService.SetTerminating(true);
        _logger.Information("[Termination] Graceful stop requested");

        // Attempt graceful termination on the current process.
        DiagnosticsProcess? proc;
        lock (_processLock)
        {
            proc = _currentProcess;
        }

        if (proc != null)
        {
            try
            {
                if (proc.Id == Environment.ProcessId)
                {
                    _logger.Error(null, "[Termination] Refusing to terminate the AutoQAC process during stop request");
                    return new StopCleaningResult(null, MayStillBeRunning: false);
                }

                if (!proc.HasExited)
                {
                    var result = await _processService.TerminateProcessAsync(proc, forceKill: false, ct: CancellationToken.None)
                        .ConfigureAwait(false);
                    _lastTerminationResult = result;

                    if (result == TerminationResult.GracePeriodExpired)
                    {
                        // Path A: Grace period expired naturally, user hasn't clicked again.
                        // Store result and retain durable identity so later confirmed escalation can reopen the target.
                        _lastTerminationResult = result;
                        RetainPendingForceEscalationProcess(proc);
                    }
                    else
                    {
                        ReleasePendingForceEscalationTarget();
                    }

                    return ToStopCleaningResult(result);
                }
            }
            catch (InvalidOperationException)
            {
                _logger.Debug("[Termination] Process already exited during graceful stop");
                _lastTerminationResult = TerminationResult.AlreadyExited;
                return ToStopCleaningResult(TerminationResult.AlreadyExited);
            }
        }

        return new StopCleaningResult(_lastTerminationResult, MayProcessStillBeRunning(_lastTerminationResult));
    }

    /// <summary>
    /// Performs a confirmed force stop against the active process or a retained pending force target.
    /// Confirmed escalation never returns cached <see cref="TerminationResult.GracePeriodExpired" /> when a pending target may still be running.
    /// </summary>
    public async Task<StopCleaningResult> ForceStopAsync()
    {
        _logger.Information("[Termination] Force stop requested -- killing process tree immediately");

        DiagnosticsProcess? proc;
        PendingForceTarget? pendingTarget = null;
        var isPendingForceEscalation = false;
        lock (_processLock)
        {
            proc = _currentProcess;
            if (proc is null && MayProcessStillBeRunning(_lastTerminationResult))
            {
                pendingTarget = _pendingForceEscalationTarget;
                isPendingForceEscalation = pendingTarget is not null;
            }
        }

        var disposeReopenedProcess = false;
        if (proc is null && pendingTarget is not null)
        {
            // The original Process is owned by the execution service and may be disposed after finalization.
            // Reopen by PID/start time so confirmed escalation never dereferences a borrowed disposed handle.
            proc = TryReopenPendingTarget(pendingTarget);
            disposeReopenedProcess = proc is not null;

            if (proc is null)
            {
                _logger.Warning("[Termination] Confirmed force stop could not reopen the pending force target");
                _lastTerminationResult = TerminationResult.ForceKillFailed;
                ReleasePendingForceEscalationTarget(pendingTarget);
                return ToStopCleaningResult(TerminationResult.ForceKillFailed);
            }
        }

        if (proc != null)
        {
            try
            {
                if (proc.Id == Environment.ProcessId)
                {
                    _logger.Error(null, "[Termination] Refusing to terminate the AutoQAC process during force stop request");
                    return new StopCleaningResult(null, MayStillBeRunning: false);
                }

                if (!proc.HasExited)
                {
                    var result = await _processService.TerminateProcessAsync(proc, forceKill: true, ct: CancellationToken.None)
                        .ConfigureAwait(false);
                    _lastTerminationResult = result;
                    if (result is TerminationResult.ForceKilled or TerminationResult.AlreadyExited)
                    {
                        ReleasePendingForceEscalationTarget();
                    }

                    return ToStopCleaningResult(result);
                }

                _lastTerminationResult = TerminationResult.AlreadyExited;
                ReleasePendingForceEscalationTarget();
                return ToStopCleaningResult(TerminationResult.AlreadyExited);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
            {
                if (isPendingForceEscalation)
                {
                    _logger.Debug("[Termination] Pending force target was unavailable during confirmed force stop");
                    _lastTerminationResult = TerminationResult.ForceKillFailed;
                    ReleasePendingForceEscalationTarget(pendingTarget);
                    return ToStopCleaningResult(TerminationResult.ForceKillFailed);
                }

                _logger.Debug("[Termination] Process already exited during force stop");
                _lastTerminationResult = TerminationResult.AlreadyExited;
                ReleasePendingForceEscalationTarget();
                return ToStopCleaningResult(TerminationResult.AlreadyExited);
            }
            finally
            {
                if (disposeReopenedProcess)
                {
                    proc.Dispose();
                }
            }
        }

        if (MayProcessStillBeRunning(_lastTerminationResult))
        {
            _logger.Warning("[Termination] Confirmed force stop had no available process target");
            _lastTerminationResult = TerminationResult.ForceKillFailed;
            return ToStopCleaningResult(TerminationResult.ForceKillFailed);
        }

        return new StopCleaningResult(_lastTerminationResult, MayProcessStillBeRunning(_lastTerminationResult));
    }

    /// <inheritdoc />
    public StopCleaningResult MarkLeftRunningByUser()
    {
        ReleasePendingForceEscalationTarget();
        _lastTerminationResult = TerminationResult.LeftRunningByUser;
        _logger.Information("[Termination] User left xEdit running after declining force termination");
        return ToStopCleaningResult(TerminationResult.LeftRunningByUser);
    }

    /// <summary>
    /// Clears per-session stop, hang, and active-process state before a new cleaning session starts.
    /// A new-session reset is the safe boundary that releases any stale unresolved pending force target.
    /// </summary>
    public void ResetForNewSession()
    {
        // (1) Clear stop-requested flag — next session is not pre-stopped.
        Interlocked.Exchange(ref _isStopRequested, 0);

        // (2) Clear UI "terminating" state — without this, prior session's terminating UI leaks (D-09).
        _stateService.SetTerminating(false);

        // (3) Clear last termination result — per-session value, not app-lifetime.
        _lastTerminationResult = null;

        // (4) Dispose any prior hang subscription AND emit `false` on the Subject.
        //     Without OnNext(false), a stale `true` from session N persists into N+1
        //     (Subject is on a Singleton — see R-11). D-09 violation if omitted.
        _hangMonitorSubscription?.Dispose();
        _hangMonitorSubscription = null;
        _hangDetected.OnNext(false);

        // (5) Clear current process reference under lock.
        lock (_processLock)
        {
            _currentProcess = null;
            _pendingForceEscalationTarget = null;
        }
    }

    /// <summary>
    /// Clears active session state after normal finalization without erasing an unresolved grace-expired pending force target.
    /// </summary>
    public void CompleteSessionFinalization()
    {
        Interlocked.Exchange(ref _isStopRequested, 0);
        _stateService.SetTerminating(false);

        _hangMonitorSubscription?.Dispose();
        _hangMonitorSubscription = null;
        _hangDetected.OnNext(false);

        lock (_processLock)
        {
            _currentProcess = null;

            if (!MayProcessStillBeRunning(_lastTerminationResult))
            {
                _pendingForceEscalationTarget = null;
                _lastTerminationResult = null;
            }
        }
    }

    /// <summary>
    /// Releases the app-lifetime hang monitor subscription and hang-detected subject owned by this coordinator.
    /// </summary>
    public void Dispose()
    {
        _hangMonitorSubscription?.Dispose();
        _hangDetected.Dispose();
    }

    private static StopCleaningResult ToStopCleaningResult(TerminationResult? result) =>
        new(result, MayProcessStillBeRunning(result));

    private static bool MayProcessStillBeRunning(TerminationResult? result) =>
        result is TerminationResult.GracePeriodExpired or TerminationResult.LeftRunningByUser or TerminationResult.ForceKillFailed;

    /// <summary>
    /// Captures durable process identity for a later confirmed force escalation without retaining the borrowed process handle.
    /// </summary>
    /// <param name="process">The currently attached process whose owner may dispose it after session finalization.</param>
    private void RetainPendingForceEscalationProcess(DiagnosticsProcess process)
    {
        var processId = TryGetProcessId(process);
        var startTime = TryGetStartTime(process);
        if (processId is null || startTime is null)
        {
            _logger.Warning("[Termination] Could not retain pending force target because the process identity was not fully verifiable");
            return;
        }

        var target = new PendingForceTarget(processId.Value, startTime.Value);

        lock (_processLock)
        {
            _pendingForceEscalationTarget = target;
        }
    }

    /// <summary>
    /// Clears the unresolved pending force target, optionally only if it still matches the expected target snapshot.
    /// </summary>
    /// <param name="target">Expected pending target, or <see langword="null" /> to clear any pending target.</param>
    private void ReleasePendingForceEscalationTarget(PendingForceTarget? target = null)
    {
        lock (_processLock)
        {
            if (target is null || Equals(_pendingForceEscalationTarget, target))
            {
                _pendingForceEscalationTarget = null;
            }
        }
    }

    /// <summary>
    /// Attempts to read the OS process identifier from a process handle that may already be unavailable.
    /// </summary>
    /// <param name="process">Process handle to inspect.</param>
    /// <returns>The process ID when available; otherwise <see langword="null" />.</returns>
    private static int? TryGetProcessId(DiagnosticsProcess process)
    {
        try
        {
            return process.Id;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Attempts to capture process start time so a later PID lookup can reject recycled process IDs.
    /// </summary>
    /// <param name="process">Process handle to inspect.</param>
    /// <returns>The process start time when available; otherwise <see langword="null" />.</returns>
    private static DateTime? TryGetStartTime(DiagnosticsProcess process)
    {
        try
        {
            return process.StartTime;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or Win32Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Reopens a pending force target by PID and validates its captured start time.
    /// </summary>
    /// <param name="target">Durable identity captured during graceful termination expiry.</param>
    /// <returns>A newly opened process handle owned by the caller, or <see langword="null" /> if unavailable or recycled.</returns>
    private static DiagnosticsProcess? TryReopenPendingTarget(PendingForceTarget target)
    {
        try
        {
            var process = DiagnosticsProcess.GetProcessById(target.ProcessId);
            if (process.StartTime != target.StartTime)
            {
                process.Dispose();
                return null;
            }

            return process;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    private void StartHangMonitoring(DiagnosticsProcess process)
    {
        // Ensure only one active monitor subscription per xEdit process lifecycle.
        _hangMonitorSubscription?.Dispose();
        _hangMonitorSubscription = _hangDetection.MonitorProcess(process)
            .Subscribe(
                isHung => _hangDetected.OnNext(isHung),
                _ => { }, // Error: monitor completed unexpectedly
                () => { } // Completed: process exited
            );
    }
}
