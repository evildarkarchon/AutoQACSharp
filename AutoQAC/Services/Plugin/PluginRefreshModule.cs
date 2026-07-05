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
using AutoQAC.Models.Configuration;
using AutoQAC.Services.Configuration;
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
    private readonly IDisposable? _userConfigurationSubscription;
    private readonly IDisposable? _skipListSubscription;
    private readonly Lock _snapshotLock = new();

    private PluginRefreshSnapshot _currentSnapshot;
    private PluginRefreshPublication _currentPublication;
    private PluginRefreshDiscoveryFreshnessToken? _currentPublicationFreshnessToken;
    private CancellationTokenSource? _activeRefreshCts;
    private long _activeGeneration;
    private int _freshnessRefreshVersion;
    private DiscoveryAffectingState _lastDiscoveryAffectingState;
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
        ILoggingService? logger = null,
        IConfigurationService? configurationService = null)
    {
        _discoveryPlanner = discoveryPlanner;
        _pluginIssueApproximationService = pluginIssueApproximationService;
        _stateService = stateService;
        _skipListPolicy = skipListPolicy;
        _logger = logger;

        _lastIsCleaning = _stateService.CurrentState.IsCleaning;
        _lastDiscoveryAffectingState = DiscoveryAffectingState.From(_stateService.CurrentState);
        _currentSnapshot = CreateInitialSnapshot(_stateService.CurrentState);
        _currentPublication = CreateMissingPublication(_currentSnapshot);
        _snapshots = new BehaviorSubject<PluginRefreshSnapshot>(_currentSnapshot);
        _stateSubscription = _stateService.StateChanged.Subscribe(new StateChangedObserver(OnAppStateChanged));
        if (configurationService is not null)
        {
            _userConfigurationSubscription = configurationService.UserConfigurationChanged.Subscribe(
                new ConfigurationChangedObserver<UserConfiguration>(_ => OnDiscoveryAffectingSettingsChanged()));
            _skipListSubscription = configurationService.SkipListChanged.Subscribe(
                new ConfigurationChangedObserver<GameType>(_ => OnDiscoveryAffectingSettingsChanged()));
        }
    }

    /// <inheritdoc />
    public IObservable<PluginRefreshSnapshot> Snapshots => _snapshots;

    /// <inheritdoc />
    public Task<PluginRefreshPublication> GetCurrentPublicationAsync(CancellationToken cancellationToken = default) =>
        GetCurrentPublicationWithFreshnessAsync(publishIfChanged: false, cancellationToken);

    private async Task<PluginRefreshPublication> GetCurrentPublicationWithFreshnessAsync(
        bool publishIfChanged,
        CancellationToken cancellationToken = default)
    {
        PluginRefreshPublication publication;
        PluginRefreshDiscoveryFreshnessToken? freshnessToken;
        lock (_snapshotLock)
        {
            publication = _currentPublication;
            freshnessToken = _currentPublicationFreshnessToken;
        }

        if (freshnessToken is null || publication.DiscoveryPlan is null)
        {
            return publication with { Freshness = PluginRefreshFreshness.Missing };
        }

        var freshness = await _discoveryPlanner.CheckFreshnessAsync(
                freshnessToken,
                CreateFreshnessContext(_stateService.CurrentState),
                cancellationToken)
            .ConfigureAwait(false);
        var refreshedPublication = publication with { Freshness = freshness };
        if (publishIfChanged)
        {
            PublishFreshnessIfCurrent(publication, freshnessToken, refreshedPublication);
        }

        return refreshedPublication;
    }

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
        _userConfigurationSubscription?.Dispose();
        _skipListSubscription?.Dispose();
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
        var acceptedSnapshot = GetCurrentSnapshot();
        var configuration = acceptedSnapshot.Configuration;
        var keepExistingRows = gameType != GameType.Unknown && gameType == acceptedSnapshot.GameType;
        if (!keepExistingRows)
        {
            ClearStaleRowsForRefreshStart(gameType);
        }

        PublishSnapshot(new PluginRefreshSnapshot(
            generation,
            gameType,
            keepExistingRows ? acceptedSnapshot.Rows : [],
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
                PublishMissingPublicationFromState(
                    generation,
                    gameType,
                    configuration,
                    IdleActivity,
                    GetPlanStatusText(planResult, gameType));
                return GetCurrentSnapshot();
            }

            var plan = planResult.Plan;
            var freshnessToken = await _discoveryPlanner.CreateFreshnessTokenAsync(plan, token).ConfigureAwait(false);
            var loadedPlugins = await _discoveryPlanner.LoadPluginsAsync(plan, token).ConfigureAwait(false);
            if (!IsVisible(generation, token)) return GetCurrentSnapshot();

            if (loadedPlugins.Plugins.Count == 0)
            {
                PublishAcceptedPublication(
                    generation,
                    gameType,
                    plan,
                    freshnessToken,
                    configuration,
                    [],
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
            var publishedRows = CreatePublishedRows(
                skipEvaluation.Decisions,
                initialApproximation,
                _stateService.CurrentState.ExcludedPluginPaths);
            var rows = publishedRows.Select(row => row.Plugin).ToList();

            PublishAcceptedPublication(
                generation,
                plan.GameType,
                plan,
                freshnessToken,
                configuration,
                publishedRows,
                new PluginRefreshActivity(IsPluginRefreshRunning: true, IsIssueApproximationRefreshRunning: false),
                $"Loading plugins for {plan.GameType}...");

            if (!plan.CanAttemptIssueApproximation)
            {
                PublishCurrentPublication(
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
            var targetLookup = PluginRefreshTargetLookup.Create(targets);
            var issueActivity = new PluginRefreshActivity(
                IsPluginRefreshRunning: true,
                IsIssueApproximationRefreshRunning: true);
            PublishCurrentPublication(
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
                            targetLookup,
                            result,
                            ref updatedCount),
                        token)
                    .ConfigureAwait(false);

                if (IsVisible(generation, token))
                {
                    PublishCurrentPublication(
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
                    targetLookup,
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
                PublishCurrentPublication(
                    generation,
                    plan.GameType,
                    configuration,
                    IdleActivity,
                    "Approximation refresh is not available for this game.");
                return GetCurrentSnapshot();
            }

            var targetLookup = PluginRefreshTargetLookup.Create(selectedTargets);
            MarkTargetsPending(plan.GameType, selectedTargets, targetLookup);
            var issueActivity = new PluginRefreshActivity(
                IsPluginRefreshRunning: false,
                IsIssueApproximationRefreshRunning: true);
            PublishCurrentPublication(
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
                        targetLookup,
                        result,
                        ref updatedCount),
                    token)
                .ConfigureAwait(false);

            if (IsVisible(generation, token))
            {
                PublishCurrentPublication(
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
                PluginRefreshTargetLookup.Create(selectedTargets),
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
        if (TryApplySelectionChangeToPublication(change, out var publicationSnapshot))
        {
            return publicationSnapshot;
        }

        return ApplySelectionChangeToState(change);
    }

    private bool TryApplySelectionChangeToPublication(
        PluginSelectionChange change,
        out PluginRefreshSnapshot snapshot)
    {
        PluginRefreshPublication publication;
        PluginRefreshDiscoveryFreshnessToken? freshnessToken;
        lock (_snapshotLock)
        {
            publication = _currentPublication;
            freshnessToken = _currentPublicationFreshnessToken;
        }

        if (publication.DiscoveryPlan is null || freshnessToken is null)
        {
            snapshot = null!;
            return false;
        }

        snapshot = GetCurrentSnapshot();
        if (publication.VisibleRows.Count == 0)
        {
            return true;
        }

        IReadOnlyList<PluginRefreshPublishedRow> rows;
        switch (change)
        {
            case PluginSelectionChange.SelectAllVisible:
                rows = publication.Rows
                    .Select(row => row.IsVisible ? row with { IsSelected = true } : row)
                    .ToList();
                break;

            case PluginSelectionChange.DeselectAllVisible:
                rows = publication.Rows
                    .Select(row => row.IsVisible ? row with { IsSelected = false } : row)
                    .ToList();
                break;

            case PluginSelectionChange.SetOne setOne:
            {
                var found = publication.Rows.Any(row => row.IsVisible && IsMatch(row.Key, setOne.Row));
                if (!found)
                {
                    return true;
                }

                rows = publication.Rows
                    .Select(row => row.IsVisible && IsMatch(row.Key, setOne.Row)
                        ? row with { IsSelected = setOne.IsSelected }
                        : row)
                    .ToList();
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(change), change, "Unknown Plugin selection change.");
        }

        var nextPublication = publication with
        {
            Rows = rows,
            VisibleRows = CreateVisibleRows(rows)
        };
        TryPublishSelectionPublicationIfCurrent(publication, nextPublication, freshnessToken, out snapshot);
        return true;
    }

    private PluginRefreshSnapshot ApplySelectionChangeToState(PluginSelectionChange change)
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

        return PublishCurrentPublication(
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

        return PublishCurrentPublication(
            snapshot.Generation,
            snapshot.GameType,
            snapshot.Configuration,
            IdleActivity,
            reason == PluginRefreshCancelReason.Manual
                ? "Approximation refresh canceled."
                : snapshot.StatusText);
    }

    private void PublishNoGameSelected(long generation)
    {
        _stateService.UpdateState(state => state with { CurrentGameType = GameType.Unknown });
        _stateService.SetPluginsToClean([]);
        PublishMissingPublicationFromState(
            generation,
            GameType.Unknown,
            CreateConfigurationProjectionFromState(_stateService.CurrentState),
            IdleActivity,
            "No game selected");
    }

    private void ClearStaleRowsForRefreshStart(GameType gameType)
    {
        // Start/Preview gates read AppState, so clear stale rows before async discovery can be canceled.
        _stateService.UpdateState(state => state with
        {
            CurrentGameType = gameType,
            PluginsToClean = Array.Empty<PluginInfo>().ToList().AsReadOnly(),
            ExcludedPluginPaths = Array.Empty<string>().ToFrozenSet(StringComparer.OrdinalIgnoreCase)
        });
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
        PluginRefreshTargetLookup targetLookup,
        PluginIssueApproximationResult result,
        ref int updatedCount)
    {
        if (!IsVisible(generation, token) || !targetLookup.Contains(result))
        {
            return;
        }

        var matchedRow = TryUpdateAcceptedPublicationRows(
            gameType,
            row => IsMatch(row.Plugin, result),
            row => row with { Plugin = row.Plugin with { Approximation = result.Approximation } },
            () => IsVisible(generation, token));
        if (!matchedRow)
        {
            matchedRow = TryUpdateStateApproximation(generation, token, result);
        }

        if (!matchedRow || !IsVisible(generation, token))
        {
            return;
        }

        var updated = Interlocked.Increment(ref updatedCount);
        PublishCurrentPublication(
            generation,
            gameType,
            configuration,
            activity,
            $"Analyzing {updated} of {targetLookup.Count} selected plugins.");
    }

    private void PublishApproximationFailure(
        long generation,
        CancellationToken token,
        GameType gameType,
        PluginRefreshConfigurationProjection configuration,
        PluginRefreshTargetLookup targetLookup,
        string message)
    {
        if (!IsVisible(generation, token))
        {
            return;
        }

        MarkTargetsUnavailable(gameType, targetLookup, generation, token);
        PublishCurrentPublication(
            generation,
            gameType,
            configuration,
            IdleActivity,
            message);
    }

    private void MarkTargetsPending(
        GameType gameType,
        IReadOnlyList<PluginRefreshRowKey> targets,
        PluginRefreshTargetLookup targetLookup)
    {
        if (TryUpdateAcceptedPublicationRows(
                gameType,
                row => targetLookup.Contains(row.Plugin),
                row => row with { Plugin = row.Plugin with { Approximation = PluginIssueApproximation.Pending } }))
        {
            return;
        }

        _stateService.UpdateState(state =>
        {
            if (state.PluginsToClean.Count == 0)
            {
                var pendingRows = CreateRowsFromTargets(gameType, targets).ToList();
                return state with { PluginsToClean = pendingRows.AsReadOnly() };
            }

            var rows = state.PluginsToClean.Select(plugin =>
                targetLookup.Contains(plugin)
                    ? plugin with { Approximation = PluginIssueApproximation.Pending }
                    : plugin).ToList();

            return state with { PluginsToClean = rows.AsReadOnly() };
        });
    }

    private void MarkTargetsUnavailable(
        GameType gameType,
        PluginRefreshTargetLookup targetLookup,
        long generation,
        CancellationToken token)
    {
        if (TryUpdateAcceptedPublicationRows(
                gameType,
                row => targetLookup.Contains(row.Plugin),
                row => row with { Plugin = row.Plugin with { Approximation = PluginIssueApproximation.Unavailable } },
                () => IsVisible(generation, token)))
        {
            return;
        }

        _stateService.UpdateState(state =>
        {
            if (!IsVisible(generation, token) || state.PluginsToClean.Count == 0)
            {
                return state;
            }

            var rows = state.PluginsToClean.Select(plugin =>
                targetLookup.Contains(plugin)
                    ? plugin with { Approximation = PluginIssueApproximation.Unavailable }
                    : plugin).ToList();

            return state with { PluginsToClean = rows.AsReadOnly() };
        });
    }

    private bool TryUpdateAcceptedPublicationRows(
        GameType gameType,
        Func<PluginRefreshPublishedRow, bool> shouldUpdate,
        Func<PluginRefreshPublishedRow, PluginRefreshPublishedRow> update,
        Func<bool>? canUpdate = null)
    {
        IReadOnlyList<PluginRefreshPublishedRow> rowsToMirror;
        lock (_snapshotLock)
        {
            if (canUpdate is not null && !canUpdate())
            {
                return false;
            }

            if (_currentPublicationFreshnessToken is null ||
                _currentPublication.DiscoveryPlan is null ||
                _currentPublication.DiscoveryPlan.GameType != gameType ||
                _currentPublication.Rows.Count == 0)
            {
                return false;
            }

            var matched = false;
            var rows = _currentPublication.Rows.Select(row =>
            {
                if (!shouldUpdate(row))
                {
                    return row;
                }

                matched = true;
                return update(row);
            }).ToList();

            if (!matched)
            {
                return false;
            }

            _currentPublication = _currentPublication with
            {
                Rows = rows,
                VisibleRows = CreateVisibleRows(rows)
            };
            _currentSnapshot = ToSnapshot(_currentPublication);
            rowsToMirror = rows;
        }

        MirrorPublicationRowsToState(rowsToMirror);
        return true;
    }

    private bool TryUpdateStateApproximation(
        long generation,
        CancellationToken token,
        PluginIssueApproximationResult result)
    {
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

        return matchedRow;
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

    private PluginRefreshSnapshot PublishMissingPublicationFromState(
        long generation,
        GameType gameType,
        PluginRefreshConfigurationProjection configuration,
        PluginRefreshActivity activity,
        string statusText)
    {
        var snapshot = PublishSnapshotFromState(generation, gameType, configuration, activity, statusText);
        lock (_snapshotLock)
        {
            _currentPublication = CreateMissingPublication(snapshot);
            _currentPublicationFreshnessToken = null;
        }

        return snapshot;
    }

    private PluginRefreshSnapshot PublishAcceptedPublication(
        long generation,
        GameType gameType,
        PluginRefreshDiscoveryPlan plan,
        PluginRefreshDiscoveryFreshnessToken freshnessToken,
        PluginRefreshConfigurationProjection configuration,
        IReadOnlyList<PluginRefreshPublishedRow> rows,
        PluginRefreshActivity activity,
        string statusText)
    {
        var visibleRows = CreateVisibleRows(rows);
        var publication = new PluginRefreshPublication(
            generation,
            gameType,
            plan,
            configuration,
            PluginRefreshFreshness.Fresh,
            rows,
            visibleRows,
            activity,
            EmptyCommands,
            statusText);
        return PublishPublicationWithMirroredRows(publication, freshnessToken);
    }

    private PluginRefreshSnapshot PublishCurrentPublication(
        long generation,
        GameType gameType,
        PluginRefreshConfigurationProjection configuration,
        PluginRefreshActivity activity,
        string statusText)
    {
        PluginRefreshPublication publication;
        PluginRefreshDiscoveryFreshnessToken? freshnessToken;
        lock (_snapshotLock)
        {
            publication = _currentPublication;
            freshnessToken = _currentPublicationFreshnessToken;
        }

        if (publication.DiscoveryPlan is null ||
            freshnessToken is null ||
            publication.DiscoveryPlan.GameType != gameType)
        {
            return PublishSnapshotFromState(generation, gameType, configuration, activity, statusText);
        }

        var nextPublication = publication with
        {
            Generation = generation,
            GameType = gameType,
            Configuration = configuration,
            VisibleRows = CreateVisibleRows(publication.Rows),
            Activity = activity,
            StatusText = statusText
        };
        return PublishPublication(nextPublication, freshnessToken, _stateService.CurrentState);
    }

    private PluginRefreshSnapshot PublishPublicationWithMirroredRows(
        PluginRefreshPublication publication,
        PluginRefreshDiscoveryFreshnessToken freshnessToken)
    {
        MirrorPublicationRowsToState(publication.Rows);
        return PublishPublication(publication, freshnessToken, _stateService.CurrentState);
    }

    private bool TryPublishSelectionPublicationIfCurrent(
        PluginRefreshPublication observedPublication,
        PluginRefreshPublication nextPublication,
        PluginRefreshDiscoveryFreshnessToken observedFreshnessToken,
        out PluginRefreshSnapshot snapshot)
    {
        if (_disposed)
        {
            snapshot = GetCurrentSnapshot();
            return false;
        }

        var state = _stateService.CurrentState;
        var commands = CreateCommandAvailability(
            nextPublication.GameType,
            nextPublication.VisibleRows,
            nextPublication.Activity,
            nextPublication.Configuration,
            state.IsCleaning);
        var committedPublication = nextPublication with { Commands = commands };

        lock (_snapshotLock)
        {
            if (!ReferenceEquals(_currentPublicationFreshnessToken, observedFreshnessToken) ||
                _currentPublication.Generation != observedPublication.Generation ||
                _currentPublication.DiscoveryPlan != observedPublication.DiscoveryPlan)
            {
                snapshot = _currentSnapshot;
                return false;
            }

            committedPublication = committedPublication with { Freshness = _currentPublication.Freshness };
            _currentPublication = committedPublication;
            _currentPublicationFreshnessToken = observedFreshnessToken;
            _currentSnapshot = ToSnapshot(committedPublication);
            snapshot = _currentSnapshot;
        }

        MirrorPublicationRowsToState(committedPublication.Rows);
        _snapshots.OnNext(snapshot);
        return true;
    }

    private void MirrorPublicationRowsToState(IReadOnlyList<PluginRefreshPublishedRow> rows)
    {
        if (_stateService.CurrentState.IsCleaning)
        {
            return;
        }

        // AppState remains a compatibility adapter during migration. Do not read it back
        // to reconstruct publication facts; mirror the accepted publication outward instead.
        var plugins = rows.Select(row => row.Plugin).ToList();
        var excludedPaths = rows
            .Where(row => row.IsVisible && !row.IsSelected)
            .Select(row => row.Plugin.FullPath)
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        _stateService.SetPluginsToClean(plugins);
        _stateService.UpdateExcludedPlugins(_ => excludedPaths);
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

    private PluginRefreshSnapshot PublishPublication(
        PluginRefreshPublication publication,
        PluginRefreshDiscoveryFreshnessToken freshnessToken,
        AppState state)
    {
        if (_disposed)
        {
            return GetCurrentSnapshot();
        }

        var commands = CreateCommandAvailability(
            publication.GameType,
            publication.VisibleRows,
            publication.Activity,
            publication.Configuration,
            state.IsCleaning);
        var nextPublication = publication with { Commands = commands };
        var snapshot = ToSnapshot(nextPublication);
        lock (_snapshotLock)
        {
            _currentPublication = nextPublication;
            _currentPublicationFreshnessToken = freshnessToken;
            _currentSnapshot = snapshot;
        }

        _snapshots.OnNext(snapshot);
        return snapshot;
    }

    private PluginRefreshSnapshot GetCurrentSnapshot()
    {
        lock (_snapshotLock)
        {
            return _currentSnapshot;
        }
    }

    private static PluginRefreshSnapshot ToSnapshot(PluginRefreshPublication publication) =>
        new(
            publication.Generation,
            publication.GameType,
            publication.VisibleRows,
            publication.Configuration,
            publication.Activity,
            publication.Commands,
            publication.StatusText);

    private static PluginRefreshPublication CreateMissingPublication(PluginRefreshSnapshot snapshot) =>
        new(
            snapshot.Generation,
            snapshot.GameType,
            DiscoveryPlan: null,
            snapshot.Configuration,
            PluginRefreshFreshness.Missing,
            Rows: [],
            VisibleRows: snapshot.Rows,
            snapshot.Activity,
            snapshot.Commands,
            snapshot.StatusText);

    private PluginRefreshSnapshot WithCommandAvailability(PluginRefreshSnapshot snapshot, AppState state) =>
        snapshot with
        {
            Commands = CreateCommandAvailability(
                snapshot.GameType,
                snapshot.Rows,
                snapshot.Activity,
                snapshot.Configuration,
                state.IsCleaning)
        };

    private PluginRefreshCommandAvailability CreateCommandAvailability(
        GameType gameType,
        IReadOnlyList<PluginRefreshRow> rows,
        PluginRefreshActivity activity,
        PluginRefreshConfigurationProjection configuration,
        bool isCleaning)
    {
        var hasRows = rows.Count > 0;
        var isRunning = activity.IsPluginRefreshRunning || activity.IsIssueApproximationRefreshRunning;
        var canUseRows = hasRows && !isCleaning;
        var affordance = _discoveryPlanner.GetAffordance(gameType, configuration.Mo2ModeEnabled);
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
            Commands: CreateCommandAvailability(
                state.CurrentGameType,
                rows,
                IdleActivity,
                configuration,
                state.IsCleaning),
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

    private static IReadOnlyList<PluginRefreshPublishedRow> CreatePublishedRows(
        IReadOnlyList<SkipListPluginDecision> decisions,
        PluginIssueApproximation initialApproximation,
        IReadOnlySet<string> excludedPaths) =>
        decisions.Select(decision =>
            {
                var plugin = decision.Plugin with { Approximation = initialApproximation };
                return new PluginRefreshPublishedRow(
                    plugin,
                    IsVisible: !decision.ShouldSkipByPolicy,
                    IsSelected: !excludedPaths.Contains(plugin.FullPath),
                    IsSkippedByPolicy: decision.ShouldSkipByPolicy,
                    new PluginRefreshRowKey(plugin.FileName, plugin.FullPath));
            })
            .ToList();

    private static IReadOnlyList<PluginRefreshRow> CreateVisibleRows(
        IReadOnlyList<PluginRefreshPublishedRow> rows) =>
        rows.Where(row => row.IsVisible)
            .Select(row => new PluginRefreshRow(
                row.Plugin.FileName,
                row.Plugin.FullPath,
                row.Plugin.DetectedGameType,
                row.IsSelected,
                row.Plugin.IsInSkipList,
                row.Plugin.Approximation))
            .ToList();

    private void OnAppStateChanged(AppState state)
    {
        if (_disposed)
        {
            return;
        }

        var discoveryAffectingState = DiscoveryAffectingState.From(state);
        if (discoveryAffectingState != _lastDiscoveryAffectingState)
        {
            _lastDiscoveryAffectingState = discoveryAffectingState;
            OnDiscoveryAffectingSettingsChanged();
        }

        if (state.IsCleaning == _lastIsCleaning)
        {
            return;
        }

        _lastIsCleaning = state.IsCleaning;
        PublishCommandAvailabilityIfChanged(state);
    }

    private void PublishCommandAvailabilityIfChanged(AppState state)
    {
        PluginRefreshSnapshot? nextSnapshot = null;
        lock (_snapshotLock)
        {
            var publicationCommands = CreateCommandAvailability(
                _currentPublication.GameType,
                _currentPublication.VisibleRows,
                _currentPublication.Activity,
                _currentPublication.Configuration,
                state.IsCleaning);
            var snapshotCommands = CreateCommandAvailability(
                _currentSnapshot.GameType,
                _currentSnapshot.Rows,
                _currentSnapshot.Activity,
                _currentSnapshot.Configuration,
                state.IsCleaning);

            if (publicationCommands == _currentPublication.Commands &&
                snapshotCommands == _currentSnapshot.Commands)
            {
                return;
            }

            _currentPublication = _currentPublication with { Commands = publicationCommands };
            _currentSnapshot = _currentSnapshot with { Commands = snapshotCommands };
            nextSnapshot = _currentSnapshot;
        }

        _snapshots.OnNext(nextSnapshot!);
    }

    private void OnDiscoveryAffectingSettingsChanged()
    {
        if (_disposed)
        {
            return;
        }

        var requestId = Interlocked.Increment(ref _freshnessRefreshVersion);
        _ = RefreshPublicationFreshnessAsync(requestId);
    }

    private async Task RefreshPublicationFreshnessAsync(int requestId)
    {
        try
        {
            await GetCurrentPublicationWithFreshnessAsync(publishIfChanged: true).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (requestId == Volatile.Read(ref _freshnessRefreshVersion) && !_disposed)
            {
                _logger?.Error(ex, "Failed to evaluate Plugin refresh publication freshness");
            }
        }
    }

    private void PublishFreshnessIfCurrent(
        PluginRefreshPublication observedPublication,
        PluginRefreshDiscoveryFreshnessToken observedFreshnessToken,
        PluginRefreshPublication refreshedPublication)
    {
        PluginRefreshSnapshot snapshot;
        lock (_snapshotLock)
        {
            if (!ReferenceEquals(_currentPublicationFreshnessToken, observedFreshnessToken) ||
                _currentPublication.Generation != observedPublication.Generation ||
                _currentPublication.DiscoveryPlan != observedPublication.DiscoveryPlan ||
                _currentPublication.Freshness == refreshedPublication.Freshness)
            {
                return;
            }

            _currentPublication = _currentPublication with { Freshness = refreshedPublication.Freshness };
            snapshot = _currentSnapshot;
        }

        // Freshness is a publication fact, not a row refresh. Re-emit the current snapshot so callers
        // can re-query publication readiness without AutoQAC changing visible rows underneath them.
        _snapshots.OnNext(snapshot);
    }

    private static PluginRefreshDiscoveryFreshnessContext CreateFreshnessContext(AppState state) =>
        new(
            state.CurrentGameType,
            state.Mo2ModeEnabled,
            state.LoadOrderPath,
            state.Mo2Profile);

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

    private static bool IsMatch(PluginRefreshRow row, PluginRefreshRowKey target)
    {
        if (HasUsablePath(row.FullPath) && HasUsablePath(target.FullPath))
        {
            return string.Equals(row.FullPath, target.FullPath, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(row.FileName, target.FileName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMatch(PluginRefreshRowKey row, PluginRefreshRowKey target)
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

    private static bool HasUsablePath(string? path) => !string.IsNullOrWhiteSpace(path);

    private sealed class PluginRefreshTargetLookup
    {
        private readonly IReadOnlySet<string> _fullPaths;
        private readonly IReadOnlySet<string> _fileNames;
        private readonly IReadOnlySet<string> _pathlessFileNames;

        private PluginRefreshTargetLookup(
            IReadOnlySet<string> fullPaths,
            IReadOnlySet<string> fileNames,
            IReadOnlySet<string> pathlessFileNames,
            int count)
        {
            _fullPaths = fullPaths;
            _fileNames = fileNames;
            _pathlessFileNames = pathlessFileNames;
            Count = count;
        }

        public int Count { get; }

        public static PluginRefreshTargetLookup Create(IReadOnlyList<PluginRefreshRowKey> targets) =>
            new(
                targets.Where(target => HasUsablePath(target.FullPath))
                    .Select(target => target.FullPath)
                    .ToFrozenSet(StringComparer.OrdinalIgnoreCase),
                targets.Select(target => target.FileName)
                    .ToFrozenSet(StringComparer.OrdinalIgnoreCase),
                targets.Where(target => !HasUsablePath(target.FullPath))
                    .Select(target => target.FileName)
                    .ToFrozenSet(StringComparer.OrdinalIgnoreCase),
                targets.Count);

        public bool Contains(PluginInfo plugin) => Contains(plugin.FileName, plugin.FullPath);

        public bool Contains(PluginIssueApproximationResult result) => Contains(result.FileName, result.FullPath);

        private bool Contains(string fileName, string? fullPath) =>
            HasUsablePath(fullPath)
                ? _fullPaths.Contains(fullPath!) || _pathlessFileNames.Contains(fileName)
                : _fileNames.Contains(fileName);
    }

    private sealed record DiscoveryAffectingState(
        GameType CurrentGameType,
        bool Mo2ModeEnabled,
        string? LoadOrderPath,
        string? Mo2Profile)
    {
        public static DiscoveryAffectingState From(AppState state) =>
            new(
                state.CurrentGameType,
                state.Mo2ModeEnabled,
                state.LoadOrderPath,
                state.Mo2Profile);
    }

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

    private sealed class ConfigurationChangedObserver<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(T value) => onNext(value);
    }
}
