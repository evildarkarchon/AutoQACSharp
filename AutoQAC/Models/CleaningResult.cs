using System;

namespace AutoQAC.Models;

public sealed record CleaningResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public CleaningStatus Status { get; init; }
    public TimeSpan Duration { get; init; }
    public CleaningStatistics? Statistics { get; init; }
}

public enum CleaningStatus
{
    Cleaned,
    Skipped,
    Failed
}

public sealed record CleaningStatistics
{
    public int ItemsRemoved { get; init; }
    public int ItemsUndeleted { get; init; }
    public int ItemsSkipped { get; init; }
    public int PartialFormsCreated { get; init; }
}
