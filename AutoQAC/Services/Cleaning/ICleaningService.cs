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

    /// <summary>
    /// Compatibility launch-readiness probe retained for legacy callers. Cleaning session
    /// preflight is the authoritative validation path and does not call this method.
    /// </summary>
    Task<bool> ValidateEnvironmentAsync(CancellationToken ct = default);
}
