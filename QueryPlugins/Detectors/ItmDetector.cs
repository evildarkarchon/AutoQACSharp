using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using QueryPlugins.Models;

namespace QueryPlugins.Detectors;

/// <summary>
/// Game-agnostic ITM (Identical to Master) detector. Works on any <see cref="IModGetter"/>
/// using Mutagen's <see cref="ILinkCache.ResolveAllSimpleContexts"/> and the deep equality
/// provided by Loqui-generated <c>Equals(object)</c> overrides on every record class.
/// </summary>
public sealed class ItmDetector : IItmDetector
{
    /// <inheritdoc />
    public IEnumerable<PluginIssue> FindItmRecords(IModGetter plugin, ILinkCache linkCache)
    {
        var pluginModKey = plugin.ModKey;

        foreach (var record in plugin.EnumerateMajorRecords())
        {
            // New records defined in this plugin cannot be ITMs — they have no master to be identical to.
            if (record.FormKey.ModKey == pluginModKey)
                continue;

            // Deleted records are a separate issue (UDR / deleted navmesh). Exclude them here
            // to avoid false positives, since a deleted flag change makes the record non-identical
            // to a non-deleted master anyway.
            if (record.IsDeleted)
                continue;

            var formLinkInfo = FormLinkInformation.Factory(record);
            var allContexts = linkCache.ResolveAllSimpleContexts(formLinkInfo).ToArray();

            // Need at least two versions: this plugin's override and a master to compare against.
            if (allContexts.Length < 2)
                continue;

            // Mutagen returns contexts in winner-first order, so the analyzed plugin may appear
            // at any index in a full load-order cache. Compare the plugin's version to the
            // next lower-priority context, which is the record it actually overrides.
            var pluginContextIndex = Array.FindIndex(allContexts, context => context.ModKey == pluginModKey);
            if (pluginContextIndex < 0 || pluginContextIndex == allContexts.Length - 1)
                continue;

            // Deep equality via Loqui-generated Equals(object) override.
            // Both records are the same concrete type (resolved from the same FormKey),
            // so virtual dispatch correctly reaches the type-specific FooCommon.Equals().
            if (allContexts[pluginContextIndex].Record.Equals(allContexts[pluginContextIndex + 1].Record))
            {
                yield return new PluginIssue(record.FormKey, record.EditorID, IssueType.ItmRecord);
            }
        }
    }
}
