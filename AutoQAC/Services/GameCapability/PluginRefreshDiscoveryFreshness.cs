using System;
using System.Collections.Generic;
using AutoQAC.Models;
using AutoQAC.Services.Plugin;

namespace AutoQAC.Services.GameCapability;

/// <summary>
/// Opaque token representing Discovery-affecting settings accepted with a Plugin refresh discovery plan.
/// </summary>
public sealed class PluginRefreshDiscoveryFreshnessToken
{
    private readonly GameType _gameType;
    private readonly bool _mo2ModeEnabled;
    private readonly string? _loadOrderPath;
    private readonly string? _gameDataFolderOverride;
    private readonly string? _mo2InstancePath;
    private readonly string? _mo2Profile;
    private readonly bool _disableSkipLists;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _skipLists;

    internal PluginRefreshDiscoveryFreshnessToken(
        GameType gameType,
        bool mo2ModeEnabled,
        string? loadOrderPath,
        string? gameDataFolderOverride,
        string? mo2InstancePath,
        string? mo2Profile,
        bool disableSkipLists,
        IReadOnlyDictionary<string, IReadOnlyList<string>> skipLists)
    {
        _gameType = gameType;
        _mo2ModeEnabled = mo2ModeEnabled;
        _loadOrderPath = loadOrderPath;
        _gameDataFolderOverride = gameDataFolderOverride;
        _mo2InstancePath = mo2InstancePath;
        _mo2Profile = mo2Profile;
        _disableSkipLists = disableSkipLists;
        _skipLists = skipLists;
    }

    internal GameType GameType => _gameType;

    internal PluginRefreshFreshness CompareWith(PluginRefreshDiscoveryFreshnessToken current)
    {
        if (current._gameType != _gameType)
        {
            return new PluginRefreshFreshness(false, PluginRefreshStalenessReason.SelectedGameChanged);
        }

        if (current._mo2ModeEnabled != _mo2ModeEnabled)
        {
            return new PluginRefreshFreshness(false, PluginRefreshStalenessReason.Mo2ModeChanged);
        }

        if (!StringComparer.OrdinalIgnoreCase.Equals(current._loadOrderPath, _loadOrderPath))
        {
            return new PluginRefreshFreshness(false, PluginRefreshStalenessReason.LoadOrderPathChanged);
        }

        if (!StringComparer.OrdinalIgnoreCase.Equals(current._gameDataFolderOverride, _gameDataFolderOverride))
        {
            return new PluginRefreshFreshness(false, PluginRefreshStalenessReason.GameDataFolderOverrideChanged);
        }

        if (!StringComparer.OrdinalIgnoreCase.Equals(current._mo2InstancePath, _mo2InstancePath))
        {
            return new PluginRefreshFreshness(false, PluginRefreshStalenessReason.Mo2InstanceChanged);
        }

        if (!StringComparer.OrdinalIgnoreCase.Equals(current._mo2Profile, _mo2Profile))
        {
            return new PluginRefreshFreshness(false, PluginRefreshStalenessReason.Mo2ProfileChanged);
        }

        if (current._disableSkipLists != _disableSkipLists || !SkipListsEqual(current._skipLists, _skipLists))
        {
            return new PluginRefreshFreshness(false, PluginRefreshStalenessReason.SkipListSettingsChanged);
        }

        return PluginRefreshFreshness.Fresh;
    }

    private static bool SkipListsEqual(
        IReadOnlyDictionary<string, IReadOnlyList<string>> left,
        IReadOnlyDictionary<string, IReadOnlyList<string>> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var (key, leftValues) in left)
        {
            if (!right.TryGetValue(key, out var rightValues) || leftValues.Count != rightValues.Count)
            {
                return false;
            }

            for (var i = 0; i < leftValues.Count; i++)
            {
                if (!string.Equals(leftValues[i], rightValues[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        return true;
    }
}

/// <summary>
/// Current AppState-owned context needed to check Plugin refresh discovery freshness lazily.
/// </summary>
/// <param name="CurrentGameType">Currently selected game.</param>
/// <param name="Mo2ModeEnabled">Whether MO2 mode is currently enabled.</param>
/// <param name="LoadOrderPath">Current direct-mode load-order path.</param>
/// <param name="Mo2Profile">Current MO2 profile selection.</param>
public sealed record PluginRefreshDiscoveryFreshnessContext(
    GameType CurrentGameType,
    bool Mo2ModeEnabled,
    string? LoadOrderPath,
    string? Mo2Profile);
