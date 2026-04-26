using System;
using System.Threading.Tasks;
using AutoQAC.Services.UI;

namespace AutoQAC.Tests.TestInfrastructure;

/// <summary>
/// Test double for <see cref="IUiDispatcher"/> that runs callbacks synchronously
/// on the calling thread, so assertions can run immediately after triggering a
/// state change without yielding.
/// </summary>
public sealed class SynchronousUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();

    public Task InvokeAsync(Func<Task> action) => action();
}
