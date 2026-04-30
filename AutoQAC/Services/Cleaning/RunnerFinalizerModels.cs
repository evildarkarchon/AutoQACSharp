using System;
using AutoQAC.Models;

namespace AutoQAC.Services.Cleaning;

/// <summary>
/// Structured output from <see cref="IPluginCleaningRunner" /> for final result construction.
/// </summary>
public sealed record PluginRunnerOutput
{
    /// <summary>Result returned by the final cleaning attempt.</summary>
    public required CleaningResult LastAttemptResult { get; init; }

    /// <summary>Number of attempts made for the plugin.</summary>
    public required int AttemptCount { get; init; }

    /// <summary>Main xEdit log offset captured before the final attempt.</summary>
    public required long MainLogOffset { get; init; }

    /// <summary>Exception xEdit log offset captured before the final attempt.</summary>
    public required long ExceptionLogOffset { get; init; }

    /// <summary>Total elapsed time for the runner's per-plugin work.</summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>True when the last attempt timed out after reaching the retry ceiling.</summary>
    public required bool ReachedMaxRetryAttempts { get; init; }
}

/// <summary>
/// Termination-state snapshot consumed by result finalization after the runner detaches the process.
/// </summary>
/// <param name="ProcessMayStillBeRunning">True when log reading is unsafe because xEdit may still be running.</param>
/// <param name="StopWasRequested">True when the user requested a stop in the current session.</param>
public sealed record TerminationFinalizeContext(
    bool ProcessMayStillBeRunning,
    bool StopWasRequested);
