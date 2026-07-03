using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.MO2;
using AutoQAC.Services.State;

namespace AutoQAC.Services.Plugin;

/// <summary>
/// Coordinates Plugin refresh context assembly, generation cancellation, plugin loading, and issue approximation invocation.
/// </summary>
public sealed partial class PluginRefreshCoordinator : IPluginRefreshCoordinator, IDisposable
{
    private readonly IPluginLoadingService _pluginLoadingService;
    private readonly IPluginIssueApproximationService _pluginIssueApproximationService;
    private readonly IStateService _stateService;
    private readonly IPluginRefreshPublication _pluginRefreshPublication;
    private readonly IPluginRefreshCapabilityPolicy _capabilityPolicy;
    private readonly IConfigurationService _configurationService;
    private readonly ISkipListPolicy _skipListPolicy;
    private readonly IMo2InstanceService _mo2InstanceService;
    private readonly ILoggingService? _logger;
    private CancellationTokenSource? _activeRefreshCts;

    /// <summary>
    /// Initializes a new refresh coordinator with the services needed to assemble refresh context and publish rows.
    /// </summary>
    public PluginRefreshCoordinator(
        IPluginLoadingService pluginLoadingService,
        IPluginIssueApproximationService pluginIssueApproximationService,
        IStateService stateService,
        IPluginRefreshPublication pluginRefreshPublication,
        IPluginRefreshCapabilityPolicy capabilityPolicy,
        IConfigurationService configurationService,
        ISkipListPolicy skipListPolicy,
        IMo2InstanceService mo2InstanceService,
        ILoggingService? logger = null)
    {
        _pluginLoadingService = pluginLoadingService;
        _pluginIssueApproximationService = pluginIssueApproximationService;
        _stateService = stateService;
        _pluginRefreshPublication = pluginRefreshPublication;
        _capabilityPolicy = capabilityPolicy;
        _configurationService = configurationService;
        _skipListPolicy = skipListPolicy;
        _mo2InstanceService = mo2InstanceService;
        _logger = logger;
    }

    /// <inheritdoc />
    public IObservable<PluginRefreshStatus> StatusChanged => _pluginRefreshPublication.StatusChanged;

    /// <inheritdoc />
    public async Task<PluginRefreshProjection> RefreshForGameAsync(
        GameType gameType,
        string? selectedLoadOrderPath = null,
        CancellationToken ct = default)
    {
        using var linkedCts = CreateAndActivateGeneration(ct);
        var token = linkedCts.Token;
        using var publicationScope = _pluginRefreshPublication.BeginRefresh(token);
        var projection = EmptyProjection(gameType);

        try
        {
            if (gameType == GameType.Unknown)
            {
                if (!publicationScope.IsVisible) return projection;
                publicationScope.PublishNoGameSelected();
                return projection;
            }

            var userConfig = await _configurationService.LoadUserConfigAsync(token).ConfigureAwait(false);
            var contextResult = await CreateContextAsync(gameType, selectedLoadOrderPath, userConfig, token)
                .ConfigureAwait(false);
            projection = contextResult.Projection;
            if (!publicationScope.IsVisible) return projection;

            publicationScope.PublishConfiguration(
                projection,
                CreatePublicationSnapshot(userConfig, projection, contextResult.Context));

            if (contextResult.Context is null)
            {
                publicationScope.PublishNoRefreshContext(contextResult.Status ?? new PluginRefreshStatus(
                    PluginRefreshStatusKind.Idle,
                    Message: GetNoPluginsFoundMessage(gameType)));
                return projection;
            }

            var context = contextResult.Context;
            publicationScope.PublishLoadingPlugins();

            var loadedPlugins = await LoadPluginsAsync(context, token).ConfigureAwait(false);
            if (!publicationScope.IsVisible) return projection;

            if (loadedPlugins.Count == 0)
            {
                publicationScope.PublishNoRefreshContext(new PluginRefreshStatus(
                    PluginRefreshStatusKind.Idle,
                    Message: GetNoPluginsFoundMessage(context.GameType)));
                return projection;
            }

            var skipEvaluation = await _skipListPolicy.EvaluateAsync(
                    context.GameType,
                    loadedPlugins,
                    context.DisableSkipLists,
                    token)
                .ConfigureAwait(false);
            if (!publicationScope.IsVisible) return projection;

            var initialApproximation = _capabilityPolicy.SupportsIssueApproximation(context.GameType)
                ? PluginIssueApproximation.Pending
                : PluginIssueApproximation.Unavailable;
            var rows = skipEvaluation.Decisions.Select(decision => decision.Plugin with
            {
                Approximation = initialApproximation
            }).ToList();
            publicationScope.PublishPluginRows(rows);

            if (!_capabilityPolicy.SupportsIssueApproximation(context.GameType))
            {
                publicationScope.PublishApproximationUnavailable();
                return projection;
            }

            var dataFolder = ResolveDataFolder(context, rows);
            var targets = skipEvaluation.Decisions
                .Where(decision => !decision.ShouldSkipByPolicy)
                .Select(decision => new PluginRefreshTarget(
                    decision.Plugin.FileName,
                    decision.Plugin.FullPath))
                .ToList();

            try
            {
                var approximationPublication = publicationScope.BeginFullApproximationRefresh(targets);
                await AnalyzeTargetsAsync(context, dataFolder, targets, approximationPublication, token)
                    .ConfigureAwait(false);
                // Publish terminal status so PluginListViewModel can clear IsApproximationRefreshRunning
                // and the cancel-refresh affordance disables. Superseded generations stay silent in publication.
                approximationPublication.PublishCompleted();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.Error(ex, "Failed to refresh plugin issue approximations");
                publicationScope.PublishApproximationFailure(targets);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Superseded and lifecycle cancellations are normal refresh flow; explicit manual cancel publishes status in CancelActiveRefresh.
        }
        finally
        {
            ReleaseGeneration(linkedCts);
        }

        return projection;
    }

    /// <inheritdoc />
    public async Task RefreshSelectedApproximationsAsync(
        IReadOnlyList<PluginRefreshTarget> selectedTargets,
        CancellationToken ct = default)
    {
        var snapshot = selectedTargets.ToList();
        if (snapshot.Count == 0)
        {
            _pluginRefreshPublication.PublishSelectPlugins();
            return;
        }

        using var linkedCts = CreateAndActivateGeneration(ct);
        var token = linkedCts.Token;
        using var publicationScope = _pluginRefreshPublication.BeginRefresh(token);

        try
        {
            var context = await GetContextForSelectedRefreshAsync(token).ConfigureAwait(false);
            if (context is null)
            {
                publicationScope.PublishApproximationUnavailable();
                return;
            }

            if (!_capabilityPolicy.SupportsIssueApproximation(context.GameType))
            {
                publicationScope.PublishApproximationUnavailable();
                return;
            }

            var approximationPublication = publicationScope.BeginSelectedApproximationRefresh(context.GameType, snapshot);
            var dataFolder = ResolveDataFolder(context, CreateRowsFromTargets(context.GameType, snapshot));
            await AnalyzeTargetsAsync(context, dataFolder, snapshot, approximationPublication, token)
                .ConfigureAwait(false);
            approximationPublication.PublishCompleted();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Manual cancellation keeps any already merged rows and leaves remaining rows pending/unavailable.
        }
        finally
        {
            ReleaseGeneration(linkedCts);
        }
    }

    /// <inheritdoc />
    public void CancelActiveRefresh(PluginRefreshCancelReason reason)
    {
        var cts = Volatile.Read(ref _activeRefreshCts);
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The active generation may have completed between Exchange and Cancel; cancellation is best-effort.
        }

        if (reason == PluginRefreshCancelReason.Manual)
        {
            _pluginRefreshPublication.PublishManualCancellation();
        }
    }

    /// <summary>
    /// Releases observable and active cancellation resources owned by this singleton service.
    /// </summary>
    public void Dispose()
    {
        var cts = Interlocked.Exchange(ref _activeRefreshCts, null);
        cts?.Cancel();
        cts?.Dispose();
    }

    private CancellationTokenSource CreateAndActivateGeneration(CancellationToken externalToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        var previous = Interlocked.Exchange(ref _activeRefreshCts, cts);
        previous?.Cancel(); // do not dispose another generation's live CTS
        return cts;
    }

    private void ReleaseGeneration(CancellationTokenSource cts)
    {
        // A newer refresh may have swapped in its own CTS while this async generation was unwinding;
        // only clear/dispose when this exact token source is still the active one.
        if (Interlocked.CompareExchange(ref _activeRefreshCts, null, cts) == cts)
        {
            cts.Dispose();
        }
    }

    private async Task<PluginRefreshContext?> GetContextForSelectedRefreshAsync(CancellationToken ct)
    {
        var currentGame = _stateService.CurrentState.CurrentGameType;
        if (currentGame == GameType.Unknown)
        {
            return null;
        }

        var userConfig = await _configurationService.LoadUserConfigAsync(ct).ConfigureAwait(false);
        var contextResult = await CreateContextAsync(
                currentGame,
                _stateService.CurrentState.LoadOrderPath,
                userConfig,
                ct)
            .ConfigureAwait(false);

        // Selected refreshes must reflect current same-game settings such as MO2 mode,
        // profile, load-order path, and data-folder overrides; a game-only cache key is stale.
        return contextResult.Context;
    }

    private async Task<PluginRefreshContextResult> CreateContextAsync(
        GameType gameType,
        string? selectedLoadOrderPath,
        UserConfiguration userConfig,
        CancellationToken ct)
    {
        if (userConfig.Settings.Mo2Mode && gameType != GameType.Unknown)
        {
            return await CreateMo2ContextAsync(gameType, userConfig, ct).ConfigureAwait(false);
        }

        return await CreateDirectContextAsync(gameType, selectedLoadOrderPath, userConfig, ct)
            .ConfigureAwait(false);
    }

    private async Task<PluginRefreshContextResult> CreateDirectContextAsync(
        GameType gameType,
        string? selectedLoadOrderPath,
        UserConfiguration userConfig,
        CancellationToken ct)
    {
        var customDataFolder = gameType == GameType.Unknown
            ? null
            : await _configurationService.GetGameDataFolderOverrideAsync(gameType, ct).ConfigureAwait(false);
        var dataFolder = gameType == GameType.Unknown
            ? null
            : _pluginLoadingService.GetGameDataFolder(gameType, customDataFolder);
        var hasDataFolderOverride = !string.IsNullOrWhiteSpace(customDataFolder);
        var loadOrderPath = string.IsNullOrWhiteSpace(selectedLoadOrderPath)
            ? null
            : selectedLoadOrderPath;

        if (string.IsNullOrWhiteSpace(loadOrderPath) && _capabilityPolicy.RequiresLoadOrderFile(gameType))
        {
            loadOrderPath = await _configurationService.GetGameLoadOrderOverrideAsync(gameType, ct)
                                .ConfigureAwait(false)
                            ?? _pluginLoadingService.GetDefaultLoadOrderPath(gameType);
        }

        var projection = new PluginRefreshProjection(
            gameType,
            LoadOrderPath: loadOrderPath,
            GameDataFolder: dataFolder,
            HasGameDataFolderOverride: hasDataFolderOverride,
            AvailableProfiles: []);

        if (_capabilityPolicy.RequiresLoadOrderFile(gameType) && string.IsNullOrWhiteSpace(loadOrderPath))
        {
            return new PluginRefreshContextResult(
                null,
                projection,
                new PluginRefreshStatus(
                    PluginRefreshStatusKind.Idle,
                    Message: $"No load order file found for {gameType}. Browse to plugins.txt or loadorder.txt."));
        }

        var context = new PluginRefreshContext(
            gameType,
            dataFolder,
            loadOrderPath,
            userConfig.Settings.DisableSkipLists,
            Mo2Mode: false,
            Mo2LoadOrderPath: null,
            Mo2PathMap: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Mo2BaseDataFolder: null);
        return new PluginRefreshContextResult(context, projection, null);
    }

    private async Task<PluginRefreshContextResult> CreateMo2ContextAsync(
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
            var projection = new PluginRefreshProjection(
                gameType,
                LoadOrderPath: null,
                GameDataFolder: dataFolder,
                HasGameDataFolderOverride: hasDataFolderOverride,
                Mo2InstancePath: instanceOverride,
                IsMo2InstanceOverride: isInstanceOverride,
                IsMo2InstanceValid: string.IsNullOrWhiteSpace(instanceOverride) ? null : Directory.Exists(instanceOverride),
                AvailableProfiles: [],
                SelectedProfile: null);
            return new PluginRefreshContextResult(
                null,
                projection,
                new PluginRefreshStatus(
                    PluginRefreshStatusKind.Idle,
                    Message: $"No MO2 instance found for {gameType}. Browse to the instance folder."));
        }

        var profiles = await Task.Run(() => _mo2InstanceService.GetProfiles(instance), ct).ConfigureAwait(false);
        var persistedProfile = await _configurationService.GetMo2ProfileAsync(gameType, ct).ConfigureAwait(false);
        var profile = _mo2InstanceService.ChooseProfile(instance, profiles, persistedProfile);
        var baseProjection = new PluginRefreshProjection(
            gameType,
            LoadOrderPath: null,
            GameDataFolder: dataFolder,
            HasGameDataFolderOverride: hasDataFolderOverride,
            Mo2InstancePath: instance.BaseDirectory,
            IsMo2InstanceOverride: isInstanceOverride,
            IsMo2InstanceValid: Directory.Exists(instance.BaseDirectory),
            AvailableProfiles: profiles,
            SelectedProfile: profile);

        if (string.IsNullOrWhiteSpace(profile))
        {
            return new PluginRefreshContextResult(
                null,
                baseProjection,
                new PluginRefreshStatus(
                    PluginRefreshStatusKind.Idle,
                    Message: $"No MO2 profiles with loadorder.txt were found for {gameType}."));
        }

        var mo2LoadOrderPath = _mo2InstanceService.GetLoadOrderPath(instance, profile);
        if (string.IsNullOrWhiteSpace(mo2LoadOrderPath) || !File.Exists(mo2LoadOrderPath))
        {
            return new PluginRefreshContextResult(
                null,
                baseProjection,
                new PluginRefreshStatus(
                    PluginRefreshStatusKind.Idle,
                    Message: $"MO2 profile '{profile}' does not contain a loadorder.txt."));
        }

        var pathMap = await Task.Run(
                () => _mo2InstanceService.BuildPluginPathMap(instance, profile, dataFolder),
                ct)
            .ConfigureAwait(false);
        var context = new PluginRefreshContext(
            gameType,
            dataFolder,
            LoadOrderPath: null,
            userConfig.Settings.DisableSkipLists,
            Mo2Mode: true,
            Mo2LoadOrderPath: mo2LoadOrderPath,
            Mo2PathMap: pathMap,
            Mo2BaseDataFolder: dataFolder);
        return new PluginRefreshContextResult(context, baseProjection, null);
    }

    private static PluginRefreshPublicationSnapshot CreatePublicationSnapshot(
        UserConfiguration userConfig,
        PluginRefreshProjection projection,
        PluginRefreshContext? context) =>
        new(
            context is { Mo2Mode: false } ? projection.LoadOrderPath : null,
            userConfig.ModOrganizer.Binary,
            userConfig.XEdit.Binary,
            projection.SelectedProfile,
            userConfig.Settings.Mo2Mode,
            userConfig.Settings.CleaningTimeout);

    private async Task<IReadOnlyList<PluginInfo>> LoadPluginsAsync(PluginRefreshContext context, CancellationToken ct)
    {
        if (context.Mo2Mode)
        {
            if (string.IsNullOrWhiteSpace(context.Mo2LoadOrderPath))
            {
                return [];
            }

            var plugins = await _pluginLoadingService.GetPluginsFromFileAsync(context.Mo2LoadOrderPath, null, ct)
                .ConfigureAwait(false);
            if (context.Mo2PathMap.Count == 0)
            {
                return plugins;
            }

            return plugins.Select(plugin => context.Mo2PathMap.TryGetValue(plugin.FileName, out var fullPath)
                    ? plugin with { FullPath = fullPath }
                    : plugin)
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(context.LoadOrderPath))
        {
            return await _pluginLoadingService
                .GetPluginsFromFileAsync(context.LoadOrderPath, context.DataFolderPath, ct)
                .ConfigureAwait(false);
        }

        if (!_capabilityPolicy.SupportsPluginLoading(context.GameType))
        {
            return [];
        }

        var loadResult = await _pluginLoadingService.TryGetPluginsAsync(
                context.GameType,
                context.DataFolderPath,
                ct)
            .ConfigureAwait(false);
        return loadResult.Status == PluginLoadingStatus.Success
            ? loadResult.Plugins
            : [];
    }

    private async Task AnalyzeTargetsAsync(
        PluginRefreshContext context,
        string? dataFolder,
        IReadOnlyList<PluginRefreshTarget> targets,
        IPluginRefreshApproximationPublication publication,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dataFolder) || targets.Count == 0)
        {
            return;
        }

        var callback = new Action<PluginIssueApproximationResult>(publication.PublishResult);

        if (context.Mo2Mode)
        {
            await _pluginIssueApproximationService.GetApproximationsAsync(
                context.GameType,
                dataFolder,
                targets.Select(target => target.FileName).ToList(),
                modKey =>
                {
                    var fileName = modKey.FileName.String;
                    return context.Mo2PathMap.TryGetValue(fileName, out var path)
                        ? path
                        : null;
                },
                callback,
                ct).ConfigureAwait(false);
        }
        else
        {
            await _pluginIssueApproximationService.GetApproximationsAsync(
                context.GameType,
                dataFolder,
                callback,
                ct).ConfigureAwait(false);
        }
    }

    private static string? ResolveDataFolder(PluginRefreshContext context, IReadOnlyList<PluginInfo> rows)
    {
        if (context.Mo2Mode)
        {
            return context.Mo2BaseDataFolder;
        }

        if (!string.IsNullOrWhiteSpace(context.DataFolderPath))
        {
            return context.DataFolderPath;
        }

        var firstPath = rows.FirstOrDefault()?.FullPath;
        return string.IsNullOrWhiteSpace(firstPath) ? null : Path.GetDirectoryName(firstPath);
    }

    private static IReadOnlyList<PluginInfo> CreateRowsFromTargets(
        GameType gameType,
        IReadOnlyList<PluginRefreshTarget> targets) =>
        targets.Select(target => new PluginInfo
        {
            FileName = target.FileName,
            FullPath = target.FullPath,
            DetectedGameType = gameType,
            Approximation = PluginIssueApproximation.Pending
        }).ToList();

    private string GetNoPluginsFoundMessage(GameType gameType) =>
        _pluginLoadingService.IsGameSupportedByMutagen(gameType)
            ? $"No plugins discovered via Mutagen for {gameType}."
            : "No plugins found in the selected load order.";

    private static PluginRefreshProjection EmptyProjection(GameType gameType) =>
        new(gameType, AvailableProfiles: []);

    private sealed record PluginRefreshContext(
        GameType GameType,
        string? DataFolderPath,
        string? LoadOrderPath,
        bool DisableSkipLists,
        bool Mo2Mode,
        string? Mo2LoadOrderPath,
        IReadOnlyDictionary<string, string> Mo2PathMap,
        string? Mo2BaseDataFolder);

    private sealed record PluginRefreshContextResult(
        PluginRefreshContext? Context,
        PluginRefreshProjection Projection,
        PluginRefreshStatus? Status);
}
