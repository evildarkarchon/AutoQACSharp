using System.Collections.Generic;

namespace AutoQAC.Models;

public sealed record AppState
{
    // Configuration paths
    public string? LoadOrderPath { get; init; }
    public string? Mo2ExecutablePath { get; init; }
    public string? XEditExecutablePath { get; init; }

    // Configuration validity
    public bool IsLoadOrderConfigured => !string.IsNullOrEmpty(LoadOrderPath);
    public bool IsMo2Configured => !string.IsNullOrEmpty(Mo2ExecutablePath);
    public bool IsXEditConfigured => !string.IsNullOrEmpty(XEditExecutablePath);

    // Runtime state
    public bool IsCleaning { get; init; }
    public string? CurrentPlugin { get; init; }
    public string? CurrentOperation { get; init; }

    // Progress
    public int Progress { get; init; }
    public int TotalPlugins { get; init; }
    public List<PluginInfo> PluginsToClean { get; init; } = new();

    // Results
    public HashSet<string> CleanedPlugins { get; init; } = new();
    public HashSet<string> FailedPlugins { get; init; } = new();
    public HashSet<string> SkippedPlugins { get; init; } = new();

    // Settings
    public int CleaningTimeout { get; init; } = 300;
    public bool Mo2ModeEnabled { get; init; }
    public bool PartialFormsEnabled { get; init; }
    public GameType CurrentGameType { get; init; } = GameType.Unknown;
}
