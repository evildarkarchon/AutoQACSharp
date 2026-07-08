using System.Threading;
using System.Threading.Tasks;

namespace AutoQAC.Services.Configuration;

/// <summary>
/// Applies Discovery settings changes and returns the accepted Plugin refresh publication projection.
/// </summary>
public interface IDiscoverySettingsModule
{
    /// <summary>
    /// Applies one Discovery settings change, including persistence and any required Plugin refresh intent.
    /// </summary>
    /// <param name="intent">The setting change to apply.</param>
    /// <param name="ct">Cancellation token for persistence and refresh work.</param>
    /// <returns>The accepted refresh snapshot, or a typed rejection when the intent is invalid.</returns>
    Task<DiscoverySettingsChangeResult> ExecuteAsync(
        DiscoverySettingsIntent intent,
        CancellationToken ct = default);
}
