using System;
using System.Reactive.Concurrency;
using ReactiveUI;
using Xunit;

namespace AutoQAC.Tests.TestInfrastructure;

public static class RxAppSchedulerCollection
{
    public const string Name = "RxApp scheduler";
}

[CollectionDefinition(RxAppSchedulerCollection.Name, DisableParallelization = true)]
public sealed class RxAppSchedulerTestCollection;

public sealed class RxAppMainThreadSchedulerScope : IDisposable
{
    private readonly IScheduler _originalScheduler;

    public RxAppMainThreadSchedulerScope(IScheduler scheduler)
    {
        _originalScheduler = RxApp.MainThreadScheduler;
        RxApp.MainThreadScheduler = scheduler;
    }

    public void Dispose()
    {
        RxApp.MainThreadScheduler = _originalScheduler;
    }
}

public sealed class RxAppEventLoopMainThreadSchedulerScope : IDisposable
{
    private readonly EventLoopScheduler _scheduler;
    private readonly RxAppMainThreadSchedulerScope _schedulerScope;

    public RxAppEventLoopMainThreadSchedulerScope()
    {
        var signal = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        _scheduler = new EventLoopScheduler(start => new Thread(() =>
        {
            signal.TrySetResult(Environment.CurrentManagedThreadId);
            start();
        })
        {
            IsBackground = true,
            Name = "RxApp.MainThreadScheduler test thread"
        });

        _schedulerScope = new RxAppMainThreadSchedulerScope(_scheduler);
        _scheduler.Schedule(() => { });
        ThreadIdTask = signal.Task;
    }

    public Task<int> ThreadIdTask { get; }

    public void Dispose()
    {
        _schedulerScope.Dispose();
        _scheduler.Dispose();
    }
}

public abstract class ImmediateMainThreadSchedulerTestBase : IDisposable
{
    private readonly RxAppMainThreadSchedulerScope _schedulerScope = new(Scheduler.Immediate);

    public void Dispose()
    {
        _schedulerScope.Dispose();
        DisposeCore();
    }

    protected virtual void DisposeCore()
    {
    }
}
