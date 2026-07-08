using AutoQAC.Models;

namespace AutoQAC.Services.Cleaning;

/// <summary>
/// Structured result returned from stop requests so UI logic can use a stable post-await snapshot.
/// </summary>
/// <param name="TerminationResult">The low-level termination outcome, if a process was observed.</param>
/// <param name="MayStillBeRunning">True when xEdit may still be active and log parsing must remain conservative.</param>
/// <param name="Message">Optional human-readable status details.</param>
public sealed record StopCleaningResult(
    TerminationResult? TerminationResult,
    bool MayStillBeRunning,
    string? Message = null);
