using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading;
using AutoQAC.Models;
using AutoQAC.Services.GameCapability;

namespace AutoQAC.Services.Plugin;

/// <summary>
/// Owns the current Plugin refresh publication, visible snapshots, and AppState compatibility mirroring.
/// </summary>
internal sealed class PluginRefreshPublicationStore : IDisposable
{
    internal static readonly PluginRefreshCommandAvailability EmptyCommands = new(false, false, false, false);

    private readonly PluginRefreshAppStateMirror _appStateMirror;
    private readonly PluginRefreshCommandAvailabilityPolicy _commandPolicy;
    private readonly BehaviorSubject<PluginRefreshSnapshot> _snapshots;
    private readonly Lock _snapshotLock = new();

    private PluginRefreshSnapshot _currentSnapshot;
    private PluginRefreshPublication _currentPublication;
    private PluginRefreshDiscoveryFreshnessToken? _currentPublicationFreshnessToken;
    private ActiveSelectedIssueApproximation? _activeSelectedIssueApproximation;
    private bool _disposed;

    /// <summary>
    /// Initializes a publication store from the current AppState mirror.
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
    /// Gets the latest visible Plugin refresh snapshots.
    /// </summary>
    internal IObservable<PluginRefreshSnapshot> Snapshots => _snapshots;

    /// <summary>
    /// Releases the snapshot stream owned by the store.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _snapshots.Dispose();
    }

    /// <summary>
    /// Gets the current visible snapshot.
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
    /// Gets the current publication and freshness token for a planner freshness check.
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
    /// Creates a configuration projection from the current AppState mirror.
    /// </summary>
    /// <returns>A Plugin refresh configuration projection.</returns>
    internal PluginRefreshConfigurationProjection CreateConfigurationProjectionFromCurrentState() =>
        PluginRefreshAppStateMirror.CreateConfigurationProjection(_appStateMirror.CurrentState);

    /// <summary>
    /// Clears compatibility rows at the start of a refresh that changes game context.
    /// </summary>
    /// <param name="gameType">Game that is becoming current.</param>
    internal void ClearRowsForRefreshStart(GameType gameType) =>
        _appStateMirror.ClearRowsForRefreshStart(gameType);

    /// <summary>
    /// Clears compatibility rows while leaving the current publication fallback to the next publish call.
    /// </summary>
    internal void ClearCompatibilityRows() =>
        _appStateMirror.ClearRows();

    /// <summary>
    /// Selects visible rows currently targeted for issue approximation refresh.
    /// </summary>
    /// <returns>The snapshot used for selection plus selected row identities.</returns>
    internal PluginRefreshSelectedIssueApproximationTargets GetSelectedIssueApproximationTargets()
    {
        var snapshot = GetCurrentSnapshot();
        var targets = snapshot.Rows
            .Where(row => row.IsSelected)
            .Select(row => row.Key)
            .ToList();
        return new PluginRefreshSelectedIssueApproximationTargets(snapshot, targets);
    }

    /// <summary>
    /// Atomically validates and starts selected Issue approximation from one accepted publication.
    /// </summary>
    /// <param name="observedPublication">Fresh publication observed before entering the store lock.</param>
    /// <param name="generation">Generation that will own selected reanalysis.</param>
    /// <param name="affordance">Game affordance facts for command projection.</param>
    /// <param name="canUpdate">Guard proving the selected generation is still active.</param>
    /// <returns>The start outcome, including immutable source, target, and prior-estimate facts when started.</returns>
    internal PluginRefreshSelectedIssueApproximationStartResult TryBeginSelectedIssueApproximation(
        PluginRefreshPublication observedPublication,
        long generation,
        PluginRefreshGameAffordance affordance,
        Func<bool> canUpdate)
    {
        PluginRefreshSnapshot? snapshot = null;
        PluginRefreshSelectedIssueApproximationOperation? operation = null;
        lock (_snapshotLock)
        {
            if (_disposed ||
                !canUpdate() ||
                !observedPublication.Freshness.IsFresh ||
                !ReferenceEquals(_currentPublication.Rows, observedPublication.Rows) ||
                _currentPublication.Generation != observedPublication.Generation ||
                _currentPublication.DiscoveryPlan is null ||
                _currentPublication.DiscoveryPlan != observedPublication.DiscoveryPlan ||
                _currentPublicationFreshnessToken is null ||
                !_currentPublication.Freshness.IsFresh)
            {
                return new PluginRefreshSelectedIssueApproximationStartResult(
                    PluginRefreshSelectedIssueApproximationStartStatus.Rejected,
                    null);
            }

            var targets = _currentPublication.Rows
                .Where(row => row.IsVisible && row.IsSelected && !row.IsSkippedByPolicy)
                .Select(row => new PluginRefreshSelectedIssueApproximationTarget(
                    row.Key,
                    row.Plugin.Approximation.Status == PluginIssueApproximationStatus.Pending
                        ? PluginIssueApproximation.Unavailable
                        : row.Plugin.Approximation))
                .ToList();
            if (targets.Count == 0)
            {
                return new PluginRefreshSelectedIssueApproximationStartResult(
                    PluginRefreshSelectedIssueApproximationStartStatus.NoTargets,
                    null);
            }

            var targetKeys = targets.Select(target => target.Key).ToList();
            var targetLookup = PluginRefreshPublicationRows.CreateTargetLookup(targetKeys);
            var rowUpdate = PluginRefreshPublicationRows.ApplyApproximationToTargets(
                _currentPublication.Rows,
                targetLookup,
                PluginIssueApproximation.Pending);
            operation = new PluginRefreshSelectedIssueApproximationOperation(
                generation,
                _currentPublication.DiscoveryPlan,
                _currentPublication.Rows.Select(row => row.Key).ToList(),
                targets);
            _activeSelectedIssueApproximation = new ActiveSelectedIssueApproximation(operation);

            var nextPublication = _currentPublication with
            {
                Generation = generation,
                Rows = rowUpdate.Commit.Rows,
                VisibleRows = rowUpdate.Commit.VisibleRows,
                Activity = new PluginRefreshActivity(
                    IsPluginRefreshRunning: false,
                    IsIssueApproximationRefreshRunning: true),
                StatusText = $"Analyzing 0 of {targets.Count} selected plugins."
            };
            var commands = CreateCommandAvailability(
                nextPublication.GameType,
                nextPublication.VisibleRows,
                nextPublication.Activity,
                nextPublication.Configuration,
                _appStateMirror.CurrentState.IsCleaning,
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
            operation);
    }

    /// <summary>
    /// Publishes a visible snapshot that is not backed by an accepted publication.
    /// </summary>
    /// <param name="snapshot">Snapshot to publish.</param>
    /// <param name="affordance">Game affordance facts for the snapshot.</param>
    /// <returns>The committed snapshot.</returns>
    internal PluginRefreshSnapshot PublishSnapshot(
        PluginRefreshSnapshot snapshot,
        PluginRefreshGameAffordance affordance) =>
        PublishSnapshot(snapshot, _appStateMirror.CurrentState, affordance);

    /// <summary>
    /// Publishes a visible snapshot projected from the AppState compatibility rows.
    /// </summary>
    /// <param name="generation">Refresh generation associated with the snapshot.</param>
    /// <param name="gameType">Game context for the snapshot.</param>
    /// <param name="configuration">Resolved configuration projection.</param>
    /// <param name="activity">Current refresh activity.</param>
    /// <param name="statusText">User-facing status text.</param>
    /// <param name="affordance">Game affordance facts for the snapshot.</param>
    /// <returns>The committed snapshot.</returns>
    internal PluginRefreshSnapshot PublishSnapshotFromState(
        long generation,
        GameType gameType,
        PluginRefreshConfigurationProjection configuration,
        PluginRefreshActivity activity,
        string statusText,
        PluginRefreshGameAffordance affordance)
    {
        var state = _appStateMirror.CurrentState;
        var rows = PluginRefreshAppStateMirror.ProjectVisibleRows(state);
        return PublishSnapshot(
            new PluginRefreshSnapshot(
                generation,
                gameType,
                rows,
                configuration,
                activity,
                EmptyCommands,
                statusText),
            state,
            affordance);
    }

    /// <summary>
    /// Publishes a missing publication whose visible rows come from AppState compatibility facts.
    /// </summary>
    /// <param name="generation">Refresh generation associated with the snapshot.</param>
    /// <param name="gameType">Game context for the snapshot.</param>
    /// <param name="configuration">Resolved configuration projection.</param>
    /// <param name="activity">Current refresh activity.</param>
    /// <param name="statusText">User-facing status text.</param>
    /// <param name="affordance">Game affordance facts for the snapshot.</param>
    /// <returns>The committed snapshot.</returns>
    internal PluginRefreshSnapshot PublishMissingPublicationFromState(
        long generation,
        GameType gameType,
        PluginRefreshConfigurationProjection configuration,
        PluginRefreshActivity activity,
        string statusText,
        PluginRefreshGameAffordance affordance)
    {
        var snapshot = PublishSnapshotFromState(generation, gameType, configuration, activity, statusText, affordance);
        lock (_snapshotLock)
        {
            _currentPublication = CreateMissingPublication(snapshot);
            _currentPublicationFreshnessToken = null;
        }

        return snapshot;
    }

    /// <summary>
    /// Publishes an accepted discovery plan and authoritative publication rows.
    /// </summary>
    /// <param name="generation">Refresh generation accepting the publication.</param>
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
        long generation,
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
            generation,
            gameType,
            plan,
            configuration,
            PluginRefreshFreshness.Fresh,
            rowCommit.Rows,
            rowCommit.VisibleRows,
            activity,
            EmptyCommands,
            statusText);
        return PublishPublicationWithMirroredRows(publication, freshnessToken, rowCommit.Mirror, affordance);
    }

    /// <summary>
    /// Publishes the current accepted publication with updated activity, configuration, and status.
    /// </summary>
    /// <param name="generation">Refresh generation associated with the update.</param>
    /// <param name="gameType">Game context for the update.</param>
    /// <param name="configuration">Resolved configuration projection.</param>
    /// <param name="activity">Current refresh activity.</param>
    /// <param name="statusText">User-facing status text.</param>
    /// <param name="affordance">Game affordance facts for the publication.</param>
    /// <returns>The committed visible snapshot.</returns>
    internal PluginRefreshSnapshot PublishCurrentPublication(
        long generation,
        GameType gameType,
        PluginRefreshConfigurationProjection configuration,
        PluginRefreshActivity activity,
        string statusText,
        PluginRefreshGameAffordance affordance)
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
            return PublishSnapshotFromState(generation, gameType, configuration, activity, statusText, affordance);
        }

        var nextPublication = publication with
        {
            Generation = generation,
            GameType = gameType,
            Configuration = configuration,
            VisibleRows = PluginRefreshPublicationRows.ProjectVisibleRows(publication.Rows),
            Activity = activity,
            StatusText = statusText
        };
        return PublishPublication(nextPublication, freshnessToken, _appStateMirror.CurrentState, affordance);
    }

    /// <summary>
    /// Publishes an idle selected-refresh status only while its generation remains current.
    /// </summary>
    /// <param name="generation">Selected-refresh generation that owns the status.</param>
    /// <param name="statusText">User-facing terminal or rejection status.</param>
    /// <param name="affordance">Game affordance facts for command projection.</param>
    /// <param name="canUpdate">Guard that rejects canceled or superseded generations.</param>
    /// <param name="snapshot">Committed snapshot when the update succeeds.</param>
    /// <returns>True when the status was committed by the active generation.</returns>
    internal bool TryPublishSelectedIdleStatus(
        long generation,
        string statusText,
        PluginRefreshGameAffordance affordance,
        Func<bool> canUpdate,
        out PluginRefreshSnapshot snapshot)
    {
        lock (_snapshotLock)
        {
            if (_disposed || !canUpdate())
            {
                snapshot = _currentSnapshot;
                return false;
            }

            var nextPublication = _currentPublication with
            {
                Generation = generation,
                Activity = new PluginRefreshActivity(false, false),
                StatusText = statusText
            };
            var commands = CreateCommandAvailability(
                nextPublication.GameType,
                nextPublication.VisibleRows,
                nextPublication.Activity,
                nextPublication.Configuration,
                _appStateMirror.CurrentState.IsCleaning,
                affordance);
            _currentPublication = nextPublication with { Commands = commands };
            _currentSnapshot = ToSnapshot(_currentPublication);
            snapshot = _currentSnapshot;
        }

        _snapshots.OnNext(snapshot);
        return true;
    }

    /// <summary>
    /// Applies a visible selection change to the accepted publication or AppState fallback rows.
    /// </summary>
    /// <param name="change">Selection change requested by the UI.</param>
    /// <param name="affordance">Game affordance facts for the current snapshot.</param>
    /// <returns>The current snapshot after applying the request.</returns>
    internal PluginRefreshSnapshot ApplySelectionChange(
        PluginSelectionChange change,
        PluginRefreshGameAffordance affordance)
    {
        if (TryApplySelectionChangeToPublication(change, affordance, out var publicationSnapshot))
        {
            return publicationSnapshot;
        }

        return ApplySelectionChangeToState(change, affordance);
    }

    /// <summary>
    /// Applies one authoritative keyed result and publishes its progress snapshot as one generation-guarded commit.
    /// </summary>
    /// <param name="generation">Accepted full-refresh generation.</param>
    /// <param name="targetLookup">Authoritative target set for the generation.</param>
    /// <param name="result">Exact keyed result returned by the Issue approximation module.</param>
    /// <param name="statusText">Deterministic progress text for the accepted result.</param>
    /// <param name="affordance">Game affordance facts for command projection.</param>
    /// <param name="canUpdate">Guard that rejects canceled or superseded generations.</param>
    /// <returns>True when the exact target result and progress snapshot were committed.</returns>
    internal bool TryPublishInitialApproximationResult(
        long generation,
        PluginRefreshPublicationRows.TargetLookup targetLookup,
        PluginIssueApproximationModuleResult result,
        string statusText,
        PluginRefreshGameAffordance affordance,
        Func<bool> canUpdate)
    {
        PluginRefreshSnapshot snapshot;
        lock (_snapshotLock)
        {
            if (_disposed ||
                !canUpdate() ||
                _currentPublication.Generation != generation ||
                !_currentPublication.Activity.IsPluginRefreshRunning ||
                !_currentPublication.Activity.IsIssueApproximationRefreshRunning ||
                !targetLookup.Contains(result.Target))
            {
                return false;
            }

            var rowUpdate = PluginRefreshPublicationRows.ApplyApproximationResult(
                _currentPublication.Rows,
                result);
            if (!rowUpdate.Matched)
            {
                return false;
            }

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
                _appStateMirror.CurrentState.IsCleaning,
                affordance);
            _currentPublication = nextPublication with { Commands = commands };
            _currentSnapshot = ToSnapshot(_currentPublication);
            snapshot = _currentSnapshot;
            // Mirror inside the same commit lock so a superseding generation cannot publish newer
            // rows and then be overwritten by this generation's delayed compatibility projection.
            _appStateMirror.MirrorRows(rowUpdate.Commit.Mirror);
        }

        _snapshots.OnNext(snapshot);
        return true;
    }

    /// <summary>
    /// Publishes the next exact selected-reanalysis result and its progress as one guarded commit.
    /// </summary>
    /// <param name="generation">Selected-reanalysis generation that owns the callback.</param>
    /// <param name="result">Exact keyed result returned by the Issue approximation module.</param>
    /// <param name="affordance">Game affordance facts for command projection.</param>
    /// <param name="canUpdate">Guard that rejects canceled or superseded generations.</param>
    /// <returns>True when the result was the next ordered target and was committed.</returns>
    internal bool TryPublishSelectedApproximationResult(
        long generation,
        PluginIssueApproximationModuleResult result,
        PluginRefreshGameAffordance affordance,
        Func<bool> canUpdate)
    {
        PluginRefreshSnapshot snapshot;
        lock (_snapshotLock)
        {
            var active = _activeSelectedIssueApproximation;
            if (_disposed ||
                !canUpdate() ||
                active is null ||
                active.Operation.Generation != generation ||
                _currentPublication.Generation != generation ||
                _currentPublication.Activity.IsPluginRefreshRunning ||
                !_currentPublication.Activity.IsIssueApproximationRefreshRunning ||
                active.NextTargetIndex >= active.Operation.Targets.Count ||
                !PluginRefreshPublicationRows.IsExactMatch(
                    active.Operation.Targets[active.NextTargetIndex].Key,
                    result.Target))
            {
                return false;
            }

            var rowUpdate = PluginRefreshPublicationRows.ApplyApproximationResult(
                _currentPublication.Rows,
                result);
            if (!rowUpdate.Matched)
            {
                return false;
            }

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
                _appStateMirror.CurrentState.IsCleaning,
                affordance);
            _currentPublication = nextPublication with { Commands = commands };
            _currentSnapshot = ToSnapshot(_currentPublication);
            snapshot = _currentSnapshot;
            // The keyed row result and its progress count must remain one generation-owned commit.
            _appStateMirror.MirrorRows(rowUpdate.Commit.Mirror);
        }

        _snapshots.OnNext(snapshot);
        return true;
    }

    /// <summary>
    /// Finalizes selected reanalysis while preserving completed results and terminalizing unfinished targets.
    /// </summary>
    /// <param name="generation">Selected-reanalysis generation to finalize.</param>
    /// <param name="disposition">Whether unfinished targets restore their prior estimate or become unavailable.</param>
    /// <param name="statusText">Terminal user-facing status.</param>
    /// <param name="affordance">Game affordance facts for command projection.</param>
    /// <param name="canUpdate">Guard proving the requested terminalization is still current.</param>
    /// <param name="snapshot">Committed terminal snapshot when finalization succeeds.</param>
    /// <returns>True when the active selected operation was finalized.</returns>
    internal bool TryFinalizeSelectedIssueApproximation(
        long generation,
        PluginRefreshSelectedIssueApproximationDisposition disposition,
        string statusText,
        PluginRefreshGameAffordance affordance,
        Func<bool> canUpdate,
        out PluginRefreshSnapshot snapshot)
    {
        lock (_snapshotLock)
        {
            var active = _activeSelectedIssueApproximation;
            if (_disposed ||
                !canUpdate() ||
                active is null ||
                active.Operation.Generation != generation ||
                _currentPublication.Generation != generation ||
                _currentPublication.Activity.IsPluginRefreshRunning ||
                !_currentPublication.Activity.IsIssueApproximationRefreshRunning)
            {
                snapshot = _currentSnapshot;
                return false;
            }

            var rowUpdate = disposition == PluginRefreshSelectedIssueApproximationDisposition.RestorePrior
                ? PluginRefreshPublicationRows.RestorePendingTargetApproximations(
                    _currentPublication.Rows,
                    active.Operation.Targets)
                : PluginRefreshPublicationRows.ApplyUnavailableToPendingRows(_currentPublication.Rows);
            var rowCommit = rowUpdate.Matched
                ? rowUpdate.Commit
                : PluginRefreshPublicationRows.Commit(_currentPublication.Rows);
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
                _appStateMirror.CurrentState.IsCleaning,
                affordance);
            _currentPublication = nextPublication with { Commands = commands };
            _currentSnapshot = ToSnapshot(_currentPublication);
            snapshot = _currentSnapshot;
            _activeSelectedIssueApproximation = null;
            // Terminal rows, idle activity, and the compatibility mirror must become visible together.
            _appStateMirror.MirrorRows(rowCommit.Mirror);
        }

        _snapshots.OnNext(snapshot);
        return true;
    }

    /// <summary>
    /// Finalizes an active full-refresh approximation without allowing a stale generation to publish.
    /// </summary>
    /// <param name="generation">Accepted full-refresh generation.</param>
    /// <param name="statusText">Terminal status text.</param>
    /// <param name="affordance">Game affordance facts for command projection.</param>
    /// <param name="canUpdate">Guard that accepts cancellation cleanup only for the current generation.</param>
    /// <param name="snapshot">Committed terminal snapshot when the operation was current.</param>
    /// <returns>True when the current full-refresh approximation was finalized.</returns>
    internal bool TryFinalizeInitialApproximation(
        long generation,
        string statusText,
        PluginRefreshGameAffordance affordance,
        Func<bool> canUpdate,
        out PluginRefreshSnapshot snapshot)
    {
        lock (_snapshotLock)
        {
            if (_disposed ||
                !canUpdate() ||
                _currentPublication.Generation != generation ||
                !_currentPublication.Activity.IsPluginRefreshRunning ||
                !_currentPublication.Activity.IsIssueApproximationRefreshRunning)
            {
                snapshot = _currentSnapshot;
                return false;
            }

            var rowUpdate = PluginRefreshPublicationRows.ApplyUnavailableToPendingRows(
                _currentPublication.Rows);
            var nextPublication = _currentPublication with
            {
                Rows = rowUpdate.Commit.Rows,
                VisibleRows = rowUpdate.Commit.VisibleRows,
                Activity = new PluginRefreshActivity(false, false),
                StatusText = statusText
            };
            var commands = CreateCommandAvailability(
                nextPublication.GameType,
                nextPublication.VisibleRows,
                nextPublication.Activity,
                nextPublication.Configuration,
                _appStateMirror.CurrentState.IsCleaning,
                affordance);
            _currentPublication = nextPublication with { Commands = commands };
            _currentSnapshot = ToSnapshot(_currentPublication);
            snapshot = _currentSnapshot;
            // Cancellation/failure terminalization and its compatibility mirror must remain one
            // generation-owned commit; otherwise a replacement refresh can be clobbered afterward.
            _appStateMirror.MirrorRows(rowUpdate.Commit.Mirror);
        }

        _snapshots.OnNext(snapshot);
        return true;
    }

    /// <summary>
    /// Makes pending rows terminal after their owning generation has been superseded.
    /// </summary>
    /// <param name="canUpdate">Guard proving the replacement generation is still current.</param>
    internal void FinalizePendingRowsForSupersession(Func<bool> canUpdate)
    {
        lock (_snapshotLock)
        {
            if (_disposed ||
                !canUpdate() ||
                _currentPublicationFreshnessToken is null ||
                _currentPublication.DiscoveryPlan is null)
            {
                return;
            }

            var rowUpdate = PluginRefreshPublicationRows.ApplyUnavailableToPendingRows(
                _currentPublication.Rows);
            if (!rowUpdate.Matched)
            {
                return;
            }

            _currentPublication = _currentPublication with
            {
                Rows = rowUpdate.Commit.Rows,
                VisibleRows = rowUpdate.Commit.VisibleRows
            };
            _currentSnapshot = ToSnapshot(_currentPublication);
            _appStateMirror.MirrorRows(rowUpdate.Commit.Mirror);
        }
    }

    /// <summary>
    /// Updates command availability after AppState cleaning state changes.
    /// </summary>
    /// <param name="state">Current AppState.</param>
    /// <param name="inspection">Publication and snapshot facts observed before affordance lookup.</param>
    /// <param name="publicationAffordance">Game affordance facts for the observed publication.</param>
    /// <param name="snapshotAffordance">Game affordance facts for the observed snapshot.</param>
    internal void PublishCommandAvailabilityIfChanged(
        AppState state,
        PluginRefreshCommandAvailabilityInspection inspection,
        PluginRefreshGameAffordance publicationAffordance,
        PluginRefreshGameAffordance snapshotAffordance)
    {
        PluginRefreshSnapshot? nextSnapshot = null;
        lock (_snapshotLock)
        {
            if (!ReferenceEquals(_currentPublication, inspection.Publication) ||
                !ReferenceEquals(_currentSnapshot, inspection.Snapshot))
            {
                return;
            }

            var publicationCommands = CreateCommandAvailability(
                _currentPublication.GameType,
                _currentPublication.VisibleRows,
                _currentPublication.Activity,
                _currentPublication.Configuration,
                state.IsCleaning,
                publicationAffordance);
            var snapshotCommands = CreateCommandAvailability(
                _currentSnapshot.GameType,
                _currentSnapshot.Rows,
                _currentSnapshot.Activity,
                _currentSnapshot.Configuration,
                state.IsCleaning,
                snapshotAffordance);

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

    /// <summary>
    /// Gets publication and snapshot facts needed to resolve command affordances outside the store.
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
    /// Publishes freshness if the observed publication is still current and the value changed.
    /// </summary>
    /// <param name="observedPublication">Publication observed before the planner freshness check.</param>
    /// <param name="observedFreshnessToken">Freshness token observed before the planner freshness check.</param>
    /// <param name="freshness">Freshness value returned by the planner.</param>
    internal void PublishFreshnessIfCurrent(
        PluginRefreshPublication observedPublication,
        PluginRefreshDiscoveryFreshnessToken observedFreshnessToken,
        PluginRefreshFreshness freshness)
    {
        PluginRefreshSnapshot snapshot;
        lock (_snapshotLock)
        {
            if (_disposed ||
                !ReferenceEquals(_currentPublicationFreshnessToken, observedFreshnessToken) ||
                _currentPublication.Generation != observedPublication.Generation ||
                _currentPublication.DiscoveryPlan != observedPublication.DiscoveryPlan ||
                _currentPublication.Freshness == freshness)
            {
                return;
            }

            _currentPublication = _currentPublication with { Freshness = freshness };
            snapshot = _currentSnapshot;
        }

        // Freshness is a publication fact, not a row refresh. Re-emit the current snapshot so callers
        // can re-query publication readiness without AutoQAC changing visible rows underneath them.
        _snapshots.OnNext(snapshot);
    }

    private bool TryApplySelectionChangeToPublication(
        PluginSelectionChange change,
        PluginRefreshGameAffordance affordance,
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

        var selection = PluginRefreshPublicationRows.ApplySelectionChange(publication.Rows, change);
        if (!selection.WasTargetFound)
        {
            return true;
        }

        var nextPublication = publication with
        {
            Rows = selection.Commit.Rows,
            VisibleRows = selection.Commit.VisibleRows
        };
        TryPublishSelectionPublicationIfCurrent(
            publication,
            nextPublication,
            freshnessToken,
            selection.Commit.Mirror,
            affordance,
            out snapshot);
        return true;
    }

    private PluginRefreshSnapshot ApplySelectionChangeToState(
        PluginSelectionChange change,
        PluginRefreshGameAffordance affordance)
    {
        var snapshot = GetCurrentSnapshot();
        var visibleRows = snapshot.Rows.ToList();
        if (visibleRows.Count == 0)
        {
            return snapshot;
        }

        var targetFound = _appStateMirror.ApplyStateSelectionChange(visibleRows, change);
        if (!targetFound)
        {
            return snapshot;
        }

        return PublishCurrentPublication(
            snapshot.Generation,
            snapshot.GameType,
            snapshot.Configuration,
            snapshot.Activity,
            snapshot.StatusText,
            affordance);
    }

    private PluginRefreshSnapshot PublishPublicationWithMirroredRows(
        PluginRefreshPublication publication,
        PluginRefreshDiscoveryFreshnessToken freshnessToken,
        PluginRefreshPublicationRowsMirror mirror,
        PluginRefreshGameAffordance affordance)
    {
        _appStateMirror.MirrorRows(mirror);
        return PublishPublication(publication, freshnessToken, _appStateMirror.CurrentState, affordance);
    }

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

        var state = _appStateMirror.CurrentState;
        var commands = CreateCommandAvailability(
            nextPublication.GameType,
            nextPublication.VisibleRows,
            nextPublication.Activity,
            nextPublication.Configuration,
            state.IsCleaning,
            affordance);
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

        _appStateMirror.MirrorRows(mirror);
        _snapshots.OnNext(snapshot);
        return true;
    }

    private PluginRefreshSnapshot PublishSnapshot(
        PluginRefreshSnapshot snapshot,
        AppState state,
        PluginRefreshGameAffordance affordance)
    {
        if (_disposed)
        {
            return GetCurrentSnapshot();
        }

        var next = WithCommandAvailability(snapshot, state, affordance);
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
        AppState state,
        PluginRefreshGameAffordance affordance)
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
            state.IsCleaning,
            affordance);
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

    private PluginRefreshSnapshot WithCommandAvailability(
        PluginRefreshSnapshot snapshot,
        AppState state,
        PluginRefreshGameAffordance affordance) =>
        snapshot with
        {
            Commands = CreateCommandAvailability(
                snapshot.GameType,
                snapshot.Rows,
                snapshot.Activity,
                snapshot.Configuration,
                state.IsCleaning,
                affordance)
        };

    private PluginRefreshCommandAvailability CreateCommandAvailability(
        GameType gameType,
        IReadOnlyList<PluginRefreshRow> rows,
        PluginRefreshActivity activity,
        PluginRefreshConfigurationProjection configuration,
        bool isCleaning,
        PluginRefreshGameAffordance affordance) =>
        _commandPolicy.Create(gameType, rows, activity, configuration, isCleaning, affordance);

    private PluginRefreshSnapshot CreateInitialSnapshot(
        AppState state,
        PluginRefreshGameAffordance initialAffordance)
    {
        var rows = PluginRefreshAppStateMirror.ProjectVisibleRows(state);
        var configuration = PluginRefreshAppStateMirror.CreateConfigurationProjection(state);
        return new PluginRefreshSnapshot(
            Generation: 0,
            GameType: state.CurrentGameType,
            Rows: rows,
            Configuration: configuration,
            Activity: new PluginRefreshActivity(false, false),
            Commands: CreateCommandAvailability(
                state.CurrentGameType,
                rows,
                new PluginRefreshActivity(false, false),
                configuration,
                state.IsCleaning,
                initialAffordance),
            StatusText: "Ready");
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

    private sealed class ActiveSelectedIssueApproximation(
        PluginRefreshSelectedIssueApproximationOperation operation)
    {
        internal PluginRefreshSelectedIssueApproximationOperation Operation { get; } = operation;

        internal int NextTargetIndex { get; set; }
    }
}

/// <summary>
/// Outcome kinds from atomically starting selected Issue approximation.
/// </summary>
internal enum PluginRefreshSelectedIssueApproximationStartStatus
{
    Started,
    NoTargets,
    Rejected
}

/// <summary>
/// Determines how unfinished selected-reanalysis targets become terminal.
/// </summary>
internal enum PluginRefreshSelectedIssueApproximationDisposition
{
    Unavailable,
    RestorePrior
}

/// <summary>
/// Immutable accepted-publication facts owned by one selected Issue approximation generation.
/// </summary>
/// <param name="Generation">Generation that owns the operation.</param>
/// <param name="Plan">Accepted discovery plan reused without replanning.</param>
/// <param name="SourceRows">Complete ordered publication row identities used as dependency context.</param>
/// <param name="Targets">Ordered selected targets and their prior estimates.</param>
internal sealed record PluginRefreshSelectedIssueApproximationOperation(
    long Generation,
    PluginRefreshDiscoveryPlan Plan,
    IReadOnlyList<PluginRefreshRowKey> SourceRows,
    IReadOnlyList<PluginRefreshSelectedIssueApproximationTarget> Targets);

/// <summary>
/// One selected target together with the estimate restored if cancellation occurs before its result.
/// </summary>
/// <param name="Key">Exact accepted publication row identity.</param>
/// <param name="PreviousApproximation">Terminal estimate visible before selected reanalysis began.</param>
internal sealed record PluginRefreshSelectedIssueApproximationTarget(
    PluginRefreshRowKey Key,
    PluginIssueApproximation PreviousApproximation);

/// <summary>
/// Result of attempting to start selected Issue approximation from an observed publication.
/// </summary>
/// <param name="Status">Whether the operation started, had no targets, or lost its publication race.</param>
/// <param name="Operation">Accepted operation facts when started.</param>
internal sealed record PluginRefreshSelectedIssueApproximationStartResult(
    PluginRefreshSelectedIssueApproximationStartStatus Status,
    PluginRefreshSelectedIssueApproximationOperation? Operation);

/// <summary>
/// Current publication facts captured before a planner freshness check.
/// </summary>
/// <param name="Publication">Publication observed by the caller.</param>
/// <param name="FreshnessToken">Freshness token for the observed publication, if one exists.</param>
internal sealed record PluginRefreshPublicationFreshnessInspection(
    PluginRefreshPublication Publication,
    PluginRefreshDiscoveryFreshnessToken? FreshnessToken);

/// <summary>
/// Snapshot-stable issue approximation target selection.
/// </summary>
/// <param name="Snapshot">Snapshot used to derive the selected targets.</param>
/// <param name="Targets">Selected row identities.</param>
internal sealed record PluginRefreshSelectedIssueApproximationTargets(
    PluginRefreshSnapshot Snapshot,
    IReadOnlyList<PluginRefreshRowKey> Targets);

/// <summary>
/// Publication and snapshot references used to resolve command affordance facts outside the store lock.
/// </summary>
/// <param name="Publication">Publication observed before command affordance lookup.</param>
/// <param name="Snapshot">Snapshot observed before command affordance lookup.</param>
internal sealed record PluginRefreshCommandAvailabilityInspection(
    PluginRefreshPublication Publication,
    PluginRefreshSnapshot Snapshot);
