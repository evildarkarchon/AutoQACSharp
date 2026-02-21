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
}
