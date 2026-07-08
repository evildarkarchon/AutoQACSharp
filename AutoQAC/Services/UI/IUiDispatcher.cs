using System;
using System.Threading.Tasks;

namespace AutoQAC.Services.UI;

/// <summary>
/// Marshals callbacks onto the UI thread. Production wires this to the active
/// UI framework dispatcher; tests substitute a synchronous implementation that
/// runs callbacks inline on the calling thread.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>
    /// Enqueues <paramref name="action"/> on the UI thread. Returns immediately;
    /// the action runs asynchronously.
    /// </summary>
    void Post(Action action);

    /// <summary>
    /// Invokes <paramref name="action"/> on the UI thread and awaits its completion.
    /// </summary>
    Task InvokeAsync(Func<Task> action);
}
