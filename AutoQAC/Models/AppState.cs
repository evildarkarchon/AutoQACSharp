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

    /// <summary>
    /// Full paths (case-insensitive) of plugins the user has explicitly deselected from
    /// the visible plugin list. Default is empty (everything selected). Storing exclusions
    /// rather than selections keeps the default behaviour stable as <see cref="PluginsToClean"/>
    /// is replaced by approximation merges. Path identity (rather than file name) prevents
    /// deselections leaking across game switches or load-order changes when two plugins
    /// happen to share a file name.
    /// </summary>
    public IReadOnlySet<string> ExcludedPluginPaths { get; init; } =
        Enumerable.Empty<string>().ToFrozenSet(System.StringComparer.OrdinalIgnoreCase);

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
