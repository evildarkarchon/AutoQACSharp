namespace AutoQAC.Models;

/// <summary>
/// Indicates whether a plugin will be cleaned or skipped during a dry-run preview.
/// </summary>
public enum DryRunStatus
{
    WillClean,
    WillSkip
}

/// <summary>
/// Describes the outcome of a single plugin in a dry-run preview.
/// Contains the plugin name, whether it will be cleaned or skipped, and the reason.
/// </summary>
public sealed record DryRunResult(string PluginName, DryRunStatus Status, string Reason);
