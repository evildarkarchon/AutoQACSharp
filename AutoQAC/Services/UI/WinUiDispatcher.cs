using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

namespace AutoQAC.Services.UI;

public sealed class WinUiDispatcher(DispatcherQueue? dispatcherQueue = null) : IUiDispatcher
{
    private readonly DispatcherQueue? _dispatcherQueue = dispatcherQueue ?? TryGetDispatcherQueueForCurrentThread();

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_dispatcherQueue == null || _dispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        _dispatcherQueue.TryEnqueue(() => action());
    }

    public Task InvokeAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_dispatcherQueue == null || _dispatcherQueue.HasThreadAccess)
        {
            return action();
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcherQueue.TryEnqueue(() =>
        {
            Task task;
            try
            {
                task = action();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
                return;
            }

            task.ContinueWith(
                completedTask =>
                {
                    if (completedTask.Exception != null)
                    {
                        completion.SetException(completedTask.Exception.InnerExceptions);
                    }
                    else if (completedTask.IsCanceled)
                    {
                        completion.SetCanceled();
                    }
                    else
                    {
                        completion.SetResult();
                    }
                },
                TaskScheduler.Default);
        }))
        {
            completion.SetException(new InvalidOperationException("Failed to enqueue work on the UI dispatcher."));
        }

        return completion.Task;
    }

    private static DispatcherQueue? TryGetDispatcherQueueForCurrentThread()
    {
        try
        {
            return DispatcherQueue.GetForCurrentThread();
        }
        catch (COMException)
        {
            return null;
        }
    }
}
