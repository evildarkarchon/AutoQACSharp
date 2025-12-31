using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoQAC.Models;

/// <summary>
/// Represents the complete results of a cleaning session (all plugins processed).
/// </summary>
public sealed record CleaningSessionResult
{
    /// <summary>
    /// When the cleaning session started.
    /// </summary>
    public DateTime StartTime { get; init; }

    /// <summary>
    /// When the cleaning session finished.
    /// </summary>
    public DateTime EndTime { get; init; }

    /// <summary>
    /// Total duration of the cleaning session.
    /// </summary>
    public TimeSpan TotalDuration => EndTime - StartTime;

    /// <summary>
    /// The game type that was cleaned.
    /// </summary>
    public GameType GameType { get; init; }

    /// <summary>
    /// Whether the session was cancelled by the user.
    /// </summary>
    public bool WasCancelled { get; init; }

    /// <summary>
    /// Detailed results for each plugin processed.
    /// </summary>
    public IReadOnlyList<PluginCleaningResult> PluginResults { get; init; } = Array.Empty<PluginCleaningResult>();

    /// <summary>
    /// Gets plugins that were successfully cleaned.
    /// </summary>
    public IEnumerable<PluginCleaningResult> CleanedPlugins =>
        PluginResults.Where(r => r.Status == CleaningStatus.Cleaned);

    /// <summary>
    /// Gets plugins that failed to clean.
    /// </summary>
    public IEnumerable<PluginCleaningResult> FailedPlugins =>
        PluginResults.Where(r => r.Status == CleaningStatus.Failed);

    /// <summary>
    /// Gets plugins that were skipped.
    /// </summary>
    public IEnumerable<PluginCleaningResult> SkippedPlugins =>
        PluginResults.Where(r => r.Status == CleaningStatus.Skipped);

    /// <summary>
    /// Total number of plugins processed.
    /// </summary>
    public int TotalPlugins => PluginResults.Count;

    /// <summary>
    /// Number of plugins successfully cleaned.
    /// </summary>
    public int CleanedCount => CleanedPlugins.Count();

    /// <summary>
    /// Number of plugins that failed.
    /// </summary>
    public int FailedCount => FailedPlugins.Count();

    /// <summary>
    /// Number of plugins that were skipped.
    /// </summary>
    public int SkippedCount => SkippedPlugins.Count();

    /// <summary>
    /// Total ITMs removed across all plugins.
    /// </summary>
    public int TotalItemsRemoved => PluginResults.Sum(r => r.ItemsRemoved);

    /// <summary>
    /// Total UDRs fixed across all plugins.
    /// </summary>
    public int TotalItemsUndeleted => PluginResults.Sum(r => r.ItemsUndeleted);

    /// <summary>
    /// Total partial forms created across all plugins.
    /// </summary>
    public int TotalPartialFormsCreated => PluginResults.Sum(r => r.PartialFormsCreated);

    /// <summary>
    /// Whether the session completed successfully (no failures, not cancelled).
    /// </summary>
    public bool IsSuccess => !WasCancelled && FailedCount == 0;

    /// <summary>
    /// Gets a summary string for the entire session.
    /// </summary>
    public string SessionSummary
    {
        get
        {
            if (WasCancelled)
                return $"Cancelled after {CleanedCount} of {TotalPlugins} plugins";

            if (FailedCount > 0)
                return $"Completed with errors: {CleanedCount} cleaned, {FailedCount} failed, {SkippedCount} skipped";

            return $"Completed: {CleanedCount} cleaned, {SkippedCount} skipped";
        }
    }

    /// <summary>
    /// Creates an empty session result for design-time use.
    /// </summary>
    public static CleaningSessionResult CreateEmpty() => new()
    {
        StartTime = DateTime.Now,
        EndTime = DateTime.Now,
        GameType = GameType.Unknown,
        WasCancelled = false,
        PluginResults = Array.Empty<PluginCleaningResult>()
    };

    /// <summary>
    /// Generates a detailed report suitable for logging or export.
    /// </summary>
    public string GenerateReport()
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("=== AutoQAC Cleaning Report ===");
        sb.AppendLine($"Date: {StartTime:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Game: {GameType}");
        sb.AppendLine($"Duration: {TotalDuration:hh\\:mm\\:ss}");
        sb.AppendLine();

        sb.AppendLine("--- Summary ---");
        sb.AppendLine($"Total Plugins: {TotalPlugins}");
        sb.AppendLine($"Cleaned: {CleanedCount}");
        sb.AppendLine($"Skipped: {SkippedCount}");
        sb.AppendLine($"Failed: {FailedCount}");
        sb.AppendLine();

        sb.AppendLine("--- Statistics ---");
        sb.AppendLine($"ITMs Removed: {TotalItemsRemoved}");
        sb.AppendLine($"UDRs Fixed: {TotalItemsUndeleted}");
        if (TotalPartialFormsCreated > 0)
            sb.AppendLine($"Partial Forms: {TotalPartialFormsCreated}");
        sb.AppendLine();

        if (CleanedPlugins.Any())
        {
            sb.AppendLine("--- Cleaned Plugins ---");
            foreach (var result in CleanedPlugins)
            {
                sb.AppendLine($"  {result.PluginName}: {result.Summary} ({result.Duration:mm\\:ss})");
            }
            sb.AppendLine();
        }

        if (SkippedPlugins.Any())
        {
            sb.AppendLine("--- Skipped Plugins ---");
            foreach (var result in SkippedPlugins)
            {
                sb.AppendLine($"  {result.PluginName}");
            }
            sb.AppendLine();
        }

        if (FailedPlugins.Any())
        {
            sb.AppendLine("--- Failed Plugins ---");
            foreach (var result in FailedPlugins)
            {
                sb.AppendLine($"  {result.PluginName}: {result.Message}");
            }
            sb.AppendLine();
        }

        if (WasCancelled)
        {
            sb.AppendLine("*** Session was cancelled by user ***");
        }

        return sb.ToString();
    }
}
