using System;
using System.Threading.Tasks;
using AutoQAC.Services.UI.Interactions;
using FluentAssertions;
using Xunit;

namespace AutoQAC.Tests.Services.UI;

public sealed class InteractionTests
{
    [Fact]
    public async Task RegisterThenHandle_ReturnsHandlerOutput()
    {
        var interaction = new Interaction<int, string>();
        using var _ = interaction.RegisterHandler(input => Task.FromResult($"got {input}"));

        var result = await interaction.Handle(42);

        result.Should().Be("got 42");
    }

    [Fact]
    public void RegisterHandler_Twice_Throws()
    {
        var interaction = new Interaction<int, int>();
        using var _ = interaction.RegisterHandler(_ => Task.FromResult(1));

        var act = () => interaction.RegisterHandler(_ => Task.FromResult(2));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Handle_WithoutHandler_Throws()
    {
        var interaction = new Interaction<int, int>();

        var act = async () => await interaction.Handle(1);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DisposeRegistration_AllowsReregister()
    {
        var interaction = new Interaction<int, string>();
        var registration = interaction.RegisterHandler(input => Task.FromResult($"first {input}"));
        registration.Dispose();

        using var _ = interaction.RegisterHandler(input => Task.FromResult($"second {input}"));
        var result = await interaction.Handle(7);

        result.Should().Be("second 7");
    }

    [Fact]
    public async Task DisposeRegistration_AfterReregister_DoesNotClearNewHandler()
    {
        var interaction = new Interaction<int, string>();
        var first = interaction.RegisterHandler(_ => Task.FromResult("first"));
        first.Dispose();
        using var second = interaction.RegisterHandler(_ => Task.FromResult("second"));

        first.Dispose();
        var result = await interaction.Handle(0);

        result.Should().Be("second");
    }

    [Fact]
    public void RegisterHandler_NullHandler_Throws()
    {
        var interaction = new Interaction<int, int>();

        var act = () => interaction.RegisterHandler(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
