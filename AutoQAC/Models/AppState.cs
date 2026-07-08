using System.Collections.Generic;
using System.Collections.Frozen;
using System.Linq;

namespace AutoQAC.Models;

/// <summary>
/// Identifies the non-xEdit file operation currently shown in the cleaning progress surface.
/// </summary>
public enum BackupOperationKind
{
    /// <summary>A plugin backup copy is active before the plugin's xEdit launch.</summary>
    Backup,

    /// <summary>Old backup-session retention cleanup is active after plugin cleaning.</summary>
    RetentionCleanup
}

/// <summary>
/// Describes visible progress for backup or retention work that must not reuse xEdit stop state.
/// </summary>
public sealed record BackupOperationState
{
    /// <summary>The kind of non-xEdit file operation currently active.</summary>
    public BackupOperationKind Kind { get; init; }

    /// <summary>User-facing phase label such as <c>Backing up: Plugin.esp</c>.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>The current file name when byte progress is available; null for count-only cleanup.</summary>
    public string? FileName { get; init; }

    /// <summary>Number of files or sessions completed by the operation.</summary>
    public int FilesCompleted { get; init; }

    /// <summary>Total files or sessions when known.</summary>
    public int? TotalFiles { get; init; }

    /// <summary>Bytes copied for the current file when known.</summary>
    public long BytesCopied { get; init; }

    /// <summary>Total bytes for the current file when known.</summary>
    public long? TotalBytes { get; init; }

    /// <summary>Whether the operation is currently running.</summary>
    public bool IsActive { get; init; }

    /// <summary>Whether the UI may offer a non-xEdit cancel affordance for this operation.</summary>
    public bool CanCancel { get; init; }
}

public sealed record AppState
{
    // Configuration paths
    public string? LoadOrderPath { get; init; }
    public string? Mo2ExecutablePath { get; init; }
    public string? Mo2Profile { get; init; }
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
    /// Progress for backup or retention file operations that are separate from xEdit process control.
    /// </summary>
    public BackupOperationState? BackupOperation { get; init; }

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
