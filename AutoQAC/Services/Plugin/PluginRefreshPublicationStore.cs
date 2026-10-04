using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading;
using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.GameCapability;

namespace AutoQAC.Services.Plugin;

/// <summary>
///     Owns the current Plugin refresh publication, visible snapshots, and AppState compatibility mirroring.
/// </summary>
/// <remarks>
///     Refresh work commits against its <see cref="RefreshOperation" /> by identity: an operation may commit only while
///     it is current, and may change estimates only in the publication it owns. Checks on publication content (row
///     identity, freshness token, plan equality, targets) stay here, next to the rows they protect.
/// </remarks>
internal sealed class PluginRefreshPublicationStore : IDisposable
{
    internal static readonly PluginRefreshCommandAvailability EmptyCommands = new(false, false, false, false, false);

    private readonly PluginRefreshAppStateMirror _appStateMirror;
    private readonly PluginRefreshCommandAvailabilityPolicy _commandPolicy;
    private readonly Lock _snapshotLock = new();
    private readonly BehaviorSubject<PluginRefreshSnapshot> _snapshots;
    private ActiveSelectedIssueApproximation? _activeSelectedIssueApproximation;
    private PluginRefreshPublication _currentPublication;
    private PluginRefreshDiscoveryFreshnessToken? _currentPublicationFreshnessToken;

    // The operation that installed _currentPublication; null before any operation has published.
    private RefreshOperation? _publicationOperation;
    private PluginRefreshSnapshot _currentSnapshot;
    private bool _disposed;

    /// <summary>
    ///     Initializes a publication store from the current AppState mirror.
    /// </summary>
    /// <param name="appStateMirror">Compatibility AppState mirror used for fallback rows and legacy consumers.</param>
    /// <param name="commandPolicy">Command availability policy for visible snapshot facts.</param>
    /// <param name="initialAffordance">Game affordance facts for the initial AppState snapshot.</param>
    internal PluginRefreshPublicationStore(
        PluginRefreshAppStateMirror appStateMirror,
        PluginRefreshCommandAvailabilityPolicy commandPolicy,
        PluginRefreshGameAffordance initialAffordance)
    {
        _appStateMirror = appStateMirror;
        _commandPolicy = commandPolicy;

        _currentSnapshot = CreateInitialSnapshot(_appStateMirror.CurrentState, initialAffordance);
        _currentPublication = CreateMissingPublication(_currentSnapshot);
        _snapshots = new BehaviorSubject<PluginRefreshSnapshot>(_currentSnapshot);
    }

    /// <summary>
    ///     Gets the latest visible Plugin refresh snapshots.
    /// </summary>
    internal IObservable<PluginRefreshSnapshot> Snapshots => _snapshots;

    /// <summary>
    ///     Releases the snapshot stream owned by the store.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _snapshots.Dispose();
    }

    /// <summary>
    ///     Test seam invoked after a selection candidate is computed and before its compare-and-swap commit. Tests use
    ///     it to land an approximation commit deterministically inside that window and prove the selection rebases
    ///     onto the newer rows. Always null in production.
    /// </summary>
    internal Action? BeforeSelectionCommit { get; set; }

    /// <summary>
    ///     Gets the current visible snapshot.
    /// </summary>
    /// <returns>The current snapshot.</returns>
    internal PluginRefreshSnapshot GetCurrentSnapshot()
    {
        lock (_snapshotLock)
        {
            return _currentSnapshot;
        }
    }

    /// <summary>
    ///     Gets the current publication and freshness token for a planner freshness check.
    /// </summary>
    /// <returns>Publication inspection facts, with missing freshness applied when no accepted plan exists.</returns>
    internal PluginRefreshPublicationFreshnessInspection GetFreshnessInspection()
    {
        lock (_snapshotLock)
        {
            var publication = _currentPublicationFreshnessToken is null || _currentPublication.DiscoveryPlan is null
                ? _currentPublication with { Freshness = PluginRefreshFreshness.Missing }
                : _currentPublication;
            return new PluginRefreshPublicationFreshnessInspection(
                publication,
                _currentPublicationFreshnessToken);
        }
    }

    /// <summary>
    ///     Whether the current publication was installed by <paramref name="operation" />, so its rows and freshness
    ///     belong to that operation's discovery.
    /// </summary>
    internal bool IsPublishedBy(RefreshOperation operation)
    {
        lock (_snapshotLock)
        {
            return ReferenceEquals(_publicationOperation, operation);
        }
    }

    /// <summary>Removes the freshness lease while retaining visible choices until replacement discovery completes.</summary>
    /// <remarks>
    ///     The caller fences refresh work first by superseding its operation; no older operation can commit again,
    ///     so it cannot restore the revoked lease.
    /// </remarks>
    internal void InvalidatePublication()
    {
        PluginRefreshSnapshot snapshot;
        lock (_snapshotLock)
        {
            _currentPublicationFreshnessToken = null;
            _currentPublication = _currentPublication with { Freshness = PluginRefreshFreshness.Missing };
            snapshot = _currentSnapshot;
        }
        // Readiness observers must re-evaluate even when the rows themselves have not changed.
        if (!_disposed) _snapshots.OnNext(snapshot);
    }

    /// <summary>
    ///     Creates a configuration projection from the current AppState mirror.
    /// </summary>
    /// <returns>A Plugin refresh configuration projection.</returns>
    internal PluginRefreshConfigurationProjection CreateConfigurationProjectionFromCurrentState()
    {
        return PluginRefreshAppStateMirror.CreateConfigurationProjection(_appStateMirror.CurrentState);
    }

    /// <summary>
    ///     Commits refresh-start rows and activity only while the requesting operation is current.
    /// </summary>
    /// <param name="operation">Operation requesting the loading snapshot.</param>
    /// <param name="gameType">Game that is becoming current.</param>
    /// <param name="affordance">Game facts used to project commands.</param>
    /// <returns>Whether the loading snapshot was committed.</returns>
    internal bool TryBeginRefresh(RefreshOperation operation, GameType gameType,
        PluginRefreshGameAffordance affordance)
    {
        lock (_snapshotLock)
        {
            if (_disposed || !operation.IsCurrent) return false;
            var accepted = _currentSnapshot;
            var keepRows = gameType != GameType.Unknown && gameType == accepted.GameType;
            if (!keepRows) _appStateMirror.ClearRowsForRefreshStart(gameType, () => operation.IsCurrent);
            // Clearing the compatibility state invokes observers synchronously; one can start a newer refresh.
            if (!operation.IsCurrent) return false;
            _currentSnapshot = WithCommandAvailability(new PluginRefreshSnapshot(
                operation.Id, gameType, keepRows ? accepted.Rows : [], accepted.Configuration,
                new PluginRefreshActivity(true, false), EmptyCommands,
                gameType == GameType.Unknown ? "No game selected" : $"Loading plugins for {gameType}..."),
                affordance);
            _snapshots.OnNext(_currentSnapshot);
            return true;
        }
    }

    /// <summary>
    ///     Atomically validates and starts selected Issue approximation from one accepted publication.
    /// </summary>
    /// <param name="observedPublication">Fresh publication observed before entering the store lock.</param>
    /// <param name="refresh">Operation that will own selected reanalysis and the publication it commits.</param>
    /// <param name="affordance">Game affordance facts for command projection.</param>
    /// <param name="freshness">Settings observation the freshness of <paramref name="observedPublication" /> rests on.</param>
    /// <returns>The start outcome, including immutable source, target, and prior-estimate facts when started.</returns>
    internal PluginRefreshSelectedIssueApproximationStartResult TryBeginSelectedIssueApproximation(
        PluginRefreshPublication observedPublication,
        RefreshOperation refresh,
        PluginRefreshGameAffordance affordance,
        FreshnessObservation freshness)
    {
        PluginRefreshSnapshot? snapshot = null;
        PluginRefreshSelectedIssueApproximationOperation? selected = null;
        lock (_snapshotLock)
        {
            if (_disposed ||
                !refresh.IsCurrent ||
                !freshness.IsCurrent ||
                !observedPublication.Freshness.IsFresh ||
                !ReferenceEquals(_currentPublication.Rows, observedPublication.Rows) ||
                _currentPublication.DiscoveryPlan is null ||
                _currentPublication.DiscoveryPlan != observedPublication.DiscoveryPlan ||
                _currentPublicationFreshnessToken is null ||
                !_currentPublication.Freshness.IsFresh)
                return new PluginRefreshSelectedIssueApproximationStartResult(
                    PluginRefreshSelectedIssueApproximationStartStatus.Rejected,
                    null);

            var targets = _currentPublication.Rows
                .Where(row => row.IsVisible && row.IsSelected && !row.IsSkippedByPolicy)
                .Select(row => new PluginRefreshSelectedIssueApproximationTarget(
                    row.Key,
                    row.Plugin.Approximation.Status == PluginIssueApproximationStatus.Pending
                        ? PluginIssueApproximation.Unavailable
                        : row.Plugin.Approximation))
                .ToList();
            if (targets.Count == 0)
                return new PluginRefreshSelectedIssueApproximationStartResult(
                    PluginRefreshSelectedIssueApproximationStartStatus.NoTargets,
                    null);

            var targetKeys = targets.Select(target => target.Key).ToList();
            var targetLookup = PluginRefreshPublicationRows.CreateTargetLookup(targetKeys);
            var rowUpdate = PluginRefreshPublicationRows.ApplyApproximationToTargets(
                _currentPublication.Rows,
                targetLookup,
                PluginIssueApproximation.Pending);
            selected = new PluginRefreshSelectedIssueApproximationOperation(
                refresh,
                _currentPublication.DiscoveryPlan,
                _currentPublication.Rows.Select(row => row.Key).ToList(),
                targets);
            _activeSelectedIssueApproximation = new ActiveSelectedIssueApproximation(selected);
            _publicationOperation = refresh;

            var nextPublication = _currentPublication with
            {
                Generation = refresh.Id,
                Rows = rowUpdate.Commit.Rows,
                VisibleRows = rowUpdate.Commit.VisibleRows,
                Activity = new PluginRefreshActivity(
                    false,
                    true),
                StatusText = $"Analyzing 0 of {targets.Count} selected plugins."
            };
            var commands = CreateCommandAvailability(
                nextPublication.GameType,
                nextPublication.VisibleRows,
                nextPublication.Activity,
                nextPublication.Configuration,
                affordance);
            _currentPublication = nextPublication with { Commands = commands };
            _currentSnapshot = ToSnapshot(_currentPublication);
            snapshot = _currentSnapshot;
            // Starting activity, Pending rows, and the compatibility projection form one publication commit.
            _appStateMirror.MirrorRows(rowUpdate.Commit.Mirror);
        }

        _snapshots.OnNext(snapshot!);
        return new PluginRefreshSelectedIssueApproximationStartResult(
            PluginRefreshSelectedIssueApproximationStartStatus.Started,
            selected);
    }

    /// <summary>
    ///     Publishes a visible snapshot projected from the AppState compatibility rows, without an accepted publication.
    /// </summary>
    /// <param name="operation">Operation that must still be current to commit.</param>
    /// <param name="gameType">Game context for the snapshot.</param>
    /// <param name="configuration">Resolved configuration projection.</param>
    /// <param name="activity">Current refresh activity.</param>
    /// <param name="statusText">User-facing status text.</param>
    /// <param name="affordance">Game affordance facts for the snapshot.</param>
    /// <returns>The committed snapshot, or the current one when <paramref name="operation" /> is no longer current.</returns>
    internal PluginRefreshSnapshot PublishSnapshotFromState(
        RefreshOperation operation,
        GameType gameType,
        PluginRefreshConfigurationProjection configuration,
        PluginRefreshActivity activity,
        string statusText,
        PluginRefreshGameAffordance affordance)
    {
        return CommitSnapshotFromState(operation, gameType, configuration, activity, statusText, affordance);
    }

    /// <summary>Commits a snapshot projected from AppState rows for an operation, or for no operation.</summary>
    /// <param name="owner">
    ///     Operation that must still be current, or null when no operation is publishing; the snapshot then keeps the
    ///     visible snapshot's generation.
    /// </param>
    /// <param name="gameType">Game context for the snapshot.</param>
    /// <param name="configuration">Resolved configuration projection.</param>
    /// <param name="activity">Current refresh activity.</param>
    /// <param name="statusText">User-facing status text.</param>
    /// <param name="affordance">Game affordance facts for the snapshot.</param>
    /// <returns>The committed snapshot, or the current one when <paramref name="owner" /> is no longer current.</returns>
    private PluginRefreshSnapshot CommitSnapshotFromState(
        RefreshOperation? owner,
        GameType gameType,
        PluginRefreshConfigurationProjection configuration,
        PluginRefreshActivity activity,
        string statusText,
        PluginRefreshGameAffordance affordance)
    {
        if (_disposed) return GetCurrentSnapshot();

        var state = _appStateMirror.CurrentState;
        var rows = PluginRefreshAppStateMirror.ProjectVisibleRows(state);
        PluginRefreshSnapshot next;
        lock (_snapshotLock)
        {
            if (owner is { IsCurrent: false }) return _currentSnapshot;
            next = WithCommandAvailability(
                new PluginRefreshSnapshot(
                    owner?.Id ?? _currentSnapshot.Generation,
                    gameType,
                    rows,
                    configuration,
                    activity,
                    EmptyCommands,
                    statusText),
                affordance);
            _currentSnapshot = next;
        }

        _snapshots.OnNext(next);
        return next;
    }

    /// <summary>
    ///     Clears compatibility rows and publishes missing-publication facts while the operation is still current.
    /// </summary>
    /// <param name="operation">Operation whose discovery failed or selected no game.</param>
    /// <param name="gameType">Game context for the snapshot.</param>
    /// <param name="configuration">Resolved configuration projection.</param>
    /// <param name="activity">Current refresh activity.</param>
    /// <param name="statusText">User-facing status text.</param>
    /// <param name="affordance">Game affordance facts for the snapshot.</param>
    /// <returns>The committed snapshot.</returns>
    internal PluginRefreshSnapshot PublishMissingPublicationFromState(
        RefreshOperation operation,
        GameType gameType,
        PluginRefreshConfigurationProjection configuration,
        PluginRefreshActivity activity,
        string statusText,
        PluginRefreshGameAffordance affordance)
    {
        lock (_snapshotLock)
        {
            // Failure paths obey the same currency as accepted discovery, including settings invalidation.
            bool CanUpdate() => !_disposed && operation.IsCurrent;
            if (!CanUpdate()) return _currentSnapshot;
            _appStateMirror.ClearRows(CanUpdate);
            // State observers run synchronously and can supersede this operation while rows are cleared.
            if (!CanUpdate()) return _currentSnapshot;
            var state = _appStateMirror.CurrentState;
            var snapshot = WithCommandAvailability(new PluginRefreshSnapshot(
                operation.Id, gameType, PluginRefreshAppStateMirror.ProjectVisibleRows(state),
                configuration, activity, EmptyCommands, statusText), affordance);
            _currentSnapshot = snapshot;
            _currentPublication = CreateMissingPublication(snapshot);
            _publicationOperation = operation;
            _currentPublicationFreshnessToken = null;
            _snapshots.OnNext(snapshot);
            return snapshot;
        }
    }

    /// <summary>
    ///     Publishes an accepted discovery plan and authoritative publication rows while the operation is current.
    /// </summary>
    /// <param name="operation">Operation accepting the publication; it owns the publication once committed.</param>
    /// <param name="gameType">Game context for the publication.</param>
    /// <param name="plan">Accepted discovery plan.</param>
    /// <param name="freshnessToken">Freshness token associated with the accepted plan.</param>
    /// <param name="configuration">Resolved configuration projection.</param>
    /// <param name="rows">Full publication rows, including hidden Skip list rows.</param>
    /// <param name="activity">Current refresh activity.</param>
    /// <param name="statusText">User-facing status text.</param>
    /// <param name="affordance">Game affordance facts for the publication.</param>
    /// <returns>The committed visible snapshot.</returns>
    internal PluginRefreshSnapshot PublishAcceptedPublication(
        RefreshOperation operation,
        GameType gameType,
        PluginRefreshDiscoveryPlan plan,
        PluginRefreshDiscoveryFreshnessToken freshnessToken,
        PluginRefreshConfigurationProjection configuration,
        IReadOnlyList<PluginRefreshPublishedRow> rows,
        PluginRefreshActivity activity,
        string statusText,
        PluginRefreshGameAffordance affordance)
    {
        var rowCommit = PluginRefreshPublicationRows.Commit(rows);
        var publication = new PluginRefreshPublication(
            operation.Id,
            gameType,
            plan,
            configuration,
            PluginRefreshFreshness.Fresh,
            rowCommit.Rows,
            rowCommit.VisibleRows,
            activity,
            EmptyCommands,
            statusText);
        return PublishPublication(operation, publication, freshnessToken, affordance, rowCommit.Mirror);
    }

    /// <summary>
    ///     Publishes the current accepted publication with updated activity, configuration, and status, as an
    ///     operation that takes ownership of the publication.
    /// </summary>
    /// <param name="operation">Operation that must still be current; it owns the publication once committed.</param>
    /// <param name="gameType">Game context for the update.</param>
    /// <param name="configuration">Resolved configuration projection.</param>
    /// <param name="activity">Current refresh activity.</param>
    /// <param name="statusText">User-facing status text.</param>
    /// <param name="affordance">Game affordance facts for the publication.</param>
    /// <returns>The committed visible snapshot.</returns>
    internal PluginRefreshSnapshot PublishCurrentPublication(
        RefreshOperation operation,
        GameType gameType,
        PluginRefreshConfigurationProjection configuration,
        PluginRefreshActivity activity,
        string statusText,
        PluginRefreshGameAffordance affordance)
    {
        return CommitCurrentPublication(operation, gameType, configuration, activity, statusText, affordance);
    }

    /// <summary>
    ///     Republishes the current accepted publication with updated activity, configuration, and status when no
    ///     operation is publishing, such as after cancellation or a fallback selection change. Ownership and the
    ///     visible snapshot's generation are unchanged.
    /// </summary>
    /// <param name="gameType">Game context for the update.</param>
    /// <param name="configuration">Resolved configuration projection.</param>
    /// <param name="activity">Current refresh activity.</param>
    /// <param name="statusText">User-facing status text.</param>
    /// <param name="affordance">Game affordance facts for the publication.</param>
    /// <returns>The committed visible snapshot.</returns>
    internal PluginRefreshSnapshot RepublishCurrentPublication(
        GameType gameType,
        PluginRefreshConfigurationProjection configuration,
        PluginRefreshActivity activity,
        string statusText,
        PluginRefreshGameAffordance affordance)
    {
        return CommitCurrentPublication(null, gameType, configuration, activity, statusText, affordance);
    }

    /// <summary>Commits the current publication with updated activity and status for an operation, or for none.</summary>
    /// <param name="owner">
    ///     Operation that must still be current and takes ownership, or null to keep ownership and the visible
    ///     snapshot's generation.
    /// </param>
    /// <param name="gameType">Game context for the update.</param>
    /// <param name="configuration">Resolved configuration projection.</param>
    /// <param name="activity">Current refresh activity.</param>
    /// <param name="statusText">User-facing status text.</param>
    /// <param name="affordance">Game affordance facts for the publication.</param>
    /// <returns>The committed visible snapshot.</returns>
    private PluginRefreshSnapshot CommitCurrentPublication(
        RefreshOperation? owner,
        GameType gameType,
        PluginRefreshConfigurationProjection configuration,
        PluginRefreshActivity activity,
        string statusText,
        PluginRefreshGameAffordance affordance)
    {
        PluginRefreshPublication publication;
        PluginRefreshDiscoveryFreshnessToken? freshnessToken;
        long snapshotGeneration;
        lock (_snapshotLock)
        {
            publication = _currentPublication;
            freshnessToken = _currentPublicationFreshnessToken;
            snapshotGeneration = owner?.Id ?? _currentSnapshot.Generation;
        }

        if (publication.DiscoveryPlan is null ||
            freshnessToken is null ||
            publication.DiscoveryPlan.GameType != gameType)
            return CommitSnapshotFromState(owner, gameType, configuration, activity, statusText, affordance);

        var nextPublication = publication with
        {
            Generation = snapshotGeneration,
            GameType = gameType,
            Configuration = configuration,
            VisibleRows = PluginRefreshPublicationRows.ProjectVisibleRows(publication.Rows),
            Activity = activity,
            StatusText = statusText
        };
        return PublishPublication(owner, nextPublication, freshnessToken, affordance);
    }

    /// <summary>
    ///     Publishes an idle selected-refresh status only while its operation remains current.
    /// </summary>
    /// <param name="operation">Selected-refresh operation that owns the status.</param>
    /// <param name="statusText">User-facing terminal or rejection status.</param>
    /// <param name="affordance">Game affordance facts for command projection.</param>
    /// <param name="freshness">
    ///     Settings observation the status depends on, if any; a newer Discovery-affecting change rejects the status.
    /// </param>
    /// <returns>True when the status was committed by the current operation.</returns>
    internal bool TryPublishSelectedIdleStatus(
        RefreshOperation operation,
        string statusText,
        PluginRefreshGameAffordance affordance,
        FreshnessObservation? freshness = null)
    {
        PluginRefreshSnapshot snapshot;
        lock (_snapshotLock)
        {
            if (_disposed || !operation.IsCurrent || freshness is { IsCurrent: false }) return false;

            var nextPublication = _currentPublication with
            {
                Generation = operation.Id,
                Activity = new PluginRefreshActivity(false, false),
                StatusText = statusText
            };
            var commands = CreateCommandAvailability(
                nextPublication.GameType,
                nextPublication.VisibleRows,
                nextPublication.Activity,
                nextPublication.Configuration,
                affordance);
            _currentPublication = nextPublication with { Commands = commands };
            _publicationOperation = operation;
            _currentSnapshot = ToSnapshot(_currentPublication);
            snapshot = _currentSnapshot;
        }

        _snapshots.OnNext(snapshot);
        return true;
    }

    /// <summary>
    ///     Applies a visible selection change to the accepted publication or AppState fallback rows.
    /// </summary>
    /// <param name="change">Selection change requested by the UI.</param>
    /// <param name="affordance">Game affordance facts for the current snapshot.</param>
    /// <returns>The current snapshot after applying the request.</returns>
    internal PluginRefreshSnapshot ApplySelectionChange(
        PluginSelectionChange change,
        PluginRefreshGameAffordance affordance)
    {
        if (TryApplySelectionChangeToPublication(change, affordance, out var publicationSnapshot))
            return publicationSnapshot;

        return ApplySelectionChangeToState(change, affordance);
    }

    /// <summary>
    ///     Applies one authoritative keyed result and publishes its progress snapshot as one operation-guarded commit.
    /// </summary>
    /// <param name="operation">Full-refresh operation that owns the accepted publication.</param>
    /// <param name="targetLookup">Authoritative target set for the operation.</param>
    /// <param name="result">Exact keyed result returned by the Issue approximation module.</param>
    /// <param name="statusText">Deterministic progress text for the accepted result.</param>
    /// <param name="affordance">Game affordance facts for command projection.</param>
    /// <returns>True when the exact target result and progress snapshot were committed.</returns>
    internal bool TryPublishInitialApproximationResult(
        RefreshOperation operation,
        PluginRefreshPublicationRows.TargetLookup targetLookup,
        PluginIssueApproximationModuleResult result,
        string statusText,
        PluginRefreshGameAffordance affordance)
    {
        PluginRefreshSnapshot snapshot;
        lock (_snapshotLock)
        {
            if (_disposed ||
                !operation.IsCurrent ||
                !ReferenceEquals(_publicationOperation, operation) ||
                !targetLookup.Contains(result.Target))
                return false;

            var rowUpdate = PluginRefreshPublicationRows.ApplyApproximationResult(
                _currentPublication.Rows,
                result);
            if (!rowUpdate.Matched) return false;

            var nextPublication = _currentPublication with
            {
                Rows = rowUpdate.Commit.Rows,
                VisibleRows = rowUpdate.Commit.VisibleRows,
                StatusText = statusText
            };
            var commands = CreateCommandAvailability(
                nextPublication.GameType,
                nextPublication.VisibleRows,
                nextPublication.Activity,
                nextPublication.Configuration,
                affordance);
            _currentPublication = nextPublication with { Commands = commands };
            _currentSnapshot = ToSnapshot(_currentPublication);
            snapshot = _currentSnapshot;
            // Mirror inside the same commit lock so a superseding operation cannot publish newer
            // rows and then be overwritten by this operation's delayed compatibility projection.
            _appStateMirror.MirrorRows(rowUpdate.Commit.Mirror);
        }

        _snapshots.OnNext(snapshot);
        return true;
    }

    /// <summary>
    ///     Publishes the next exact selected-reanalysis result and its progress as one guarded commit.
    /// </summary>
    /// <param name="operation">Selected-reanalysis operation that owns the callback.</param>
    /// <param name="result">Exact keyed result returned by the Issue approximation module.</param>
    /// <param name="affordance">Game affordance facts for command projection.</param>
    /// <returns>True when the result was the next ordered target and was committed.</returns>
    internal bool TryPublishSelectedApproximationResult(
        RefreshOperation operation,
        PluginIssueApproximationModuleResult result,
        PluginRefreshGameAffordance affordance)
    {
        PluginRefreshSnapshot snapshot;
        lock (_snapshotLock)
        {
            var active = _activeSelectedIssueApproximation;
            if (_disposed ||
                !operation.IsCurrent ||
                !ReferenceEquals(_publicationOperation, operation) ||
                active is null ||
                !ReferenceEquals(active.Operation.Refresh, operation) ||
                active.NextTargetIndex >= active.Operation.Targets.Count ||
                !PluginRefreshPublicationRows.IsExactMatch(
                    active.Operation.Targets[active.NextTargetIndex].Key,
                    result.Target))
                return false;

            var rowUpdate = PluginRefreshPublicationRows.ApplyApproximationResult(
                _currentPublication.Rows,
                result);
            if (!rowUpdate.Matched) return false;

            active.NextTargetIndex++;
            var nextPublication = _currentPublication with
            {
                Rows = rowUpdate.Commit.Rows,
                VisibleRows = rowUpdate.Commit.VisibleRows,
                StatusText =
                $"Analyzing {active.NextTargetIndex} of {active.Operation.Targets.Count} selected plugins."
            };
            var commands = CreateCommandAvailability(
                nextPublication.GameType,
                nextPublication.VisibleRows,
                nextPublication.Activity,
                nextPublication.Configuration,
                affordance);
            _currentPublication = nextPublication with { Commands = commands };
            _currentSnapshot = ToSnapshot(_currentPublication);
            snapshot = _currentSnapshot;
            // The keyed row result and its progress count must remain one operation-owned commit.
            _appStateMirror.MirrorRows(rowUpdate.Commit.Mirror);
        }

        _snapshots.OnNext(snapshot);
        return true;
    }

    /// <summary>
    ///     Finalizes selected reanalysis while preserving completed results and terminalizing unfinished targets.
    /// </summary>
    /// <param name="operation">
    ///     Selected-reanalysis operation to finalize. It may be canceled, but not superseded: supersession already
    ///     finalized it through <see cref="TryFinalizeSupersededOperation" />.
    /// </param>
    /// <param name="disposition">Whether unfinished targets restore their prior estimate or become unavailable.</param>
    /// <param name="statusText">Terminal user-facing status.</param>
    /// <param name="affordance">Game affordance facts for command projection.</param>
    /// <param name="snapshot">Committed terminal snapshot when finalization succeeds.</param>
    /// <returns>True when the operation's active selected reanalysis was finalized.</returns>
    internal bool TryFinalizeSelectedIssueApproximation(
        RefreshOperation operation,
        PluginRefreshSelectedIssueApproximationDisposition disposition,
        string statusText,
        PluginRefreshGameAffordance affordance,
        out PluginRefreshSnapshot snapshot)
    {
        lock (_snapshotLock)
        {
            if (_disposed || operation.IsSuperseded || !IsRunningSelected(operation))
            {
                snapshot = _currentSnapshot;
                return false;
            }

            snapshot = CommitSelectedFinalization(disposition, statusText, affordance);
        }

        _snapshots.OnNext(snapshot);
        return true;
    }

    /// <summary>
    ///     Finalizes a full refresh's unfinished Issue approximation tail, turning its Pending rows Unavailable.
    /// </summary>
    /// <param name="operation">
    ///     Full-refresh operation to finalize. It may be canceled, but not superseded: supersession already finalized
    ///     it through <see cref="TryFinalizeSupersededOperation" />.
    /// </param>
    /// <param name="statusText">Terminal status text.</param>
    /// <param name="affordance">Game affordance facts for command projection.</param>
    /// <param name="snapshot">Committed terminal snapshot when the tail was finalized.</param>
    /// <returns>True when the operation's approximation tail was finalized.</returns>
    internal bool TryFinalizeInitialApproximation(
        RefreshOperation operation,
        string statusText,
        PluginRefreshGameAffordance affordance,
        out PluginRefreshSnapshot snapshot)
    {
        lock (_snapshotLock)
        {
            if (_disposed || operation.IsSuperseded || !IsRunningInitialTail(operation))
            {
                snapshot = _currentSnapshot;
                return false;
            }

            snapshot = CommitInitialFinalization(statusText, affordance);
        }

        _snapshots.OnNext(snapshot);
        return true;
    }

    /// <summary>
    ///     Finalizes a superseded operation's unfinished estimates: selected reanalysis restores prior estimates and an
    ///     initial approximation tail turns Pending rows Unavailable. This is the only commit a superseded operation's
    ///     publication receives; afterward it never commits again.
    /// </summary>
    /// <param name="operation">Operation superseded by a newer operation or a settings fence.</param>
    /// <param name="statusText">Terminal status text.</param>
    /// <param name="affordance">Game affordance facts for command projection.</param>
    /// <param name="snapshot">Committed terminal snapshot when unfinished estimates were finalized.</param>
    /// <returns>True when the superseded operation still owned unfinished estimates and they were finalized.</returns>
    internal bool TryFinalizeSupersededOperation(
        RefreshOperation operation,
        string statusText,
        PluginRefreshGameAffordance affordance,
        out PluginRefreshSnapshot snapshot)
    {
        lock (_snapshotLock)
        {
            if (_disposed || !operation.IsSuperseded)
            {
                snapshot = _currentSnapshot;
                return false;
            }

            if (IsRunningSelected(operation))
                snapshot = CommitSelectedFinalization(
                    PluginRefreshSelectedIssueApproximationDisposition.RestorePrior,
                    statusText,
                    affordance);
            else if (IsRunningInitialTail(operation))
                snapshot = CommitInitialFinalization(statusText, affordance);
            else
            {
                snapshot = _currentSnapshot;
                return false;
            }
        }

        _snapshots.OnNext(snapshot);
        return true;
    }

    /// <summary>Whether the operation owns the publication and its selected reanalysis is still unfinished.</summary>
    private bool IsRunningSelected(RefreshOperation operation)
    {
        return ReferenceEquals(_publicationOperation, operation) &&
               ReferenceEquals(_activeSelectedIssueApproximation?.Operation.Refresh, operation);
    }

    /// <summary>
    ///     Whether the operation owns the publication and its initial approximation tail is still unfinished. The tail
    ///     is the only state in which an accepted publication is both refreshing and analyzing.
    /// </summary>
    private bool IsRunningInitialTail(RefreshOperation operation)
    {
        return ReferenceEquals(_publicationOperation, operation) &&
               _currentPublication.Activity is { IsPluginRefreshRunning: true, IsIssueApproximationRefreshRunning: true };
    }

    /// <summary>Commits the active selected reanalysis's terminal rows and idle activity; caller holds the lock.</summary>
    private PluginRefreshSnapshot CommitSelectedFinalization(
        PluginRefreshSelectedIssueApproximationDisposition disposition,
        string statusText,
        PluginRefreshGameAffordance affordance)
    {
        var active = _activeSelectedIssueApproximation!;
        var rowUpdate = disposition == PluginRefreshSelectedIssueApproximationDisposition.RestorePrior
            ? PluginRefreshPublicationRows.RestorePendingTargetApproximations(
                _currentPublication.Rows,
                active.Operation.Targets)
            : PluginRefreshPublicationRows.ApplyUnavailableToPendingRows(_currentPublication.Rows);
        var rowCommit = rowUpdate.Matched
            ? rowUpdate.Commit
            : PluginRefreshPublicationRows.Commit(_currentPublication.Rows);
        _activeSelectedIssueApproximation = null;
        return CommitTerminalRows(rowCommit, statusText, affordance);
    }

    /// <summary>Commits the initial tail's Unavailable rows and idle activity; caller holds the lock.</summary>
    private PluginRefreshSnapshot CommitInitialFinalization(
        string statusText,
        PluginRefreshGameAffordance affordance)
    {
        var rowUpdate = PluginRefreshPublicationRows.ApplyUnavailableToPendingRows(
            _currentPublication.Rows);
        return CommitTerminalRows(rowUpdate.Commit, statusText, affordance);
    }

    /// <summary>
    ///     Commits finalized rows with idle activity and a terminal status, then mirrors them; caller holds the lock.
    /// </summary>
    /// <param name="rowCommit">Rows whose unfinished estimates are already terminal.</param>
    /// <param name="statusText">Terminal user-facing status.</param>
    /// <param name="affordance">Game affordance facts for command projection.</param>
    /// <returns>The committed snapshot, for the caller to emit after releasing the lock.</returns>
    private PluginRefreshSnapshot CommitTerminalRows(
        PluginRefreshPublicationRowsCommit rowCommit,
        string statusText,
        PluginRefreshGameAffordance affordance)
    {
        var nextPublication = _currentPublication with
        {
            Rows = rowCommit.Rows,
            VisibleRows = rowCommit.VisibleRows,
            Activity = new PluginRefreshActivity(false, false),
            StatusText = statusText
        };
        var commands = CreateCommandAvailability(
            nextPublication.GameType,
            nextPublication.VisibleRows,
            nextPublication.Activity,
            nextPublication.Configuration,
            affordance);
        _currentPublication = nextPublication with { Commands = commands };
        _currentSnapshot = ToSnapshot(_currentPublication);
        // Terminal rows, idle activity, and the compatibility mirror must become visible together as one
        // operation-owned commit; otherwise a replacement refresh can be clobbered afterward.
        _appStateMirror.MirrorRows(rowCommit.Mirror);
        return _currentSnapshot;
    }

    /// <summary>
    ///     Updates command availability after a Cleaning admission transition.
    /// </summary>
    /// <param name="inspection">Publication and snapshot facts observed before affordance lookup.</param>
    /// <param name="publicationAffordance">Game affordance facts for the observed publication.</param>
    /// <param name="snapshotAffordance">Game affordance facts for the observed snapshot.</param>
    /// <returns>True when the inspected publication is still current; false when the caller must inspect again.</returns>
    internal bool PublishCommandAvailabilityIfChanged(
        PluginRefreshCommandAvailabilityInspection inspection,
        PluginRefreshGameAffordance publicationAffordance,
        PluginRefreshGameAffordance snapshotAffordance)
    {
        PluginRefreshSnapshot? nextSnapshot = null;
        lock (_snapshotLock)
        {
            if (_disposed) return true;
            if (!ReferenceEquals(_currentPublication, inspection.Publication) ||
                !ReferenceEquals(_currentSnapshot, inspection.Snapshot))
                return false;

            var publicationCommands = CreateCommandAvailability(
                _currentPublication.GameType,
                _currentPublication.VisibleRows,
                _currentPublication.Activity,
                _currentPublication.Configuration,
                publicationAffordance);
            var snapshotCommands = CreateCommandAvailability(
                _currentSnapshot.GameType,
                _currentSnapshot.Rows,
                _currentSnapshot.Activity,
                _currentSnapshot.Configuration,
                snapshotAffordance);

            if (publicationCommands == _currentPublication.Commands &&
                snapshotCommands == _currentSnapshot.Commands)
                return true;

            _currentPublication = _currentPublication with { Commands = publicationCommands };
            _currentSnapshot = _currentSnapshot with { Commands = snapshotCommands };
            nextSnapshot = _currentSnapshot;
        }

        _snapshots.OnNext(nextSnapshot!);
        return true;
    }

    /// <summary>
    ///     Gets publication and snapshot facts needed to resolve command affordances outside the store.
    /// </summary>
    /// <returns>The current publication and snapshot references.</returns>
    internal PluginRefreshCommandAvailabilityInspection GetCommandAvailabilityInspection()
    {
        lock (_snapshotLock)
        {
            return new PluginRefreshCommandAvailabilityInspection(_currentPublication, _currentSnapshot);
        }
    }

    /// <summary>
    ///     Publishes freshness if the observed publication is still current and the value changed.
    /// </summary>
    /// <param name="observedPublication">Publication observed before the planner freshness check.</param>
    /// <param name="observedFreshnessToken">Freshness token observed before the planner freshness check.</param>
    /// <param name="freshness">Freshness value returned by the planner.</param>
    /// <param name="observation">
    ///     Settings observation the check was made against, if any; a newer Discovery-affecting change rejects the
    ///     verdict at commit time.
    /// </param>
    /// <returns>True when the observation is still current, including an unchanged freshness value.</returns>
    internal bool PublishFreshnessIfCurrent(
        PluginRefreshPublication observedPublication,
        PluginRefreshDiscoveryFreshnessToken observedFreshnessToken,
        PluginRefreshFreshness freshness,
        FreshnessObservation? observation = null)
    {
        PluginRefreshSnapshot snapshot;
        lock (_snapshotLock)
        {
            // Every publication that keeps this token object describes the same accepted discovery, so the verdict
            // applies to it whichever operation last committed rows or status.
            if (_disposed || observation is { IsCurrent: false } ||
                !ReferenceEquals(_currentPublicationFreshnessToken, observedFreshnessToken) ||
                _currentPublication.DiscoveryPlan != observedPublication.DiscoveryPlan)
                return false;

            if (_currentPublication.Freshness == freshness) return true;

            _currentPublication = _currentPublication with { Freshness = freshness };
            snapshot = _currentSnapshot;
        }

        // Freshness is a publication fact, not a row refresh. Re-emit the current snapshot so callers
        // can re-query publication readiness without AutoQAC changing visible rows underneath them.
        _snapshots.OnNext(snapshot);
        return true;
    }

    /// <summary>
    ///     Applies a selection change to the latest accepted publication, retrying when approximation progress
    ///     replaces the observed rows before the selection can commit.
    /// </summary>
    private bool TryApplySelectionChangeToPublication(
        PluginSelectionChange change,
        PluginRefreshGameAffordance affordance,
        out PluginRefreshSnapshot snapshot)
    {
        while (true)
        {
            PluginRefreshPublication publication;
            PluginRefreshDiscoveryFreshnessToken? freshnessToken;
            lock (_snapshotLock)
            {
                publication = _currentPublication;
                freshnessToken = _currentPublicationFreshnessToken;
                snapshot = _currentSnapshot;
                if (_disposed) return true;
            }

            if (publication.DiscoveryPlan is null || freshnessToken is null)
            {
                snapshot = null!;
                return false;
            }

            if (publication.VisibleRows.Count == 0) return true;

            var selection = PluginRefreshPublicationRows.ApplySelectionChange(publication.Rows, change);
            if (!selection.WasTargetFound) return true;

            var nextPublication = publication with
            {
                Rows = selection.Commit.Rows,
                VisibleRows = selection.Commit.VisibleRows
            };
            if (TryPublishSelectionPublicationIfCurrent(
                    publication,
                    nextPublication,
                    freshnessToken,
                    selection.Commit.Mirror,
                    affordance,
                    out snapshot))
                return true;

            // Approximation callbacks replace the rows as whole immutable commits. Rebase the user's selection
            // onto that latest commit instead of reporting success for the rejected stale candidate.
        }
    }

    private PluginRefreshSnapshot ApplySelectionChangeToState(
        PluginSelectionChange change,
        PluginRefreshGameAffordance affordance)
    {
        var snapshot = GetCurrentSnapshot();
        var visibleRows = snapshot.Rows.ToList();
        if (visibleRows.Count == 0) return snapshot;

        var targetFound = _appStateMirror.ApplyStateSelectionChange(visibleRows, change);
        if (!targetFound) return snapshot;

        return RepublishCurrentPublication(
            snapshot.GameType,
            snapshot.Configuration,
            snapshot.Activity,
            snapshot.StatusText,
            affordance);
    }

    /// <summary>
    ///     Commits selection only if the observed publication is still current, preserving streamed approximation
    ///     results and any activity or status committed since the selection was computed.
    /// </summary>
    private bool TryPublishSelectionPublicationIfCurrent(
        PluginRefreshPublication observedPublication,
        PluginRefreshPublication nextPublication,
        PluginRefreshDiscoveryFreshnessToken observedFreshnessToken,
        PluginRefreshPublicationRowsMirror mirror,
        PluginRefreshGameAffordance affordance,
        out PluginRefreshSnapshot snapshot)
    {
        if (_disposed)
        {
            snapshot = GetCurrentSnapshot();
            return false;
        }

        BeforeSelectionCommit?.Invoke();
        var commands = CreateCommandAvailability(
            nextPublication.GameType,
            nextPublication.VisibleRows,
            nextPublication.Activity,
            nextPublication.Configuration,
            affordance);
        var committedPublication = nextPublication with { Commands = commands };

        lock (_snapshotLock)
        {
            // Any commit replaces the publication object, so identity covers rows, freshness, activity, and status.
            if (!ReferenceEquals(_currentPublication, observedPublication) ||
                !ReferenceEquals(_currentPublicationFreshnessToken, observedFreshnessToken))
            {
                snapshot = _currentSnapshot;
                return false;
            }

            // A selection commit edits rows only; the publication keeps its owning operation.
            _currentPublication = committedPublication;
            _currentSnapshot = ToSnapshot(committedPublication);
            snapshot = _currentSnapshot;
            // Keep selection exclusions and rows in the same commit as the publication.
            _appStateMirror.MirrorRows(mirror);
        }

        _snapshots.OnNext(snapshot);
        return true;
    }

    /// <summary>Commits a winning publication and optional compatibility rows under one currency guard.</summary>
    /// <param name="operation">
    ///     Operation that must still be current and takes ownership of the publication, or null to republish the
    ///     current publication without changing its owner.
    /// </param>
    /// <param name="publication">Publication to commit.</param>
    /// <param name="freshnessToken">Freshness token for the publication's accepted discovery plan.</param>
    /// <param name="affordance">Game affordance facts for command projection.</param>
    /// <param name="mirror">Compatibility rows to reconcile and mirror, when the rows themselves are new.</param>
    private PluginRefreshSnapshot PublishPublication(
        RefreshOperation? operation,
        PluginRefreshPublication publication,
        PluginRefreshDiscoveryFreshnessToken freshnessToken,
        PluginRefreshGameAffordance affordance,
        PluginRefreshPublicationRowsMirror? mirror = null)
    {
        if (_disposed) return GetCurrentSnapshot();

        var commands = CreateCommandAvailability(
            publication.GameType,
            publication.VisibleRows,
            publication.Activity,
            publication.Configuration,
            affordance);
        var nextPublication = publication with { Commands = commands };
        var snapshot = ToSnapshot(nextPublication);
        lock (_snapshotLock)
        {
            // A superseded operation never commits; a settings fence supersedes before revoking the freshness lease.
            if (operation is { IsCurrent: false }) return _currentSnapshot;
            if (mirror is not null)
            {
                // Retained rows stay selectable during discovery. Reconcile under the selection commit lock
                // so accepting discovery cannot restore exclusions captured before the user's latest click.
                var currentState = _appStateMirror.CurrentState;
                var exclusions = currentState.ExcludedPluginPaths;
                var retainedPaths = new HashSet<string>(currentState.PluginsToClean.Select(row => row.FullPath),
                    StringComparer.OrdinalIgnoreCase);
                var rowCommit = PluginRefreshPublicationRows.Commit(publication.Rows.Select(row => row with
                {
                    IsSelected = retainedPaths.Contains(row.Plugin.FullPath)
                        ? row.IsSkippedByPolicy || !exclusions.Contains(row.Plugin.FullPath)
                        : row.IsSelected
                }).ToList());
                nextPublication = publication with
                {
                    Rows = rowCommit.Rows,
                    VisibleRows = rowCommit.VisibleRows,
                    Commands = CreateCommandAvailability(publication.GameType, rowCommit.VisibleRows,
                        publication.Activity, publication.Configuration, affordance)
                };
                snapshot = ToSnapshot(nextPublication);
                mirror = rowCommit.Mirror;
            }
            _currentPublication = nextPublication;
            if (operation is not null) _publicationOperation = operation;
            _currentPublicationFreshnessToken = freshnessToken;
            _currentSnapshot = snapshot;
            // Install ownership before mirroring: synchronous state observers can revoke this lease,
            // and no delayed write may restore it or overwrite a newer operation's compatibility rows.
            if (mirror is not null) _appStateMirror.MirrorRows(mirror);
        }

        _snapshots.OnNext(snapshot);
        return snapshot;
    }

    private PluginRefreshSnapshot WithCommandAvailability(
        PluginRefreshSnapshot snapshot,
        PluginRefreshGameAffordance affordance)
    {
        return snapshot with
        {
            Commands = CreateCommandAvailability(
                snapshot.GameType,
                snapshot.Rows,
                snapshot.Activity,
                snapshot.Configuration,
                affordance)
        };
    }

    private PluginRefreshCommandAvailability CreateCommandAvailability(
        GameType gameType,
        IReadOnlyList<PluginRefreshRow> rows,
        PluginRefreshActivity activity,
        PluginRefreshConfigurationProjection configuration,
        PluginRefreshGameAffordance affordance)
    {
        return _commandPolicy.Create(gameType, rows, activity, configuration, affordance);
    }

    private PluginRefreshSnapshot CreateInitialSnapshot(
        AppState state,
        PluginRefreshGameAffordance initialAffordance)
    {
        var rows = PluginRefreshAppStateMirror.ProjectVisibleRows(state);
        var configuration = PluginRefreshAppStateMirror.CreateConfigurationProjection(state);
        return new PluginRefreshSnapshot(
            0,
            state.CurrentGameType,
            rows,
            configuration,
            new PluginRefreshActivity(false, false),
            CreateCommandAvailability(
                state.CurrentGameType,
                rows,
                new PluginRefreshActivity(false, false),
                configuration,
                initialAffordance),
            "Ready");
    }

    private static PluginRefreshSnapshot ToSnapshot(PluginRefreshPublication publication)
    {
        return new PluginRefreshSnapshot(
            publication.Generation,
            publication.GameType,
            publication.VisibleRows,
            publication.Configuration,
            publication.Activity,
            publication.Commands,
            publication.StatusText);
    }

    private static PluginRefreshPublication CreateMissingPublication(PluginRefreshSnapshot snapshot)
    {
        return new PluginRefreshPublication(
            snapshot.Generation,
            snapshot.GameType,
            null,
            snapshot.Configuration,
            PluginRefreshFreshness.Missing,
            [],
            snapshot.Rows,
            snapshot.Activity,
            snapshot.Commands,
            snapshot.StatusText);
    }

    private sealed class ActiveSelectedIssueApproximation(
        PluginRefreshSelectedIssueApproximationOperation operation)
    {
        internal PluginRefreshSelectedIssueApproximationOperation Operation { get; } = operation;

        internal int NextTargetIndex { get; set; }
    }
}

/// <summary>
///     Outcome kinds from atomically starting selected Issue approximation.
/// </summary>
internal enum PluginRefreshSelectedIssueApproximationStartStatus
{
    Started,
    NoTargets,
    Rejected
}

/// <summary>
///     Determines how unfinished selected-reanalysis targets become terminal.
/// </summary>
internal enum PluginRefreshSelectedIssueApproximationDisposition
{
    Unavailable,
    RestorePrior
}

/// <summary>
///     Immutable accepted-publication facts owned by one selected Issue approximation refresh operation.
/// </summary>
/// <param name="Refresh">Refresh operation that owns the selected reanalysis.</param>
/// <param name="Plan">Accepted discovery plan reused without replanning.</param>
/// <param name="SourceRows">Complete ordered publication row identities used as dependency context.</param>
/// <param name="Targets">Ordered selected targets and their prior estimates.</param>
internal sealed record PluginRefreshSelectedIssueApproximationOperation(
    RefreshOperation Refresh,
    PluginRefreshDiscoveryPlan Plan,
    IReadOnlyList<PluginRefreshRowKey> SourceRows,
    IReadOnlyList<PluginRefreshSelectedIssueApproximationTarget> Targets);

/// <summary>
///     One selected target together with the estimate restored if cancellation occurs before its result.
/// </summary>
/// <param name="Key">Exact accepted publication row identity.</param>
/// <param name="PreviousApproximation">Terminal estimate visible before selected reanalysis began.</param>
internal sealed record PluginRefreshSelectedIssueApproximationTarget(
    PluginRefreshRowKey Key,
    PluginIssueApproximation PreviousApproximation);

/// <summary>
///     Result of attempting to start selected Issue approximation from an observed publication.
/// </summary>
/// <param name="Status">Whether the operation started, had no targets, or lost its publication race.</param>
/// <param name="Operation">Accepted operation facts when started.</param>
internal sealed record PluginRefreshSelectedIssueApproximationStartResult(
    PluginRefreshSelectedIssueApproximationStartStatus Status,
    PluginRefreshSelectedIssueApproximationOperation? Operation);

/// <summary>
///     Current publication facts captured before a planner freshness check.
/// </summary>
/// <param name="Publication">Publication observed by the caller.</param>
/// <param name="FreshnessToken">Freshness token for the observed publication, if one exists.</param>
internal sealed record PluginRefreshPublicationFreshnessInspection(
    PluginRefreshPublication Publication,
    PluginRefreshDiscoveryFreshnessToken? FreshnessToken);

/// <summary>
///     Publication and snapshot references used to resolve command affordance facts outside the store lock.
/// </summary>
/// <param name="Publication">Publication observed before command affordance lookup.</param>
/// <param name="Snapshot">Snapshot observed before command affordance lookup.</param>
internal sealed record PluginRefreshCommandAvailabilityInspection(
    PluginRefreshPublication Publication,
    PluginRefreshSnapshot Snapshot);
