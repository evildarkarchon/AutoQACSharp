using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AutoQAC.Services.Configuration;

/// <summary>Coordinates discovery mutations, previews, and refresh work with the complete Cleaning session lifetime.</summary>
public sealed class DiscoverySettingsAdmission
{
    private readonly Lock _sync = new();
    private readonly SemaphoreSlim _mutation = new(1, 1);
    private readonly HashSet<RefreshRegistration> _refreshes = [];
    private CancellationTokenSource? _activePreviewCancellation;
    private bool _isCleaning;

    /// <summary>Whether cleaning has reserved admission, including while already-admitted writes drain.</summary>
    public bool IsCleaning { get { lock (_sync) return _isCleaning; } }

    /// <summary>Signals a cleaning reservation transition. Handlers must not throw.</summary>
    public event EventHandler? CleaningChanged;

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

    /// <summary>
    ///     Tracks publication work launched by the current settings mutation so Cleaning can cancel and drain it
    ///     across the mutation-lease handoff.
    /// </summary>
    /// <param name="publication">Publication task that completes after cancellation cleanup has fully unwound.</param>
    /// <param name="cancellation">Cancellation source owned by the publication task.</param>
    internal void TrackSettingsPublication(Task publication, CancellationTokenSource cancellation)
    {
        TrackRefresh(publication, cancellation, rejectWhenCleaning: false);
    }

    /// <summary>
    ///     Registers a manually started Plugin refresh before it can begin, so Cleaning drains its import.
    /// </summary>
    /// <param name="refresh">Task completed after the Plugin refresh has fully unwound.</param>
    /// <param name="cancellation">Cancellation source owned by the refresh caller.</param>
    /// <returns>False when Cleaning has already reserved admission and the caller must not start the refresh.</returns>
    internal bool TryTrackManualRefresh(Task refresh, CancellationTokenSource cancellation)
    {
        return TrackRefresh(refresh, cancellation, rejectWhenCleaning: true);
    }

    /// <summary>Registers a refresh lifetime atomically with the Cleaning reservation boundary.</summary>
    /// <param name="refresh">Task representing all remaining work in this refresh.</param>
    /// <param name="cancellation">Source to cancel if Cleaning reserves admission.</param>
    /// <param name="rejectWhenCleaning">Whether a new manual refresh must be rejected after reservation.</param>
    /// <returns>Whether the refresh was registered for cancellation and draining.</returns>
    private bool TrackRefresh(Task refresh, CancellationTokenSource cancellation, bool rejectWhenCleaning)
    {
        ArgumentNullException.ThrowIfNull(refresh);
        ArgumentNullException.ThrowIfNull(cancellation);

        var registration = new RefreshRegistration(refresh, cancellation);
        bool cancelForCleaning;
        lock (_sync)
        {
            if (rejectWhenCleaning && _isCleaning) return false;
            _refreshes.Add(registration);
            cancelForCleaning = _isCleaning;
        }

        _ = refresh.ContinueWith(
            _ => RemoveRefresh(registration),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        if (cancelForCleaning) RequestRefreshCancellation(cancellation);
        return true;
    }

    /// <summary>Reserves cleaning before draining earlier settings writes, previews, and refresh work.</summary>
    /// <returns>A lease to dispose after Cleaning session finalization.</returns>
    /// <exception cref="InvalidOperationException">Another Cleaning session already reserved admission.</exception>
    public async Task<IDisposable> EnterCleaningAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        List<RefreshRegistration> refreshesToCancel;
        CancellationTokenSource? previewToCancel;
        lock (_sync)
        {
            if (_isCleaning) throw new InvalidOperationException("A cleaning session is already in progress.");
            _isCleaning = true;
            refreshesToCancel = [.. _refreshes];
            previewToCancel = _activePreviewCancellation;
        }
        if (previewToCancel is not null) RequestRefreshCancellation(previewToCancel);
        foreach (var refresh in refreshesToCancel)
            RequestRefreshCancellation(refresh.Cancellation);

        var mutationAcquired = false;
        try
        {
            CleaningChanged?.Invoke(this, EventArgs.Empty);
            // Reserve before draining: queued settings callers cannot slip in ahead of startup.
            await _mutation.WaitAsync(ct).ConfigureAwait(false);
            mutationAcquired = true;

            Task[] refreshesToDrain;
            lock (_sync)
            {
                refreshesToDrain = new Task[_refreshes.Count];
                var index = 0;
                foreach (var refresh in _refreshes)
                    refreshesToDrain[index++] = refresh.Refresh;
            }
            if (refreshesToDrain.Length > 0)
            {
                var drain = Task.WhenAll(refreshesToDrain);
                try
                {
                    await drain.WaitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (drain.IsCanceled && !ct.IsCancellationRequested)
                {
                    // Cleaning requested this cancellation; the refresh has finished unwinding.
                }
            }

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

    /// <summary>Removes refresh work after its complete success, failure, or cancellation unwind.</summary>
    private void RemoveRefresh(RefreshRegistration registration)
    {
        lock (_sync) _refreshes.Remove(registration);
    }

    /// <summary>Requests cancellation without allowing a concurrently completed refresh to break admission.</summary>
    private static void RequestRefreshCancellation(CancellationTokenSource cancellation)
    {
        try
        {
            _ = cancellation.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
            // Completion may dispose the source between the tracked-task snapshot and this cancellation request.
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
        CleaningChanged?.Invoke(this, EventArgs.Empty);
        AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Lease(Action release) : IDisposable
    {
        private Action? _release = release;

        /// <summary>Releases this lease once, including when cleanup and cancellation both dispose it.</summary>
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }

    private sealed record RefreshRegistration(
        Task Refresh,
        CancellationTokenSource Cancellation);
}
