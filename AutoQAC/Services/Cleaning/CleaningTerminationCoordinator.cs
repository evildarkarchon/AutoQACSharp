using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Monitoring;
using AutoQAC.Services.Process;
using AutoQAC.Services.State;

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
    private volatile bool _isStopRequested;
    private System.Diagnostics.Process? _currentProcess;
    private TerminationResult? _lastTerminationResult;
    private IDisposable? _hangMonitorSubscription;

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
    public bool IsStopRequested => _isStopRequested;

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
    public void AttachProcess(System.Diagnostics.Process process)
    {
        lock (_processLock)
        {
            _currentProcess = process;
        }

        StartHangMonitoring(process);
    }

    /// <inheritdoc />
    public void DetachProcess()
    {
        // Stop hang monitoring and dismiss any visible warning.
        _hangMonitorSubscription?.Dispose();
        _hangMonitorSubscription = null;
        _hangDetected.OnNext(false);

        // Clear the current process reference after plugin is done.
        lock (_processLock)
        {
            _currentProcess = null;
        }
    }

    /// <inheritdoc />
    public async Task<StopCleaningResult> StopAsync()
    {
        if (_isStopRequested)
        {
            // Path B: Second click during grace period -- immediate force kill, no prompt
            _logger.Information("[Termination] Second stop requested -- escalating to force kill");
            return await ForceStopAsync().ConfigureAwait(false);
        }

        _isStopRequested = true;
        _stateService.SetTerminating(true);
        _logger.Information("[Termination] Graceful stop requested");

        // Attempt graceful termination on the current process.
        System.Diagnostics.Process? proc;
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
                        // Store result so the ViewModel can react and prompt the user.
                        _lastTerminationResult = result;
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

    /// <inheritdoc />
    public async Task<StopCleaningResult> ForceStopAsync()
    {
        _logger.Information("[Termination] Force stop requested -- killing process tree immediately");

        // Force kill the process tree.
        System.Diagnostics.Process? proc;
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
                    _logger.Error(null, "[Termination] Refusing to terminate the AutoQAC process during force stop request");
                    return new StopCleaningResult(null, MayStillBeRunning: false);
                }

                if (!proc.HasExited)
                {
                    var result = await _processService.TerminateProcessAsync(proc, forceKill: true, ct: CancellationToken.None)
                        .ConfigureAwait(false);
                    _lastTerminationResult = result;
                    return ToStopCleaningResult(result);
                }

                _lastTerminationResult = TerminationResult.AlreadyExited;
                return ToStopCleaningResult(TerminationResult.AlreadyExited);
            }
            catch (InvalidOperationException)
            {
                _logger.Debug("[Termination] Process already exited during force stop");
                _lastTerminationResult = TerminationResult.AlreadyExited;
                return ToStopCleaningResult(TerminationResult.AlreadyExited);
            }
        }

        return new StopCleaningResult(_lastTerminationResult, MayProcessStillBeRunning(_lastTerminationResult));
    }

    /// <inheritdoc />
    public StopCleaningResult MarkLeftRunningByUser()
    {
        _lastTerminationResult = TerminationResult.LeftRunningByUser;
        _logger.Information("[Termination] User left xEdit running after declining force termination");
        return ToStopCleaningResult(TerminationResult.LeftRunningByUser);
    }

    /// <inheritdoc />
    public void ResetForNewSession()
    {
        // (1) Clear stop-requested flag — next session is not pre-stopped.
        _isStopRequested = false;

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

    private void StartHangMonitoring(System.Diagnostics.Process process)
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
