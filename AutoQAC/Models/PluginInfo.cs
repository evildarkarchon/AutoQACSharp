namespace AutoQAC.Models;

/// <summary>
/// Describes the result of validating a plugin file on disk.
/// </summary>
public enum PluginWarningKind
{
    None,
    NotFound,
    Unreadable,
    ZeroByte,
    MalformedEntry,
    InvalidExtension
}

public sealed record PluginInfo
{
    public required string FileName { get; init; }
    public required string FullPath { get; init; }
    public bool IsInSkipList { get; init; }
    public GameType DetectedGameType { get; init; }
    public PluginIssueApproximation Approximation { get; init; } = PluginIssueApproximation.Unavailable;

    /// <summary>
    /// Optional validation result from ValidatePluginFile.
    /// </summary>
    public PluginWarningKind Warning { get; init; } = PluginWarningKind.None;

    /// <summary>
    /// Whether this plugin is selected for cleaning. Defaults to true.
    /// Mutation is only supported on the UI thread (checkbox binding lifecycle).
    /// Background/services must treat this as read-only snapshot data.
    /// </summary>
    public bool IsSelected { get; set; } = true;

    public bool HasApproximationPreview => Approximation.Status == PluginIssueApproximationStatus.Available;
    public bool IsApproximationPending => Approximation.Status == PluginIssueApproximationStatus.Pending;

    public string ApproximationDisplayText => Approximation.Status switch
    {
        PluginIssueApproximationStatus.Available =>
            $"Approx. ITM {Approximation.ItmCount} | UDR {Approximation.DeletedReferenceCount} | Nav {Approximation.DeletedNavmeshCount}",
        PluginIssueApproximationStatus.Pending => "Analyzing preview...",
        _ => "Preview unavailable"
    };
}
