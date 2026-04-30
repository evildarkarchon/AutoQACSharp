using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.State;

namespace AutoQAC.Services.Plugin;

/// <summary>
/// Coordinates plugin list refresh, generation cancellation, and incremental issue approximation updates.
/// The coordinator publishes rows through <see cref="IStateService"/> before background approximation work starts.
/// </summary>
public sealed class PluginRefreshCoordinator : IPluginRefreshCoordinator, IDisposable
{
    private readonly IPluginLoadingService _pluginLoadingService;
    private readonly IPluginIssueApproximationService _pluginIssueApproximationService;
    private readonly IStateService _stateService;
    private readonly IPluginRefreshCapabilityPolicy _capabilityPolicy;
    private readonly IConfigurationService? _configurationService;
    private readonly ILoggingService? _logger;
    private readonly Subject<PluginRefreshStatus> _statusChanged = new();
    private CancellationTokenSource? _activeRefreshCts;
    private int _refreshGeneration;

    /// <summary>
    /// Initializes a new refresh coordinator with the services needed for row loading and approximation publication.
    /// </summary>
    /// <param name="pluginLoadingService">Service used to load plugin rows.</param>
    /// <param name="pluginIssueApproximationService">Service used to analyze plugin issue approximations.</param>
    /// <param name="stateService">Shared state hub that receives row and approximation updates.</param>
    /// <param name="capabilityPolicy">Refresh-scoped game capability policy.</param>
    /// <param name="configurationService">Optional configuration service used for skip-list and fallback load-order lookup.</param>
    /// <param name="logger">Optional logger for per-plugin and workflow failures.</param>
    public PluginRefreshCoordinator(
        IPluginLoadingService pluginLoadingService,
        IPluginIssueApproximationService pluginIssueApproximationService,
        IStateService stateService,
        IPluginRefreshCapabilityPolicy capabilityPolicy,
        IConfigurationService? configurationService = null,
        ILoggingService? logger = null)
    {
        _pluginLoadingService = pluginLoadingService;
        _pluginIssueApproximationService = pluginIssueApproximationService;
        _stateService = stateService;
        _capabilityPolicy = capabilityPolicy;
        _configurationService = configurationService;
        _logger = logger;
    }

    /// <inheritdoc />
    public IObservable<PluginRefreshStatus> StatusChanged => _statusChanged.AsObservable();

    /// <inheritdoc />
    public async Task RefreshForGameAsync(PluginRefreshRequest request, CancellationToken ct = default)
    {
        var generation = Interlocked.Increment(ref _refreshGeneration);
        using var linkedCts = CreateAndActivateGeneration(ct);
        var token = linkedCts.Token;

        try
        {
            if (request.GameType == GameType.Unknown && string.IsNullOrWhiteSpace(request.LoadOrderPath))
            {
                if (!IsCurrent(generation, token)) return;
                _stateService.UpdateState(s => s with { CurrentGameType = GameType.Unknown });
                _stateService.SetPluginsToClean(new List<PluginInfo>());
                Publish(new PluginRefreshStatus(PluginRefreshStatusKind.Idle, Message: "No game selected"));
                return;
            }

            if (!IsCurrent(generation, token)) return;
            _stateService.UpdateState(s => s with { CurrentGameType = request.GameType });
            Publish(new PluginRefreshStatus(PluginRefreshStatusKind.LoadingPlugins));

            var loadedPlugins = await LoadPluginsAsync(request, token).ConfigureAwait(false);
            if (!IsCurrent(generation, token)) return;

            var skipList = await GetSkipListAsync(request.GameType, token).ConfigureAwait(false);
            if (!IsCurrent(generation, token)) return;

            var initialApproximation = _capabilityPolicy.SupportsIssueApproximation(request.GameType)
                ? PluginIssueApproximation.Pending
                : PluginIssueApproximation.Unavailable;
            var rows = ApplySkipListStatus(loadedPlugins, skipList, request.GameType, disableSkipLists: request.DisableSkipLists, initialApproximation);
            _stateService.SetPluginsToClean(rows);

            if (rows.Count == 0)
            {
                var message = _pluginLoadingService.IsGameSupportedByMutagen(request.GameType)
                    ? $"No plugins discovered via Mutagen for {request.GameType}."
                    : "No plugins found in the selected load order.";
                Publish(new PluginRefreshStatus(PluginRefreshStatusKind.Idle, Message: message));
                return;
            }

            if (!_capabilityPolicy.SupportsIssueApproximation(request.GameType))
            {
                Publish(new PluginRefreshStatus(PluginRefreshStatusKind.ApproximationUnavailable));
                return;
            }

            var dataFolder = ResolveDataFolder(request, rows);
            var targets = rows
                .Where(plugin => !plugin.IsInSkipList)
                .Select(plugin => new PluginRefreshTarget(plugin.FileName, plugin.FullPath))
                .ToList();
            try
            {
                var updated = await AnalyzeTargetsAsync(request.GameType, dataFolder, targets, generation, token).ConfigureAwait(false);
                if (IsCurrent(generation, token))
                {
                    // Publish terminal status so PluginListViewModel can clear IsApproximationRefreshRunning
                    // and the cancel-refresh affordance disables. Superseded generations stay silent (per D-10).
                    Publish(PluginRefreshStatus.FullRefreshCompleted(updated));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.Error(ex, "Failed to refresh plugin issue approximations");
                foreach (var target in targets)
                {
                    if (!IsCurrent(generation, token)) return;
                    _stateService.MergePluginApproximation(new PluginIssueApproximationResult
                    {
                        FileName = target.FileName,
                        FullPath = target.FullPath,
                        Approximation = PluginIssueApproximation.Unavailable
                    });
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
    }

    /// <inheritdoc />
    public async Task RefreshSelectedApproximationsAsync(
        PluginRefreshRequest request,
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
            if (!_capabilityPolicy.SupportsIssueApproximation(request.GameType))
            {
                Publish(new PluginRefreshStatus(PluginRefreshStatusKind.ApproximationUnavailable));
                return;
            }

            var pendingRows = snapshot.Select(target => new PluginInfo
            {
                FileName = target.FileName,
                FullPath = target.FullPath,
                DetectedGameType = request.GameType,
                Approximation = PluginIssueApproximation.Pending
            }).ToList();
            if (IsCurrent(generation, token))
            {
                var targetPaths = snapshot.Select(target => target.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var targetNames = snapshot.Select(target => target.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                _stateService.UpdateState(s =>
                {
                    if (s.PluginsToClean.Count == 0)
                    {
                        return s with { PluginsToClean = pendingRows.AsReadOnly() };
                    }

                    var rows = s.PluginsToClean.Select(plugin =>
                        targetPaths.Contains(plugin.FullPath) || targetNames.Contains(plugin.FileName)
                            ? plugin with { Approximation = PluginIssueApproximation.Pending }
                            : plugin).ToList();

                    return s with { PluginsToClean = rows.AsReadOnly() };
                });
            }

            var dataFolder = ResolveDataFolder(request, pendingRows);
            var updated = await AnalyzeTargetsAsync(request.GameType, dataFolder, snapshot, generation, token).ConfigureAwait(false);
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

    private async Task<IReadOnlyList<PluginInfo>> LoadPluginsAsync(PluginRefreshRequest request, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.LoadOrderPath))
        {
            return await _pluginLoadingService.GetPluginsFromFileAsync(request.LoadOrderPath, request.DataFolderPath, ct)
                .ConfigureAwait(false);
        }

        if (!_capabilityPolicy.SupportsPluginLoading(request.GameType))
        {
            return Array.Empty<PluginInfo>();
        }

        if (_capabilityPolicy.RequiresLoadOrderFile(request.GameType))
        {
            var loadOrderPath = request.LoadOrderPath;
            if (string.IsNullOrWhiteSpace(loadOrderPath) && _configurationService is not null)
            {
                loadOrderPath = await _configurationService.GetGameLoadOrderOverrideAsync(request.GameType, ct).ConfigureAwait(false)
                    ?? _pluginLoadingService.GetDefaultLoadOrderPath(request.GameType);
            }

            return string.IsNullOrWhiteSpace(loadOrderPath)
                ? Array.Empty<PluginInfo>()
                : await _pluginLoadingService.GetPluginsFromFileAsync(loadOrderPath, request.DataFolderPath, ct).ConfigureAwait(false);
        }

        var loadResult = await _pluginLoadingService.TryGetPluginsAsync(request.GameType, request.DataFolderPath, ct)
            .ConfigureAwait(false);
        return loadResult?.Status == PluginLoadingStatus.Success
            ? loadResult.Plugins
            : Array.Empty<PluginInfo>();
    }

    private async Task<List<string>> GetSkipListAsync(GameType gameType, CancellationToken ct)
    {
        if (_configurationService is null)
        {
            return [];
        }

        return await _configurationService.GetSkipListAsync(gameType, ct: ct).ConfigureAwait(false) ?? [];
    }

    private async Task<int> AnalyzeTargetsAsync(
        GameType gameType,
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

        await _pluginIssueApproximationService.GetApproximationsAsync(
            gameType,
            dataFolder,
            approximation =>
            {
                if (!IsCurrent(generation, ct) || !IsTarget(approximation, targetPaths, targetNames))
                {
                    return;
                }

                updated++;
                Publish(PluginRefreshStatus.AnalyzingSelected(updated, total));
                _stateService.MergePluginApproximation(approximation);
            },
            ct).ConfigureAwait(false);

        return updated;
    }

    private static bool IsTarget(
        PluginIssueApproximationResult approximation,
        IReadOnlySet<string> targetPaths,
        IReadOnlySet<string> targetNames) =>
        targetPaths.Contains(approximation.FullPath) || targetNames.Contains(approximation.FileName);

    private static string? ResolveDataFolder(PluginRefreshRequest request, IReadOnlyList<PluginInfo> rows)
    {
        if (!string.IsNullOrWhiteSpace(request.DataFolderPath))
        {
            return request.DataFolderPath;
        }

        var firstPath = rows.FirstOrDefault()?.FullPath;
        return string.IsNullOrWhiteSpace(firstPath) ? null : System.IO.Path.GetDirectoryName(firstPath);
    }

    private static List<PluginInfo> ApplySkipListStatus(
        IReadOnlyList<PluginInfo> plugins,
        IReadOnlyList<string> skipList,
        GameType gameType,
        bool disableSkipLists,
        PluginIssueApproximation approximation)
    {
        var skipSet = new HashSet<string>(skipList, StringComparer.OrdinalIgnoreCase);
        return plugins.Select(plugin => plugin with
        {
            IsInSkipList = !disableSkipLists && skipSet.Contains(plugin.FileName),
            DetectedGameType = gameType,
            Approximation = approximation
        }).ToList();
    }

    private void Publish(PluginRefreshStatus status) => _statusChanged.OnNext(status);
}
