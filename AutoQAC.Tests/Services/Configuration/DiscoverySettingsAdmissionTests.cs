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

    /// <summary>Cleaning cancels and drains a settings publication registered before its mutation lease is released.</summary>
    [Fact]
    public async Task CleaningReservation_WaitsForTrackedSettingsPublicationToUnwind()
    {
        var admission = new DiscoverySettingsAdmission();
        using var mutation = await admission.TryEnterSettingsAsync();
        using var publicationCancellation = new CancellationTokenSource();
        var publication = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = publicationCancellation.Token.Register(cancellationObserved.SetResult);
        admission.TrackSettingsPublication(publication.Task, publicationCancellation);

        var cleaning = admission.EnterCleaningAsync();
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        mutation!.Dispose();

        try
        {
            var firstCompletion = await Task.WhenAny(cleaning, Task.Delay(TimeSpan.FromSeconds(1)));
            firstCompletion.Should().NotBeSameAs(cleaning,
                "Cleaning must retain admission until the canceled settings publication has fully unwound");
        }
        finally
        {
            publication.TrySetResult();
            using var cleaningLease = await cleaning.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }
}
