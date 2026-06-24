using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;
using Mutagen.Bethesda.Plugins;

namespace AutoQAC.Services.Plugin;

public interface IPluginIssueApproximationService
{
    Task<IReadOnlyList<PluginIssueApproximationResult>> GetApproximationsAsync(
        GameType gameType,
        string dataFolder,
        Action<PluginIssueApproximationResult>? onApproximationReady = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<PluginIssueApproximationResult>> GetApproximationsAsync(
        GameType gameType,
        string baseDataFolder,
        IReadOnlyList<string> orderedPluginNames,
        Func<ModKey, string?> pathResolver,
        Action<PluginIssueApproximationResult>? onApproximationReady = null,
        CancellationToken ct = default);
}
