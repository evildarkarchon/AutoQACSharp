using System;
using System.Threading;
using System.Threading.Tasks;

namespace AutoQAC.Services.Configuration;

/// <summary>Coordinates settings mutation with the complete Cleaning session lifetime, including startup.</summary>
public sealed class DiscoverySettingsAdmission
{
    private readonly Lock _sync = new();
    private readonly SemaphoreSlim _mutation = new(1, 1);
    private bool _isCleaning;

    /// <summary>Whether cleaning has reserved admission, including while already-admitted writes drain.</summary>
    public bool IsCleaning { get { lock (_sync) return _isCleaning; } }

    /// <summary>Signals a cleaning reservation transition. Handlers must not throw.</summary>
    public event EventHandler? CleaningChanged;

    /// <summary>Signals when deferred external changes may retry. Handlers must not throw.</summary>
    public event EventHandler? AvailabilityChanged;

    /// <summary>Serializes settings writes; returns null if cleaning reserves admission before this write enters.</summary>
    public async Task<IDisposable?> TryEnterSettingsAsync(CancellationToken ct = default)
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

    /// <summary>Reserves cleaning before awaiting earlier settings writes. Dispose the returned lease after finalization.</summary>
    /// <exception cref="InvalidOperationException">Another Cleaning session already reserved admission.</exception>
    public async Task<IDisposable> EnterCleaningAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_isCleaning) throw new InvalidOperationException("A cleaning session is already in progress.");
            _isCleaning = true;
        }
        try
        {
            CleaningChanged?.Invoke(this, EventArgs.Empty);
            // Reserve before draining: queued settings callers cannot slip in ahead of startup.
            await _mutation.WaitAsync(ct).ConfigureAwait(false);
            _mutation.Release();
            return new Lease(ReleaseCleaning);
        }
        catch
        {
            ReleaseCleaning();
            throw;
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
}
