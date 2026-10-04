using System;
using System.Threading;
using System.Threading.Tasks;

namespace AutoQAC.Services.Cleaning;

/// <summary>
///     One Plugin refresh admitted by <see cref="CleaningAdmission" />, from discovery through its Issue approximation
///     tail. It carries the refresh's only cancellation token and is the identity the publication store commits
///     against. It stops being current once superseded or canceled, and leaves admission's drain set only when
///     disposed after the whole refresh has unwound.
/// </summary>
internal sealed class RefreshOperation : IDisposable
{
    private readonly CleaningAdmission _admission;
    private readonly CancellationTokenSource _cancellation;
    private readonly TaskCompletionSource _unwound = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _isSuperseded;
    private volatile bool _isCanceledForCleaning;
    private int _disposed;

    /// <summary>Creates an operation; only <see cref="CleaningAdmission.TryBeginRefresh" /> issues them.</summary>
    /// <param name="admission">Admission that drains this operation until it is disposed.</param>
    /// <param name="id">Admission order, published as the snapshot generation.</param>
    /// <param name="cancellation">Source owned by this operation, already linked to the caller's token.</param>
    internal RefreshOperation(CleaningAdmission admission, long id, CancellationTokenSource cancellation)
    {
        _admission = admission;
        _cancellation = cancellation;
        Id = id;
        Token = cancellation.Token;
    }

    /// <summary>
    ///     Admission order, published as the snapshot and publication generation for diagnostics and observers.
    ///     Currency is decided by <see cref="IsCurrent" /> and identity, never by comparing ids.
    /// </summary>
    internal long Id { get; }

    /// <summary>The one cancellation token for the whole Plugin refresh, including its Issue approximation tail.</summary>
    internal CancellationToken Token { get; }

    /// <summary>
    ///     Whether this operation may still commit: false once it is superseded, canceled for cleaning, or its token
    ///     is canceled for any other reason.
    /// </summary>
    internal bool IsCurrent => !_isSuperseded && !_isCanceledForCleaning && !Token.IsCancellationRequested;

    /// <summary>
    ///     Whether a newer operation or a settings fence replaced this one. Supersession finalizes the operation's
    ///     estimates synchronously, so a superseded operation may not even run its own cancellation cleanup.
    /// </summary>
    internal bool IsSuperseded => _isSuperseded;

    /// <summary>Completes when the operation is disposed, meaning the refresh has fully unwound.</summary>
    internal Task Unwound => _unwound.Task;

    /// <summary>Signals that the refresh has fully unwound and removes it from admission's drain set.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _admission.CompleteRefresh(this);
        _cancellation.Dispose();
        _unwound.TrySetResult();
    }

    /// <summary>Cancels the operation's token synchronously; safe after disposal.</summary>
    internal void Cancel()
    {
        try
        {
            _cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The operation can finish unwinding between being observed and being canceled.
        }
    }

    /// <summary>
    ///     Requests cancellation for Cleaning admission without running token callbacks on the reserving thread.
    /// </summary>
    internal void CancelForCleaning()
    {
        try
        {
            _ = _cancellation.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
            // The operation can finish unwinding between admission's snapshot and this cancellation request.
        }
    }

    /// <summary>Marks supersession; admission calls this under its lock, before canceling the token.</summary>
    internal void MarkSuperseded()
    {
        _isSuperseded = true;
    }

    /// <summary>
    ///     Marks Cleaning cancellation under admission's lock, so the operation stops being current atomically with
    ///     the reservation rather than when its token cancellation is requested afterward.
    /// </summary>
    internal void MarkCanceledForCleaning()
    {
        _isCanceledForCleaning = true;
    }
}
