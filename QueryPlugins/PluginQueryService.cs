using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using QueryPlugins.Detectors;
using QueryPlugins.Detectors.Games;
using QueryPlugins.Models;

namespace QueryPlugins;

/// <summary>
/// Orchestrates all plugin issue detectors and returns a consolidated
/// <see cref="PluginAnalysisResult"/>.
///
/// <para>
/// A default instance is available via <see cref="Default"/>. Alternatively,
/// construct one directly to supply custom detector implementations (e.g. for testing).
/// </para>
/// </summary>
public sealed class PluginQueryService : IPluginQueryService
{
    /// <summary>
    /// A lazily-initialised default instance using the standard detector implementations.
    /// </summary>
    public static readonly PluginQueryService Default = new();

    private readonly IItmDetector _itmDetector;
    private readonly IReadOnlyDictionary<GameRelease, IGameSpecificDetector> _detectorsByRelease;

    /// <summary>
    /// Creates a service with the standard built-in detectors.
    /// </summary>
    public PluginQueryService()
        : this(
            new ItmDetector(),
            [new SkyrimDetector(), new Fallout4Detector(), new StarfieldDetector(), new OblivionDetector()])
    {
    }

    /// <summary>
    /// Creates a service with custom detector implementations. Intended for testing.
    /// </summary>
    /// <param name="itmDetector">The ITM detector to use.</param>
    /// <param name="gameDetectors">
    /// The game-specific detectors. Each must have a unique, non-overlapping set of
    /// <see cref="IGameSpecificDetector.SupportedReleases"/>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown if two detectors claim the same <see cref="GameRelease"/>.
    /// </exception>
    public PluginQueryService(IItmDetector itmDetector, IEnumerable<IGameSpecificDetector> gameDetectors)
    {
        _itmDetector = itmDetector;

        var byRelease = new Dictionary<GameRelease, IGameSpecificDetector>();
        foreach (var detector in gameDetectors)
        {
            foreach (var release in detector.SupportedReleases)
            {
                if (!byRelease.TryAdd(release, detector))
                {
                    throw new ArgumentException(
                        $"Duplicate detector registered for {release}: " +
                        $"{byRelease[release].GetType().Name} and {detector.GetType().Name}.",
                        nameof(gameDetectors));
                }
            }
        }

        _detectorsByRelease = byRelease;
    }

    /// <inheritdoc />
    public PluginAnalysisResult Analyse(IModGetter plugin, ILinkCache linkCache, GameRelease gameRelease)
    {
        if (!_detectorsByRelease.TryGetValue(gameRelease, out var gameDetector))
            throw new NotSupportedException(
                $"No game-specific detector is registered for {gameRelease}. " +
                $"Supported releases: {string.Join(", ", _detectorsByRelease.Keys)}.");

        var issues = new List<PluginIssue>();

        issues.AddRange(_itmDetector.FindItmRecords(plugin, linkCache));
        issues.AddRange(gameDetector.FindDeletedReferences(plugin));
        issues.AddRange(gameDetector.FindDeletedNavmeshes(plugin));

        return new PluginAnalysisResult(issues);
    }
}
