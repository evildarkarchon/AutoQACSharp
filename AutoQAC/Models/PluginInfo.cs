namespace AutoQAC.Models;

public sealed record PluginInfo
{
    public required string FileName { get; init; }
    public required string FullPath { get; init; }
    public bool IsInSkipList { get; init; }
    public GameType DetectedGameType { get; init; }

    /// <summary>
    /// Whether this plugin is selected for cleaning. Defaults to true.
    /// Uses set (not init) to allow reactive UI binding.
    /// </summary>
    public bool IsSelected { get; set; } = true;
}
