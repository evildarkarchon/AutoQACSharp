using System;
using System.Threading.Tasks;
using AutoQAC.Tests.TestInfrastructure;
using FluentAssertions;
using Xunit;

namespace AutoQAC.Tests.Services.UI;

public sealed class SynchronousUiDispatcherTests
{
    [Fact]
    public void Post_RunsActionBeforeReturning()
    {
        var dispatcher = new SynchronousUiDispatcher();
        var ran = false;

        dispatcher.Post(() => ran = true);

        ran.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_AwaitsInnerTask()
    {
        var dispatcher = new SynchronousUiDispatcher();
        var ran = false;

        await dispatcher.InvokeAsync(async () =>
        {
            await Task.Yield();
            ran = true;
        });

        ran.Should().BeTrue();
    }

    [Fact]
    public void Post_PropagatesExceptions()
    {
        var dispatcher = new SynchronousUiDispatcher();

        var act = () => dispatcher.Post(() => throw new InvalidOperationException("boom"));

        act.Should().Throw<InvalidOperationException>().WithMessage("boom");
    }
}
