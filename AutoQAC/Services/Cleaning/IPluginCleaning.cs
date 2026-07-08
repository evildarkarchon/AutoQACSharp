using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;

namespace AutoQAC.Services.Cleaning;

/// <summary>
/// Cleans one plugin inside an already-prepared Cleaning session and returns the final per-plugin result.
/// Backup policy, session metadata, plugin selection, and user-visible state publication remain Cleaning session concerns.
/// </summary>
public interface IPluginCleaning
{
    /// <summary>
    /// Runs the per-plugin xEdit Quick Auto Clean attempt sequence, including retry decisions, log slicing,
    /// termination attachment, and final result projection.
    /// </summary>
    /// <param name="context">Small per-plugin context resolved by the Cleaning session preflight.</param>
    /// <param name="ct">Cancellation token for launch, retry decision, and log-read work.</param>
    /// <returns>The final cleaning result for the plugin.</returns>
    Task<PluginCleaningResult> CleanAsync(PluginCleaningContext context, CancellationToken ct);
}
