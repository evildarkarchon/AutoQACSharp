using System;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;

namespace AutoQAC.Services.Cleaning;

public interface ICleaningService
{
    // Main cleaning entry point
    Task<CleaningResult> CleanPluginAsync(
        PluginInfo plugin,
        Action<System.Diagnostics.Process>? onProcessStarted = null,
        CancellationToken ct = default);

    // Pre-cleaning validation
    Task<bool> ValidateEnvironmentAsync(CancellationToken ct = default);
}
