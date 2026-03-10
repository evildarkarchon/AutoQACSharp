namespace QueryPlugins.Models;

/// <summary>
/// Aggregated result of a full plugin analysis run covering ITM records,
/// deleted references, and deleted navmeshes.
/// </summary>
public sealed class PluginAnalysisResult
{
    /// <summary>All issues found across all detectors, in discovery order.</summary>
    public IReadOnlyList<PluginIssue> Issues { get; }

    /// <summary>Number of Identical to Master records detected.</summary>
    public int ItmCount { get; }

    /// <summary>Number of deleted placed references (REFR/ACHR equivalents) detected.</summary>
    public int DeletedReferenceCount { get; }

    /// <summary>Number of deleted navigation mesh records detected.</summary>
    public int DeletedNavmeshCount { get; }

    public PluginAnalysisResult(IReadOnlyList<PluginIssue> issues)
    {
        Issues = issues;
        ItmCount = issues.Count(i => i.Type == IssueType.ItmRecord);
        DeletedReferenceCount = issues.Count(i => i.Type == IssueType.DeletedReference);
        DeletedNavmeshCount = issues.Count(i => i.Type == IssueType.DeletedNavmesh);
    }

    /// <summary>True if no issues of any kind were found.</summary>
    public bool IsClean => Issues.Count == 0;
}
