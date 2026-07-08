using AutoQAC.Services.Process;
using FluentAssertions;

namespace AutoQAC.Tests.Services;

/// <summary>
/// Tests for the named-mutex single-instance guard.
/// </summary>
public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void FirstGuard_ForUniqueName_ShouldAcquireInstanceLock()
    {
        using var guard = new SingleInstanceGuard($"Local\\AutoQACTest-{Guid.NewGuid():N}");

        guard.HasInstanceLock.Should().BeTrue();
    }

    [Fact]
    public void SecondGuard_ForSameUniqueName_ShouldNotAcquireUntilFirstDisposed()
    {
        var name = $"Local\\AutoQACTest-{Guid.NewGuid():N}";
        using var first = new SingleInstanceGuard(name);
        using var second = new SingleInstanceGuard(name);

        first.HasInstanceLock.Should().BeTrue();
        second.HasInstanceLock.Should().BeFalse();
    }

    [Fact]
    public void DisposingNonOwningGuard_ShouldNotReleaseOwningGuardMutex()
    {
        var name = $"Local\\AutoQACTest-{Guid.NewGuid():N}";
        using var first = new SingleInstanceGuard(name);
        using (var second = new SingleInstanceGuard(name))
        {
            second.HasInstanceLock.Should().BeFalse();
        }

        using var third = new SingleInstanceGuard(name);
        third.HasInstanceLock.Should().BeFalse();
        first.HasInstanceLock.Should().BeTrue();
    }
}
