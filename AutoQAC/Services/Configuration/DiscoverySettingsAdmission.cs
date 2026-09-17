using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AutoQAC.Services.Configuration;

/// <summary>Coordinates settings mutation with the complete Cleaning session lifetime, including startup.</summary>
public sealed class DiscoverySettingsAdmission
{
    private readonly Lock _sync = new();
    private readonly SemaphoreSlim _mutation = new(1, 1);
    private readonly HashSet<SettingsPublicationRegistration> _settingsPublications = [];
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
        ArgumentNullException.ThrowIfNull(publication);
        ArgumentNullException.ThrowIfNull(cancellation);

        var registration = new SettingsPublicationRegistration(publication, cancellation);
        bool cancelForCleaning;
        lock (_sync)
        {
            _settingsPublications.Add(registration);
            cancelForCleaning = _isCleaning;
        }

        _ = publication.ContinueWith(
            _ => RemoveSettingsPublication(registration),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        if (cancelForCleaning) RequestPublicationCancellation(cancellation);
    }

    /// <summary>Reserves cleaning before awaiting earlier settings writes. Dispose the returned lease after finalization.</summary>
    /// <exception cref="InvalidOperationException">Another Cleaning session already reserved admission.</exception>
    public async Task<IDisposable> EnterCleaningAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        List<SettingsPublicationRegistration> publicationsToCancel;
        lock (_sync)
        {
            if (_isCleaning) throw new InvalidOperationException("A cleaning session is already in progress.");
            _isCleaning = true;
            publicationsToCancel = [.. _settingsPublications];
        }
        foreach (var publication in publicationsToCancel)
            RequestPublicationCancellation(publication.Cancellation);

        var mutationAcquired = false;
        try
        {
            CleaningChanged?.Invoke(this, EventArgs.Empty);
            // Reserve before draining: queued settings callers cannot slip in ahead of startup.
            await _mutation.WaitAsync(ct).ConfigureAwait(false);
            mutationAcquired = true;

            Task[] publicationsToDrain;
            lock (_sync)
            {
                publicationsToDrain = new Task[_settingsPublications.Count];
                var index = 0;
                foreach (var publication in _settingsPublications)
                    publicationsToDrain[index++] = publication.Publication;
            }
            if (publicationsToDrain.Length > 0)
                await Task.WhenAll(publicationsToDrain).WaitAsync(ct).ConfigureAwait(false);

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

    /// <summary>Removes publication work after its complete success, failure, or cancellation unwind.</summary>
    private void RemoveSettingsPublication(SettingsPublicationRegistration registration)
    {
        lock (_sync) _settingsPublications.Remove(registration);
    }

    /// <summary>Requests cancellation without allowing a concurrently completed publication to break admission.</summary>
    private static void RequestPublicationCancellation(CancellationTokenSource cancellation)
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

    /// <summary>Releases the serialized mutation and lets deferred external changes retry.</summary>
    private void ReleaseMutation()
    {
        _mutation.Release();
        AvailabilityChanged?.Invoke(this, EventArgs.Empty);
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

    private sealed record SettingsPublicationRegistration(
        Task Publication,
        CancellationTokenSource Cancellation);
}
