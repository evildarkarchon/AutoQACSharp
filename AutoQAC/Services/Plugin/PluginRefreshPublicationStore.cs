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
    /// Marks selected issue approximation targets pending.
    /// </summary>
    /// <param name="gameType">Game context for the targets.</param>
    /// <param name="targets">Target row identities.</param>
    /// <param name="targetLookup">Lookup used to identify target rows.</param>
    internal void MarkTargetsPending(
        GameType gameType,
        IReadOnlyList<PluginRefreshRowKey> targets,
        PluginRefreshPublicationRows.TargetLookup targetLookup)
    {
        if (TryUpdateAcceptedPublicationRows(
                gameType,
                rows => PluginRefreshPublicationRows.ApplyApproximationToTargets(
                    rows,
                    targetLookup,
                    PluginIssueApproximation.Pending)))
        {
            return;
        }

        _appStateMirror.MarkTargetsPending(gameType, targets, targetLookup);
    }

    /// <summary>
    /// Marks selected issue approximation targets unavailable.
    /// </summary>
    /// <param name="gameType">Game context for the targets.</param>
    /// <param name="targetLookup">Lookup used to identify target rows.</param>
    /// <param name="canUpdate">Visibility guard supplied by the active refresh generation.</param>
    internal void MarkTargetsUnavailable(
        GameType gameType,
        PluginRefreshPublicationRows.TargetLookup targetLookup,
        Func<bool> canUpdate)
    {
        if (TryUpdateAcceptedPublicationRows(
                gameType,
                rows => PluginRefreshPublicationRows.ApplyApproximationToTargets(
                    rows,
                    targetLookup,
                    PluginIssueApproximation.Unavailable),
                canUpdate))
        {
            return;
        }

        _appStateMirror.MarkTargetsUnavailable(targetLookup, canUpdate);
    }

    /// <summary>
    /// Applies one issue approximation result to the current publication or AppState fallback rows.
    /// </summary>
    /// <param name="gameType">Game context for the result.</param>
    /// <param name="targetLookup">Lookup containing the active analysis targets.</param>
    /// <param name="result">Approximation result to merge.</param>
    /// <param name="canUpdate">Visibility guard supplied by the active refresh generation.</param>
    /// <returns>True when a publication or AppState row matched the result.</returns>
    internal bool TryApplyApproximationResult(
        GameType gameType,
        PluginRefreshPublicationRows.TargetLookup targetLookup,
        PluginIssueApproximationResult result,
        Func<bool> canUpdate)
    {
        if (!canUpdate() || !targetLookup.Contains(result))
        {
            return false;
        }

        var matchedRow = TryUpdateAcceptedPublicationRows(
            gameType,
            rows => PluginRefreshPublicationRows.ApplyApproximationResult(rows, result),
            canUpdate);
        if (!matchedRow)
        {
            matchedRow = _appStateMirror.TryApplyApproximationResult(canUpdate, result);
        }

        return matchedRow;
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

    private bool TryUpdateAcceptedPublicationRows(
        GameType gameType,
        Func<IReadOnlyList<PluginRefreshPublishedRow>, PluginRefreshPublicationRowsUpdate> updateRows,
        Func<bool>? canUpdate = null)
    {
        PluginRefreshPublicationRowsMirror mirror;
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

            var result = updateRows(_currentPublication.Rows);
            if (!result.Matched)
            {
                return false;
            }

            _currentPublication = _currentPublication with
            {
                Rows = result.Commit.Rows,
                VisibleRows = result.Commit.VisibleRows
            };
            _currentSnapshot = ToSnapshot(_currentPublication);
            mirror = result.Commit.Mirror;
        }

        _appStateMirror.MirrorRows(mirror);
        return true;
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
}

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
