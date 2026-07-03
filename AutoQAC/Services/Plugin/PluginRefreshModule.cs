using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.GameCapability;
using AutoQAC.Services.State;

namespace AutoQAC.Services.Plugin;

/// <summary>
/// Deep Plugin refresh module that accepts refresh/selection/cancellation intents and emits whole visible snapshots.
/// </summary>
public sealed class PluginRefreshModule : IPluginRefreshModule, IDisposable
{
    private static readonly PluginRefreshActivity IdleActivity = new(false, false);
    private static readonly PluginRefreshCommandAvailability EmptyCommands = new(false, false, false, false);

    private readonly IPluginRefreshDiscoveryPlanner _discoveryPlanner;
    private readonly IPluginIssueApproximationService _pluginIssueApproximationService;
    private readonly IStateService _stateService;
    private readonly ISkipListPolicy _skipListPolicy;
    private readonly ILoggingService? _logger;
    private readonly BehaviorSubject<PluginRefreshSnapshot> _snapshots;
    private readonly IDisposable _stateSubscription;
    private readonly Lock _snapshotLock = new();

    private PluginRefreshSnapshot _currentSnapshot;
    private CancellationTokenSource? _activeRefreshCts;
    private long _activeGeneration;
    private bool _lastIsCleaning;
    private bool _disposed;

    /// <summary>
    /// Initializes a Plugin refresh module with the adapters needed for context assembly, publication, and AppState compatibility.
    /// </summary>
    public PluginRefreshModule(
        IPluginRefreshDiscoveryPlanner discoveryPlanner,
        IPluginIssueApproximationService pluginIssueApproximationService,
        IStateService stateService,
        ISkipListPolicy skipListPolicy,
        ILoggingService? logger = null)
    {
        _discoveryPlanner = discoveryPlanner;
        _pluginIssueApproximationService = pluginIssueApproximationService;
        _stateService = stateService;
        _skipListPolicy = skipListPolicy;
        _logger = logger;

        _lastIsCleaning = _stateService.CurrentState.IsCleaning;
        _currentSnapshot = CreateInitialSnapshot(_stateService.CurrentState);
        _snapshots = new BehaviorSubject<PluginRefreshSnapshot>(_currentSnapshot);
        _stateSubscription = _stateService.StateChanged.Subscribe(new StateChangedObserver(OnAppStateChanged));
    }

    /// <inheritdoc />
    public IObservable<PluginRefreshSnapshot> Snapshots => _snapshots;

    /// <inheritdoc />
    public Task<PluginRefreshSnapshot> ExecuteAsync(
        PluginRefreshIntent intent,
        CancellationToken cancellationToken = default) =>
        intent switch
        {
            PluginRefreshIntent.RefreshGame refresh => RefreshGameAsync(
                refresh.GameType,
                refresh.SelectedLoadOrderPath,
                cancellationToken),
            PluginRefreshIntent.RefreshSelectedIssueApproximations => RefreshSelectedIssueApproximationsAsync(
                cancellationToken),
            PluginRefreshIntent.ChangeSelection selection => Task.FromResult(ApplySelectionChange(selection.Change)),
            PluginRefreshIntent.Cancel cancel => Task.FromResult(CancelActiveRefresh(cancel.Reason)),
            _ => throw new ArgumentOutOfRangeException(nameof(intent), intent, "Unknown Plugin refresh intent.")
        };

    /// <summary>
    /// Releases observable and active cancellation resources owned by this singleton module.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        _stateSubscription.Dispose();
        var cts = Interlocked.Exchange(ref _activeRefreshCts, null);
        cts?.Cancel();
        cts?.Dispose();
        _snapshots.Dispose();
    }

    private async Task<PluginRefreshSnapshot> RefreshGameAsync(
        GameType gameType,
        string? selectedLoadOrderPath,
        CancellationToken cancellationToken)
    {
        using var linkedCts = CreateAndActivateGeneration(cancellationToken, out var generation);
        var token = linkedCts.Token;
        var configuration = GetCurrentSnapshot().Configuration;

        PublishSnapshot(new PluginRefreshSnapshot(
            generation,
            gameType,
            gameType == GetCurrentSnapshot().GameType ? GetCurrentSnapshot().Rows : [],
            configuration,
            new PluginRefreshActivity(IsPluginRefreshRunning: true, IsIssueApproximationRefreshRunning: false),
            EmptyCommands,
            gameType == GameType.Unknown ? "No game selected" : $"Loading plugins for {gameType}..."));

        try
        {
            if (gameType == GameType.Unknown)
            {
                PublishNoGameSelected(generation);
                return GetCurrentSnapshot();
            }

            var planResult = await _discoveryPlanner.CreatePlanAsync(
                    new PluginRefreshDiscoveryPlanRequest(gameType, selectedLoadOrderPath),
                    token)
                .ConfigureAwait(false);
            configuration = planResult.Configuration;
            if (!IsVisible(generation, token)) return GetCurrentSnapshot();

            PublishRuntimeConfiguration(configuration, gameType);
            PublishSnapshotFromState(
                generation,
                gameType,
                configuration,
                new PluginRefreshActivity(IsPluginRefreshRunning: true, IsIssueApproximationRefreshRunning: false),
                $"Loading plugins for {gameType}...");

            if (planResult.Plan is null)
            {
                _stateService.SetPluginsToClean([]);
                PublishSnapshotFromState(
                    generation,
                    gameType,
                    configuration,
                    IdleActivity,
                    GetPlanStatusText(planResult, gameType));
                return GetCurrentSnapshot();
            }

            var plan = planResult.Plan;
            var loadedPlugins = await _discoveryPlanner.LoadPluginsAsync(plan, token).ConfigureAwait(false);
            if (!IsVisible(generation, token)) return GetCurrentSnapshot();

            if (loadedPlugins.Plugins.Count == 0)
            {
                _stateService.SetPluginsToClean([]);
                PublishSnapshotFromState(
                    generation,
                    gameType,
                    configuration,
                    IdleActivity,
                    GetNoPluginsFoundMessage(plan));
                return GetCurrentSnapshot();
            }

            var skipEvaluation = await _skipListPolicy.EvaluateAsync(
                    plan.GameType,
                    loadedPlugins.Plugins,
                    plan.DisableSkipLists,
                    token)
                .ConfigureAwait(false);
            if (!IsVisible(generation, token)) return GetCurrentSnapshot();

            var initialApproximation = plan.CanAttemptIssueApproximation
                ? PluginIssueApproximation.Pending
                : PluginIssueApproximation.Unavailable;
            var rows = skipEvaluation.Decisions.Select(decision => decision.Plugin with
            {
                Approximation = initialApproximation
            }).ToList();

            _stateService.SetPluginsToClean(rows);
            PublishSnapshotFromState(
                generation,
                plan.GameType,
                configuration,
                new PluginRefreshActivity(IsPluginRefreshRunning: true, IsIssueApproximationRefreshRunning: false),
                $"Loading plugins for {plan.GameType}...");

            if (!plan.CanAttemptIssueApproximation)
            {
                PublishSnapshotFromState(
                    generation,
                    plan.GameType,
                    configuration,
                    IdleActivity,
                    "Approximation refresh is not available for this game.");
                return GetCurrentSnapshot();
            }

            var dataFolder = ResolveDataFolder(plan, rows);
            var targets = skipEvaluation.Decisions
                .Where(decision => !decision.ShouldSkipByPolicy)
                .Select(decision => new PluginRefreshRowKey(
                    decision.Plugin.FileName,
                    decision.Plugin.FullPath))
                .ToList();
            var issueActivity = new PluginRefreshActivity(
                IsPluginRefreshRunning: true,
                IsIssueApproximationRefreshRunning: true);
            PublishSnapshotFromState(
                generation,
                plan.GameType,
                configuration,
                issueActivity,
                targets.Count == 0 ? "Refreshed 0 plugin approximations." : $"Analyzing 0 of {targets.Count} selected plugins.");

            try
            {
                var updatedCount = 0;
                await AnalyzeTargetsAsync(
                        plan,
                        dataFolder,
                        targets,
                        result => PublishApproximationResult(
                            generation,
                            token,
                            plan.GameType,
                            configuration,
                            issueActivity,
                            targets,
                            result,
                            ref updatedCount),
                        token)
                    .ConfigureAwait(false);

                if (IsVisible(generation, token))
                {
                    PublishSnapshotFromState(
                        generation,
                        plan.GameType,
                        configuration,
                        IdleActivity,
                        $"Refreshed {Volatile.Read(ref updatedCount)} plugin approximations.");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.Error(ex, "Failed to refresh plugin issue approximations");
                PublishApproximationFailure(
                    generation,
                    token,
                    plan.GameType,
                    configuration,
                    targets,
                    "Approximation refresh failed.");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Superseded and lifecycle cancellations are ordinary Plugin refresh control flow.
        }
        finally
        {
            ReleaseGeneration(linkedCts);
        }

        return GetCurrentSnapshot();
    }

    private async Task<PluginRefreshSnapshot> RefreshSelectedIssueApproximationsAsync(CancellationToken cancellationToken)
    {
        var acceptedSnapshot = GetCurrentSnapshot();
        var selectedTargets = acceptedSnapshot.Rows
            .Where(row => row.IsSelected)
            .Select(row => row.Key)
            .ToList();

        if (selectedTargets.Count == 0)
        {
            return PublishSnapshot(acceptedSnapshot with
            {
                Activity = IdleActivity,
                StatusText = "Select plugins to refresh."
            });
        }

        using var linkedCts = CreateAndActivateGeneration(cancellationToken, out var generation);
        var token = linkedCts.Token;

        try
        {
            var planResult = await GetPlanForSelectedRefreshAsync(token).ConfigureAwait(false);
            if (planResult.Plan is null)
            {
                PublishSnapshot(acceptedSnapshot with
                {
                    Generation = generation,
                    Activity = IdleActivity,
                    Configuration = planResult.Configuration,
                    StatusText = planResult.Status == PluginRefreshDiscoveryPlanStatus.NoGameSelected
                        ? "Approximation refresh is not available for this game."
                        : GetPlanStatusText(planResult, _stateService.CurrentState.CurrentGameType)
                });
                return GetCurrentSnapshot();
            }

            var plan = planResult.Plan;
            var configuration = planResult.Configuration;
            if (!plan.CanAttemptIssueApproximation)
            {
                PublishSnapshotFromState(
                    generation,
                    plan.GameType,
                    configuration,
                    IdleActivity,
                    "Approximation refresh is not available for this game.");
                return GetCurrentSnapshot();
            }

            MarkTargetsPending(plan.GameType, selectedTargets);
            var issueActivity = new PluginRefreshActivity(
                IsPluginRefreshRunning: false,
                IsIssueApproximationRefreshRunning: true);
            PublishSnapshotFromState(
                generation,
                plan.GameType,
                configuration,
                issueActivity,
                $"Analyzing 0 of {selectedTargets.Count} selected plugins.");

            var dataFolder = ResolveDataFolder(plan, CreateRowsFromTargets(plan.GameType, selectedTargets));
            var updatedCount = 0;
            await AnalyzeTargetsAsync(
                    plan,
                    dataFolder,
                    selectedTargets,
                    result => PublishApproximationResult(
                        generation,
                        token,
                        plan.GameType,
                        configuration,
                        issueActivity,
                        selectedTargets,
                        result,
                        ref updatedCount),
                    token)
                .ConfigureAwait(false);

            if (IsVisible(generation, token))
            {
                PublishSnapshotFromState(
                    generation,
                    plan.GameType,
                    configuration,
                    IdleActivity,
                    $"Updated {Volatile.Read(ref updatedCount)} selected plugin approximations.");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Manual cancellation publishes the visible canceled snapshot in CancelActiveRefresh.
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to refresh selected plugin issue approximations");
            var snapshot = GetCurrentSnapshot();
            PublishApproximationFailure(
                generation,
                token,
                snapshot.GameType,
                snapshot.Configuration,
                selectedTargets,
                "Approximation refresh failed.");
        }
        finally
        {
            ReleaseGeneration(linkedCts);
        }

        return GetCurrentSnapshot();
    }

    private PluginRefreshSnapshot ApplySelectionChange(PluginSelectionChange change)
    {
        var snapshot = GetCurrentSnapshot();
        var visibleRows = snapshot.Rows.ToList();
        if (visibleRows.Count == 0)
        {
            return snapshot;
        }

        switch (change)
        {
            case PluginSelectionChange.SelectAllVisible:
            {
                var visiblePaths = visibleRows.Select(row => row.FullPath).ToList();
                _stateService.UpdateExcludedPlugins(current =>
                {
                    if (current.Count == 0)
                    {
                        return current;
                    }

                    var next = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
                    foreach (var path in visiblePaths)
                    {
                        next.Remove(path);
                    }

                    return next.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
                });
                break;
            }

            case PluginSelectionChange.DeselectAllVisible:
            {
                var visiblePaths = visibleRows.Select(row => row.FullPath).ToList();
                _stateService.UpdateExcludedPlugins(current =>
                {
                    var next = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
                    foreach (var path in visiblePaths)
                    {
                        next.Add(path);
                    }

                    return next.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
                });
                break;
            }

            case PluginSelectionChange.SetOne setOne:
            {
                var row = visibleRows.FirstOrDefault(visible => IsMatch(visible, setOne.Row));
                if (row is null)
                {
                    return snapshot;
                }

                _stateService.UpdateExcludedPlugins(current =>
                {
                    var alreadyExcluded = current.Contains(row.FullPath);
                    if (setOne.IsSelected ? !alreadyExcluded : alreadyExcluded)
                    {
                        return current;
                    }

                    var next = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
                    if (setOne.IsSelected)
                    {
                        next.Remove(row.FullPath);
                    }
                    else
                    {
                        next.Add(row.FullPath);
                    }

                    return next.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
                });
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(change), change, "Unknown Plugin selection change.");
        }

        return PublishSnapshotFromState(
            snapshot.Generation,
            snapshot.GameType,
            snapshot.Configuration,
            snapshot.Activity,
            snapshot.StatusText);
    }

    private PluginRefreshSnapshot CancelActiveRefresh(PluginRefreshCancelReason reason)
    {
        var cts = Volatile.Read(ref _activeRefreshCts);
        if (cts is not null)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The active generation may have completed between read and cancel; cancellation is best-effort.
            }
        }

        var snapshot = GetCurrentSnapshot();
        if (reason == PluginRefreshCancelReason.Disposed)
        {
            return snapshot;
        }

        return PublishSnapshot(snapshot with
        {
            Activity = IdleActivity,
            StatusText = reason == PluginRefreshCancelReason.Manual
                ? "Approximation refresh canceled."
                : snapshot.StatusText
        });
    }

    private void PublishNoGameSelected(long generation)
    {
        _stateService.UpdateState(state => state with { CurrentGameType = GameType.Unknown });
        _stateService.SetPluginsToClean([]);
        PublishSnapshotFromState(
            generation,
            GameType.Unknown,
            CreateConfigurationProjectionFromState(_stateService.CurrentState),
            IdleActivity,
            "No game selected");
    }

    private CancellationTokenSource CreateAndActivateGeneration(
        CancellationToken externalToken,
        out long generation)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        generation = Interlocked.Increment(ref _activeGeneration);
        var previous = Interlocked.Exchange(ref _activeRefreshCts, cts);
        previous?.Cancel(); // do not dispose another generation's live token source
        return cts;
    }

    private void ReleaseGeneration(CancellationTokenSource cts)
    {
        // A newer refresh may have swapped in its own CTS while this async generation was unwinding.
        // Only clear the active slot when this exact token source is still active; the using scope disposes it.
        Interlocked.CompareExchange(ref _activeRefreshCts, null, cts);
    }

    private bool IsVisible(long generation, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && generation == Volatile.Read(ref _activeGeneration);

    private async Task<PluginRefreshDiscoveryPlanResult> GetPlanForSelectedRefreshAsync(CancellationToken ct)
    {
        var currentGame = _stateService.CurrentState.CurrentGameType;
        if (currentGame == GameType.Unknown)
        {
            return new PluginRefreshDiscoveryPlanResult(
                PluginRefreshDiscoveryPlanStatus.NoGameSelected,
                null,
                GetCurrentSnapshot().Configuration);
        }

        // Selected refreshes rebuild the plan so same-game MO2/profile/path changes are never cached stale.
        return await _discoveryPlanner.CreatePlanAsync(
                new PluginRefreshDiscoveryPlanRequest(currentGame, _stateService.CurrentState.LoadOrderPath),
                ct)
            .ConfigureAwait(false);
    }

    private void PublishRuntimeConfiguration(
        PluginRefreshConfigurationProjection configuration,
        GameType gameType)
    {
        _stateService.UpdateConfigurationPaths(
            configuration.Mo2ModeEnabled ? null : configuration.LoadOrderPath,
            configuration.Mo2Path,
            configuration.XEditPath,
            configuration.SelectedProfile);
        _stateService.UpdateState(state => state with
        {
            CurrentGameType = gameType,
            Mo2ModeEnabled = configuration.Mo2ModeEnabled,
            CleaningTimeout = configuration.CleaningTimeout
        });
    }

    private async Task AnalyzeTargetsAsync(
        PluginRefreshDiscoveryPlan plan,
        string? dataFolder,
        IReadOnlyList<PluginRefreshRowKey> targets,
        Action<PluginIssueApproximationResult> onApproximationReady,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dataFolder) || targets.Count == 0)
        {
            return;
        }

        if (plan.Mode == PluginRefreshDiscoveryMode.Mo2LoadOrderFile)
        {
            await _pluginIssueApproximationService.GetApproximationsAsync(
                new PluginIssueApproximationRequest(
                    plan.GameType,
                    new PluginIssueApproximationSource.ResolvedLoadOrder(
                        dataFolder,
                        targets.Select(target => target.FileName).ToList(),
                        plan.Mo2PathMap)),
                onApproximationReady,
                ct).ConfigureAwait(false);
        }
        else
        {
            await _pluginIssueApproximationService.GetApproximationsAsync(
                new PluginIssueApproximationRequest(
                    plan.GameType,
                    new PluginIssueApproximationSource.DirectDataFolder(dataFolder)),
                onApproximationReady,
                ct).ConfigureAwait(false);
        }
    }

    private void PublishApproximationResult(
        long generation,
        CancellationToken token,
        GameType gameType,
        PluginRefreshConfigurationProjection configuration,
        PluginRefreshActivity activity,
        IReadOnlyList<PluginRefreshRowKey> targets,
        PluginIssueApproximationResult result,
        ref int updatedCount)
    {
        if (!IsVisible(generation, token) || !targets.Any(target => IsMatch(target, result)))
        {
            return;
        }

        var matchedRow = false;
        _stateService.UpdateState(state =>
        {
            if (!IsVisible(generation, token) || state.PluginsToClean.Count == 0)
            {
                return state;
            }

            var rows = state.PluginsToClean.Select(plugin =>
            {
                if (!IsMatch(plugin, result))
                {
                    return plugin;
                }

                matchedRow = true;
                return plugin with { Approximation = result.Approximation };
            }).ToList();

            return state with { PluginsToClean = rows.AsReadOnly() };
        });

        if (!matchedRow || !IsVisible(generation, token))
        {
            return;
        }

        var updated = Interlocked.Increment(ref updatedCount);
        PublishSnapshotFromState(
            generation,
            gameType,
            configuration,
            activity,
            $"Analyzing {updated} of {targets.Count} selected plugins.");
    }

    private void PublishApproximationFailure(
        long generation,
        CancellationToken token,
        GameType gameType,
        PluginRefreshConfigurationProjection configuration,
        IReadOnlyList<PluginRefreshRowKey> targets,
        string message)
    {
        if (!IsVisible(generation, token))
        {
            return;
        }

        MarkTargetsUnavailable(targets, generation, token);
        PublishSnapshotFromState(
            generation,
            gameType,
            configuration,
            IdleActivity,
            message);
    }

    private void MarkTargetsPending(GameType gameType, IReadOnlyList<PluginRefreshRowKey> targets)
    {
        _stateService.UpdateState(state =>
        {
            if (state.PluginsToClean.Count == 0)
            {
                var pendingRows = CreateRowsFromTargets(gameType, targets).ToList();
                return state with { PluginsToClean = pendingRows.AsReadOnly() };
            }

            var rows = state.PluginsToClean.Select(plugin =>
                targets.Any(target => IsMatch(plugin, target))
                    ? plugin with { Approximation = PluginIssueApproximation.Pending }
                    : plugin).ToList();

            return state with { PluginsToClean = rows.AsReadOnly() };
        });
    }

    private void MarkTargetsUnavailable(
        IReadOnlyList<PluginRefreshRowKey> targets,
        long generation,
        CancellationToken token)
    {
        _stateService.UpdateState(state =>
        {
            if (!IsVisible(generation, token) || state.PluginsToClean.Count == 0)
            {
                return state;
            }

            var rows = state.PluginsToClean.Select(plugin =>
                targets.Any(target => IsMatch(plugin, target))
                    ? plugin with { Approximation = PluginIssueApproximation.Unavailable }
                    : plugin).ToList();

            return state with { PluginsToClean = rows.AsReadOnly() };
        });
    }

    private PluginRefreshSnapshot PublishSnapshotFromState(
        long generation,
        GameType gameType,
        PluginRefreshConfigurationProjection configuration,
        PluginRefreshActivity activity,
        string statusText)
    {
        var state = _stateService.CurrentState;
        var rows = CreateVisibleRows(state.PluginsToClean, state.ExcludedPluginPaths);
        return PublishSnapshot(
            new PluginRefreshSnapshot(
                generation,
                gameType,
                rows,
                configuration,
                activity,
                EmptyCommands,
                statusText),
            state);
    }

    private PluginRefreshSnapshot PublishSnapshot(PluginRefreshSnapshot snapshot, AppState? state = null)
    {
        if (_disposed)
        {
            return GetCurrentSnapshot();
        }

        var next = WithCommandAvailability(snapshot, state ?? _stateService.CurrentState);
        lock (_snapshotLock)
        {
            _currentSnapshot = next;
        }

        _snapshots.OnNext(next);
        return next;
    }

    private PluginRefreshSnapshot GetCurrentSnapshot()
    {
        lock (_snapshotLock)
        {
            return _currentSnapshot;
        }
    }

    private PluginRefreshSnapshot WithCommandAvailability(PluginRefreshSnapshot snapshot, AppState state) =>
        snapshot with { Commands = CreateCommandAvailability(snapshot.GameType, snapshot.Rows, snapshot.Activity, state) };

    private PluginRefreshCommandAvailability CreateCommandAvailability(
        GameType gameType,
        IReadOnlyList<PluginRefreshRow> rows,
        PluginRefreshActivity activity,
        AppState state)
    {
        var hasRows = rows.Count > 0;
        var isRunning = activity.IsPluginRefreshRunning || activity.IsIssueApproximationRefreshRunning;
        var canUseRows = hasRows && !state.IsCleaning;
        var affordance = _discoveryPlanner.GetAffordance(gameType, state.Mo2ModeEnabled);
        var canRefreshApproximations = canUseRows &&
                                      !isRunning &&
                                      rows.Any(row => row.IsSelected) &&
                                      gameType != GameType.Unknown &&
                                      affordance.CanAttemptIssueApproximation;

        return new PluginRefreshCommandAvailability(
            CanSelectAll: canUseRows,
            CanDeselectAll: canUseRows,
            CanRefreshSelectedIssueApproximations: canRefreshApproximations,
            CanCancelRefresh: isRunning);
    }

    private PluginRefreshSnapshot CreateInitialSnapshot(AppState state)
    {
        var rows = CreateVisibleRows(state.PluginsToClean, state.ExcludedPluginPaths);
        var configuration = CreateConfigurationProjectionFromState(state);
        return new PluginRefreshSnapshot(
            Generation: 0,
            GameType: state.CurrentGameType,
            Rows: rows,
            Configuration: configuration,
            Activity: IdleActivity,
            Commands: CreateCommandAvailability(state.CurrentGameType, rows, IdleActivity, state),
            StatusText: "Ready");
    }

    private static PluginRefreshConfigurationProjection CreateConfigurationProjectionFromState(AppState state) =>
        new(
            LoadOrderPath: state.LoadOrderPath,
            GameDataFolder: null,
            HasGameDataFolderOverride: false,
            XEditPath: state.XEditExecutablePath,
            Mo2Path: state.Mo2ExecutablePath,
            Mo2ModeEnabled: state.Mo2ModeEnabled,
            Mo2InstancePath: null,
            IsMo2InstanceOverride: false,
            IsMo2InstanceValid: null,
            AvailableProfiles: [],
            SelectedProfile: state.Mo2Profile,
            CleaningTimeout: state.CleaningTimeout);

    private static IReadOnlyList<PluginRefreshRow> CreateVisibleRows(
        IReadOnlyList<PluginInfo> plugins,
        IReadOnlySet<string> excludedPaths) =>
        plugins.Where(plugin => !plugin.IsInSkipList)
            .Select(plugin => new PluginRefreshRow(
                plugin.FileName,
                plugin.FullPath,
                plugin.DetectedGameType,
                IsSelected: !excludedPaths.Contains(plugin.FullPath),
                plugin.IsInSkipList,
                plugin.Approximation))
            .ToList();

    private void OnAppStateChanged(AppState state)
    {
        if (_disposed || state.IsCleaning == _lastIsCleaning)
        {
            return;
        }

        _lastIsCleaning = state.IsCleaning;
        var snapshot = GetCurrentSnapshot();
        var next = WithCommandAvailability(snapshot, state);
        if (next.Commands == snapshot.Commands)
        {
            return;
        }

        PublishSnapshot(next, state);
    }

    private static string? ResolveDataFolder(PluginRefreshDiscoveryPlan plan, IReadOnlyList<PluginInfo> rows)
    {
        if (plan.Mode == PluginRefreshDiscoveryMode.Mo2LoadOrderFile)
        {
            return plan.Mo2BaseDataFolder;
        }

        if (!string.IsNullOrWhiteSpace(plan.DataFolderPath))
        {
            return plan.DataFolderPath;
        }

        var firstPath = rows.FirstOrDefault()?.FullPath;
        return string.IsNullOrWhiteSpace(firstPath) ? null : Path.GetDirectoryName(firstPath);
    }

    private static IReadOnlyList<PluginInfo> CreateRowsFromTargets(
        GameType gameType,
        IReadOnlyList<PluginRefreshRowKey> targets) =>
        targets.Select(target => new PluginInfo
        {
            FileName = target.FileName,
            FullPath = target.FullPath,
            DetectedGameType = gameType,
            Approximation = PluginIssueApproximation.Pending
        }).ToList();

    private static string GetPlanStatusText(PluginRefreshDiscoveryPlanResult result, GameType gameType) =>
        result.Status switch
        {
            PluginRefreshDiscoveryPlanStatus.NoGameSelected => "No game selected",
            PluginRefreshDiscoveryPlanStatus.MissingLoadOrderFile =>
                $"No load order file found for {gameType}. Browse to plugins.txt or loadorder.txt.",
            PluginRefreshDiscoveryPlanStatus.MissingMo2Instance =>
                $"No MO2 instance found for {gameType}. Browse to the instance folder.",
            PluginRefreshDiscoveryPlanStatus.MissingMo2Profile =>
                $"No MO2 profiles with loadorder.txt were found for {gameType}.",
            PluginRefreshDiscoveryPlanStatus.MissingMo2ProfileLoadOrder =>
                $"MO2 profile '{result.Configuration.SelectedProfile}' does not contain a loadorder.txt.",
            _ => "No plugins found in the selected load order."
        };

    private static string GetNoPluginsFoundMessage(PluginRefreshDiscoveryPlan plan) =>
        plan.Mode == PluginRefreshDiscoveryMode.DirectAutomatic
            ? $"No plugins discovered via Mutagen for {plan.GameType}."
            : "No plugins found in the selected load order.";

    private static bool IsMatch(PluginInfo plugin, PluginRefreshRowKey target)
    {
        if (HasUsablePath(plugin.FullPath) && HasUsablePath(target.FullPath))
        {
            return string.Equals(plugin.FullPath, target.FullPath, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(plugin.FileName, target.FileName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMatch(PluginRefreshRow row, PluginRefreshRowKey target)
    {
        if (HasUsablePath(row.FullPath) && HasUsablePath(target.FullPath))
        {
            return string.Equals(row.FullPath, target.FullPath, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(row.FileName, target.FileName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMatch(PluginInfo plugin, PluginIssueApproximationResult result)
    {
        if (HasUsablePath(plugin.FullPath) && HasUsablePath(result.FullPath))
        {
            return string.Equals(plugin.FullPath, result.FullPath, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(plugin.FileName, result.FileName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMatch(PluginRefreshRowKey target, PluginIssueApproximationResult result)
    {
        if (HasUsablePath(target.FullPath) && HasUsablePath(result.FullPath))
        {
            return string.Equals(target.FullPath, result.FullPath, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(target.FileName, result.FileName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasUsablePath(string? path) => !string.IsNullOrWhiteSpace(path);

    private sealed class StateChangedObserver(Action<AppState> onNext) : IObserver<AppState>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(AppState value) => onNext(value);
    }
}
