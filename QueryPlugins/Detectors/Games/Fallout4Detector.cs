using Mutagen.Bethesda;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins.Records;
using QueryPlugins.Models;

namespace QueryPlugins.Detectors.Games;

/// <summary>
/// Game-specific detector for Fallout 4 and Fallout 4 VR.
/// Detects deleted placed references and deleted navigation mesh records.
/// </summary>
public sealed class Fallout4Detector : IGameSpecificDetector
{
    /// <inheritdoc />
    public IReadOnlySet<GameRelease> SupportedReleases { get; } = new HashSet<GameRelease>
    {
        GameRelease.Fallout4,
        GameRelease.Fallout4VR,
    };

    /// <inheritdoc />
    public IEnumerable<PluginIssue> FindDeletedReferences(IModGetter plugin)
    {
        if (plugin is not IFallout4ModGetter fo4Mod)
            throw new ArgumentException(
                $"Expected an {nameof(IFallout4ModGetter)} but received {plugin.GetType().Name}.",
                nameof(plugin));

        return fo4Mod.EnumerateMajorRecords<IPlacedGetter>()
            .Where(placed => placed.IsDeleted)
            .Select(placed => new PluginIssue(
                placed.FormKey,
                placed.EditorID,
                IssueType.DeletedReference));
    }

    /// <inheritdoc />
    public IEnumerable<PluginIssue> FindDeletedNavmeshes(IModGetter plugin)
    {
        if (plugin is not IFallout4ModGetter fo4Mod)
            throw new ArgumentException(
                $"Expected an {nameof(IFallout4ModGetter)} but received {plugin.GetType().Name}.",
                nameof(plugin));

        return fo4Mod.EnumerateMajorRecords<INavigationMeshGetter>()
            .Where(navm => navm.IsDeleted)
            .Select(navm => new PluginIssue(
                navm.FormKey,
                navm.EditorID,
                IssueType.DeletedNavmesh));
    }
}
