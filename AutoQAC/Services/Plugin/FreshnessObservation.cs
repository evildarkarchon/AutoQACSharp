using System.Threading;

namespace AutoQAC.Services.Plugin;

/// <summary>
///     The Plugin refresh module's count of Discovery-affecting settings changes. Each change advances it, so freshness
///     work observed before the change can no longer publish. It describes publication content, not whether a refresh
///     is still wanted, which is why it lives beside the module rather than in Cleaning admission.
/// </summary>
internal sealed class PluginRefreshFreshnessVersion
{
    private FreshnessObservation _current;

    /// <summary>Starts with an observation that is current until the first settings change.</summary>
    internal PluginRefreshFreshnessVersion()
    {
        _current = new FreshnessObservation(this);
    }

    /// <summary>Captures the current settings observation without advancing it.</summary>
    internal FreshnessObservation Observe()
    {
        return Volatile.Read(ref _current);
    }

    /// <summary>Records a Discovery-affecting settings change, making every earlier observation stale.</summary>
    /// <returns>The new current observation, owned by the caller reacting to the change.</returns>
    internal FreshnessObservation Advance()
    {
        var next = new FreshnessObservation(this);
        Interlocked.Exchange(ref _current, next);
        return next;
    }
}

/// <summary>
///     A settings observation captured by freshness work and handed to the publication store, which commits that work
///     only while no newer Discovery-affecting settings change has been recorded.
/// </summary>
internal sealed class FreshnessObservation
{
    private readonly PluginRefreshFreshnessVersion _version;

    /// <summary>Creates an observation; only <see cref="PluginRefreshFreshnessVersion" /> issues them.</summary>
    /// <param name="version">Counter whose current observation decides whether this one is still current.</param>
    internal FreshnessObservation(PluginRefreshFreshnessVersion version)
    {
        _version = version;
    }

    /// <summary>Whether no settings change has been recorded since this observation was captured.</summary>
    internal bool IsCurrent => ReferenceEquals(_version.Observe(), this);
}
