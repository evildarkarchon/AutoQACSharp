using System.Collections.Generic;
using AutoQAC.Models;

namespace AutoQAC.Services.Plugin;

/// <summary>
/// Display-only fields resolved by a Plugin refresh that are not part of <see cref="AppState"/>.
/// ViewModels use this projection to update local controls without learning how refresh context is assembled.
/// </summary>
/// <param name="GameType">Game selected for the refresh intent.</param>
/// <param name="LoadOrderPath">Effective direct-mode load-order path, if one is being used.</param>
/// <param name="GameDataFolder">Effective game data folder used for loading or analysis, if resolved.</param>
/// <param name="HasGameDataFolderOverride">True when the effective game data folder came from a user override.</param>
/// <param name="Mo2InstancePath">Resolved MO2 instance path for display, if MO2 mode is active.</param>
/// <param name="IsMo2InstanceOverride">True when the MO2 instance path came from a per-game override.</param>
/// <param name="IsMo2InstanceValid">Null when no instance is selected; otherwise whether the instance path exists.</param>
/// <param name="AvailableProfiles">Resolved MO2 profiles that contain a load-order source.</param>
/// <param name="SelectedProfile">Resolved MO2 profile selected for the refresh, if any.</param>
public sealed record PluginRefreshProjection(
    GameType GameType,
    string? LoadOrderPath = null,
    string? GameDataFolder = null,
    bool HasGameDataFolderOverride = false,
    string? Mo2InstancePath = null,
    bool IsMo2InstanceOverride = false,
    bool? IsMo2InstanceValid = null,
    IReadOnlyList<string>? AvailableProfiles = null,
    string? SelectedProfile = null)
{
    /// <summary>
    /// Returns a projection with empty collection defaults so ViewModels can apply it without null branching.
    /// </summary>
    public IReadOnlyList<string> Profiles => AvailableProfiles ?? [];
}

/// <summary>
/// Immutable snapshot of a plugin row selected for targeted approximation refresh.
/// The full path is the primary identity, while file name remains available for existing merge fallback behavior.
/// </summary>
/// <param name="FileName">Plugin file name shown in the UI.</param>
/// <param name="FullPath">Resolved full path used to match state rows.</param>
public sealed record PluginRefreshTarget(string FileName, string FullPath);
