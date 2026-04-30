using AutoQAC.Models;

namespace AutoQAC.Services.Plugin;

/// <summary>
/// Refresh-scoped capability policy for plugin row loading and issue approximation refresh.
/// It intentionally stays narrower than an app-wide game capability registry.
/// </summary>
public sealed class PluginRefreshCapabilityPolicy : IPluginRefreshCapabilityPolicy
{
    private readonly IPluginLoadingService _pluginLoadingService;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginRefreshCapabilityPolicy"/> class.
    /// </summary>
    /// <param name="pluginLoadingService">Plugin loading service that owns Mutagen support checks.</param>
    public PluginRefreshCapabilityPolicy(IPluginLoadingService pluginLoadingService)
    {
        _pluginLoadingService = pluginLoadingService;
    }

    /// <inheritdoc />
    public bool SupportsPluginLoading(GameType gameType) =>
        gameType != GameType.Unknown;

    /// <inheritdoc />
    public bool SupportsIssueApproximation(GameType gameType) => gameType is
        GameType.SkyrimLe or
        GameType.SkyrimSe or
        GameType.SkyrimVr or
        GameType.Fallout4 or
        GameType.Fallout4Vr;

    /// <inheritdoc />
    public bool RequiresLoadOrderFile(GameType gameType) =>
        gameType != GameType.Unknown && !_pluginLoadingService.IsGameSupportedByMutagen(gameType);
}
