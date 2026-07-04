using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.MO2;
using AutoQAC.Services.Plugin;

namespace AutoQAC.Services.GameCapability;

/// <summary>
/// Default Plugin refresh discovery planner backed by configuration, plugin loading, MO2 probing, and the Game capability catalog.
/// </summary>
public sealed class PluginRefreshDiscoveryPlanner : IPluginRefreshDiscoveryPlanner
{
    private readonly IConfigurationService _configurationService;
    private readonly IPluginLoadingService _pluginLoadingService;
    private readonly IMo2InstanceService _mo2InstanceService;
    private readonly IGameCapabilityProvider _gameCapabilityProvider;

    /// <summary>
    /// Initializes a planner with the adapter services needed to resolve Plugin refresh discovery details.
    /// </summary>
    public PluginRefreshDiscoveryPlanner(
        IConfigurationService configurationService,
        IPluginLoadingService pluginLoadingService,
        IMo2InstanceService mo2InstanceService,
        IGameCapabilityProvider gameCapabilityProvider)
    {
        _configurationService = configurationService;
        _pluginLoadingService = pluginLoadingService;
        _mo2InstanceService = mo2InstanceService;
        _gameCapabilityProvider = gameCapabilityProvider;
    }

    /// <inheritdoc />
    public IReadOnlyList<GameType> GetAvailableGames() => _gameCapabilityProvider.GetAvailableGames();

    /// <inheritdoc />
    public PluginRefreshGameAffordance GetAffordance(GameType gameType, bool mo2ModeEnabled)
    {
        var capability = _gameCapabilityProvider.Get(gameType);
        return new PluginRefreshGameAffordance(
            gameType,
            capability.SupportsAutomaticPluginDiscovery,
            gameType != GameType.Unknown && !mo2ModeEnabled && capability.RequiresLoadOrderFile,
            capability.SupportsIssueApproximation);
    }

    /// <inheritdoc />
    public async Task<PluginRefreshDiscoveryPlanResult> CreatePlanAsync(
        PluginRefreshDiscoveryPlanRequest request,
        CancellationToken ct = default)
    {
        var userConfig = await _configurationService.LoadUserConfigAsync(ct).ConfigureAwait(false);
        if (request.GameType == GameType.Unknown)
        {
            return new PluginRefreshDiscoveryPlanResult(
                PluginRefreshDiscoveryPlanStatus.NoGameSelected,
                null,
                CreateDirectConfiguration(userConfig, null, null, hasDataFolderOverride: false));
        }

        if (userConfig.Settings.Mo2Mode)
        {
            return await CreateMo2PlanAsync(request.GameType, userConfig, ct).ConfigureAwait(false);
        }

        return await CreateDirectPlanAsync(request.GameType, request.SelectedLoadOrderPath, userConfig, ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PluginRefreshDiscoveryFreshnessToken> CreateFreshnessTokenAsync(
        PluginRefreshDiscoveryPlan plan,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var userConfig = await _configurationService.LoadUserConfigAsync(ct).ConfigureAwait(false);
        var gameDataFolderOverride = await _configurationService.GetGameDataFolderOverrideAsync(plan.GameType, ct)
            .ConfigureAwait(false);
        var mo2Instance = plan.Configuration.Mo2ModeEnabled
            ? GetConfiguredMo2InstancePath(userConfig, plan.GameType)
            : null;

        return new PluginRefreshDiscoveryFreshnessToken(
            plan.GameType,
            plan.Configuration.Mo2ModeEnabled,
            NormalizePath(plan.Configuration.Mo2ModeEnabled ? null : plan.Configuration.LoadOrderPath),
            NormalizePath(gameDataFolderOverride),
            NormalizePath(mo2Instance),
            NormalizeText(plan.Configuration.SelectedProfile),
            userConfig.Settings.DisableSkipLists,
            NormalizeSkipLists(userConfig));
    }

    /// <inheritdoc />
    public async Task<PluginRefreshFreshness> CheckFreshnessAsync(
        PluginRefreshDiscoveryFreshnessToken accepted,
        PluginRefreshDiscoveryFreshnessContext current,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(accepted);
        ArgumentNullException.ThrowIfNull(current);

        var currentToken = await CreateCurrentFreshnessTokenAsync(accepted.GameType, current, ct).ConfigureAwait(false);
        return accepted.CompareWith(currentToken);
    }

    /// <inheritdoc />
    public async Task<PluginRefreshDiscoveredPlugins> LoadPluginsAsync(
        PluginRefreshDiscoveryPlan plan,
        CancellationToken ct = default)
    {
        switch (plan.Mode)
        {
            case PluginRefreshDiscoveryMode.DirectAutomatic:
            {
                var loadResult = await _pluginLoadingService.TryGetPluginsAsync(
                        plan.GameType,
                        plan.DataFolderPath,
                        ct)
                    .ConfigureAwait(false);
                var plugins = loadResult.Status == PluginLoadingStatus.Success
                    ? loadResult.Plugins
                    : [];
                return new PluginRefreshDiscoveredPlugins(plan, plugins, loadResult.Status);
            }

            case PluginRefreshDiscoveryMode.DirectLoadOrderFile:
            {
                if (string.IsNullOrWhiteSpace(plan.LoadOrderPath))
                {
                    return new PluginRefreshDiscoveredPlugins(plan, [], null);
                }

                var plugins = await _pluginLoadingService.GetPluginsFromFileAsync(
                        plan.LoadOrderPath,
                        plan.DataFolderPath,
                        ct)
                    .ConfigureAwait(false);
                return new PluginRefreshDiscoveredPlugins(plan, plugins, null);
            }

            case PluginRefreshDiscoveryMode.Mo2LoadOrderFile:
            {
                if (string.IsNullOrWhiteSpace(plan.Mo2LoadOrderPath))
                {
                    return new PluginRefreshDiscoveredPlugins(plan, [], null);
                }

                var plugins = await _pluginLoadingService.GetPluginsFromFileAsync(
                        plan.Mo2LoadOrderPath,
                        null,
                        ct)
                    .ConfigureAwait(false);
                if (plan.Mo2PathMap.Count == 0)
                {
                    return new PluginRefreshDiscoveredPlugins(plan, plugins, null);
                }

                var mappedPlugins = plugins.Select(plugin => plan.Mo2PathMap.TryGetValue(plugin.FileName, out var fullPath)
                        ? plugin with { FullPath = fullPath }
                        : plugin)
                    .ToList();
                return new PluginRefreshDiscoveredPlugins(plan, mappedPlugins, null);
            }

            default:
                return new PluginRefreshDiscoveredPlugins(plan, [], PluginLoadingStatus.UnsupportedGame);
        }
    }

    private async Task<PluginRefreshDiscoveryFreshnessToken> CreateCurrentFreshnessTokenAsync(
        GameType publicationGameType,
        PluginRefreshDiscoveryFreshnessContext current,
        CancellationToken ct)
    {
        var userConfig = await _configurationService.LoadUserConfigAsync(ct).ConfigureAwait(false);
        var gameDataFolderOverride = await _configurationService.GetGameDataFolderOverrideAsync(publicationGameType, ct)
            .ConfigureAwait(false);

        return new PluginRefreshDiscoveryFreshnessToken(
            current.CurrentGameType,
            current.Mo2ModeEnabled,
            NormalizePath(current.Mo2ModeEnabled ? null : current.LoadOrderPath),
            NormalizePath(gameDataFolderOverride),
            NormalizePath(current.Mo2ModeEnabled ? GetConfiguredMo2InstancePath(userConfig, publicationGameType) : null),
            NormalizeText(current.Mo2ModeEnabled ? current.Mo2Profile : null),
            userConfig.Settings.DisableSkipLists,
            NormalizeSkipLists(userConfig));
    }

    private async Task<PluginRefreshDiscoveryPlanResult> CreateDirectPlanAsync(
        GameType gameType,
        string? selectedLoadOrderPath,
        UserConfiguration userConfig,
        CancellationToken ct)
    {
        var customDataFolder = await _configurationService.GetGameDataFolderOverrideAsync(gameType, ct)
            .ConfigureAwait(false);
        var dataFolder = _pluginLoadingService.GetGameDataFolder(gameType, customDataFolder);
        var hasDataFolderOverride = !string.IsNullOrWhiteSpace(customDataFolder);
        var loadOrderPath = string.IsNullOrWhiteSpace(selectedLoadOrderPath)
            ? null
            : selectedLoadOrderPath;

        var capability = _gameCapabilityProvider.Get(gameType);
        if (string.IsNullOrWhiteSpace(loadOrderPath) && capability.RequiresLoadOrderFile)
        {
            loadOrderPath = await _configurationService.GetGameLoadOrderOverrideAsync(gameType, ct)
                                .ConfigureAwait(false)
                            ?? _pluginLoadingService.GetDefaultLoadOrderPath(gameType);
        }

        var configuration = CreateDirectConfiguration(userConfig, loadOrderPath, dataFolder, hasDataFolderOverride);
        if (!capability.SupportsPluginLoading)
        {
            return new PluginRefreshDiscoveryPlanResult(
                PluginRefreshDiscoveryPlanStatus.UnsupportedGame,
                null,
                configuration);
        }

        if (capability.RequiresLoadOrderFile && string.IsNullOrWhiteSpace(loadOrderPath))
        {
            return new PluginRefreshDiscoveryPlanResult(
                PluginRefreshDiscoveryPlanStatus.MissingLoadOrderFile,
                null,
                configuration);
        }

        var mode = string.IsNullOrWhiteSpace(loadOrderPath)
            ? PluginRefreshDiscoveryMode.DirectAutomatic
            : PluginRefreshDiscoveryMode.DirectLoadOrderFile;
        var plan = new PluginRefreshDiscoveryPlan(
            gameType,
            mode,
            configuration,
            userConfig.Settings.DisableSkipLists,
            capability.SupportsIssueApproximation,
            dataFolder,
            loadOrderPath,
            Mo2LoadOrderPath: null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Mo2BaseDataFolder: null);
        return new PluginRefreshDiscoveryPlanResult(
            PluginRefreshDiscoveryPlanStatus.Ready,
            plan,
            configuration);
    }

    private async Task<PluginRefreshDiscoveryPlanResult> CreateMo2PlanAsync(
        GameType gameType,
        UserConfiguration userConfig,
        CancellationToken ct)
    {
        var customDataFolder = await _configurationService.GetGameDataFolderOverrideAsync(gameType, ct)
            .ConfigureAwait(false);
        var dataFolder = _pluginLoadingService.GetGameDataFolder(gameType, customDataFolder);
        var hasDataFolderOverride = !string.IsNullOrWhiteSpace(customDataFolder);
        var instanceOverride = await _configurationService.GetMo2InstanceOverrideAsync(gameType, ct)
            .ConfigureAwait(false);
        var isInstanceOverride = !string.IsNullOrWhiteSpace(instanceOverride);

        var instance = await _mo2InstanceService.ResolveInstanceAsync(
                gameType,
                userConfig.ModOrganizer.Binary,
                instanceOverride,
                ct)
            .ConfigureAwait(false);
        if (instance is null)
        {
            var configuration = CreateMo2Configuration(
                userConfig,
                dataFolder,
                hasDataFolderOverride,
                instanceOverride,
                isInstanceOverride,
                string.IsNullOrWhiteSpace(instanceOverride) ? null : Directory.Exists(instanceOverride),
                [],
                selectedProfile: null);
            return new PluginRefreshDiscoveryPlanResult(
                PluginRefreshDiscoveryPlanStatus.MissingMo2Instance,
                null,
                configuration);
        }

        var profiles = await Task.Run(() => _mo2InstanceService.GetProfiles(instance), ct).ConfigureAwait(false);
        var persistedProfile = await _configurationService.GetMo2ProfileAsync(gameType, ct).ConfigureAwait(false);
        var profile = _mo2InstanceService.ChooseProfile(instance, profiles, persistedProfile);
        var baseConfiguration = CreateMo2Configuration(
            userConfig,
            dataFolder,
            hasDataFolderOverride,
            instance.BaseDirectory,
            isInstanceOverride,
            Directory.Exists(instance.BaseDirectory),
            profiles,
            profile);

        if (string.IsNullOrWhiteSpace(profile))
        {
            return new PluginRefreshDiscoveryPlanResult(
                PluginRefreshDiscoveryPlanStatus.MissingMo2Profile,
                null,
                baseConfiguration);
        }

        var mo2LoadOrderPath = _mo2InstanceService.GetLoadOrderPath(instance, profile);
        if (string.IsNullOrWhiteSpace(mo2LoadOrderPath) || !File.Exists(mo2LoadOrderPath))
        {
            return new PluginRefreshDiscoveryPlanResult(
                PluginRefreshDiscoveryPlanStatus.MissingMo2ProfileLoadOrder,
                null,
                baseConfiguration);
        }

        var pathMap = await Task.Run(
                () => _mo2InstanceService.BuildPluginPathMap(instance, profile, dataFolder),
                ct)
            .ConfigureAwait(false);
        var capability = _gameCapabilityProvider.Get(gameType);
        var plan = new PluginRefreshDiscoveryPlan(
            gameType,
            PluginRefreshDiscoveryMode.Mo2LoadOrderFile,
            baseConfiguration,
            userConfig.Settings.DisableSkipLists,
            capability.SupportsIssueApproximation,
            dataFolder,
            LoadOrderPath: null,
            mo2LoadOrderPath,
            pathMap,
            Mo2BaseDataFolder: dataFolder);
        return new PluginRefreshDiscoveryPlanResult(
            PluginRefreshDiscoveryPlanStatus.Ready,
            plan,
            baseConfiguration);
    }

    private static PluginRefreshConfigurationProjection CreateDirectConfiguration(
        UserConfiguration userConfig,
        string? loadOrderPath,
        string? dataFolder,
        bool hasDataFolderOverride) =>
        new(
            LoadOrderPath: loadOrderPath,
            GameDataFolder: dataFolder,
            HasGameDataFolderOverride: hasDataFolderOverride,
            XEditPath: userConfig.XEdit.Binary,
            Mo2Path: userConfig.ModOrganizer.Binary,
            Mo2ModeEnabled: userConfig.Settings.Mo2Mode,
            Mo2InstancePath: null,
            IsMo2InstanceOverride: false,
            IsMo2InstanceValid: null,
            AvailableProfiles: [],
            SelectedProfile: null,
            CleaningTimeout: userConfig.Settings.CleaningTimeout);

    private static PluginRefreshConfigurationProjection CreateMo2Configuration(
        UserConfiguration userConfig,
        string? dataFolder,
        bool hasDataFolderOverride,
        string? instancePath,
        bool isInstanceOverride,
        bool? isInstanceValid,
        IReadOnlyList<string> profiles,
        string? selectedProfile) =>
        new(
            LoadOrderPath: null,
            GameDataFolder: dataFolder,
            HasGameDataFolderOverride: hasDataFolderOverride,
            XEditPath: userConfig.XEdit.Binary,
            Mo2Path: userConfig.ModOrganizer.Binary,
            Mo2ModeEnabled: userConfig.Settings.Mo2Mode,
            Mo2InstancePath: instancePath,
            IsMo2InstanceOverride: isInstanceOverride,
            IsMo2InstanceValid: isInstanceValid,
            AvailableProfiles: profiles,
            SelectedProfile: selectedProfile,
            CleaningTimeout: userConfig.Settings.CleaningTimeout);

    private static string? GetConfiguredMo2InstancePath(UserConfiguration userConfig, GameType gameType)
    {
        var key = gameType.ToString();
        return userConfig.Mo2InstanceOverrides.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> NormalizeSkipLists(UserConfiguration userConfig)
    {
        var normalized = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, values) in userConfig.SkipLists.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
        {
            normalized[NormalizeText(key) ?? string.Empty] = (values ?? [])
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return normalized;
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path.Trim();
        }
    }

    private static string? NormalizeText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
