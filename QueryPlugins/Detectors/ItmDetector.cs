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
    public IEnumerable<PluginIssue> FindItmRecords(IModGetter plugin, ILinkCache linkCache, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var pluginModKey = plugin.ModKey;

        if (!linkCache.ListedOrder.Any(mod => mod.ModKey == pluginModKey))
        {
            throw new ArgumentException(
                $"The supplied link cache does not contain analyzed plugin {pluginModKey}.",
                nameof(linkCache));
        }

        return EnumerateItmRecords(plugin, linkCache, pluginModKey, ct);
    }

    private static IEnumerable<PluginIssue> EnumerateItmRecords(
        IModGetter plugin,
        ILinkCache linkCache,
        ModKey pluginModKey,
        CancellationToken ct)
    {
        foreach (var record in plugin.EnumerateMajorRecords())
        {
            ct.ThrowIfCancellationRequested();

            // New records defined in this plugin cannot be ITMs — they have no master to be identical to.
            if (record.FormKey.ModKey == pluginModKey)
                continue;

            // Deleted records are a separate issue (UDR / deleted navmesh). Exclude them here
            // to avoid false positives, since a deleted flag change makes the record non-identical
            // to a non-deleted master anyway.
            if (record.IsDeleted)
                continue;

            var formLinkInfo = FormLinkInformation.Factory(record);

            if (IsIdenticalToImmediateLowerPriorityContext(linkCache, formLinkInfo, pluginModKey, record.FormKey, ct))
            {
                yield return new PluginIssue(record.FormKey, record.EditorID, IssueType.ItmRecord);
            }
        }
    }

    private static bool IsIdenticalToImmediateLowerPriorityContext(
        ILinkCache linkCache,
        FormLinkInformation formLinkInfo,
        ModKey pluginModKey,
        FormKey recordFormKey,
        CancellationToken ct)
    {
        IModContext<IMajorRecordGetter>? pluginContext = null;

        // Mutagen returns contexts in winner-first order, so the analyzed plugin may appear
        // anywhere in the load-order cache. Once found, only the next yielded context is the
        // immediate lower-priority version the plugin actually overrides; retaining more would
        // increase peak memory without changing exact ITM semantics.
        foreach (var context in linkCache.ResolveAllSimpleContexts(formLinkInfo))
        {
            ct.ThrowIfCancellationRequested();

            if (pluginContext is null)
            {
                if (context.ModKey == pluginModKey)
                {
                    pluginContext = context;
                }

                continue;
            }

            // Deep equality via Loqui-generated Equals(object) override.
            // Both records are the same concrete type (resolved from the same FormKey),
            // so virtual dispatch correctly reaches the type-specific FooCommon.Equals().
            return pluginContext.Record.Equals(context.Record);
        }

        if (pluginContext is null)
        {
            throw new ArgumentException(
                $"The supplied link cache does not contain analyzed plugin {pluginModKey} for record {recordFormKey}.",
                nameof(linkCache));
        }

        // The plugin is the lowest-priority context for this FormKey, so there is no lower-priority
        // overridden version to compare against.
        return false;
    }
}
