using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameDetection;

namespace AutoQAC.Services.Plugin;

/// <summary>
/// Variant-aware Skip list policy shared by Plugin refresh and Cleaning session preflight.
/// </summary>
public sealed class SkipListPolicy(
    IConfigurationService configurationService,
    IGameDetectionService gameDetectionService)
    : ISkipListPolicy
{
    /// <inheritdoc />
    public async Task<SkipListEvaluation> EvaluateAsync(
        GameType gameType,
        IReadOnlyList<PluginInfo> plugins,
        bool disableSkipLists,
        CancellationToken ct = default)
    {
        var pluginNames = plugins.Select(plugin => plugin.FileName).ToList();
        var variant = gameDetectionService.DetectVariant(gameType, pluginNames);

        HashSet<string>? skipSet = null;
        if (!disableSkipLists)
        {
            var skipList = await configurationService.GetSkipListAsync(gameType, variant, ct)
                .ConfigureAwait(false);
            skipSet = new HashSet<string>(skipList, StringComparer.OrdinalIgnoreCase);
        }

        var decisions = plugins.Select(plugin =>
        {
            var isInEffectiveSkipList = skipSet?.Contains(plugin.FileName) ?? false;
            var enrichedPlugin = plugin with
            {
                // Disabled Skip lists should not leave stale hidden-row state behind.
                IsInSkipList = isInEffectiveSkipList,
                DetectedGameType = gameType
            };

            return new SkipListPluginDecision(
                enrichedPlugin,
                IsInEffectiveSkipList: isInEffectiveSkipList,
                ShouldSkipByPolicy: isInEffectiveSkipList);
        }).ToList();

        return new SkipListEvaluation(variant, decisions);
    }
}
