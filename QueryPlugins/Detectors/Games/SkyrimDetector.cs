using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using QueryPlugins.Models;

namespace QueryPlugins.Detectors.Games;

/// <summary>
/// Game-specific detector for Skyrim (LE, SE, VR) and Enderal variants.
/// Detects deleted placed references (REFR/ACHR and equivalents) and deleted
/// navigation mesh records (NAVM) using Mutagen's typed record enumeration.
/// </summary>
public sealed class SkyrimDetector : IGameSpecificDetector
{
    /// <inheritdoc />
    public IReadOnlySet<GameRelease> SupportedReleases { get; } = new HashSet<GameRelease>
    {
        GameRelease.SkyrimLE,
        GameRelease.SkyrimSE,
        GameRelease.SkyrimSEGog,
        GameRelease.SkyrimVR,
        GameRelease.EnderalLE,
        GameRelease.EnderalSE,
    };

    /// <inheritdoc />
    public IEnumerable<PluginIssue> FindDeletedReferences(IModGetter plugin)
    {
        if (plugin is not ISkyrimModGetter skyrimMod)
            throw new ArgumentException(
                $"Expected an {nameof(ISkyrimModGetter)} but received {plugin.GetType().Name}.",
                nameof(plugin));

        return skyrimMod.EnumerateMajorRecords<IPlacedGetter>()
            .Where(placed => placed.IsDeleted)
            .Select(placed => new PluginIssue(
                placed.FormKey,
                placed.EditorID,
                IssueType.DeletedReference));
    }

    /// <inheritdoc />
    public IEnumerable<PluginIssue> FindDeletedNavmeshes(IModGetter plugin)
    {
        if (plugin is not ISkyrimModGetter skyrimMod)
            throw new ArgumentException(
                $"Expected an {nameof(ISkyrimModGetter)} but received {plugin.GetType().Name}.",
                nameof(plugin));

        return skyrimMod.EnumerateMajorRecords<INavigationMeshGetter>()
            .Where(navm => navm.IsDeleted)
            .Select(navm => new PluginIssue(
                navm.FormKey,
                navm.EditorID,
                IssueType.DeletedNavmesh));
    }
}
