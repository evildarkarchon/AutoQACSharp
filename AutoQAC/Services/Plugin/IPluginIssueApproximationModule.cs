using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;

namespace AutoQAC.Services.Plugin;

/// <summary>
/// Describes one authoritative Issue approximation operation.
/// </summary>
/// <param name="GameType">Game whose existing Issue approximation capability rules apply.</param>
/// <param name="Source">Complete source context from which the load order is assembled.</param>
/// <param name="Targets">Ordered, unique Plugin refresh row identities to analyze.</param>
public sealed record PluginIssueApproximationModuleRequest(
    GameType GameType,
    PluginIssueApproximationModuleSource Source,
    IReadOnlyList<PluginRefreshRowKey> Targets);

/// <summary>
/// Identifies the complete load-order source used by an Issue approximation operation.
/// </summary>
public abstract record PluginIssueApproximationModuleSource
{
    /// <summary>
    /// Uses the game's effective load order rooted in a direct data folder.
    /// </summary>
    /// <param name="DataFolder">Game data folder containing the effective plugin files.</param>
    public sealed record DirectDataFolder(string DataFolder) : PluginIssueApproximationModuleSource;

    /// <summary>
    /// Uses an already resolved effective load order, such as an accepted MO2 conflict-winning view.
    /// </summary>
    /// <param name="BaseDataFolder">Base game data folder used for binary import context.</param>
    /// <param name="Rows">Complete ordered source rows, with one effective row per plugin filename.</param>
    public sealed record ResolvedLoadOrder(
        string BaseDataFolder,
        IReadOnlyList<PluginRefreshRowKey> Rows) : PluginIssueApproximationModuleSource;
}

/// <summary>
/// Carries one terminal Issue approximation back to its unchanged target row identity.
/// </summary>
/// <param name="Target">The exact target supplied by the request.</param>
/// <param name="Approximation">Terminal available or unavailable approximation.</param>
public sealed record PluginIssueApproximationModuleResult(
    PluginRefreshRowKey Target,
    PluginIssueApproximation Approximation);

/// <summary>
/// Builds authoritative dependency context and streams keyed Issue approximations for requested targets.
/// </summary>
public interface IPluginIssueApproximationModule
{
    /// <summary>
    /// Validates the complete request, analyzes targets sequentially, and reports each terminal result in target order.
    /// </summary>
    /// <param name="request">Source context and ordered target collection.</param>
    /// <param name="onResult">Callback invoked once for each completed target unless cancellation interrupts the operation.</param>
    /// <param name="ct">Cancellation token that prevents partial or later target publication once observed.</param>
    /// <returns>A task that completes after all target results have been reported.</returns>
    /// <exception cref="ArgumentException">The source or a row identity is structurally inconsistent.</exception>
    /// <exception cref="OperationCanceledException">Cancellation is observed before the operation completes.</exception>
    Task AnalyzeAsync(
        PluginIssueApproximationModuleRequest request,
        Action<PluginIssueApproximationModuleResult> onResult,
        CancellationToken ct = default);
}
