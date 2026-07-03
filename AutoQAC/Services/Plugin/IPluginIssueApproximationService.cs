using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;

namespace AutoQAC.Services.Plugin;

/// <summary>
/// Describes an issue approximation request for a selected game and plugin source.
/// </summary>
/// <param name="GameType">Game whose issue approximation rules should be applied.</param>
/// <param name="Source">Source used to assemble the analyzed load order.</param>
public sealed record PluginIssueApproximationRequest(
    GameType GameType,
    PluginIssueApproximationSource Source);

/// <summary>
/// Describes the source used to assemble plugin issue approximation inputs.
/// </summary>
public abstract record PluginIssueApproximationSource
{
    /// <summary>
    /// Uses the full load order discovered from a game data folder.
    /// </summary>
    /// <param name="DataFolder">Game data folder to analyze.</param>
    public sealed record DirectDataFolder(string DataFolder) : PluginIssueApproximationSource;

    /// <summary>
    /// Uses an already ordered plugin-name list plus resolved paths, such as an MO2 profile view.
    /// </summary>
    /// <param name="BaseDataFolder">Base game data folder used for load-order import context.</param>
    /// <param name="OrderedPluginNames">Plugin names in analysis order.</param>
    /// <param name="PathsByFileName">Resolved full paths keyed by plugin file name.</param>
    public sealed record ResolvedLoadOrder(
        string BaseDataFolder,
        IReadOnlyList<string> OrderedPluginNames,
        IReadOnlyDictionary<string, string> PathsByFileName) : PluginIssueApproximationSource;
}

/// <summary>
/// Provides best-effort pre-cleaning issue approximations for supported games.
/// </summary>
public interface IPluginIssueApproximationService
{
    /// <summary>
    /// Gets issue approximations for the requested game/source and optionally reports each result as it becomes ready.
    /// </summary>
    /// <param name="request">Issue approximation request.</param>
    /// <param name="onApproximationReady">Optional callback invoked as individual results are ready.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Issue approximation results for the request.</returns>
    Task<IReadOnlyList<PluginIssueApproximationResult>> GetApproximationsAsync(
        PluginIssueApproximationRequest request,
        Action<PluginIssueApproximationResult>? onApproximationReady = null,
        CancellationToken ct = default);
}
