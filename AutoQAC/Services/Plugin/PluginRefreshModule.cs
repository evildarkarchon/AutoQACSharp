using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameCapability;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;

namespace AutoQAC.Services.Plugin;

/// <summary>
///     Deep Plugin refresh module that accepts refresh/selection/cancellation intents and emits whole visible snapshots.
/// </summary>
public sealed class PluginRefreshModule : IPluginRefreshModule, IDisposable
{
    private static readonly PluginRefreshActivity IdleActivity = new(false, false);
    private const string CanceledStatusText = "Approximation refresh canceled.";

    private readonly IPluginRefreshDiscoveryPlanner _discoveryPlanner;
    private readonly ILoggingService? _logger;
    private readonly IPluginIssueApproximationModule _pluginIssueApproximationModule;
    private readonly PluginRefreshPublicationStore _publicationStore;
    private readonly ISkipListPolicy _skipListPolicy;
    private readonly CleaningAdmission _admission;
    private readonly IDisposable _admissionSubscription;
    private readonly IDisposable? _skipListSubscription;
    private readonly IStateService _stateService;
    private readonly IDisposable _stateSubscription;
    private readonly IDisposable? _userConfigurationSubscription;
    private readonly PluginRefreshFreshnessVersion _freshnessVersion = new();

    // Makes beginning or fencing an operation atomic with finalizing the operation it supersedes, so no successor
    // can publish over rows whose superseded owner has not been finalized yet. Lock is reentrant for observers.
    private readonly Lock _supersessionSync = new();
    private bool _disposed;
    private DiscoveryAffectingState _lastDiscoveryAffectingState;
    private bool _lastCleaningReserved;

    /// <summary>
    ///     Initializes a Plugin refresh module with the adapters needed for context assembly, publication, and AppState
    ///     compatibility.
    /// </summary>
    internal PluginRefreshModule(
        IPluginRefreshDiscoveryPlanner discoveryPlanner,
        IPluginIssueApproximationModule pluginIssueApproximationModule,
        IStateService stateService,
        ISkipListPolicy skipListPolicy,
        PluginRefreshPublicationStore publicationStore,
        CleaningAdmission admission,
        ILoggingService? logger = null,
        IConfigurationService? configurationService = null)
    {
        _discoveryPlanner = discoveryPlanner;
        _pluginIssueApproximationModule = pluginIssueApproximationModule;
        _stateService = stateService;
        _skipListPolicy = skipListPolicy;
        _logger = logger;
        _publicationStore = publicationStore;
        _admission = admission;

        _lastDiscoveryAffectingState = DiscoveryAffectingState.From(_stateService.CurrentState);
        _stateSubscription = _stateService.StateChanged.Subscribe(new StateChangedObserver(OnAppStateChanged));
        if (configurationService is not null)
        {
            _userConfigurationSubscription = configurationService.UserConfigurationChanged.Subscribe(
                new ConfigurationChangedObserver<UserConfiguration>(_ => OnDiscoveryAffectingSettingsChanged()));
            _skipListSubscription = configurationService.SkipListChanged.Subscribe(
                new ConfigurationChangedObserver<GameType>(_ => OnDiscoveryAffectingSettingsChanged()));
        }
        // Subscribe last: the replayed current reservation must see a fully constructed module.
        _admissionSubscription = _admission.CleaningState.Subscribe(
            new CallbackObserver<bool>(OnCleaningAdmissionChanged));
    }

    /// <summary>
    ///     Releases observable and active cancellation resources owned by this singleton module.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        _admissionSubscription.Dispose();
        _stateSubscription.Dispose();
        _userConfigurationSubscription?.Dispose();
        _skipListSubscription?.Dispose();
        CancelActiveRefresh(RefreshCancellationCause.Disposed);
        _publicationStore.Dispose();
    }

    /// <inheritdoc />
    public void InvalidateForSettings()
    {
        lock (_supersessionSync)
        {
            // A same-value retry must still require its own successful publication, so earlier freshness work is stale.
            _freshnessVersion.Advance();
            // Fence without a successor: a failed settings save or an external change may never launch a replacement
            // refresh, so the superseded operation's estimates are finalized here rather than by a successor.
            var superseded = _admission.SupersedeRefresh();
            if (superseded is not null) FinalizeSuperseded(superseded, CanceledStatusText);
            _publicationStore.InvalidatePublication();
        }
    }

    /// <inheritdoc />
    public Task<PluginRefreshCompletion> RefreshForSettingsAsync(GameType gameType, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromResult(new PluginRefreshCompletion(PluginRefreshCompletionStatus.Canceled));
        if (_disposed)
            return Task.FromResult(new PluginRefreshCompletion(PluginRefreshCompletionStatus.Failed));
        // Begin before the first await: the Discovery settings module's settings lease must still order this
        // operation against later settings changes and Cleaning admission.
        var operation = BeginOperation(cancellationToken);
        if (operation is null)
            return Task.FromResult(new PluginRefreshCompletion(PluginRefreshCompletionStatus.Canceled));
        var completion = new TaskCompletionSource<PluginRefreshCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
        // The operation outlives the caller's wait: it keeps running its Issue approximation tail after rows publish.
        _ = ObserveSettingsRefreshAsync(operation, gameType, completion);
        return completion.Task;
    }

    /// <summary>Owns the complete operation lifetime while its caller waits only for correlated publication.</summary>
    /// <param name="operation">Operation begun for this settings refresh; disposed once the refresh fully unwinds.</param>
    /// <param name="gameType">Game selected by the persisted settings.</param>
    /// <param name="completion">Completed with the operation's publication outcome.</param>
    private async Task ObserveSettingsRefreshAsync(RefreshOperation operation, GameType gameType,
        TaskCompletionSource<PluginRefreshCompletion> completion)
    {
        try
        {
            await RefreshGameAsync(operation, gameType, null, completion).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to refresh plugins for Discovery settings");
            completion.TrySetResult(new PluginRefreshCompletion(
                operation.IsCanceled
                    ? PluginRefreshCompletionStatus.Canceled
                    : PluginRefreshCompletionStatus.Failed));
        }
        finally
        {
            operation.Dispose();
        }
    }

    /// <summary>Accepts only this operation's fresh publication; estimate completion is deliberately independent.</summary>
    private async Task CompleteSettingsPublicationAsync(RefreshOperation operation,
        TaskCompletionSource<PluginRefreshCompletion>? completion)
    {
        if (completion is null) return;
        var observation = _freshnessVersion.Observe();
        var publication = await GetCurrentPublicationWithFreshnessAsync(true, operation.Token).ConfigureAwait(false);
        var snapshot = _publicationStore.GetCurrentSnapshot();
        if (!operation.IsCurrent || !observation.IsCurrent || !_publicationStore.IsPublishedBy(operation))
        {
            completion.TrySetResult(new PluginRefreshCompletion(
                !operation.IsSuperseded && operation.IsCanceled
                    ? PluginRefreshCompletionStatus.Canceled : PluginRefreshCompletionStatus.Superseded));
            return;
        }
        completion.TrySetResult(publication.Freshness.IsFresh
            ? new PluginRefreshCompletion(PluginRefreshCompletionStatus.Published, snapshot, publication)
            : new PluginRefreshCompletion(PluginRefreshCompletionStatus.Superseded));
    }

    /// <inheritdoc />
    public IObservable<PluginRefreshSnapshot> Snapshots => _publicationStore.Snapshots;

    /// <inheritdoc />
    public Task<PluginRefreshPublication> GetCurrentPublicationAsync(CancellationToken cancellationToken = default)
    {
        return GetCurrentPublicationWithFreshnessAsync(false, cancellationToken);
    }

    /// <inheritdoc />
    public Task<PluginRefreshSnapshot> ExecuteAsync(
        PluginRefreshIntent intent,
        CancellationToken cancellationToken = default)
    {
        return intent switch
        {
            PluginRefreshIntent.RefreshGame refresh => StartManualRefresh(
                operation => RefreshGameAsync(operation, refresh.GameType, refresh.SelectedLoadOrderPath),
                cancellationToken),
            PluginRefreshIntent.RefreshSelectedIssueApproximations => StartManualRefresh(
                RefreshSelectedIssueApproximationsAsync,
                cancellationToken),
            PluginRefreshIntent.ChangeSelection when IsPluginMutationBlocked =>
                Task.FromResult(_publicationStore.GetCurrentSnapshot()),
            PluginRefreshIntent.ChangeSelection selection => ApplySelectionChangeAsync(
                selection.Change,
                cancellationToken),
            PluginRefreshIntent.Cancel cancel => Task.FromResult(CancelActiveRefresh(cancel.Reason switch
            {
                PluginRefreshCancelReason.Manual => RefreshCancellationCause.Manual,
                PluginRefreshCancelReason.Disposed => RefreshCancellationCause.Disposed,
                _ => throw new ArgumentOutOfRangeException(nameof(intent), cancel.Reason, "Unknown cancel reason.")
            })),
            _ => throw new ArgumentOutOfRangeException(nameof(intent), intent, "Unknown Plugin refresh intent.")
        };
    }

    /// <summary>Evaluates an accepted publication, committing only while its settings observation remains current.</summary>
    /// <param name="publishIfChanged">Whether to publish the evaluated freshness.</param>
    /// <param name="cancellationToken">Cancels the planner check.</param>
    /// <param name="notification">Settings notification to evaluate and, if stale, cancel selected analysis for.</param>
    /// <returns>The observed publication with its evaluated freshness.</returns>
    private async Task<PluginRefreshPublication> GetCurrentPublicationWithFreshnessAsync(
        bool publishIfChanged,
        CancellationToken cancellationToken = default,
        FreshnessObservation? notification = null)
    {
        var observation = notification ?? _freshnessVersion.Observe();
        var inspection = _publicationStore.GetFreshnessInspection();
        var publication = inspection.Publication;
        var freshnessToken = inspection.FreshnessToken;

        if (freshnessToken is null || publication.DiscoveryPlan is null)
            return publication with { Freshness = PluginRefreshFreshness.Missing };

        var freshness = await _discoveryPlanner.CheckFreshnessAsync(
                freshnessToken,
                CreateFreshnessContext(_stateService.CurrentState),
                cancellationToken)
            .ConfigureAwait(false);
        var refreshedPublication = publication with { Freshness = freshness };
        if (publishIfChanged && _publicationStore.PublishFreshnessIfCurrent(
                publication, freshnessToken, refreshedPublication.Freshness, observation) &&
            notification is not null && !freshness.IsFresh && observation.IsCurrent)
            CancelSelectedApproximationForStaleness();

        return refreshedPublication;
    }

    /// <summary>Owns a full refresh operation and its estimates, optionally acknowledging its earlier settings publication.</summary>
    /// <param name="operation">Operation that owns this refresh; its token cancels discovery and remaining approximation work.</param>
    /// <param name="gameType">Game selected by this operation.</param>
    /// <param name="selectedLoadOrderPath">Explicit load order override, or null to resolve configured discovery.</param>
    /// <param name="completion">Optional operation-owned publication completion; never receives another operation's snapshot.</param>
    private async Task<PluginRefreshSnapshot> RefreshGameAsync(
        RefreshOperation operation,
        GameType gameType,
        string? selectedLoadOrderPath,
        TaskCompletionSource<PluginRefreshCompletion>? completion = null)
    {
        var token = operation.Token;
        // Cancellation must release the settings caller even if a discovery adapter ignores its token.
        // Supersession is marked before the token is canceled, so this callback reports it correctly.
        using var completionCancellation = completion is null ? default : token.Register(() =>
            completion.TrySetResult(new PluginRefreshCompletion(
                operation.IsSuperseded
                    ? PluginRefreshCompletionStatus.Superseded : PluginRefreshCompletionStatus.Canceled)));
        var acceptedSnapshot = _publicationStore.GetCurrentSnapshot();
        var configuration = acceptedSnapshot.Configuration;

        try
        {
            if (!_publicationStore.TryBeginRefresh(operation, gameType, GetAffordance(gameType, configuration)))
                return _publicationStore.GetCurrentSnapshot();

            if (gameType == GameType.Unknown)
            {
                token.ThrowIfCancellationRequested();
                PublishNoGameSelected(operation);
                var noGameSnapshot = _publicationStore.GetCurrentSnapshot();
                if (operation.IsCurrent && _publicationStore.IsPublishedBy(operation))
                    completion?.TrySetResult(new PluginRefreshCompletion(PluginRefreshCompletionStatus.NoGame, noGameSnapshot));
                return noGameSnapshot;
            }

            var planResult = await _discoveryPlanner.CreatePlanAsync(
                    new PluginRefreshDiscoveryPlanRequest(gameType, selectedLoadOrderPath),
                    token)
                .ConfigureAwait(false);
            configuration = planResult.Configuration;
            if (!operation.IsCurrent) return _publicationStore.GetCurrentSnapshot();

            PublishRuntimeConfiguration(configuration, gameType, operation);
            if (!operation.IsCurrent) return _publicationStore.GetCurrentSnapshot();
            _publicationStore.PublishSnapshotFromState(
                operation,
                gameType,
                configuration,
                new PluginRefreshActivity(true, false),
                $"Loading plugins for {gameType}...",
                GetAffordance(gameType, configuration));

            if (planResult.Plan is null)
            {
                _publicationStore.PublishMissingPublicationFromState(
                    operation,
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
            if (!operation.IsCurrent) return _publicationStore.GetCurrentSnapshot();

            if (loadedPlugins.LoadingStatus is PluginLoadingStatus.Failed or PluginLoadingStatus.DataFolderNotFound or PluginLoadingStatus.UnsupportedGame)
            {
                // Empty successful load orders are valid; a loader failure must never grant a freshness lease to empty rows.
                _publicationStore.PublishMissingPublicationFromState(
                    operation, gameType, configuration, IdleActivity,
                    "Plugin discovery failed. Check the game configuration and refresh plugins.",
                    GetAffordance(gameType, configuration));
                return _publicationStore.GetCurrentSnapshot();
            }

            if (loadedPlugins.Plugins.Count == 0)
            {
                _publicationStore.PublishAcceptedPublication(
                    operation,
                    gameType,
                    plan,
                    freshnessToken,
                    configuration,
                    [],
                    IdleActivity,
                    GetNoPluginsFoundMessage(plan),
                    GetAffordance(gameType, configuration));
                await CompleteSettingsPublicationAsync(operation, completion).ConfigureAwait(false);
                return _publicationStore.GetCurrentSnapshot();
            }

            var skipEvaluation = await _skipListPolicy.EvaluateAsync(
                    plan.GameType,
                    loadedPlugins.Plugins,
                    plan.DisableSkipLists,
                    token)
                .ConfigureAwait(false);
            if (!operation.IsCurrent) return _publicationStore.GetCurrentSnapshot();
            // Variant detection uses the loaded rows; narrow the original settings snapshot without recapturing edits.
            freshnessToken = freshnessToken.WithVariant(skipEvaluation.Variant);

            // Hidden Skip-list rows remain dependency context but never become targets, so begin
            // every row terminal and mark only this operation's authoritative targets Pending.
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
                    true,
                    true);

            _publicationStore.PublishAcceptedPublication(
                operation,
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

            await CompleteSettingsPublicationAsync(operation, completion).ConfigureAwait(false);

            if (!plan.CanAttemptIssueApproximation)
            {
                _publicationStore.PublishCurrentPublication(
                    operation,
                    plan.GameType,
                    configuration,
                    IdleActivity,
                    "Approximation refresh is not available for this game.",
                    GetAffordance(plan.GameType, configuration));
                return _publicationStore.GetCurrentSnapshot();
            }

            if (targets.Count == 0) return _publicationStore.GetCurrentSnapshot();

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
                            operation,
                            targetLookup,
                            result,
                            ref updatedCount),
                        token)
                    .ConfigureAwait(false);

                if (operation.IsCurrent)
                    _publicationStore.TryFinalizeInitialApproximation(
                        operation,
                        $"Refreshed {Volatile.Read(ref updatedCount)} plugin approximations.",
                        GetAffordance(plan.GameType, configuration),
                        out _);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.Error(ex, "Failed to refresh plugin issue approximations");
                if (operation.IsCurrent)
                    _publicationStore.TryFinalizeInitialApproximation(
                        operation,
                        "Approximation refresh failed.",
                        GetAffordance(plan.GameType, configuration),
                        out _);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Superseded and lifecycle cancellations are ordinary Plugin refresh control flow. Supersession has
            // already finalized this operation, so the store accepts this cleanup only for other cancellations.
            var snapshot = _publicationStore.GetCurrentSnapshot();
            _publicationStore.TryFinalizeInitialApproximation(
                operation,
                snapshot.StatusText,
                GetAffordance(snapshot),
                out _);
        }
        finally
        {
            completion?.TrySetResult(new PluginRefreshCompletion(
                operation.IsSuperseded ? PluginRefreshCompletionStatus.Superseded :
                operation.IsCanceled ? PluginRefreshCompletionStatus.Canceled : PluginRefreshCompletionStatus.Failed));
        }

        return _publicationStore.GetCurrentSnapshot();
    }

    /// <summary>Begins manual Plugin refresh work before it can publish rows or begin analysis.</summary>
    /// <param name="refresh">Full or selected refresh to run under the operation admission issued.</param>
    /// <param name="cancellationToken">Caller cancellation linked into the operation's token.</param>
    /// <returns>The current snapshot if admission is closed, or the refresh result.</returns>
    private Task<PluginRefreshSnapshot> StartManualRefresh(
        Func<RefreshOperation, Task<PluginRefreshSnapshot>> refresh,
        CancellationToken cancellationToken)
    {
        // Begin before starting discovery or analysis: Cleaning must drain work after rows become usable.
        var operation = BeginOperation(cancellationToken);
        if (operation is null) return Task.FromResult(_publicationStore.GetCurrentSnapshot());

        return ObserveManualRefreshAsync(operation, refresh);
    }

    /// <summary>Disposes the operation only after a manual refresh and any canceled importer have unwound.</summary>
    /// <param name="operation">Operation admission issued for this refresh; disposal lets Cleaning continue.</param>
    /// <param name="refresh">Refresh to run under the operation.</param>
    /// <returns>The visible snapshot returned by the refresh.</returns>
    private static async Task<PluginRefreshSnapshot> ObserveManualRefreshAsync(
        RefreshOperation operation,
        Func<RefreshOperation, Task<PluginRefreshSnapshot>> refresh)
    {
        try
        {
            return await refresh(operation).ConfigureAwait(false);
        }
        finally
        {
            operation.Dispose();
        }
    }

    /// <summary>
    ///     Reanalyzes selected rows from one fresh accepted publication and preserves terminal row state.
    /// </summary>
    /// <param name="operation">Operation that owns the selected reanalysis; its token cancels the analysis.</param>
    /// <returns>The current visible snapshot after rejection, completion, failure, or cancellation.</returns>
    private async Task<PluginRefreshSnapshot> RefreshSelectedIssueApproximationsAsync(RefreshOperation operation)
    {
        var token = operation.Token;
        var freshness = _freshnessVersion.Observe();

        try
        {
            // Selected reanalysis must prove the accepted row identities are still current before
            // any row becomes Pending; rebuilding a plan here would combine new facts with old keys.
            var publication = await GetCurrentPublicationWithFreshnessAsync(
                    true,
                    token)
                .ConfigureAwait(false);
            if (!operation.IsCurrent) return _publicationStore.GetCurrentSnapshot();

            if (!freshness.IsCurrent ||
                !publication.Freshness.IsFresh ||
                publication.DiscoveryPlan is null)
            {
                _publicationStore.TryPublishSelectedIdleStatus(
                    operation,
                    "Run a full Plugin refresh before refreshing selected approximations.",
                    GetAffordance(publication));
                return _publicationStore.GetCurrentSnapshot();
            }

            var plan = publication.DiscoveryPlan;
            var configuration = publication.Configuration;
            if (!plan.CanAttemptIssueApproximation)
            {
                _publicationStore.TryPublishSelectedIdleStatus(
                    operation,
                    "Approximation refresh is not available for this game.",
                    GetAffordance(plan.GameType, configuration),
                    freshness);
                return _publicationStore.GetCurrentSnapshot();
            }

            var start = _publicationStore.TryBeginSelectedIssueApproximation(
                publication,
                operation,
                GetAffordance(plan.GameType, configuration),
                freshness);
            if (start.Status == PluginRefreshSelectedIssueApproximationStartStatus.Rejected)
            {
                _publicationStore.TryPublishSelectedIdleStatus(
                    operation,
                    "Run a full Plugin refresh before refreshing selected approximations.",
                    GetAffordance(plan.GameType, configuration));
                return _publicationStore.GetCurrentSnapshot();
            }

            if (start.Status == PluginRefreshSelectedIssueApproximationStartStatus.NoTargets)
            {
                _publicationStore.TryPublishSelectedIdleStatus(
                    operation,
                    "Select plugins to refresh.",
                    GetAffordance(plan.GameType, configuration),
                    freshness);
                return _publicationStore.GetCurrentSnapshot();
            }

            var selected = start.Operation!;
            var request = CreateSelectedApproximationRequest(selected);
            var updatedCount = 0;
            await _pluginIssueApproximationModule.AnalyzeAsync(
                    request,
                    result =>
                    {
                        if (_publicationStore.TryPublishSelectedApproximationResult(
                                operation,
                                result,
                                GetAffordance(plan.GameType, configuration)))
                            Interlocked.Increment(ref updatedCount);
                    },
                    token)
                .ConfigureAwait(false);

            if (operation.IsCurrent)
                _publicationStore.TryFinalizeSelectedIssueApproximation(
                    operation,
                    PluginRefreshSelectedIssueApproximationDisposition.Unavailable,
                    $"Updated {Volatile.Read(ref updatedCount)} selected plugin approximations.",
                    GetAffordance(plan.GameType, configuration),
                    out _);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Supersession has already restored this operation's estimates; the store accepts other cancellations.
            var snapshot = _publicationStore.GetCurrentSnapshot();
            _publicationStore.TryFinalizeSelectedIssueApproximation(
                operation,
                PluginRefreshSelectedIssueApproximationDisposition.RestorePrior,
                CanceledStatusText,
                GetAffordance(snapshot),
                out _);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to refresh selected plugin issue approximations");
            var snapshot = _publicationStore.GetCurrentSnapshot();
            if (operation.IsCurrent)
                _publicationStore.TryFinalizeSelectedIssueApproximation(
                    operation,
                    PluginRefreshSelectedIssueApproximationDisposition.Unavailable,
                    "Approximation refresh failed.",
                    GetAffordance(snapshot),
                    out _);
        }

        return _publicationStore.GetCurrentSnapshot();
    }

    /// <summary>Commits selection only while its mutation lease precedes Cleaning session admission.</summary>
    private async Task<PluginRefreshSnapshot> ApplySelectionChangeAsync(
        PluginSelectionChange change,
        CancellationToken cancellationToken)
    {
        using var mutation = await _admission.TryEnterPluginMutationAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = _publicationStore.GetCurrentSnapshot();
        if (mutation is null) return snapshot;

        // EnterCleaningAsync cannot pass the same mutation lane until this synchronous publication commit completes.
        return _publicationStore.ApplySelectionChange(change, GetAffordance(snapshot));
    }

    private bool IsPluginMutationBlocked => _admission.IsCleaning;

    /// <summary>Cancels the current operation and terminalizes estimates still owned by its publication.</summary>
    /// <param name="cause">Cancellation cause controlling the terminal status text and idle publication.</param>
    /// <returns>The current snapshot after cancellation cleanup.</returns>
    /// <remarks>
    ///     Cancellation is not supersession: the operation stays current in admission until it unwinds, and its
    ///     own cancellation cleanup may still run after this, finding nothing left to finalize.
    /// </remarks>
    private PluginRefreshSnapshot CancelActiveRefresh(RefreshCancellationCause cause)
    {
        lock (_supersessionSync)
        {
            var operation = _admission.CurrentRefresh;
            operation?.Cancel();

            var snapshot = _publicationStore.GetCurrentSnapshot();
            var statusText = cause == RefreshCancellationCause.Manual
                ? CanceledStatusText
                : snapshot.StatusText;
            if (operation is not null)
            {
                if (_publicationStore.TryFinalizeSelectedIssueApproximation(
                        operation,
                        PluginRefreshSelectedIssueApproximationDisposition.RestorePrior,
                        statusText,
                        GetAffordance(snapshot),
                        out var finalizedSelected))
                    return finalizedSelected;

                if (_publicationStore.TryFinalizeInitialApproximation(
                        operation,
                        statusText,
                        GetAffordance(snapshot),
                        out var finalized))
                    return finalized;
            }

            if (cause == RefreshCancellationCause.Disposed) return _publicationStore.GetCurrentSnapshot();

            return _publicationStore.PublishCurrentPublication(
                null,
                snapshot.GameType,
                snapshot.Configuration,
                IdleActivity,
                statusText,
                GetAffordance(snapshot));
        }
    }

    /// <summary>Begins a Plugin refresh operation and finalizes the operation it supersedes.</summary>
    /// <param name="cancellationToken">Caller cancellation linked into the operation's token.</param>
    /// <returns>The new operation, or null when Cleaning has reserved admission.</returns>
    private RefreshOperation? BeginOperation(CancellationToken cancellationToken)
    {
        lock (_supersessionSync)
        {
            var operation = _admission.TryBeginRefresh(cancellationToken, out var superseded);
            if (superseded is not null) FinalizeSuperseded(superseded, null);
            return operation;
        }
    }

    /// <summary>
    ///     The one supersession finalization path, run synchronously when an operation is superseded by a successor
    ///     or by a fence: selected reanalysis restores prior estimates and an initial tail turns Pending rows
    ///     Unavailable. The superseded operation never commits again, so nothing else would end its estimates.
    /// </summary>
    /// <param name="superseded">Operation just marked superseded by admission. Caller holds the supersession lock.</param>
    /// <param name="fenceStatusText">
    ///     Status for a fence with no successor, which must also end any visible activity the superseded operation
    ///     left behind; null when a successor keeps the visible status until it publishes its own.
    /// </param>
    private void FinalizeSuperseded(RefreshOperation superseded, string? fenceStatusText)
    {
        var snapshot = _publicationStore.GetCurrentSnapshot();
        if (_publicationStore.TryFinalizeSupersededOperation(
                superseded,
                fenceStatusText ?? snapshot.StatusText,
                GetAffordance(snapshot),
                out _))
            return;

        // A fenced discovery may have left a loading snapshot that no successor will replace. An operation that
        // already published its terminal status keeps it: nothing visible was canceled.
        if (fenceStatusText is not null &&
            (snapshot.Activity.IsPluginRefreshRunning || snapshot.Activity.IsIssueApproximationRefreshRunning))
            _publicationStore.PublishCurrentPublication(
                null,
                snapshot.GameType,
                snapshot.Configuration,
                IdleActivity,
                fenceStatusText,
                GetAffordance(snapshot));
    }

    /// <summary>Publishes the empty state only while the no-game refresh operation is still current.</summary>
    private void PublishNoGameSelected(RefreshOperation operation)
    {
        // TryBeginRefresh already changed the current game under its operation guard.
        var configuration = _publicationStore.CreateConfigurationProjectionFromCurrentState();
        _publicationStore.PublishMissingPublicationFromState(
            operation,
            GameType.Unknown,
            configuration,
            IdleActivity,
            "No game selected",
            GetAffordance(GameType.Unknown, configuration));
    }

    private PluginRefreshGameAffordance GetAffordance(PluginRefreshSnapshot snapshot)
    {
        return GetAffordance(snapshot.GameType, snapshot.Configuration);
    }

    private PluginRefreshGameAffordance GetAffordance(PluginRefreshPublication publication)
    {
        return GetAffordance(publication.GameType, publication.Configuration);
    }

    private PluginRefreshGameAffordance GetAffordance(
        GameType gameType,
        PluginRefreshConfigurationProjection configuration)
    {
        return _discoveryPlanner.GetAffordance(gameType, configuration.Mo2ModeEnabled);
    }

    /// <summary>Projects an operation's resolved settings in one state update so observers cannot interleave Reset between fields.</summary>
    private void PublishRuntimeConfiguration(
        PluginRefreshConfigurationProjection configuration,
        GameType gameType,
        RefreshOperation operation)
    {
        // The guard runs inside the state update; a superseded operation cannot restore its former game or paths.
        _stateService.UpdateState(state => !operation.IsCurrent ? state : state with
        {
            LoadOrderPath = configuration.Mo2ModeEnabled ? null : configuration.LoadOrderPath,
            Mo2ExecutablePath = configuration.Mo2Path,
            XEditExecutablePath = configuration.XEditPath,
            Mo2Profile = configuration.SelectedProfile,
            CurrentGameType = gameType,
            Mo2ModeEnabled = configuration.Mo2ModeEnabled,
            CleaningTimeout = configuration.CleaningTimeout
        });
    }

    /// <summary>
    ///     Builds one full-refresh analysis request from the accepted discovery plan and publication rows.
    /// </summary>
    /// <param name="plan">Discovery plan accepted by the current operation.</param>
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
            throw new InvalidOperationException(
                "The accepted Plugin refresh plan did not resolve an Issue approximation data folder.");

        // Full refresh already accepted one ordered source. Reusing those exact keys prevents a
        // later direct-load-order enumeration or MO2 conflict change from replacing its context.
        PluginIssueApproximationModuleSource source =
            new PluginIssueApproximationModuleSource.ResolvedLoadOrder(
                dataFolder,
                publicationRows.Select(row => row.Key).ToList());
        return new PluginIssueApproximationModuleRequest(plan.GameType, source, targets);
    }

    /// <summary>
    ///     Builds selected reanalysis from the accepted plan, complete source rows, and ordered selected targets.
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
            throw new InvalidOperationException(
                "The accepted Plugin refresh publication did not resolve an Issue approximation data folder.");

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
    ///     Publishes one exact keyed result with its deterministic progress count.
    /// </summary>
    /// <param name="operation">Operation that owns the result.</param>
    /// <param name="targetLookup">Authoritative target set.</param>
    /// <param name="result">Exact keyed terminal result.</param>
    /// <param name="updatedCount">Count of results already accepted for publication.</param>
    private void PublishInitialApproximationResult(
        RefreshOperation operation,
        PluginRefreshPublicationRows.TargetLookup targetLookup,
        PluginIssueApproximationModuleResult result,
        ref int updatedCount)
    {
        var nextCount = Volatile.Read(ref updatedCount) + 1;
        if (_publicationStore.TryPublishInitialApproximationResult(
                operation,
                targetLookup,
                result,
                $"Analyzing {nextCount} of {targetLookup.Count} plugins.",
                GetAffordance(_publicationStore.GetCurrentSnapshot())))
            Volatile.Write(ref updatedCount, nextCount);
    }

    private void OnAppStateChanged(AppState state)
    {
        if (_disposed) return;

        var discoveryAffectingState = DiscoveryAffectingState.From(state);
        if (discoveryAffectingState == _lastDiscoveryAffectingState) return;

        _lastDiscoveryAffectingState = discoveryAffectingState;
        OnDiscoveryAffectingSettingsChanged();
    }

    /// <summary>Cancels analysis and republishes command policy as soon as Cleaning session admission changes.</summary>
    /// <param name="reserved">The reservation state published by admission on the transitioning thread.</param>
    private void OnCleaningAdmissionChanged(bool reserved)
    {
        if (_disposed) return;
        // Admission replays its current state on subscription; only an actual transition changes commands.
        // Admission publishes transitions serially under its publish lock. The subscribe-time replay is not under
        // that lock, but it runs in the constructor before this module can be observed by anything else.
        if (reserved == _lastCleaningReserved) return;
        _lastCleaningReserved = reserved;

        try
        {
            if (reserved) CancelActiveRefresh(RefreshCancellationCause.CleaningReserved);
        }
        catch (Exception ex)
        {
            LogAdmissionHandlerFailure(ex);
        }

        try
        {
            PublishCurrentCommandAvailability();
        }
        catch (Exception ex)
        {
            LogAdmissionHandlerFailure(ex);
        }
    }

    /// <summary>Re-inspects command facts if freshness or row publication changes during affordance lookup.</summary>
    private void PublishCurrentCommandAvailability()
    {
        while (!_disposed)
        {
            var inspection = _publicationStore.GetCommandAvailabilityInspection();
            var publicationAffordance = GetAffordance(inspection.Publication);
            var snapshotAffordance = GetAffordance(inspection.Snapshot);
            if (_publicationStore.PublishCommandAvailabilityIfChanged(
                    inspection,
                    publicationAffordance,
                    snapshotAffordance))
                return;

            // A concurrent publication can replace the inspected facts without another admission transition.
        }
    }

    /// <summary>Reports an admission callback failure without violating the admission event's no-throw contract.</summary>
    private void LogAdmissionHandlerFailure(Exception exception)
    {
        try
        {
            _logger?.Error(exception, "Failed to update Plugin refresh state for Cleaning admission");
        }
        catch
        {
            // Admission callbacks must never abort Cleaning startup or finalization, even if diagnostics fail.
        }
    }

    private void OnDiscoveryAffectingSettingsChanged()
    {
        if (_disposed) return;

        var notification = _freshnessVersion.Advance();
        // Operational saves also emit configuration notifications; only a confirmed mismatch invalidates analysis.
        _ = RefreshPublicationFreshnessAsync(notification);
    }

    /// <summary>
    ///     Restores unfinished selected targets when Discovery-affecting settings invalidate their freshness lease.
    /// </summary>
    private void CancelSelectedApproximationForStaleness()
    {
        lock (_supersessionSync)
        {
            var operation = _admission.CurrentRefresh;
            if (operation is null) return;

            var snapshot = _publicationStore.GetCurrentSnapshot();
            // Finalize before canceling: the operation's own cancellation cleanup could otherwise run inline and
            // replace this staleness status with the generic canceled status.
            if (_publicationStore.TryFinalizeSelectedIssueApproximation(
                    operation,
                    PluginRefreshSelectedIssueApproximationDisposition.RestorePrior,
                    "Run a full Plugin refresh before refreshing selected approximations.",
                    GetAffordance(snapshot),
                    out _))
                operation.Cancel();
        }
    }

    /// <summary>Checks a settings notification without allowing an older completion to replace a newer verdict.</summary>
    /// <param name="notification">Observation recorded for the settings change being checked.</param>
    private async Task RefreshPublicationFreshnessAsync(FreshnessObservation notification)
    {
        try
        {
            await GetCurrentPublicationWithFreshnessAsync(true, notification: notification).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (notification.IsCurrent && !_disposed)
                _logger?.Error(ex, "Failed to evaluate Plugin refresh publication freshness");
        }
    }

    private static PluginRefreshDiscoveryFreshnessContext CreateFreshnessContext(AppState state)
    {
        return new PluginRefreshDiscoveryFreshnessContext(
            state.CurrentGameType,
            state.Mo2ModeEnabled,
            state.LoadOrderPath,
            state.Mo2Profile);
    }

    /// <summary>
    ///     Resolves the accepted plan's analysis root, using a publication path only as a direct-mode fallback.
    /// </summary>
    /// <param name="plan">Accepted discovery plan that owns the analysis source.</param>
    /// <param name="firstPublicationPath">First accepted row path, when the plan has no explicit direct data folder.</param>
    /// <returns>The resolved analysis data folder, or null when the accepted publication has no usable root.</returns>
    private static string? ResolveDataFolder(
        PluginRefreshDiscoveryPlan plan,
        string? firstPublicationPath)
    {
        if (plan.Mode == PluginRefreshDiscoveryMode.Mo2LoadOrderFile) return plan.Mo2BaseDataFolder;

        if (!string.IsNullOrWhiteSpace(plan.DataFolderPath)) return plan.DataFolderPath;

        return string.IsNullOrWhiteSpace(firstPublicationPath)
            ? null
            : Path.GetDirectoryName(firstPublicationPath);
    }

    private static string GetPlanStatusText(PluginRefreshDiscoveryPlanResult result, GameType gameType)
    {
        return result.Status switch
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
    }

    private static string GetNoPluginsFoundMessage(PluginRefreshDiscoveryPlan plan)
    {
        return plan.Mode == PluginRefreshDiscoveryMode.DirectAutomatic
            ? $"No plugins discovered via Mutagen for {plan.GameType}."
            : "No plugins found in the selected load order.";
    }

    /// <summary>Why the module is canceling its active refresh; Cleaning cancellation is never a caller intent.</summary>
    private enum RefreshCancellationCause
    {
        /// <summary>User or settings supersession canceled the visible refresh.</summary>
        Manual,

        /// <summary>Cleaning admission was reserved and now owns the xEdit-facing workflow.</summary>
        CleaningReserved,

        /// <summary>The module or its owner is being disposed.</summary>
        Disposed
    }

    private sealed record DiscoveryAffectingState(
        GameType CurrentGameType,
        bool Mo2ModeEnabled,
        string? LoadOrderPath,
        string? Mo2Profile)
    {
        public static DiscoveryAffectingState From(AppState state)
        {
            return new DiscoveryAffectingState(
                state.CurrentGameType,
                state.Mo2ModeEnabled,
                state.LoadOrderPath,
                state.Mo2Profile);
        }
    }

    private sealed class StateChangedObserver(Action<AppState> onNext) : IObserver<AppState>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(AppState value)
        {
            onNext(value);
        }
    }

    private sealed class ConfigurationChangedObserver<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(T value)
        {
            onNext(value);
        }
    }
}
