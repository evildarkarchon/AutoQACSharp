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
using AutoQAC.Services.GameCapability;
using AutoQAC.Services.State;

namespace AutoQAC.Services.Plugin;

/// <summary>
/// Deep Plugin refresh module that accepts refresh/selection/cancellation intents and emits whole visible snapshots.
/// </summary>
public sealed class PluginRefreshModule : IPluginRefreshModule, IDisposable
{
    private static readonly PluginRefreshActivity IdleActivity = new(false, false);

    private readonly IPluginRefreshDiscoveryPlanner _discoveryPlanner;
    private readonly IPluginIssueApproximationModule _pluginIssueApproximationModule;
    private readonly IStateService _stateService;
    private readonly ISkipListPolicy _skipListPolicy;
    private readonly ILoggingService? _logger;
    private readonly PluginRefreshPublicationStore _publicationStore;
    private readonly IDisposable _stateSubscription;
    private readonly IDisposable? _userConfigurationSubscription;
    private readonly IDisposable? _skipListSubscription;

    private CancellationTokenSource? _activeRefreshCts;
    private long _activeGeneration;
    private int _freshnessRefreshVersion;
    private DiscoveryAffectingState _lastDiscoveryAffectingState;
    private bool _lastIsCleaning;
    private bool _disposed;

    /// <summary>
    /// Initializes a Plugin refresh module with the adapters needed for context assembly, publication, and AppState compatibility.
    /// </summary>
    internal PluginRefreshModule(
        IPluginRefreshDiscoveryPlanner discoveryPlanner,
        IPluginIssueApproximationModule pluginIssueApproximationModule,
        IStateService stateService,
        ISkipListPolicy skipListPolicy,
        PluginRefreshPublicationStore publicationStore,
        ILoggingService? logger = null,
        IConfigurationService? configurationService = null)
    {
        _discoveryPlanner = discoveryPlanner;
        _pluginIssueApproximationModule = pluginIssueApproximationModule;
        _stateService = stateService;
        _skipListPolicy = skipListPolicy;
        _logger = logger;
        _publicationStore = publicationStore;

        _lastIsCleaning = _stateService.CurrentState.IsCleaning;
        _lastDiscoveryAffectingState = DiscoveryAffectingState.From(_stateService.CurrentState);
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
    public IObservable<PluginRefreshSnapshot> Snapshots => _publicationStore.Snapshots;

    /// <inheritdoc />
    public Task<PluginRefreshPublication> GetCurrentPublicationAsync(CancellationToken cancellationToken = default) =>
        GetCurrentPublicationWithFreshnessAsync(publishIfChanged: false, cancellationToken);

    private async Task<PluginRefreshPublication> GetCurrentPublicationWithFreshnessAsync(
        bool publishIfChanged,
        CancellationToken cancellationToken = default)
    {
        var inspection = _publicationStore.GetFreshnessInspection();
        var publication = inspection.Publication;
        var freshnessToken = inspection.FreshnessToken;

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
            _publicationStore.PublishFreshnessIfCurrent(publication, freshnessToken, refreshedPublication.Freshness);
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
        CancelActiveRefresh(PluginRefreshCancelReason.Disposed);
        var cts = Interlocked.Exchange(ref _activeRefreshCts, null);
        cts?.Dispose();
        _publicationStore.Dispose();
    }

    private async Task<PluginRefreshSnapshot> RefreshGameAsync(
        GameType gameType,
        string? selectedLoadOrderPath,
        CancellationToken cancellationToken)
    {
        using var linkedCts = CreateAndActivateGeneration(cancellationToken, out var generation);
        var token = linkedCts.Token;
        var acceptedSnapshot = _publicationStore.GetCurrentSnapshot();
        var configuration = acceptedSnapshot.Configuration;
        var keepExistingRows = gameType != GameType.Unknown && gameType == acceptedSnapshot.GameType;
        if (!keepExistingRows)
        {
            _publicationStore.ClearRowsForRefreshStart(gameType);
        }

        _publicationStore.PublishSnapshot(new PluginRefreshSnapshot(
            generation,
            gameType,
            keepExistingRows ? acceptedSnapshot.Rows : [],
            configuration,
            new PluginRefreshActivity(IsPluginRefreshRunning: true, IsIssueApproximationRefreshRunning: false),
            PluginRefreshPublicationStore.EmptyCommands,
            gameType == GameType.Unknown ? "No game selected" : $"Loading plugins for {gameType}..."),
            GetAffordance(gameType, configuration));

        try
        {
            if (gameType == GameType.Unknown)
            {
                PublishNoGameSelected(generation);
                return _publicationStore.GetCurrentSnapshot();
            }

            var planResult = await _discoveryPlanner.CreatePlanAsync(
                    new PluginRefreshDiscoveryPlanRequest(gameType, selectedLoadOrderPath),
                    token)
                .ConfigureAwait(false);
            configuration = planResult.Configuration;
            if (!IsVisible(generation, token)) return _publicationStore.GetCurrentSnapshot();

            PublishRuntimeConfiguration(configuration, gameType);
            _publicationStore.PublishSnapshotFromState(
                generation,
                gameType,
                configuration,
                new PluginRefreshActivity(IsPluginRefreshRunning: true, IsIssueApproximationRefreshRunning: false),
                $"Loading plugins for {gameType}...",
                GetAffordance(gameType, configuration));

            if (planResult.Plan is null)
            {
                _publicationStore.ClearCompatibilityRows();
                _publicationStore.PublishMissingPublicationFromState(
                    generation,
                    gameType,
                    configuration,
                    IdleActivity,
                    GetPlanStatusText(planResult, gameType),
                    GetAffordance(gameType, configuration));
                return _publicationStore.GetCurrentSnapshot();
            }

            var plan = planResult.Plan;
            var freshnessToken = await _discoveryPlanner.CreateFreshnessTokenAsync(plan, token).ConfigureAwait(false);
            var loadedPlugins = await _discoveryPlanner.LoadPluginsAsync(plan, token).ConfigureAwait(false);
            if (!IsVisible(generation, token)) return _publicationStore.GetCurrentSnapshot();

            if (loadedPlugins.Plugins.Count == 0)
            {
                _publicationStore.PublishAcceptedPublication(
                    generation,
                    gameType,
                    plan,
                    freshnessToken,
                    configuration,
                    [],
                    IdleActivity,
                    GetNoPluginsFoundMessage(plan),
                    GetAffordance(gameType, configuration));
                return _publicationStore.GetCurrentSnapshot();
            }

            var skipEvaluation = await _skipListPolicy.EvaluateAsync(
                    plan.GameType,
                    loadedPlugins.Plugins,
                    plan.DisableSkipLists,
                    token)
                .ConfigureAwait(false);
            if (!IsVisible(generation, token)) return _publicationStore.GetCurrentSnapshot();

            // Hidden Skip-list rows remain dependency context but never become targets, so begin
            // every row terminal and mark only this generation's authoritative targets Pending.
            var acceptedRows = PluginRefreshPublicationRows.Accept(
                skipEvaluation.Decisions,
                PluginIssueApproximation.Unavailable,
                _stateService.CurrentState.ExcludedPluginPaths);
            // Reuse accepted row keys verbatim so result publication cannot drift back to filename correlation.
            var targets = plan.CanAttemptIssueApproximation
                ? acceptedRows.Rows
                    .Where(row => !row.IsSkippedByPolicy)
                    .Select(row => row.Key)
                    .ToList()
                : [];
            var targetLookup = PluginRefreshPublicationRows.CreateTargetLookup(targets);
            var publishedRows = targets.Count == 0
                ? acceptedRows
                : PluginRefreshPublicationRows.ApplyApproximationToTargets(
                    acceptedRows.Rows,
                    targetLookup,
                    PluginIssueApproximation.Pending).Commit;
            var rows = publishedRows.Rows.Select(row => row.Plugin).ToList();
            var issueActivity = targets.Count == 0
                ? IdleActivity
                : new PluginRefreshActivity(
                    IsPluginRefreshRunning: true,
                    IsIssueApproximationRefreshRunning: true);

            _publicationStore.PublishAcceptedPublication(
                generation,
                plan.GameType,
                plan,
                freshnessToken,
                configuration,
                publishedRows.Rows,
                issueActivity,
                plan.CanAttemptIssueApproximation
                    ? targets.Count == 0
                        ? "Refreshed 0 plugin approximations."
                        : $"Analyzing 0 of {targets.Count} plugins."
                    : $"Loading plugins for {plan.GameType}...",
                GetAffordance(plan.GameType, configuration));

            if (!plan.CanAttemptIssueApproximation)
            {
                _publicationStore.PublishCurrentPublication(
                    generation,
                    plan.GameType,
                    configuration,
                    IdleActivity,
                    "Approximation refresh is not available for this game.",
                    GetAffordance(plan.GameType, configuration));
                return _publicationStore.GetCurrentSnapshot();
            }

            if (targets.Count == 0)
            {
                return _publicationStore.GetCurrentSnapshot();
            }

            try
            {
                var dataFolder = ResolveDataFolder(plan, rows.FirstOrDefault()?.FullPath);
                var request = CreateInitialApproximationRequest(
                    plan,
                    dataFolder,
                    publishedRows.Rows,
                    targets);
                var updatedCount = 0;
                await _pluginIssueApproximationModule.AnalyzeAsync(
                        request,
                        result => PublishInitialApproximationResult(
                            generation,
                            token,
                            targetLookup,
                            result,
                            ref updatedCount),
                        token)
                    .ConfigureAwait(false);

                if (IsVisible(generation, token))
                {
                    _publicationStore.TryFinalizeInitialApproximation(
                        generation,
                        $"Refreshed {Volatile.Read(ref updatedCount)} plugin approximations.",
                        GetAffordance(plan.GameType, configuration),
                        () => IsVisible(generation, token),
                        out _);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.Error(ex, "Failed to refresh plugin issue approximations");
                _publicationStore.TryFinalizeInitialApproximation(
                    generation,
                    "Approximation refresh failed.",
                    GetAffordance(plan.GameType, configuration),
                    () => IsVisible(generation, token),
                    out _);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Superseded and lifecycle cancellations are ordinary Plugin refresh control flow.
            var snapshot = _publicationStore.GetCurrentSnapshot();
            _publicationStore.TryFinalizeInitialApproximation(
                generation,
                snapshot.StatusText,
                GetAffordance(snapshot),
                () => generation == Volatile.Read(ref _activeGeneration),
                out _);
        }
        finally
        {
            ReleaseGeneration(linkedCts);
        }

        return _publicationStore.GetCurrentSnapshot();
    }

    /// <summary>
    /// Reanalyzes selected rows from one fresh accepted publication and preserves terminal row state.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the active selected-reanalysis generation.</param>
    /// <returns>The current visible snapshot after rejection, completion, failure, or cancellation.</returns>
    private async Task<PluginRefreshSnapshot> RefreshSelectedIssueApproximationsAsync(
        CancellationToken cancellationToken)
    {
        using var linkedCts = CreateAndActivateGeneration(cancellationToken, out var generation);
        var token = linkedCts.Token;
        var freshnessVersion = Volatile.Read(ref _freshnessRefreshVersion);

        try
        {
            // Selected reanalysis must prove the accepted row identities are still current before
            // any row becomes Pending; rebuilding a plan here would combine new facts with old keys.
            var publication = await GetCurrentPublicationWithFreshnessAsync(
                    publishIfChanged: true,
                    token)
                .ConfigureAwait(false);
            if (!IsVisible(generation, token))
            {
                return _publicationStore.GetCurrentSnapshot();
            }

            var hasFreshnessLease =
                freshnessVersion == Volatile.Read(ref _freshnessRefreshVersion);
            if (!hasFreshnessLease ||
                !publication.Freshness.IsFresh ||
                publication.DiscoveryPlan is null)
            {
                _publicationStore.TryPublishSelectedIdleStatus(
                    generation,
                    "Run a full Plugin refresh before refreshing selected approximations.",
                    GetAffordance(publication),
                    () => IsVisible(generation, token),
                    out _);
                return _publicationStore.GetCurrentSnapshot();
            }

            var plan = publication.DiscoveryPlan;
            var configuration = publication.Configuration;
            if (!plan.CanAttemptIssueApproximation)
            {
                _publicationStore.TryPublishSelectedIdleStatus(
                    generation,
                    "Approximation refresh is not available for this game.",
                    GetAffordance(plan.GameType, configuration),
                    () =>
                        IsVisible(generation, token) &&
                        freshnessVersion == Volatile.Read(ref _freshnessRefreshVersion),
                    out _);
                return _publicationStore.GetCurrentSnapshot();
            }

            var start = _publicationStore.TryBeginSelectedIssueApproximation(
                publication,
                generation,
                GetAffordance(plan.GameType, configuration),
                () =>
                    IsVisible(generation, token) &&
                    freshnessVersion == Volatile.Read(ref _freshnessRefreshVersion));
            if (start.Status == PluginRefreshSelectedIssueApproximationStartStatus.Rejected)
            {
                _publicationStore.TryPublishSelectedIdleStatus(
                    generation,
                    "Run a full Plugin refresh before refreshing selected approximations.",
                    GetAffordance(plan.GameType, configuration),
                    () => IsVisible(generation, token),
                    out _);
                return _publicationStore.GetCurrentSnapshot();
            }

            if (start.Status == PluginRefreshSelectedIssueApproximationStartStatus.NoTargets)
            {
                _publicationStore.TryPublishSelectedIdleStatus(
                    generation,
                    "Select plugins to refresh.",
                    GetAffordance(plan.GameType, configuration),
                    () =>
                        IsVisible(generation, token) &&
                        freshnessVersion == Volatile.Read(ref _freshnessRefreshVersion),
                    out _);
                return _publicationStore.GetCurrentSnapshot();
            }

            var operation = start.Operation!;
            var request = CreateSelectedApproximationRequest(operation);
            var updatedCount = 0;
            await _pluginIssueApproximationModule.AnalyzeAsync(
                    request,
                    result =>
                    {
                        if (_publicationStore.TryPublishSelectedApproximationResult(
                                generation,
                                result,
                                GetAffordance(plan.GameType, configuration),
                                () => IsVisible(generation, token)))
                        {
                            Interlocked.Increment(ref updatedCount);
                        }
                    },
                    token)
                .ConfigureAwait(false);

            if (IsVisible(generation, token))
            {
                _publicationStore.TryFinalizeSelectedIssueApproximation(
                    generation,
                    PluginRefreshSelectedIssueApproximationDisposition.Unavailable,
                    $"Updated {Volatile.Read(ref updatedCount)} selected plugin approximations.",
                    GetAffordance(plan.GameType, configuration),
                    () => IsVisible(generation, token),
                    out _);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            var snapshot = _publicationStore.GetCurrentSnapshot();
            _publicationStore.TryFinalizeSelectedIssueApproximation(
                generation,
                PluginRefreshSelectedIssueApproximationDisposition.RestorePrior,
                "Approximation refresh canceled.",
                GetAffordance(snapshot),
                () => generation == Volatile.Read(ref _activeGeneration),
                out _);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to refresh selected plugin issue approximations");
            var snapshot = _publicationStore.GetCurrentSnapshot();
            _publicationStore.TryFinalizeSelectedIssueApproximation(
                generation,
                PluginRefreshSelectedIssueApproximationDisposition.Unavailable,
                "Approximation refresh failed.",
                GetAffordance(snapshot),
                () => IsVisible(generation, token),
                out _);
        }
        finally
        {
            ReleaseGeneration(linkedCts);
        }

        return _publicationStore.GetCurrentSnapshot();
    }

    private PluginRefreshSnapshot ApplySelectionChange(PluginSelectionChange change)
    {
        var snapshot = _publicationStore.GetCurrentSnapshot();
        return _publicationStore.ApplySelectionChange(change, GetAffordance(snapshot));
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

        var snapshot = _publicationStore.GetCurrentSnapshot();
        var statusText = reason == PluginRefreshCancelReason.Manual
            ? "Approximation refresh canceled."
            : snapshot.StatusText;
        if (!snapshot.Activity.IsPluginRefreshRunning &&
            snapshot.Activity.IsIssueApproximationRefreshRunning &&
            _publicationStore.TryFinalizeSelectedIssueApproximation(
                snapshot.Generation,
                PluginRefreshSelectedIssueApproximationDisposition.RestorePrior,
                statusText,
                GetAffordance(snapshot),
                () => snapshot.Generation == Volatile.Read(ref _activeGeneration),
                out var finalizedSelected))
        {
            return finalizedSelected;
        }

        if (snapshot.Activity.IsPluginRefreshRunning &&
            snapshot.Activity.IsIssueApproximationRefreshRunning &&
            _publicationStore.TryFinalizeInitialApproximation(
                snapshot.Generation,
                statusText,
                GetAffordance(snapshot),
                () => snapshot.Generation == Volatile.Read(ref _activeGeneration),
                out var finalized))
        {
            return finalized;
        }

        if (reason == PluginRefreshCancelReason.Disposed)
        {
            return _publicationStore.GetCurrentSnapshot();
        }

        return _publicationStore.PublishCurrentPublication(
            snapshot.Generation,
            snapshot.GameType,
            snapshot.Configuration,
            IdleActivity,
            statusText,
            GetAffordance(snapshot));
    }

    private void PublishNoGameSelected(long generation)
    {
        _stateService.UpdateState(state => state with { CurrentGameType = GameType.Unknown });
        _publicationStore.ClearCompatibilityRows();
        var configuration = _publicationStore.CreateConfigurationProjectionFromCurrentState();
        _publicationStore.PublishMissingPublicationFromState(
            generation,
            GameType.Unknown,
            configuration,
            IdleActivity,
            "No game selected",
            GetAffordance(GameType.Unknown, configuration));
    }

    private CancellationTokenSource CreateAndActivateGeneration(
        CancellationToken externalToken,
        out long generation)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        var activatedGeneration = Interlocked.Increment(ref _activeGeneration);
        generation = activatedGeneration;
        var previous = Interlocked.Exchange(ref _activeRefreshCts, cts);
        previous?.Cancel(); // do not dispose another generation's live token source
        var snapshot = _publicationStore.GetCurrentSnapshot();
        _publicationStore.TryFinalizeSelectedIssueApproximation(
            snapshot.Generation,
            PluginRefreshSelectedIssueApproximationDisposition.RestorePrior,
            snapshot.StatusText,
            GetAffordance(snapshot),
            () => activatedGeneration == Volatile.Read(ref _activeGeneration),
            out _);
        // The generation flip blocks old callbacks before retained rows are terminalized for the
        // replacement snapshot, preventing an inactive Pending estimate from surviving supersession.
        _publicationStore.FinalizePendingRowsForSupersession(
            () => activatedGeneration == Volatile.Read(ref _activeGeneration));
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

    private PluginRefreshGameAffordance GetAffordance(PluginRefreshSnapshot snapshot) =>
        GetAffordance(snapshot.GameType, snapshot.Configuration);

    private PluginRefreshGameAffordance GetAffordance(PluginRefreshPublication publication) =>
        GetAffordance(publication.GameType, publication.Configuration);

    private PluginRefreshGameAffordance GetAffordance(
        GameType gameType,
        PluginRefreshConfigurationProjection configuration) =>
        _discoveryPlanner.GetAffordance(gameType, configuration.Mo2ModeEnabled);

    private async Task<PluginRefreshDiscoveryPlanResult> GetPlanForSelectedRefreshAsync(CancellationToken ct)
    {
        var currentGame = _stateService.CurrentState.CurrentGameType;
        if (currentGame == GameType.Unknown)
        {
            return new PluginRefreshDiscoveryPlanResult(
                PluginRefreshDiscoveryPlanStatus.NoGameSelected,
                null,
                _publicationStore.GetCurrentSnapshot().Configuration);
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

    /// <summary>
    /// Builds one full-refresh analysis request from the accepted discovery plan and publication rows.
    /// </summary>
    /// <param name="plan">Discovery plan accepted by the active generation.</param>
    /// <param name="dataFolder">Resolved base data folder for import context.</param>
    /// <param name="publicationRows">Complete ordered publication rows, including hidden Skip-list context.</param>
    /// <param name="targets">Ordered non-Skip-list row keys to analyze.</param>
    /// <returns>An authoritative module request that keeps source context separate from targets.</returns>
    private static PluginIssueApproximationModuleRequest CreateInitialApproximationRequest(
        PluginRefreshDiscoveryPlan plan,
        string? dataFolder,
        IReadOnlyList<PluginRefreshPublishedRow> publicationRows,
        IReadOnlyList<PluginRefreshRowKey> targets)
    {
        if (string.IsNullOrWhiteSpace(dataFolder))
        {
            throw new InvalidOperationException(
                "The accepted Plugin refresh plan did not resolve an Issue approximation data folder.");
        }

        // Full refresh already accepted one ordered source. Reusing those exact keys prevents a
        // later direct-load-order enumeration or MO2 conflict change from replacing its context.
        PluginIssueApproximationModuleSource source =
            new PluginIssueApproximationModuleSource.ResolvedLoadOrder(
                dataFolder,
                publicationRows.Select(row => row.Key).ToList());
        return new PluginIssueApproximationModuleRequest(plan.GameType, source, targets);
    }

    /// <summary>
    /// Builds selected reanalysis from the accepted plan, complete source rows, and ordered selected targets.
    /// </summary>
    /// <param name="operation">Atomic selected-reanalysis facts captured from one fresh publication.</param>
    /// <returns>An authoritative keyed module request.</returns>
    private static PluginIssueApproximationModuleRequest CreateSelectedApproximationRequest(
        PluginRefreshSelectedIssueApproximationOperation operation)
    {
        var dataFolder = ResolveDataFolder(
            operation.Plan,
            operation.SourceRows.FirstOrDefault()?.FullPath);
        if (string.IsNullOrWhiteSpace(dataFolder))
        {
            throw new InvalidOperationException(
                "The accepted Plugin refresh publication did not resolve an Issue approximation data folder.");
        }

        // A resolved source is used for direct and MO2 publications alike so no filesystem
        // re-enumeration can replace accepted dependency rows or conflict-winning paths.
        var source = new PluginIssueApproximationModuleSource.ResolvedLoadOrder(
            dataFolder,
            operation.SourceRows);
        return new PluginIssueApproximationModuleRequest(
            operation.Plan.GameType,
            source,
            operation.Targets.Select(target => target.Key).ToList());
    }

    /// <summary>
    /// Publishes one exact keyed result with its deterministic progress count.
    /// </summary>
    /// <param name="generation">Generation that owns the result.</param>
    /// <param name="token">Cancellation boundary for the generation.</param>
    /// <param name="targetLookup">Authoritative target set.</param>
    /// <param name="result">Exact keyed terminal result.</param>
    /// <param name="updatedCount">Count of results already accepted for publication.</param>
    private void PublishInitialApproximationResult(
        long generation,
        CancellationToken token,
        PluginRefreshPublicationRows.TargetLookup targetLookup,
        PluginIssueApproximationModuleResult result,
        ref int updatedCount)
    {
        var nextCount = Volatile.Read(ref updatedCount) + 1;
        if (_publicationStore.TryPublishInitialApproximationResult(
                generation,
                targetLookup,
                result,
                $"Analyzing {nextCount} of {targetLookup.Count} plugins.",
                GetAffordance(_publicationStore.GetCurrentSnapshot()),
                () => IsVisible(generation, token)))
        {
            Volatile.Write(ref updatedCount, nextCount);
        }
    }

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
        var commandInspection = _publicationStore.GetCommandAvailabilityInspection();
        _publicationStore.PublishCommandAvailabilityIfChanged(
            state,
            commandInspection,
            GetAffordance(commandInspection.Publication),
            GetAffordance(commandInspection.Snapshot));
    }

    private void OnDiscoveryAffectingSettingsChanged()
    {
        if (_disposed)
        {
            return;
        }

        var requestId = Interlocked.Increment(ref _freshnessRefreshVersion);
        CancelSelectedApproximationForStaleness();
        _ = RefreshPublicationFreshnessAsync(requestId);
    }

    /// <summary>
    /// Restores unfinished selected targets when Discovery-affecting settings invalidate their freshness lease.
    /// </summary>
    private void CancelSelectedApproximationForStaleness()
    {
        var snapshot = _publicationStore.GetCurrentSnapshot();
        if (snapshot.Activity.IsPluginRefreshRunning ||
            !snapshot.Activity.IsIssueApproximationRefreshRunning)
        {
            return;
        }

        var cts = Volatile.Read(ref _activeRefreshCts);
        if (cts is not null)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // A selected generation can finish while its settings-change notification is being dispatched.
            }
        }

        _publicationStore.TryFinalizeSelectedIssueApproximation(
            snapshot.Generation,
            PluginRefreshSelectedIssueApproximationDisposition.RestorePrior,
            "Run a full Plugin refresh before refreshing selected approximations.",
            GetAffordance(snapshot),
            () => snapshot.Generation == Volatile.Read(ref _activeGeneration),
            out _);
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

    private static PluginRefreshDiscoveryFreshnessContext CreateFreshnessContext(AppState state) =>
        new(
            state.CurrentGameType,
            state.Mo2ModeEnabled,
            state.LoadOrderPath,
            state.Mo2Profile);

    /// <summary>
    /// Resolves the accepted plan's analysis root, using a publication path only as a direct-mode fallback.
    /// </summary>
    /// <param name="plan">Accepted discovery plan that owns the analysis source.</param>
    /// <param name="firstPublicationPath">First accepted row path, when the plan has no explicit direct data folder.</param>
    /// <returns>The resolved analysis data folder, or null when the accepted publication has no usable root.</returns>
    private static string? ResolveDataFolder(
        PluginRefreshDiscoveryPlan plan,
        string? firstPublicationPath)
    {
        if (plan.Mode == PluginRefreshDiscoveryMode.Mo2LoadOrderFile)
        {
            return plan.Mo2BaseDataFolder;
        }

        if (!string.IsNullOrWhiteSpace(plan.DataFolderPath))
        {
            return plan.DataFolderPath;
        }

        return string.IsNullOrWhiteSpace(firstPublicationPath)
            ? null
            : Path.GetDirectoryName(firstPublicationPath);
    }

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
