using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;

namespace AutoQAC.Services.Plugin;

public interface IPluginIssueApproximationService
{
    Task<IReadOnlyList<PluginIssueApproximationResult>> GetApproximationsAsync(
        GameType gameType,
        string dataFolder,
        Action<PluginIssueApproximationResult>? onApproximationReady = null,
        CancellationToken ct = default);
}
