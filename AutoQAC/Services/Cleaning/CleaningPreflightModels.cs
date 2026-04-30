using System.Collections.Generic;
using AutoQAC.Models;

namespace AutoQAC.Services.Cleaning;

/// <summary>
/// Immutable result of the shared cleaning preflight pipeline.
/// </summary>
public sealed record CleaningPreflightPlan
{
    /// <summary>Detected game type after executable/load-order fallback detection.</summary>
    public required GameType DetectedGameType { get; init; }

    /// <summary>Detected game variant used for variant-aware skip-list loading.</summary>
    public required GameVariant DetectedGameVariant { get; init; }

    /// <summary>Per-plugin clean/skip rows shared by real cleaning and dry-run preview.</summary>
    public required IReadOnlyList<PreflightPluginRow> PluginRows { get; init; }

    /// <summary>True when MO2 launch mode is active for this preflight plan.</summary>
    public required bool IsMo2ModeActive { get; init; }

    /// <summary>True when backups are skipped by policy for this plan.</summary>
    public required bool BackupSkippedByPolicy { get; init; }

    /// <summary>True when plugin file validation is skipped because MO2 VFS resolves paths at runtime.</summary>
    public required bool FileValidationSkippedByPolicy { get; init; }

    /// <summary>User-facing launch mode label used by downstream cleaning result messages.</summary>
    public required string LaunchModeLabel { get; init; }

    /// <summary>Cleaning timeout in seconds from the current runtime state.</summary>
    public required int CleaningTimeoutSeconds { get; init; }

    /// <summary>True when user configuration enables backup before cleaning.</summary>
    public required bool BackupEnabled { get; init; }

    /// <summary>Maximum retained backup sessions from user configuration.</summary>
    public required int BackupMaxSessions { get; init; }

    /// <summary>
    /// xEdit install directory computed once during preflight; the runner captures log offsets
    /// and the finalizer reads logs from this single source of truth (R-08).
    /// </summary>
    public required string XEditDirectory { get; init; }
}

/// <summary>
/// One plugin's preflight decision and optional skip reason.
/// </summary>
public sealed record PreflightPluginRow(PluginInfo Plugin, PreflightDecision Decision, PreflightSkipReason? SkipReason);

/// <summary>
/// Describes whether preflight selected a plugin for cleaning or skipped it.
/// </summary>
public enum PreflightDecision
{
    /// <summary>The plugin is ready to be cleaned.</summary>
    Clean,

    /// <summary>The plugin is skipped and carries a specific skip reason.</summary>
    Skip
}

/// <summary>
/// Stable skip reason vocabulary shared by dry-run and real-run preflight selection.
/// </summary>
public enum PreflightSkipReason
{
    /// <summary>The plugin was explicitly deselected in the UI.</summary>
    NotSelected,

    /// <summary>The plugin name matched the configured skip list.</summary>
    InSkipList,

    /// <summary>The plugin file was not found on disk.</summary>
    FileNotFound,

    /// <summary>The plugin file exists but cannot be read.</summary>
    Unreadable,

    /// <summary>The plugin file exists but is empty.</summary>
    ZeroByte,

    /// <summary>The load-order entry could not be parsed as a valid plugin file name.</summary>
    MalformedEntry,

    /// <summary>The plugin file extension is not supported for cleaning.</summary>
    InvalidExtension
}
