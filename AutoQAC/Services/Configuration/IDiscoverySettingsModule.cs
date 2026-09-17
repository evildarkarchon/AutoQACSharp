using System.Threading;
using System.Threading.Tasks;

namespace AutoQAC.Services.Configuration;

/// <summary>
///     Owns durable Discovery settings changes and their operation-correlated publication outcomes.
/// </summary>
public interface IDiscoverySettingsModule
{
    /// <summary>
    ///     Applies a validated change against the latest settings. Acceptance requires disk persistence and
    ///     matching publication, but does not wait for Issue approximation; a later valid choice may supersede it.
    /// </summary>
    /// <param name="intent">The setting change to apply.</param>
    /// <param name="ct">Withdraws acceptance; an already-submitted disk write settles and is not rolled back.</param>
    /// <returns>Acceptance, rejection, save/refresh failure, cancellation, or supersession with saved-choice facts.</returns>
    Task<DiscoverySettingsChangeResult> ExecuteAsync(
        DiscoverySettingsIntent intent,
        CancellationToken ct = default);
}
