using System;
using System.Threading.Tasks;
using AutoQAC.Models;

namespace AutoQAC.Services.Cleaning;

/// <summary>
/// Owns xEdit termination policy: process attach/detach, two-stage stop, force-stop,
/// hang-monitor lifecycle, and Phase 5 stop semantics. Lifetime: per cleaning session;
/// state is reset before sessions via <see cref="ResetForNewSession" /> and finalized after sessions via <see cref="CompleteSessionFinalization" />.
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

    /// <summary>
    /// Detaches the active xEdit process and stops hang monitoring. Idempotent.
    /// This clears active-cleaning semantics only; an unresolved GracePeriodExpired target may remain retained for a later confirmed force escalation.
    /// </summary>
    void DetachProcess();

    /// <summary>
    /// Two-stage stop. First call: sets stop-requested flag, requests graceful termination,
    /// returns GracePeriodExpired when graceful close did not exit within 2.5s. Second call
    /// during grace: escalates to <see cref="ForceStopAsync" /> (Phase 5 D-01..D-05 locks).
    /// </summary>
    Task<StopCleaningResult> StopAsync();

    /// <summary>
    /// Performs the confirmed force-stop path. Uses the active process first, then any retained pending target from GracePeriodExpired,
    /// and returns a terminal result instead of reusing cached GracePeriodExpired after confirmation.
    /// </summary>
    Task<StopCleaningResult> ForceStopAsync();

    /// <summary>User declined the force-terminate prompt (Phase 5 D-03).</summary>
    StopCleaningResult MarkLeftRunningByUser();

    /// <summary>
    /// Resets per-session termination state for a new cleaning session: stop-requested flag, UI terminating state,
    /// last termination result, hang monitor state, active process, and any stale unresolved pending force target.
    /// Session-finalization cleanup must not call this before the user resolves a visible GracePeriodExpired prompt.
    /// </summary>
    void ResetForNewSession();

    /// <summary>
    /// Cleans up active session state after a cleaning run completes while preserving an unresolved
    /// <see cref="TerminationResult.GracePeriodExpired" /> pending force-escalation target until the user resolves it or the next-session reset clears it.
    /// </summary>
    void CompleteSessionFinalization();

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
