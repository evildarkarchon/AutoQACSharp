using System.Threading;
using System.Threading.Tasks;

namespace AutoQAC.Services.Process;

/// <summary>
/// Provides the cancelable process-exit wait used after starting, gracefully stopping, or killing a process.
/// </summary>
public interface IProcessExitWaiter
{
    /// <summary>
    /// Waits until the supplied process exits or the cancellation token is canceled.
    /// </summary>
    /// <param name="process">The process whose exit should be observed.</param>
    /// <param name="ct">A token that cancels the wait without implying the process exited.</param>
    Task WaitForExitAsync(System.Diagnostics.Process process, CancellationToken ct);
}

/// <summary>
/// Default <see cref="IProcessExitWaiter"/> implementation backed by <see cref="System.Diagnostics.Process.WaitForExitAsync(CancellationToken)"/>.
/// </summary>
public sealed class ProcessExitWaiter : IProcessExitWaiter
{
    /// <inheritdoc />
    public Task WaitForExitAsync(System.Diagnostics.Process process, CancellationToken ct) =>
        process.WaitForExitAsync(ct);
}
