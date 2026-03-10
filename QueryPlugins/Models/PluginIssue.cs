using Mutagen.Bethesda.Plugins;

namespace QueryPlugins.Models;

/// <summary>
/// Represents a single issue found in a plugin during Mutagen-based analysis.
/// </summary>
/// <param name="FormKey">The unique record identifier (includes originating mod name).</param>
/// <param name="EditorID">The human-readable editor ID, if present on the record.</param>
/// <param name="Type">The category of issue detected.</param>
public sealed record PluginIssue(
    FormKey FormKey,
    string? EditorID,
    IssueType Type);
