using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using QueryPlugins.Models;

namespace QueryPlugins.Detectors.Games;

/// <summary>
/// Game-specific detector for Starfield.
/// Detects deleted placed references and deleted navigation mesh records.
/// </summary>
public sealed class StarfieldDetector : IGameSpecificDetector
{
    /// <inheritdoc />
    public IReadOnlySet<GameRelease> SupportedReleases { get; } = new HashSet<GameRelease>
    {
        GameRelease.Starfield,
    };

    /// <inheritdoc />
    public IEnumerable<PluginIssue> FindDeletedReferences(IModGetter plugin)
    {
        if (plugin is not IStarfieldModGetter starfieldMod)
            throw new ArgumentException(
                $"Expected an {nameof(IStarfieldModGetter)} but received {plugin.GetType().Name}.",
                nameof(plugin));

        return starfieldMod.EnumerateMajorRecords<IPlacedGetter>()
            .Where(placed => placed.IsDeleted)
            .Select(placed => new PluginIssue(
                placed.FormKey,
                placed.EditorID,
                IssueType.DeletedReference));
    }

    /// <inheritdoc />
    public IEnumerable<PluginIssue> FindDeletedNavmeshes(IModGetter plugin)
    {
        if (plugin is not IStarfieldModGetter starfieldMod)
            throw new ArgumentException(
                $"Expected an {nameof(IStarfieldModGetter)} but received {plugin.GetType().Name}.",
                nameof(plugin));

        return starfieldMod.EnumerateMajorRecords<INavigationMeshGetter>()
            .Where(navm => navm.IsDeleted)
            .Select(navm => new PluginIssue(
                navm.FormKey,
                navm.EditorID,
                IssueType.DeletedNavmesh));
    }
}
