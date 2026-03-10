using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins.Records;
using QueryPlugins.Models;

namespace QueryPlugins.Detectors;

/// <summary>
/// Game-specific detector for issues that require traversing game-specific record
/// hierarchies: deleted placed references (UDRs) and deleted navigation meshes.
/// Each implementation handles one or more closely related <see cref="GameRelease"/> values.
/// </summary>
public interface IGameSpecificDetector
{
    /// <summary>
    /// The set of <see cref="GameRelease"/> values this detector handles.
    /// </summary>
    IReadOnlySet<GameRelease> SupportedReleases { get; }

    /// <summary>
    /// Finds placed references (REFR/ACHR or game equivalents) whose Deleted flag is set.
    /// The mod is cast internally to the concrete game-specific type.
    /// </summary>
    /// <param name="plugin">The plugin to scan. Must match one of <see cref="SupportedReleases"/>.</param>
    /// <returns>One <see cref="PluginIssue"/> per deleted placed reference found.</returns>
    IEnumerable<PluginIssue> FindDeletedReferences(IModGetter plugin);

    /// <summary>
    /// Finds navigation mesh records (NAVM or game equivalents) whose Deleted flag is set.
    /// Games that do not have navigation meshes (e.g. Oblivion) return an empty sequence.
    /// </summary>
    /// <param name="plugin">The plugin to scan. Must match one of <see cref="SupportedReleases"/>.</param>
    /// <returns>One <see cref="PluginIssue"/> per deleted navmesh found.</returns>
    IEnumerable<PluginIssue> FindDeletedNavmeshes(IModGetter plugin);
}
