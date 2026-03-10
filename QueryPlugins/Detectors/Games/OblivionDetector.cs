using Mutagen.Bethesda;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins.Records;
using QueryPlugins.Models;

namespace QueryPlugins.Detectors.Games;

/// <summary>
/// Game-specific detector for The Elder Scrolls IV: Oblivion and Oblivion Remastered.
/// Detects deleted placed references. Oblivion uses PathGrids rather than Navigation Meshes,
/// so <see cref="FindDeletedNavmeshes"/> always returns an empty sequence.
/// PathGrid support may be added in a future iteration.
/// </summary>
public sealed class OblivionDetector : IGameSpecificDetector
{
    /// <inheritdoc />
    public IReadOnlySet<GameRelease> SupportedReleases { get; } = new HashSet<GameRelease>
    {
        GameRelease.Oblivion,
        GameRelease.OblivionRE,
    };

    /// <inheritdoc />
    public IEnumerable<PluginIssue> FindDeletedReferences(IModGetter plugin)
    {
        if (plugin is not IOblivionModGetter oblivionMod)
            throw new ArgumentException(
                $"Expected an {nameof(IOblivionModGetter)} but received {plugin.GetType().Name}.",
                nameof(plugin));

        return oblivionMod.EnumerateMajorRecords<IPlacedGetter>()
            .Where(placed => placed.IsDeleted)
            .Select(placed => new PluginIssue(
                placed.FormKey,
                placed.EditorID,
                IssueType.DeletedReference));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Oblivion does not have Navigation Mesh records (it uses PathGrids). This method
    /// returns an empty sequence. PathGrid deletion detection is deferred to a future iteration.
    /// </remarks>
    public IEnumerable<PluginIssue> FindDeletedNavmeshes(IModGetter plugin) =>
        Enumerable.Empty<PluginIssue>();
}
