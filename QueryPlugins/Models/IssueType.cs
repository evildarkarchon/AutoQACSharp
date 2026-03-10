namespace QueryPlugins.Models;

/// <summary>
/// Categories of plugin issues that can be detected via Mutagen analysis.
/// These correspond to the same issue types that xEdit's Quick Auto Clean identifies.
/// </summary>
public enum IssueType
{
    /// <summary>
    /// Identical to Master — an override record that is byte-for-byte identical to the
    /// version it overrides in its master. These records have no effect and can be removed.
    /// </summary>
    ItmRecord,

    /// <summary>
    /// Deleted Reference — a placed object (REFR/ACHR or game equivalent) whose Deleted
    /// flag is set. The engine handles these poorly; the standard fix is to undelete and
    /// disable them instead.
    /// </summary>
    DeletedReference,

    /// <summary>
    /// Deleted Navigation Mesh — a NAVM record whose Deleted flag is set. Deleted navmeshes
    /// can cause engine crashes and AI pathfinding failures.
    /// </summary>
    DeletedNavmesh,
}
