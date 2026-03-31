using System.Collections.Generic;

namespace AutoQAC.Models;

/// <summary>
/// Result of reading xEdit log files after a cleaning run.
/// Contains lines from the main log appended during this session,
/// any exception log content, and optional warning information.
/// </summary>
public sealed record LogReadResult
{
    /// <summary>Lines from the main log file appended during this xEdit run.</summary>
    public required List<string> LogLines { get; init; }

    /// <summary>Content from the exception log appended during this run, or null if no new exceptions.</summary>
    public string? ExceptionContent { get; init; }

    /// <summary>Warning message if log reading had issues but partially succeeded.</summary>
    public string? Warning { get; init; }
}
