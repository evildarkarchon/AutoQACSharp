using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;

namespace AutoQAC.Services.Plugin;

/// <summary>
/// Centralizes variant-aware Skip list decisions so Plugin refresh rows and Cleaning session preflight cannot drift.
/// </summary>
public interface ISkipListPolicy
{
    /// <summary>
    /// Detects the game variant, loads the effective Skip list unless disabled, and returns per-plugin decisions.
    /// </summary>
    /// <param name="gameType">Game context for Skip list lookup.</param>
    /// <param name="plugins">Plugin rows whose names determine variant and policy matches.</param>
    /// <param name="disableSkipLists">When true, plugins are not marked or skipped by Skip list policy.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Variant plus per-plugin policy decisions.</returns>
    Task<SkipListEvaluation> EvaluateAsync(
        GameType gameType,
        IReadOnlyList<PluginInfo> plugins,
        bool disableSkipLists,
        CancellationToken ct = default);
}

/// <summary>
/// Complete Skip list evaluation for one game context.
/// </summary>
/// <param name="Variant">Detected variant such as TTW or Enderal.</param>
/// <param name="Decisions">Per-plugin policy decisions with enriched rows.</param>
public sealed record SkipListEvaluation(
    GameVariant Variant,
    IReadOnlyList<SkipListPluginDecision> Decisions)
{
    /// <summary>
    /// The plugin rows after applying visible Skip list state.
    /// </summary>
    public IReadOnlyList<PluginInfo> Plugins => Decisions.Select(decision => decision.Plugin).ToList();
}

/// <summary>
/// Skip list decision for a single plugin row.
/// </summary>
/// <param name="Plugin">Plugin row enriched with current game and visible Skip list state.</param>
/// <param name="IsInEffectiveSkipList">True when the effective Skip list contains this plugin name.</param>
/// <param name="ShouldSkipByPolicy">True when the plugin should be excluded because Skip lists are enabled and matched.</param>
public sealed record SkipListPluginDecision(
    PluginInfo Plugin,
    bool IsInEffectiveSkipList,
    bool ShouldSkipByPolicy);
