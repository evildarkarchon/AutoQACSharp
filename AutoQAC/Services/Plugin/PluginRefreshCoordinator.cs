using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
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
/// Coordinates Plugin refresh context assembly, row publication, generation cancellation, and issue approximation updates.
/// </summary>
public sealed partial class PluginRefreshCoordinator : IPluginRefreshCoordinator, IDisposable
{
    private readonly IPluginLoadingService _pluginLoadingService;
    private readonly IPluginIssueApproximationService _pluginIssueApproximationService;
    private readonly IStateService _stateService;
    private readonly IPluginRefreshCapabilityPolicy _capabilityPolicy;
    private readonly IConfigurationService _configurationService;
    private readonly ISkipListPolicy _skipListPolicy;
    private readonly IMo2InstanceService _mo2InstanceService;
    private readonly ILoggingService? _logger;
    private readonly Subject<PluginRefreshStatus> _statusChanged = new();
    private CancellationTokenSource? _activeRefreshCts;
    private PluginRefreshContext? _lastSuccessfulContext;
    private int _refreshGeneration;

    /// <summary>
    /// Initializes a new refresh coordinator with the services needed to assemble refresh context and publish rows.
    /// </summary>
    public PluginRefreshCoordinator(
        IPluginLoadingService pluginLoadingService,
        IPluginIssueApproximationService pluginIssueApproximationService,
        IStateService stateService,
        IPluginRefreshCapabilityPolicy capabilityPolicy,
        IConfigurationService configurationService,
        ISkipListPolicy skipListPolicy,
        IMo2InstanceService mo2InstanceService,
        ILoggingService? logger = null)
    {
        _pluginLoadingService = pluginLoadingService;
        _pluginIssueApproximationService = pluginIssueApproximationService;
        _stateService = stateService;
        _capabilityPolicy = capabilityPolicy;
        _configurationService = configurationService;
        _skipListPolicy = skipListPolicy;
        _mo2InstanceService = mo2InstanceService;
        _logger = logger;
    }

    /// <inheritdoc />
    public IObservable<PluginRefreshStatus> StatusChanged => _statusChanged.AsObservable();

    /// <inheritdoc />
    public async Task<PluginRefreshProjection> RefreshForGameAsync(
        GameType gameType,
        string? selectedLoadOrderPath = null,
        CancellationToken ct = default)
    {
        var generation = Interlocked.Increment(ref _refreshGeneration);
        using var linkedCts = CreateAndActivateGeneration(ct);
        var token = linkedCts.Token;
        var projection = EmptyProjection(gameType);

        try
        {
            if (gameType == GameType.Unknown)
            {
                if (!IsCurrent(generation, token)) return projection;
                _lastSuccessfulContext = null;
                _stateService.UpdateState(s => s with { CurrentGameType = GameType.Unknown });
                _stateService.SetPluginsToClean([]);
                Publish(new PluginRefreshStatus(PluginRefreshStatusKind.Idle, Message: "No game selected"));
                return projection;
            }

            var userConfig = await _configurationService.LoadUserConfigAsync(token).ConfigureAwait(false);
            var contextResult = await CreateContextAsync(gameType, selectedLoadOrderPath, userConfig, token)
                .ConfigureAwait(false);
            projection = contextResult.Projection;
            if (!IsCurrent(generation, token)) return projection;

            PublishConfigurationState(projection, userConfig, contextResult.Context);

            if (contextResult.Context is null)
            {
                _lastSuccessfulContext = null;
                _stateService.SetPluginsToClean([]);
                Publish(contextResult.Status ?? new PluginRefreshStatus(
                    PluginRefreshStatusKind.Idle,
                    Message: GetNoPluginsFoundMessage(gameType)));
                return projection;
            }

            var context = contextResult.Context;
            _lastSuccessfulContext = context;
            Publish(new PluginRefreshStatus(PluginRefreshStatusKind.LoadingPlugins));

            var loadedPlugins = await LoadPluginsAsync(context, token).ConfigureAwait(false);
            if (!IsCurrent(generation, token)) return projection;

            if (loadedPlugins.Count == 0)
            {
                _stateService.SetPluginsToClean([]);
                Publish(new PluginRefreshStatus(PluginRefreshStatusKind.Idle,
                    Message: GetNoPluginsFoundMessage(context.GameType)));
                return projection;
            }

            var skipEvaluation = await _skipListPolicy.EvaluateAsync(
                    context.GameType,
                    loadedPlugins,
                    context.DisableSkipLists,
                    token)
                .ConfigureAwait(false);
            if (!IsCurrent(generation, token)) return projection;

            var initialApproximation = _capabilityPolicy.SupportsIssueApproximation(context.GameType)
                ? PluginIssueApproximation.Pending
                : PluginIssueApproximation.Unavailable;
            var rows = skipEvaluation.Decisions.Select(decision => decision.Plugin with
            {
                Approximation = initialApproximation
            }).ToList();
            _stateService.SetPluginsToClean(rows);

            if (!_capabilityPolicy.SupportsIssueApproximation(context.GameType))
            {
                Publish(new PluginRefreshStatus(PluginRefreshStatusKind.ApproximationUnavailable));
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
                var updated = await AnalyzeTargetsAsync(context, dataFolder, targets, generation, token)
                    .ConfigureAwait(false);
                if (IsCurrent(generation, token))
                {
                    // Publish terminal status so PluginListViewModel can clear IsApproximationRefreshRunning
                    // and the cancel-refresh affordance disables. Superseded generations stay silent.
                    Publish(PluginRefreshStatus.FullRefreshCompleted(updated));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.Error(ex, "Failed to refresh plugin issue approximations");
                foreach (var target in targets)
                {
                    if (!IsCurrent(generation, token)) return projection;
                    _stateService.MergePluginApproximation(new PluginIssueApproximationResult
                    {
                        FileName = target.FileName,
                        FullPath = target.FullPath,
                        Approximation = PluginIssueApproximation.Unavailable
                    });
                }

                if (IsCurrent(generation, token))
                {
                    Publish(new PluginRefreshStatus(PluginRefreshStatusKind.Idle,
                        Message: "Approximation refresh failed."));
                }
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
            Publish(new PluginRefreshStatus(PluginRefreshStatusKind.SelectPlugins));
            return;
        }

        var generation = Interlocked.Increment(ref _refreshGeneration);
        using var linkedCts = CreateAndActivateGeneration(ct);
        var token = linkedCts.Token;

        try
        {
            var context = await GetContextForSelectedRefreshAsync(token).ConfigureAwait(false);
            if (context is null)
            {
                Publish(new PluginRefreshStatus(PluginRefreshStatusKind.ApproximationUnavailable));
                return;
            }

            if (!_capabilityPolicy.SupportsIssueApproximation(context.GameType))
            {
                Publish(new PluginRefreshStatus(PluginRefreshStatusKind.ApproximationUnavailable));
                return;
            }

            var pendingRows = snapshot.Select(target => new PluginInfo
            {
                FileName = target.FileName,
                FullPath = target.FullPath,
                DetectedGameType = context.GameType,
                Approximation = PluginIssueApproximation.Pending
            }).ToList();
            if (IsCurrent(generation, token))
            {
                var targetPaths = snapshot.Select(target => target.FullPath)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var targetNames = snapshot.Select(target => target.FileName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                _stateService.UpdateState(s =>
                {
                    if (s.PluginsToClean.Count == 0)
                    {
                        return s with { PluginsToClean = pendingRows.AsReadOnly() };
                    }

                    var rows = s.PluginsToClean.Select(plugin =>
                    {
                        // Prefer path matching; fall back to name only when the row has no path.
                        var isTarget = !string.IsNullOrWhiteSpace(plugin.FullPath)
                            ? targetPaths.Contains(plugin.FullPath)
                            : targetNames.Contains(plugin.FileName);
                        return isTarget
                            ? plugin with { Approximation = PluginIssueApproximation.Pending }
                            : plugin;
                    }).ToList();

                    return s with { PluginsToClean = rows.AsReadOnly() };
                });
            }

            var dataFolder = ResolveDataFolder(context, pendingRows);
            var updated = await AnalyzeTargetsAsync(context, dataFolder, snapshot, generation, token)
                .ConfigureAwait(false);
            if (IsCurrent(generation, token))
            {
                Publish(PluginRefreshStatus.SelectedRefreshCompleted(updated));
            }
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
            Publish(new PluginRefreshStatus(PluginRefreshStatusKind.Canceled));
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
        _statusChanged.Dispose();
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

    private bool IsCurrent(int generation, CancellationToken token) =>
        !token.IsCancellationRequested && generation == Volatile.Read(ref _refreshGeneration);

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
        _lastSuccessfulContext = contextResult.Context;

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

    private void PublishConfigurationState(
        PluginRefreshProjection projection,
        UserConfiguration userConfig,
        PluginRefreshContext? context)
    {
        _stateService.UpdateConfigurationPaths(
            context is { Mo2Mode: false } ? projection.LoadOrderPath : null,
            userConfig.ModOrganizer.Binary,
            userConfig.XEdit.Binary,
            projection.SelectedProfile);
        _stateService.UpdateState(s => s with
        {
            CurrentGameType = projection.GameType,
            Mo2ModeEnabled = userConfig.Settings.Mo2Mode,
            CleaningTimeout = userConfig.Settings.CleaningTimeout
        });
    }

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

    private async Task<int> AnalyzeTargetsAsync(
        PluginRefreshContext context,
        string? dataFolder,
        IReadOnlyList<PluginRefreshTarget> targets,
        int generation,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dataFolder) || targets.Count == 0)
        {
            return 0;
        }

        var targetPaths = targets.Select(target => target.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targetNames = targets.Select(target => target.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var updated = 0;
        var total = targets.Count;

        var callback = new Action<PluginIssueApproximationResult>(approximation =>
        {
            if (!IsCurrent(generation, ct) || !IsTarget(approximation, targetPaths, targetNames))
            {
                return;
            }

            updated++;
            Publish(PluginRefreshStatus.AnalyzingSelected(updated, total));
            _stateService.MergePluginApproximation(approximation);
        });

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

        return updated;
    }

    private static bool IsTarget(
        PluginIssueApproximationResult approximation,
        IReadOnlySet<string> targetPaths,
        IReadOnlySet<string> targetNames)
    {
        // Prefer full path when the approximation has one.
        if (!string.IsNullOrWhiteSpace(approximation.FullPath) && targetPaths.Contains(approximation.FullPath))
            return true;

        // Fall back to file name only when the approximation has no usable path.
        return string.IsNullOrWhiteSpace(approximation.FullPath) && targetNames.Contains(approximation.FileName);
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

    private string GetNoPluginsFoundMessage(GameType gameType) =>
        _pluginLoadingService.IsGameSupportedByMutagen(gameType)
            ? $"No plugins discovered via Mutagen for {gameType}."
            : "No plugins found in the selected load order.";

    private static PluginRefreshProjection EmptyProjection(GameType gameType) =>
        new(gameType, AvailableProfiles: []);

    private void Publish(PluginRefreshStatus status) => _statusChanged.OnNext(status);

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
