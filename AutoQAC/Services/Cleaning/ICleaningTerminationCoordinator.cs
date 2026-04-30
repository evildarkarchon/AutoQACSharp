using System;
using System.Threading.Tasks;
using AutoQAC.Models;

namespace AutoQAC.Services.Cleaning;

/// <summary>
/// Owns xEdit termination policy: process attach/detach, two-stage stop, force-stop,
/// hang-monitor lifecycle, and Phase 5 stop semantics. Lifetime: per cleaning session;
/// state is reset between sessions via <see cref="ResetForNewSession" />.
/// </summary>
public interface ICleaningTerminationCoordinator
{
    /// <summary>
    /// Attaches the active xEdit process and starts hang monitoring.
    /// Per R-04: in Wave 3 the orchestrator's foreach lambda calls this directly;
    /// in Wave 4 (08-05) the lambda is hoisted into IPluginCleaningRunner via the
    /// attachProcess delegate parameter.
    /// </summary>
    void AttachProcess(System.Diagnostics.Process process);

    /// <summary>Detaches the active xEdit process and stops hang monitoring. Idempotent.</summary>
    void DetachProcess();

    /// <summary>
    /// Two-stage stop. First call: sets stop-requested flag, requests graceful termination,
    /// returns GracePeriodExpired when graceful close did not exit within 2.5s. Second call
    /// during grace: escalates to <see cref="ForceStopAsync" /> (Phase 5 D-01..D-05 locks).
    /// </summary>
    Task<StopCleaningResult> StopAsync();

    /// <summary>Immediate Kill(true) of the process tree (Phase 5 D-04).</summary>
    Task<StopCleaningResult> ForceStopAsync();

    /// <summary>User declined the force-terminate prompt (Phase 5 D-03).</summary>
    StopCleaningResult MarkLeftRunningByUser();

    /// <summary>
    /// Resets ALL per-session termination state. Per R-03, this MUST mirror
    /// CleaningOrchestrator.cs:622-648 verbatim — five resets:
    ///   1. _isStopRequested = false
    ///   2. _stateService.SetTerminating(false)
    ///   3. _lastTerminationResult = null
    ///   4. _hangMonitorSubscription?.Dispose() + null + _hangDetected.OnNext(false)
    ///   5. _currentProcess = null under _processLock
    /// Omitting any leaks user-visible UI state across sessions (D-09 violation).
    /// </summary>
    void ResetForNewSession();

    /// <summary>True when a stop was requested in the current session.</summary>
    bool IsStopRequested { get; }

    /// <summary>Last termination result for this session, or null if no termination occurred.</summary>
    TerminationResult? LastTerminationResult { get; }

    /// <summary>True when xEdit is currently attached (used by backup-cancel rejection).</summary>
    bool HasActiveProcess { get; }

    /// <summary>True when log read should be skipped because the process may still be running.</summary>
    bool ProcessMayStillBeRunning { get; }

    /// <summary>
    /// Observable that emits hang detection state changes during cleaning.
    /// Emits true when xEdit appears hung (near-zero CPU for 60+ seconds),
    /// false when xEdit resumes activity. Emits false on plugin change and
    /// cleaning end to auto-dismiss any visible warning.
    /// </summary>
    IObservable<bool> HangDetected { get; }
}
