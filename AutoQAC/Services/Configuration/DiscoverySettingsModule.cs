using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;
using AutoQAC.Models.Configuration;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;

namespace AutoQAC.Services.Configuration;

/// <summary>
/// Owns durable Discovery settings changes and their matching publication. Mutations are serialized,
/// while discovery may be superseded; callers never coordinate generation or persistence ordering.
/// </summary>
public sealed class DiscoverySettingsModule : IDiscoverySettingsModule, IDisposable
{
    private readonly IConfigurationService _configuration;
    private readonly IStateService _state;
    private readonly IPluginRefreshModule _refresh;
    private readonly DiscoverySettingsAdmission _admission;
    private readonly ILoggingService? _logger;
    private readonly Lock _sync = new();
    private readonly List<PendingChange> _pending = [];
    private readonly Dictionary<string, long> _latestChoices = new(StringComparer.Ordinal);
    private readonly IDisposable _configurationSubscription;
    private readonly IDisposable _skipListSubscription;
    private CancellationTokenSource? _refreshCancellation;
    private string? _expectedConfiguration;
    private long _revision;
    private long _nextIntent;
    private long _resetFence;
    private bool _disposed;

    /// <summary>Uses shared admission in production; optional admission supports isolated module tests.</summary>
    public DiscoverySettingsModule(IConfigurationService configurationService, IStateService stateService,
        IPluginRefreshModule pluginRefreshModule, DiscoverySettingsAdmission? admission = null, ILoggingService? logger = null)
    {
        _configuration = configurationService;
        _state = stateService;
        _refresh = pluginRefreshModule;
        _admission = admission ?? new DiscoverySettingsAdmission();
        _logger = logger;
        _configurationSubscription = configurationService.UserConfigurationChanged.Subscribe(OnConfigurationChanged);
        _skipListSubscription = configurationService.SkipListChanged.Subscribe(_ => InvalidateExternalChange());
    }

    /// <inheritdoc />
    public async Task<DiscoverySettingsChangeResult> ExecuteAsync(DiscoverySettingsIntent intent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        // Snapshot editor-owned objects before the first await so queued edits cannot change underneath validation.
        if (intent is DiscoverySettingsIntent.ApplySettings editor)
            intent = new DiscoverySettingsIntent.ApplySettings(editor.Baseline.Copy(), editor.Requested.Copy());
        if (intent is DiscoverySettingsIntent.SetSkipList skipList)
            intent = new DiscoverySettingsIntent.SetSkipList(skipList.GameType, skipList.Plugins.ToArray());
        var validation = DiscoverySettingsChanges.Validate(intent);
        if (validation is not null) return DiscoverySettingsChangeResult.Rejected(validation);
        if (_state.CurrentState.IsCleaning || _admission.IsCleaning) return CleaningRejected();
        var intentOrder = Interlocked.Increment(ref _nextIntent);

        PendingChange? operation = null;
        CancellationTokenRegistration registration = default;
        try
        {
            // Cold facade initialization must read disk before admission: reload defers while a mutation owns
            // the lease, which would otherwise make the first edit overwrite persisted settings with defaults.
            await _configuration.LoadUserConfigAsync(ct).ConfigureAwait(false);
            using (var lease = await _admission.TryEnterSettingsAsync(ct).ConfigureAwait(false))
            {
                if (lease is null || _state.CurrentState.IsCleaning) return CleaningRejected();
                ct.ThrowIfCancellationRequested();
                var current = await _configuration.LoadUserConfigAsync(ct).ConfigureAwait(false);
                var requested = current.Copy();
                var keys = DiscoverySettingsChanges.Apply(intent, requested);
                var requiresPublication = DiscoverySettingsChanges.RequiresPublication(intent, keys);
                lock (_sync)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    // Intent order precedes cold-load awaits. A delayed caller must not become the "newest"
                    // choice merely because it finally acquires admission after Reset or a later edit.
                    if (intentOrder < _resetFence || keys.Any(key => _latestChoices.GetValueOrDefault(key) > intentOrder) ||
                        intent is DiscoverySettingsIntent.Reset && _latestChoices.Values.Any(order => order > intentOrder))
                        return new DiscoverySettingsChangeResult(DiscoverySettingsChangeStatus.Superseded, null, null);
                    if (intent is DiscoverySettingsIntent.Reset) _resetFence = intentOrder;
                    foreach (var key in keys) _latestChoices[key] = intentOrder;
                    foreach (var prior in _pending.ToArray())
                        if (intent is DiscoverySettingsIntent.Reset || prior.Keys.Overlaps(keys))
                            Complete(prior, DiscoverySettingsChangeStatus.Superseded);
                    operation = new PendingChange(keys, requiresPublication);
                    _pending.Add(operation);
                    if (requiresPublication)
                    {
                        _revision++;
                        CancelRefresh();
                    }
                    // Own-save notifications must not be confused with an external settings replacement.
                    _expectedConfiguration = DiscoverySettingsChanges.Fingerprint(requested);
                    if (requiresPublication) _refresh.InvalidateForSettings();
                }
                var admitted = operation;
                registration = ct.Register(() => CancelOperation(admitted));
                try
                {
                    // Once submitted, settle the write barrier even if the caller cancels. Otherwise a queued
                    // save could write after Reset, or report that nothing saved when the disk write succeeded.
                    await _configuration.SaveUserConfigAsync(requested, CancellationToken.None).ConfigureAwait(false);
                    var persistence = await _configuration.FlushPendingSavesAsync(CancellationToken.None).ConfigureAwait(false);
                    var active = await _configuration.LoadUserConfigAsync(CancellationToken.None).ConfigureAwait(false);
                    if (persistence.Status is not (ConfigPersistenceStatusKind.Success or ConfigPersistenceStatusKind.NoOp)
                        || (persistence.Status == ConfigPersistenceStatusKind.NoOp &&
                            _configuration.LastFailure is { Kind: ConfigPersistenceFailureKind.WriteFailed } lastWrite &&
                            lastWrite.Generation >= persistence.Generation)
                        || DiscoverySettingsChanges.Fingerprint(active) != DiscoverySettingsChanges.Fingerprint(requested))
                    {
                        lock (_sync)
                        {
                            operation.Mutating = false;
                            Complete(operation, DiscoverySettingsChangeStatus.SaveFailed, new DiscoverySettingsChangeFailure(
                                DiscoverySettingsChangeFailureKind.PersistenceFailed,
                                persistence.Failure?.SafeSummary ?? "Could not save settings.", "Check the settings file and try again."));
                            if (requiresPublication) FailRemaining();
                            _expectedConfiguration = DiscoverySettingsChanges.Fingerprint(active);
                        }
                    }
                    else
                    {
                        lock (_sync)
                        {
                            operation.Saved = true;
                            operation.Mutating = false;
                            if (!_disposed && _pending.Contains(operation))
                            {
                                if (requiresPublication) MirrorConfiguration(active, intent is DiscoverySettingsIntent.Reset);
                                else
                                    // Operational editor changes must retain paths resolved by the accepted discovery plan.
                                    _state.UpdateState(state => state with
                                    {
                                        XEditExecutablePath = active.XEdit.Binary,
                                        CleaningTimeout = active.Settings.CleaningTimeout
                                    });
                                if (operation.CancelRequested || ct.IsCancellationRequested)
                                    Complete(operation, DiscoverySettingsChangeStatus.Canceled);
                                else if (!requiresPublication)
                                    Complete(operation, DiscoverySettingsChangeStatus.Accepted);
                                if (requiresPublication && _pending.Count > 0)
                                {
                                    var cancellation = new CancellationTokenSource();
                                    _refreshCancellation = cancellation;
                                    _ = PublishAsync(_revision, active.Copy(), cancellation);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, "Failed to persist a Discovery settings change");
                    lock (_sync)
                    {
                        operation.Mutating = false;
                        var message = ex is ConfigPersistenceFailureException failure
                            ? failure.Failure.SafeSummary : "Could not save settings.";
                        Complete(operation, DiscoverySettingsChangeStatus.SaveFailed,
                            new DiscoverySettingsChangeFailure(DiscoverySettingsChangeFailureKind.PersistenceFailed,
                                message, "Check the latest configuration save error and try again."));
                        if (requiresPublication) FailRemaining();
                    }
                }
            }
            return await operation.Completion.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new DiscoverySettingsChangeResult(DiscoverySettingsChangeStatus.Canceled, null, null);
        }
        finally
        {
            registration.Dispose();
        }
    }

    /// <summary>Resolves pending choices from an exact publication, independently of later approximation work.</summary>
    private async Task PublishAsync(long revision, UserConfiguration requested, CancellationTokenSource cancellation)
    {
        try
        {
            var game = Enum.TryParse<GameType>(requested.SelectedGame, out var selected) ? selected : GameType.Unknown;
            var completion = await _refresh.RefreshForSettingsAsync(game, cancellation.Token).ConfigureAwait(false);
            var active = await _configuration.LoadUserConfigAsync(cancellation.Token).ConfigureAwait(false);
            lock (_sync)
            {
                if (_disposed || revision != _revision || cancellation.IsCancellationRequested) return;
                if (DiscoverySettingsChanges.Fingerprint(active) != DiscoverySettingsChanges.Fingerprint(requested))
                {
                    InvalidateExternalChange();
                    return;
                }
                foreach (var pending in _pending.Where(p => p.Saved && !p.Mutating && p.RequiresPublication).ToArray())
                {
                    if (completion.Status is PluginRefreshCompletionStatus.Published or PluginRefreshCompletionStatus.NoGame
                        && completion.Snapshot is not null)
                        Complete(pending, DiscoverySettingsChangeStatus.Accepted, snapshot: completion.Snapshot);
                    else if (completion.Status == PluginRefreshCompletionStatus.Canceled)
                        Complete(pending, DiscoverySettingsChangeStatus.Canceled);
                    else if (completion.Status == PluginRefreshCompletionStatus.Superseded)
                        Complete(pending, DiscoverySettingsChangeStatus.Superseded);
                    else
                        Complete(pending, DiscoverySettingsChangeStatus.RefreshFailed, RefreshFailure());
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A newer mutation or caller cancellation owns completion of affected operations.
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to publish a Discovery settings change");
            lock (_sync)
                if (revision == _revision) FailRemaining();
        }
        finally
        {
            lock (_sync)
                if (ReferenceEquals(_refreshCancellation, cancellation)) _refreshCancellation = null;
            cancellation.Dispose();
        }
    }

    /// <summary>Mirrors only durable values; discovery later supplies resolved paths and profiles.</summary>
    private void MirrorConfiguration(UserConfiguration config, bool reset)
    {
        var game = Enum.TryParse<GameType>(config.SelectedGame, out var selected) ? selected : GameType.Unknown;
        _state.UpdateState(state => state with
        {
            CurrentGameType = game,
            Mo2ModeEnabled = config.Settings.Mo2Mode,
            Mo2ExecutablePath = config.ModOrganizer.Binary,
            XEditExecutablePath = config.XEdit.Binary,
            CleaningTimeout = config.Settings.CleaningTimeout,
            LoadOrderPath = config.LoadOrderFileOverrides.GetValueOrDefault(DiscoverySettingsChanges.GameKey(game)) ?? config.LoadOrder.File,
            Mo2Profile = config.Mo2ProfileSelections.GetValueOrDefault(DiscoverySettingsChanges.GameKey(game)),
            PartialFormsEnabled = !reset && state.PartialFormsEnabled
        });
        // Preserve the deliberate empty no-game outcome instead of retaining rows from the old game.
        if (game == GameType.Unknown) _state.SetPluginsToClean([]);
    }

    /// <summary>Only external discovery changes invalidate pending acceptance; ordinary own-save echoes do not.</summary>
    private void OnConfigurationChanged(UserConfiguration config)
    {
        if (config is null) return;
        lock (_sync)
        {
            var fingerprint = DiscoverySettingsChanges.Fingerprint(config);
            if (fingerprint != _expectedConfiguration) InvalidateExternalChange();
            _expectedConfiguration = fingerprint;
        }
    }

    /// <summary>Fences obsolete completions without adding a new automatic refresh trigger.</summary>
    private void InvalidateExternalChange()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _revision++;
            CancelRefresh();
            foreach (var pending in _pending.ToArray())
            {
                // A persistence failure publishes the last good configuration during the write barrier.
                // The mutating operation must report that save failure, not a misleading supersession.
                if (!pending.Mutating) Complete(pending, DiscoverySettingsChangeStatus.Superseded);
            }
            _refresh.InvalidateForSettings();
        }
    }

    /// <summary>Cancels only this operation's interest; independent pending choices retain their shared refresh.</summary>
    private void CancelOperation(PendingChange operation)
    {
        lock (_sync)
        {
            operation.CancelRequested = true;
            if (!operation.Mutating) Complete(operation, DiscoverySettingsChangeStatus.Canceled);
            if (_pending.Count == 0) CancelRefresh();
        }
    }

    /// <summary>Completes an operation once and removes it from shared publication acceptance.</summary>
    private void Complete(PendingChange operation, DiscoverySettingsChangeStatus status,
        DiscoverySettingsChangeFailure? failure = null, PluginRefreshSnapshot? snapshot = null)
    {
        _pending.Remove(operation);
        operation.Completion.TrySetResult(new DiscoverySettingsChangeResult(status, snapshot, failure)
        { SettingsSaved = operation.Saved });
    }

    /// <summary>Fails saved choices when their shared discovery attempt cannot produce a publication.</summary>
    private void FailRemaining()
    {
        foreach (var operation in _pending.Where(p => p.Saved && !p.Mutating && p.RequiresPublication).ToArray())
            Complete(operation, DiscoverySettingsChangeStatus.RefreshFailed, RefreshFailure());
    }

    private static DiscoverySettingsChangeFailure RefreshFailure() => new(
        DiscoverySettingsChangeFailureKind.PublicationFailed,
        "Settings were saved, but plugins could not be refreshed.", "Refresh plugins before starting cleaning.");

    private static DiscoverySettingsChangeResult CleaningRejected() => DiscoverySettingsChangeResult.Rejected(new(
        DiscoverySettingsChangeFailureKind.CleaningActive,
        "Settings cannot change while a Cleaning session is starting or active.", "Wait for cleaning to finish."));

    /// <summary>Requests asynchronous cancellation without running dependency callbacks under the ownership lock.</summary>
    private void CancelRefresh()
    {
        if (_refreshCancellation is { } cancellation) _ = cancellation.CancelAsync();
        _refreshCancellation = null;
    }

    /// <summary>Stops pending work and removes external notifications. In-flight writes still settle their disk barrier.</summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            CancelRefresh();
            foreach (var operation in _pending.ToArray()) Complete(operation, DiscoverySettingsChangeStatus.Canceled);
        }
        _configurationSubscription.Dispose();
        _skipListSubscription.Dispose();
    }

    private sealed class PendingChange(HashSet<string> keys, bool requiresPublication)
    {
        public HashSet<string> Keys { get; } = keys;
        public bool RequiresPublication { get; } = requiresPublication;
        public bool Saved { get; set; }
        public bool Mutating { get; set; } = true;
        public bool CancelRequested { get; set; }
        public TaskCompletionSource<DiscoverySettingsChangeResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
