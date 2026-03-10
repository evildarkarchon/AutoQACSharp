using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using QueryPlugins.Models;

namespace QueryPlugins.Detectors;

/// <summary>
/// Detects Identical to Master (ITM) records in a plugin using Mutagen's link cache.
/// ITM detection is game-agnostic — it works on any <see cref="IModGetter"/> via
/// the generic <see cref="IModGetter.EnumerateMajorRecords"/> enumeration and deep
/// equality comparison through Mutagen's generated Equals implementations.
/// </summary>
public interface IItmDetector
{
    /// <summary>
    /// Scans <paramref name="plugin"/> for records that are identical to the version
    /// they override in the preceding load order.
    /// </summary>
    /// <param name="plugin">
    /// The plugin to analyse. It must be present in <paramref name="linkCache"/> so each
    /// overridden record can be matched to this plugin's position in the load order.
    /// </param>
    /// <param name="linkCache">
    /// A link cache built from the full load order containing <paramref name="plugin"/> and
    /// its related overrides/masters. Each candidate record is compared to the immediately
    /// lower-priority context for that FormKey. The caller owns the cache lifetime.
    /// </param>
    /// <returns>One <see cref="PluginIssue"/> per ITM record found.</returns>
    IEnumerable<PluginIssue> FindItmRecords(IModGetter plugin, ILinkCache linkCache);
}
