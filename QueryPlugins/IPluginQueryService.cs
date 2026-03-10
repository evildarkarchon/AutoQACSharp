using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using QueryPlugins.Models;

namespace QueryPlugins;

/// <summary>
/// Top-level service for analysing a plugin for all supported issue types:
/// ITM records, deleted placed references, and deleted navigation meshes.
/// </summary>
public interface IPluginQueryService
{
    /// <summary>
    /// Runs all detectors against <paramref name="plugin"/> and returns a consolidated result.
    /// </summary>
    /// <param name="plugin">
    /// The plugin to analyse. It must be present in <paramref name="linkCache"/>.
    /// </param>
    /// <param name="linkCache">
    /// A link cache built from the full load order containing <paramref name="plugin"/>.
    /// Required for ITM comparison against the immediately lower-priority version of each
    /// overridden record. The caller owns the cache lifetime.
    /// </param>
    /// <param name="gameRelease">
    /// The game release the plugin targets. Used to select the correct game-specific
    /// detector for UDR and navmesh traversal.
    /// </param>
    /// <returns>Aggregated analysis result containing all discovered issues.</returns>
    PluginAnalysisResult Analyse(IModGetter plugin, ILinkCache linkCache, GameRelease gameRelease);
}
