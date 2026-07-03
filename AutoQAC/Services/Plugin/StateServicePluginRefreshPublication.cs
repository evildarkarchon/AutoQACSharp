using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using AutoQAC.Models;
using AutoQAC.Services.State;

namespace AutoQAC.Services.Plugin;

/// <summary>
/// Publishes Plugin refresh state and statuses through <see cref="IStateService"/> while filtering stale generations.
/// </summary>
public sealed class StateServicePluginRefreshPublication : IPluginRefreshPublication
{
    private readonly IStateService _stateService;
    private readonly Subject<PluginRefreshStatus> _statusChanged = new();
    private int _generation;

    /// <summary>
    /// Initializes a new state-backed Plugin refresh publication adapter.
    /// </summary>
    /// <param name="stateService">Application state service to publish visible refresh effects through.</param>
    public StateServicePluginRefreshPublication(IStateService stateService)
    {
        _stateService = stateService;
    }

    /// <inheritdoc />
    public IObservable<PluginRefreshStatus> StatusChanged => _statusChanged.AsObservable();

    /// <inheritdoc />
    public IPluginRefreshPublicationScope BeginRefresh(CancellationToken cancellationToken = default)
    {
        var generation = Interlocked.Increment(ref _generation);
        return new Scope(this, generation, cancellationToken);
    }

    /// <inheritdoc />
    public void PublishSelectPlugins() => Publish(new PluginRefreshStatus(PluginRefreshStatusKind.SelectPlugins));

    /// <inheritdoc />
    public void PublishManualCancellation() => Publish(new PluginRefreshStatus(PluginRefreshStatusKind.Canceled));

    /// <inheritdoc />
    public void Dispose() => _statusChanged.Dispose();

    private bool IsVisible(int generation, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && generation == Volatile.Read(ref _generation);

    private void Publish(PluginRefreshStatus status) => _statusChanged.OnNext(status);

    private sealed class Scope : IPluginRefreshPublicationScope
    {
        private readonly StateServicePluginRefreshPublication _owner;
        private readonly int _generation;
        private readonly CancellationToken _cancellationToken;

        public Scope(
            StateServicePluginRefreshPublication owner,
            int generation,
            CancellationToken cancellationToken)
        {
            _owner = owner;
            _generation = generation;
            _cancellationToken = cancellationToken;
        }

        /// <inheritdoc />
        public bool IsVisible => _owner.IsVisible(_generation, _cancellationToken);

        /// <inheritdoc />
        public void PublishNoGameSelected()
        {
            UpdateStateIfVisible(state => state with { CurrentGameType = GameType.Unknown });
            if (IsVisible)
            {
                _owner._stateService.SetPluginsToClean([]);
            }

            PublishStatusIfVisible(new PluginRefreshStatus(PluginRefreshStatusKind.Idle, Message: "No game selected"));
        }

        /// <inheritdoc />
        public void PublishConfiguration(
            PluginRefreshProjection projection,
            PluginRefreshPublicationSnapshot snapshot)
        {
            if (IsVisible)
            {
                _owner._stateService.UpdateConfigurationPaths(
                    snapshot.Mo2ModeEnabled ? null : snapshot.LoadOrderPath,
                    snapshot.Mo2ExecutablePath,
                    snapshot.XEditExecutablePath,
                    snapshot.Mo2Profile);
            }

            UpdateStateIfVisible(state => state with
            {
                CurrentGameType = projection.GameType,
                Mo2ModeEnabled = snapshot.Mo2ModeEnabled,
                CleaningTimeout = snapshot.CleaningTimeout
            });
        }

        /// <inheritdoc />
        public void PublishNoRefreshContext(PluginRefreshStatus status)
        {
            if (IsVisible)
            {
                _owner._stateService.SetPluginsToClean([]);
            }

            PublishStatusIfVisible(status);
        }

        /// <inheritdoc />
        public void PublishLoadingPlugins() =>
            PublishStatusIfVisible(new PluginRefreshStatus(PluginRefreshStatusKind.LoadingPlugins));

        /// <inheritdoc />
        public void PublishPluginRows(IReadOnlyList<PluginInfo> rows)
        {
            if (!IsVisible)
            {
                return;
            }

            _owner._stateService.SetPluginsToClean(rows.ToList());
        }

        /// <inheritdoc />
        public void PublishApproximationUnavailable() =>
            PublishStatusIfVisible(new PluginRefreshStatus(PluginRefreshStatusKind.ApproximationUnavailable));

        /// <inheritdoc />
        public IPluginRefreshApproximationPublication BeginFullApproximationRefresh(
            IReadOnlyList<PluginRefreshTarget> targets) =>
            new ApproximationPublication(this, targets, PluginRefreshStatusKind.FullRefreshCompleted);

        /// <inheritdoc />
        public IPluginRefreshApproximationPublication BeginSelectedApproximationRefresh(
            GameType gameType,
            IReadOnlyList<PluginRefreshTarget> targets)
        {
            if (IsVisible)
            {
                var pendingRows = targets.Select(target => new PluginInfo
                {
                    FileName = target.FileName,
                    FullPath = target.FullPath,
                    DetectedGameType = gameType,
                    Approximation = PluginIssueApproximation.Pending
                }).ToList();

                _owner._stateService.UpdateState(state =>
                {
                    if (!IsVisible)
                    {
                        return state;
                    }

                    if (state.PluginsToClean.Count == 0)
                    {
                        return state with { PluginsToClean = pendingRows.AsReadOnly() };
                    }

                    var rows = state.PluginsToClean.Select(plugin =>
                        targets.Any(target => IsMatch(plugin, target))
                            ? plugin with { Approximation = PluginIssueApproximation.Pending }
                            : plugin).ToList();

                    return state with { PluginsToClean = rows.AsReadOnly() };
                });
            }

            return new ApproximationPublication(this, targets, PluginRefreshStatusKind.SelectedRefreshCompleted);
        }

        /// <inheritdoc />
        public void PublishApproximationFailure(
            IReadOnlyList<PluginRefreshTarget> targets,
            string message = "Approximation refresh failed.")
        {
            UpdateStateIfVisible(state =>
            {
                if (state.PluginsToClean.Count == 0)
                {
                    return state;
                }

                var rows = state.PluginsToClean.Select(plugin =>
                    targets.Any(target => IsMatch(plugin, target))
                        ? plugin with { Approximation = PluginIssueApproximation.Unavailable }
                        : plugin).ToList();

                return state with { PluginsToClean = rows.AsReadOnly() };
            });

            PublishStatusIfVisible(new PluginRefreshStatus(PluginRefreshStatusKind.Idle, Message: message));
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }

        private void UpdateStateIfVisible(Func<AppState, AppState> update)
        {
            if (!IsVisible)
            {
                return;
            }

            _owner._stateService.UpdateState(state => IsVisible ? update(state) : state);
        }

        private void PublishStatusIfVisible(PluginRefreshStatus status)
        {
            if (IsVisible)
            {
                _owner.Publish(status);
            }
        }

        private static bool IsMatch(PluginInfo plugin, PluginRefreshTarget target)
        {
            if (HasUsablePath(plugin.FullPath) && HasUsablePath(target.FullPath))
            {
                return string.Equals(plugin.FullPath, target.FullPath, StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(plugin.FileName, target.FileName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMatch(PluginInfo plugin, PluginIssueApproximationResult result)
        {
            if (HasUsablePath(plugin.FullPath) && HasUsablePath(result.FullPath))
            {
                return string.Equals(plugin.FullPath, result.FullPath, StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(plugin.FileName, result.FileName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMatch(PluginRefreshTarget target, PluginIssueApproximationResult result)
        {
            if (HasUsablePath(target.FullPath) && HasUsablePath(result.FullPath))
            {
                return string.Equals(target.FullPath, result.FullPath, StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(target.FileName, result.FileName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasUsablePath(string? path) => !string.IsNullOrWhiteSpace(path);

        private sealed class ApproximationPublication : IPluginRefreshApproximationPublication
        {
            private readonly Scope _scope;
            private readonly IReadOnlyList<PluginRefreshTarget> _targets;
            private readonly PluginRefreshStatusKind _completionKind;
            private int _updated;

            public ApproximationPublication(
                Scope scope,
                IReadOnlyList<PluginRefreshTarget> targets,
                PluginRefreshStatusKind completionKind)
            {
                _scope = scope;
                _targets = targets.ToList();
                _completionKind = completionKind;
            }

            /// <inheritdoc />
            public void PublishResult(PluginIssueApproximationResult result)
            {
                if (!_scope.IsVisible || !_targets.Any(target => IsMatch(target, result)))
                {
                    return;
                }

                var matchedVisibleRow = false;
                _scope.UpdateStateIfVisible(state =>
                {
                    if (state.PluginsToClean.Count == 0)
                    {
                        return state;
                    }

                    var rows = state.PluginsToClean.Select(plugin =>
                    {
                        if (!IsMatch(plugin, result))
                        {
                            return plugin;
                        }

                        matchedVisibleRow = true;
                        return plugin with { Approximation = result.Approximation };
                    }).ToList();

                    return state with { PluginsToClean = rows.AsReadOnly() };
                });

                if (!matchedVisibleRow || !_scope.IsVisible)
                {
                    return;
                }

                var updated = Interlocked.Increment(ref _updated);
                _scope.PublishStatusIfVisible(PluginRefreshStatus.AnalyzingSelected(updated, _targets.Count));
            }

            /// <inheritdoc />
            public void PublishCompleted()
            {
                var count = Volatile.Read(ref _updated);
                var status = _completionKind == PluginRefreshStatusKind.FullRefreshCompleted
                    ? PluginRefreshStatus.FullRefreshCompleted(count)
                    : PluginRefreshStatus.SelectedRefreshCompleted(count);

                _scope.PublishStatusIfVisible(status);
            }
        }
    }
}
