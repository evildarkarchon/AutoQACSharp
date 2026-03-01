using System.Collections.Generic;

namespace AutoQAC.Models;

/// <summary>
/// Typed status for plugin loading operations.
/// </summary>
public enum PluginLoadingStatus
{
    Success,
    UnsupportedGame,
    DataFolderNotFound,
    NoPluginsDiscovered,
    Failed
}

/// <summary>
/// Result payload for plugin loading operations.
/// </summary>
public sealed record PluginLoadingResult
{
    public required PluginLoadingStatus Status { get; init; }
    public required IReadOnlyList<PluginInfo> Plugins { get; init; }
    public string? DataFolder { get; init; }
    public string? FailureReason { get; init; }
}
