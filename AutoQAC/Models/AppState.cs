using System.Collections.Generic;
using System.Collections.Frozen;
using System.Linq;

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
    public IReadOnlyList<PluginInfo> PluginsToClean { get; init; } = [];

    // Results
    public IReadOnlySet<string> CleanedPlugins { get; init; } = Enumerable.Empty<string>().ToFrozenSet();
    public IReadOnlySet<string> FailedPlugins { get; init; } = Enumerable.Empty<string>().ToFrozenSet();
    public IReadOnlySet<string> SkippedPlugins { get; init; } = Enumerable.Empty<string>().ToFrozenSet();

    // Settings
    public int CleaningTimeout { get; init; } = 300;
    public bool Mo2ModeEnabled { get; init; }
    public bool PartialFormsEnabled { get; init; }
    public GameType CurrentGameType { get; init; } = GameType.Unknown;
}
