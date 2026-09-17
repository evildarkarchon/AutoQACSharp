using AutoQAC.Services.Configuration;
using FluentAssertions;

namespace AutoQAC.Tests.Services.Configuration;

public sealed class DiscoverySettingsAdmissionTests
{
    /// <summary>Rejects new writes immediately while startup drains the existing write.</summary>
    [Fact]
    public async Task CleaningReservation_RejectsNewMutationsWhileDrainingAdmittedMutation()
    {
        var admission = new DiscoverySettingsAdmission();
        using var mutation = await admission.TryEnterSettingsAsync();
        mutation.Should().NotBeNull();
        var cleaning = admission.EnterCleaningAsync();
        admission.IsCleaning.Should().BeTrue();
        cleaning.IsCompleted.Should().BeFalse();
        (await admission.TryEnterSettingsAsync()).Should().BeNull();
        mutation!.Dispose();
        using var session = await cleaning.WaitAsync(TimeSpan.FromSeconds(2));
        (await admission.TryEnterSettingsAsync()).Should().BeNull();
        session.Dispose();
        using var next = await admission.TryEnterSettingsAsync();
        next.Should().NotBeNull();
    }

    /// <summary>A canceled startup must not release a mutation lease owned by another caller.</summary>
    [Fact]
    public async Task CanceledCleaningReservation_ReopensAdmissionWithoutReleasingActiveMutation()
    {
        var admission = new DiscoverySettingsAdmission();
        using var mutation = await admission.TryEnterSettingsAsync();
        using var cancellation = new CancellationTokenSource();
        var cleaning = admission.EnterCleaningAsync(cancellation.Token);
        cancellation.Cancel();
        await FluentActions.Awaiting(() => cleaning).Should().ThrowAsync<OperationCanceledException>();
        admission.IsCleaning.Should().BeFalse();
        var next = admission.TryEnterSettingsAsync();
        next.IsCompleted.Should().BeFalse();
        mutation!.Dispose();
        using var admitted = await next.WaitAsync(TimeSpan.FromSeconds(2));
        admitted.Should().NotBeNull();
    }
}
