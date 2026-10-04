using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

namespace AutoQAC.Services.Cleaning;

/// <summary>
///     Cleaning admission: the reservation that admits one Cleaning session and excludes Discovery settings changes,
///     Plugin selection commits, previews, and Plugin refreshes from Cleaning session startup through finalization.
///     It is the only answer to "is cleaning underway?" for anything deciding whether it may proceed.
/// </summary>
public sealed class CleaningAdmission
{
    private readonly Lock _sync = new();
    private readonly Lock _publishSync = new();
    private readonly SemaphoreSlim _mutation = new(1, 1);
    private readonly HashSet<RefreshOperation> _refreshes = [];
    private readonly BehaviorSubject<bool> _cleaningState = new(false);
    private CancellationTokenSource? _activePreviewCancellation;
    private RefreshOperation? _currentRefresh;
    private long _nextRefreshId;
    private bool _isCleaning;

    /// <summary>Whether cleaning has reserved admission, including while already-admitted writes drain.</summary>
    public bool IsCleaning { get { lock (_sync) return _isCleaning; } }

    /// <summary>
    ///     Replays the current reservation state to each subscriber, then emits every reservation transition.
    ///     The reservation is published synchronously before startup drains earlier work, and its release only
    ///     after the Cleaning lease is disposed. Observers run on the transitioning thread and must not throw.
    /// </summary>
    public IObservable<bool> CleaningState => _cleaningState;

    /// <summary>Signals when deferred external changes may retry. Handlers must not throw.</summary>
    public event EventHandler? AvailabilityChanged;

    /// <summary>Serializes settings writes; returns null if cleaning reserves admission before this write enters.</summary>
    public Task<IDisposable?> TryEnterSettingsAsync(CancellationToken ct = default)
    {
        return TryEnterMutationAsync(ct);
    }

    /// <summary>Serializes a Plugin selection commit with settings writes and Cleaning session startup.</summary>
    internal Task<IDisposable?> TryEnterPluginMutationAsync(CancellationToken ct = default)
    {
        return TryEnterMutationAsync(ct);
    }

    /// <summary>Admits preview preflight before Cleaning reserves admission and holds its mutation lane until it unwinds.</summary>
    /// <param name="cancellation">Caller-owned source canceled when Cleaning reserves admission.</param>
    /// <param name="ct">Cancels the wait for the mutation lane.</param>
    /// <returns>A lease to release after all preview file reads finish, or null if Cleaning reserved admission.</returns>
    internal async Task<IDisposable?> TryEnterPreviewAsync(
        CancellationTokenSource cancellation,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cancellation);
        var mutation = await TryEnterMutationAsync(ct).ConfigureAwait(false);
        if (mutation is null) return null;

        bool cleaningReserved;
        lock (_sync)
        {
            cleaningReserved = _isCleaning;
            if (!cleaningReserved) _activePreviewCancellation = cancellation;
        }
        if (cleaningReserved)
        {
            // Cleaning may reserve admission between the mutation lease and preview registration.
            mutation.Dispose();
            return null;
        }

        return new Lease(() => ReleasePreview(cancellation, mutation));
    }

    /// <summary>Enters the shared pre-clean mutation lane unless Cleaning has already reserved it.</summary>
    private async Task<IDisposable?> TryEnterMutationAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
            if (_isCleaning) return null;
        await _mutation.WaitAsync(ct).ConfigureAwait(false);
        lock (_sync)
        {
            if (!_isCleaning) return new Lease(ReleaseMutation);
        }
        _mutation.Release();
        return null;
    }

    /// <summary>Attempts synchronous external-change admission without blocking the persistence consumer on its own save.</summary>
    internal IDisposable? TryEnterExternalSettings()
    {
        lock (_sync)
            return !_isCleaning && _mutation.Wait(0) ? new Lease(ReleaseMutation) : null;
    }

    /// <summary>The operation the next refresh or fence would supersede: begun, not superseded, not yet unwound.</summary>
    internal RefreshOperation? CurrentRefresh
    {
        get
        {
            lock (_sync) return _currentRefresh;
        }
    }

    /// <summary>
    ///     Begins a Plugin refresh operation that Cleaning will cancel and drain, superseding the current operation.
    /// </summary>
    /// <param name="ct">Caller cancellation linked into the operation's only cancellation token.</param>
    /// <param name="superseded">
    ///     The operation this one replaced, already marked superseded and canceled. The caller must finalize its
    ///     estimates before the new operation publishes anything.
    /// </param>
    /// <returns>
    ///     The new current operation, or null when Cleaning has reserved admission. A failing cancellation callback on
    ///     the superseded operation is recorded for <see cref="RefreshOperation.TakeCancellationFailure" />, not thrown.
    /// </returns>
    internal RefreshOperation? TryBeginRefresh(CancellationToken ct, out RefreshOperation? superseded)
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        RefreshOperation operation;
        lock (_sync)
        {
            superseded = null;
            if (_isCleaning)
            {
                cancellation.Dispose();
                return null;
            }

            operation = new RefreshOperation(this, ++_nextRefreshId, cancellation);
            // Mark before installing the successor so the old operation stops being current atomically with it.
            superseded = _currentRefresh;
            superseded?.MarkSuperseded();
            _currentRefresh = operation;
            _refreshes.Add(operation);
        }

        // Cancel outside the lock: token callbacks and inline continuations must not run under admission. Cancel
        // never throws, so the successor always reaches its caller, which alone can dispose it once it unwinds.
        superseded?.Cancel();
        return operation;
    }

    /// <summary>
    ///     Supersedes the current operation without a successor, leaving no current operation. A Discovery settings
    ///     change fences refresh work this way because a replacement refresh may never follow it.
    /// </summary>
    /// <returns>The superseded operation for the caller to finalize, or null when nothing was current.</returns>
    internal RefreshOperation? SupersedeRefresh()
    {
        RefreshOperation? superseded;
        lock (_sync)
        {
            superseded = _currentRefresh;
            superseded?.MarkSuperseded();
            _currentRefresh = null;
        }

        superseded?.Cancel();
        return superseded;
    }

    /// <summary>Removes a fully unwound operation from the drain set and from currency.</summary>
    /// <param name="operation">Operation being disposed; called once, from <see cref="RefreshOperation.Dispose" />.</param>
    internal void CompleteRefresh(RefreshOperation operation)
    {
        lock (_sync)
        {
            _refreshes.Remove(operation);
            if (ReferenceEquals(_currentRefresh, operation)) _currentRefresh = null;
        }
    }

    /// <summary>Reserves cleaning before draining earlier settings writes, previews, and refresh work.</summary>
    /// <returns>A lease to dispose after Cleaning session finalization.</returns>
    /// <exception cref="InvalidOperationException">Another Cleaning session already reserved admission.</exception>
    public async Task<IDisposable> EnterCleaningAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        List<RefreshOperation> refreshesToCancel;
        CancellationTokenSource? previewToCancel;
        lock (_sync)
        {
            if (_isCleaning) throw new InvalidOperationException("A cleaning session is already in progress.");
            _isCleaning = true;
            refreshesToCancel = [.. _refreshes];
            // Operations stop being current with the reservation, before their token cancellation is requested.
            foreach (var refresh in refreshesToCancel) refresh.MarkCanceledForCleaning();
            previewToCancel = _activePreviewCancellation;
        }
        if (previewToCancel is not null) RequestPreviewCancellation(previewToCancel);
        foreach (var refresh in refreshesToCancel)
            refresh.CancelForCleaning();

        var mutationAcquired = false;
        try
        {
            PublishCleaningState();
            // Reserve before draining: queued settings callers cannot slip in ahead of startup.
            await _mutation.WaitAsync(ct).ConfigureAwait(false);
            mutationAcquired = true;

            Task[] refreshesToDrain;
            lock (_sync)
            {
                refreshesToDrain = new Task[_refreshes.Count];
                var index = 0;
                foreach (var refresh in _refreshes)
                    refreshesToDrain[index++] = refresh.Unwound;
            }
            // Unwound only completes successfully, so a refresh canceled for Cleaning still lets startup continue.
            if (refreshesToDrain.Length > 0)
                await Task.WhenAll(refreshesToDrain).WaitAsync(ct).ConfigureAwait(false);

            _mutation.Release();
            mutationAcquired = false;
            return new Lease(ReleaseCleaning);
        }
        catch
        {
            if (mutationAcquired) _mutation.Release();
            ReleaseCleaning();
            throw;
        }
    }

    /// <summary>Requests cancellation without allowing a concurrently completed preview to break admission.</summary>
    private static void RequestPreviewCancellation(CancellationTokenSource cancellation)
    {
        try
        {
            _ = cancellation.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
            // Completion may dispose the source between the preview snapshot and this cancellation request.
        }
    }

    /// <summary>Releases the shared pre-clean lane and lets deferred external changes retry.</summary>
    private void ReleaseMutation()
    {
        _mutation.Release();
        AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Clears a preview's cancellation target before reopening the shared mutation lane.</summary>
    private void ReleasePreview(CancellationTokenSource cancellation, IDisposable mutation)
    {
        lock (_sync)
            if (ReferenceEquals(_activePreviewCancellation, cancellation)) _activePreviewCancellation = null;
        mutation.Dispose();
    }

    /// <summary>Reopens admission after cleaning finalization or canceled startup.</summary>
    private void ReleaseCleaning()
    {
        lock (_sync) _isCleaning = false;
        PublishCleaningState();
        AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Publishes the reservation as it is now, suppressing repeats of the last published value.</summary>
    /// <remarks>
    ///     A release and the next reservation can race on different threads. Reading the state inside the publish
    ///     lock, rather than passing the transition's own value, guarantees the last value observers receive is the
    ///     current reservation even if the two transitions publish out of order.
    /// </remarks>
    private void PublishCleaningState()
    {
        lock (_publishSync)
        {
            var isCleaning = IsCleaning;
            if (_cleaningState.Value != isCleaning) _cleaningState.OnNext(isCleaning);
        }
    }

    private sealed class Lease(Action release) : IDisposable
    {
        private Action? _release = release;

        /// <summary>Releases this lease once, including when cleanup and cancellation both dispose it.</summary>
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
