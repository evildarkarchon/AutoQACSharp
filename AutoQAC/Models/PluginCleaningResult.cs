using System;

namespace AutoQAC.Models;

/// <summary>
/// Represents the detailed result of cleaning a single plugin.
/// Combines plugin information with cleaning outcome and statistics.
/// </summary>
public sealed record PluginCleaningResult
{
    /// <summary>
    /// The name of the plugin file.
    /// </summary>
    public required string PluginName { get; init; }

    /// <summary>
    /// The cleaning status (Cleaned, Skipped, Failed).
    /// </summary>
    public CleaningStatus Status { get; init; }

    /// <summary>
    /// Whether the cleaning operation was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// A human-readable message describing the result.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// How long the cleaning operation took.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Detailed statistics about what was cleaned (ITMs, UDRs, etc.).
    /// Null if the plugin was skipped or failed before parsing.
    /// </summary>
    public CleaningStatistics? Statistics { get; init; }

    /// <summary>
    /// Gets the number of ITMs (Identical To Master) records removed.
    /// </summary>
    public int ItemsRemoved => Statistics?.ItemsRemoved ?? 0;

    /// <summary>
    /// Gets the number of UDRs (Undeleted References) fixed.
    /// </summary>
    public int ItemsUndeleted => Statistics?.ItemsUndeleted ?? 0;

    /// <summary>
    /// Gets the number of records skipped during cleaning.
    /// </summary>
    public int ItemsSkipped => Statistics?.ItemsSkipped ?? 0;

    /// <summary>
    /// Gets the number of partial forms created.
    /// </summary>
    public int PartialFormsCreated => Statistics?.PartialFormsCreated ?? 0;

    /// <summary>
    /// Gets the total number of records processed.
    /// </summary>
    public int TotalProcessed => ItemsRemoved + ItemsUndeleted + ItemsSkipped + PartialFormsCreated;

    /// <summary>
    /// Warning message when log file parsing fails (missing, stale, or unreadable).
    /// Null when log parsing succeeded or was not attempted.
    /// </summary>
    public string? LogParseWarning { get; init; }

    /// <summary>
    /// Whether a log parse warning exists for this plugin.
    /// </summary>
    public bool HasLogParseWarning => !string.IsNullOrEmpty(LogParseWarning);

    /// <summary>
    /// Gets a short summary string for display.
    /// </summary>
    public string Summary
    {
        get
        {
            if (Status == CleaningStatus.Skipped)
                return "Skipped";

            if (Status == CleaningStatus.Failed)
                return $"Failed: {Message}";

            if (TotalProcessed == 0)
                return "No changes";

            var parts = new System.Collections.Generic.List<string>();
            if (ItemsRemoved > 0) parts.Add($"{ItemsRemoved} ITMs");
            if (ItemsUndeleted > 0) parts.Add($"{ItemsUndeleted} UDRs");
            if (PartialFormsCreated > 0) parts.Add($"{PartialFormsCreated} partial");

            return parts.Count > 0 ? string.Join(", ", parts) : "Cleaned";
        }
    }
}
