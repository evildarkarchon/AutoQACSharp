using System;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace AutoQAC.Services.UI;

public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);

    public Task InvokeAsync(Func<Task> action) => Dispatcher.UIThread.InvokeAsync(action);
}
