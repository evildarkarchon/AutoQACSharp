using System;
using System.Threading;
using System.Threading.Tasks;

namespace AutoQAC.Services.Plugin;

/// <summary>
/// Executes Plugin refresh intents and publishes whole visible refresh snapshots.
/// </summary>
public interface IPluginRefreshModule
{
    /// <summary>
    /// Gets the latest visible Plugin refresh publication snapshots.
    /// </summary>
    IObservable<PluginRefreshSnapshot> Snapshots { get; }

    /// <summary>
    /// Executes a Plugin refresh intent and returns the final visible snapshot accepted for that intent.
    /// </summary>
    /// <param name="intent">User or domain intent to apply to Plugin refresh state.</param>
    /// <param name="cancellationToken">Cancellation token for the accepted intent.</param>
    /// <returns>The final visible snapshot after the intent has been applied.</returns>
    Task<PluginRefreshSnapshot> ExecuteAsync(
        PluginRefreshIntent intent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current authoritative Plugin refresh publication, including full row facts
    /// and whether the accepted discovery plan still matches Discovery-affecting settings.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for freshness inspection.</param>
    /// <returns>The current Plugin refresh publication.</returns>
    Task<PluginRefreshPublication> GetCurrentPublicationAsync(CancellationToken cancellationToken = default);
}
