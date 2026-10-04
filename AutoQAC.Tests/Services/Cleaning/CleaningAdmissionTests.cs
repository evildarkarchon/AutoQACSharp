using AutoQAC.Services.Cleaning;
using AutoQAC.Services.UI;
using FluentAssertions;

namespace AutoQAC.Tests.Services.Cleaning;

public sealed class CleaningAdmissionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    /// <summary>Rejects new writes immediately while startup drains the existing write.</summary>
    [Fact]
    public async Task CleaningReservation_RejectsNewMutationsWhileDrainingAdmittedMutation()
    {
        var admission = new CleaningAdmission();
        using var mutation = await admission.TryEnterSettingsAsync();
        mutation.Should().NotBeNull();
        var cleaning = admission.EnterCleaningAsync();
        admission.IsCleaning.Should().BeTrue();
        cleaning.IsCompleted.Should().BeFalse();
        (await admission.TryEnterSettingsAsync()).Should().BeNull();
        mutation!.Dispose();
        using var session = await cleaning.WaitAsync(Timeout);
        (await admission.TryEnterSettingsAsync()).Should().BeNull();
        session.Dispose();
        using var next = await admission.TryEnterSettingsAsync();
        next.Should().NotBeNull();
    }

    /// <summary>
    ///     A settings caller already queued behind an admitted write must not slip in ahead of Cleaning startup:
    ///     the reservation is taken before the drain, so the queued caller is rejected once it wins the lane.
    /// </summary>
    [Fact]
    public async Task EnterCleaning_ReservesBeforeDraining_QueuedSettingsCallerIsRejected()
    {
        var admission = new CleaningAdmission();
        var admitted = await admission.TryEnterSettingsAsync();
        admitted.Should().NotBeNull();
        var queued = admission.TryEnterSettingsAsync();
        queued.IsCompleted.Should().BeFalse("the queued caller waits for the admitted write");

        var cleaning = admission.EnterCleaningAsync();
        admission.IsCleaning.Should().BeTrue("the reservation is visible before any drain completes");
        cleaning.IsCompleted.Should().BeFalse();

        admitted!.Dispose();
        (await queued.WaitAsync(Timeout)).Should().BeNull();
        using var cleaningLease = await cleaning.WaitAsync(Timeout);
        admission.IsCleaning.Should().BeTrue();
    }

    /// <summary>Every pre-clean entry point is rejected once Cleaning holds the reservation.</summary>
    [Fact]
    public async Task Reservation_RejectsEveryEntryPointUntilReleased()
    {
        var admission = new CleaningAdmission();
        var cleaningLease = await admission.EnterCleaningAsync().WaitAsync(Timeout);
        using var previewCancellation = new CancellationTokenSource();

        (await admission.TryEnterSettingsAsync()).Should().BeNull();
        (await admission.TryEnterPluginMutationAsync()).Should().BeNull();
        (await admission.TryEnterPreviewAsync(previewCancellation)).Should().BeNull();
        admission.TryEnterExternalSettings().Should().BeNull();
        admission.TryBeginRefresh(CancellationToken.None, out var superseded).Should().BeNull();
        superseded.Should().BeNull();
        await FluentActions.Awaiting(() => admission.EnterCleaningAsync())
            .Should().ThrowAsync<InvalidOperationException>();

        cleaningLease.Dispose();

        using var settings = await admission.TryEnterSettingsAsync();
        settings.Should().NotBeNull("releasing the Cleaning lease reopens admission");
    }

    /// <summary>A canceled startup must not release a mutation lease owned by another caller.</summary>
    [Fact]
    public async Task CanceledCleaningReservation_ReopensAdmissionWithoutReleasingActiveMutation()
    {
        var admission = new CleaningAdmission();
        using var mutation = await admission.TryEnterSettingsAsync();
        using var cancellation = new CancellationTokenSource();
        var cleaning = admission.EnterCleaningAsync(cancellation.Token);
        cancellation.Cancel();
        await FluentActions.Awaiting(() => cleaning).Should().ThrowAsync<OperationCanceledException>();
        admission.IsCleaning.Should().BeFalse();
        var next = admission.TryEnterSettingsAsync();
        next.IsCompleted.Should().BeFalse();
        mutation!.Dispose();
        using var admitted = await next.WaitAsync(Timeout);
        admitted.Should().NotBeNull();
    }

    /// <summary>
    ///     A preview lease disposed by its cancellation callback while Cleaning is canceling it, and again by the
    ///     preview's own cleanup, releases the mutation lane exactly once.
    /// </summary>
    [Fact]
    public async Task PreviewLeaseDisposedWhileCancellationInProgress_ReleasesLaneExactlyOnce()
    {
        var admission = new CleaningAdmission();
        using var previewCancellation = new CancellationTokenSource();
        var preview = await admission.TryEnterPreviewAsync(previewCancellation);
        preview.Should().NotBeNull();
        var disposedByCancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = previewCancellation.Token.Register(() =>
        {
            // Cancellation-driven cleanup races the preview's own using-scope disposal below.
            preview!.Dispose();
            disposedByCancellation.TrySetResult();
        });

        var cleaning = admission.EnterCleaningAsync();
        preview!.Dispose();
        await disposedByCancellation.Task.WaitAsync(Timeout);
        var cleaningLease = await cleaning.WaitAsync(Timeout);
        cleaningLease.Dispose();

        admission.IsCleaning.Should().BeFalse();
        var first = await admission.TryEnterSettingsAsync().WaitAsync(Timeout);
        first.Should().NotBeNull();
        var second = admission.TryEnterSettingsAsync();
        second.IsCompleted.Should().BeFalse("a twice-disposed lease must not leave two lane slots open");
        first!.Dispose();
        using var secondLease = await second.WaitAsync(Timeout);
        secondLease.Should().NotBeNull();
    }

    /// <summary>
    ///     Cleaning cancels a refresh operation begun before its mutation lease is released, and keeps admission
    ///     until the operation is disposed, meaning the refresh has fully unwound.
    /// </summary>
    [Fact]
    public async Task CleaningReservation_WaitsForRefreshOperationToUnwind()
    {
        var admission = new CleaningAdmission();
        using var mutation = await admission.TryEnterSettingsAsync();
        var operation = admission.TryBeginRefresh(CancellationToken.None, out _)!;

        var cleaning = admission.EnterCleaningAsync();

        operation.IsCurrent.Should().BeFalse("the operation stops being current with the reservation");
        operation.IsSuperseded.Should().BeFalse("Cleaning cancels operations; it does not supersede them");
        operation.Token.IsCancellationRequested.Should().BeTrue();
        mutation!.Dispose();
        // Startup can only finish by awaiting Unwound, which nothing completes until the operation is disposed.
        operation.Unwound.IsCompleted.Should().BeFalse();
        cleaning.IsCompleted.Should().BeFalse(
            "Cleaning must retain admission until the canceled refresh has fully unwound");

        operation.Dispose();
        using var cleaningLease = await cleaning.WaitAsync(Timeout);
        admission.IsCleaning.Should().BeTrue();
        admission.CurrentRefresh.Should().BeNull();
    }

    /// <summary>
    ///     Reservation cancels a refresh operation and an active preview, then completes once both have unwound.
    /// </summary>
    [Fact]
    public async Task EnterCleaning_CancelsThenDrainsRefreshAndPreview()
    {
        var admission = new CleaningAdmission();
        var operation = admission.TryBeginRefresh(CancellationToken.None, out _)!;
        using var previewCancellation = new CancellationTokenSource();
        var preview = await admission.TryEnterPreviewAsync(previewCancellation);
        preview.Should().NotBeNull();

        var cleaning = admission.EnterCleaningAsync();

        operation.Token.IsCancellationRequested.Should().BeTrue();
        previewCancellation.IsCancellationRequested.Should().BeTrue();
        cleaning.IsCompleted.Should().BeFalse("the preview still holds the mutation lane");

        preview!.Dispose();
        operation.Unwound.IsCompleted.Should().BeFalse();
        cleaning.IsCompleted.Should().BeFalse("the canceled refresh has not unwound yet");

        operation.Dispose();
        using var cleaningLease = await cleaning.WaitAsync(Timeout);
        admission.IsCleaning.Should().BeTrue();
    }

    /// <summary>A refresh canceled for Cleaning must drain without canceling the Cleaning reservation.</summary>
    [Fact]
    public async Task CleaningReservation_RefreshCanceledForCleaningStillAdmitsCleaning()
    {
        var admission = new CleaningAdmission();
        using var mutation = await admission.TryEnterSettingsAsync();
        var operation = admission.TryBeginRefresh(CancellationToken.None, out _)!;
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = operation.Token.Register(cancellationObserved.SetResult);
        using var subscription = admission.CleaningState.Subscribe(new CallbackObserver<bool>(reserved =>
        {
            if (reserved) mutation!.Dispose();
        }));

        var cleaning = admission.EnterCleaningAsync();
        await cancellationObserved.Task.WaitAsync(Timeout);
        cleaning.IsCompleted.Should().BeFalse("Cleaning must wait for the canceled refresh to unwind");

        // The refresh unwinds through its own cancellation; disposal is still a normal completion for the drain.
        operation.Dispose();
        using var cleaningLease = await cleaning.WaitAsync(Timeout);
        admission.IsCleaning.Should().BeTrue();
    }

    /// <summary>Beginning an operation supersedes the current one before the successor becomes current.</summary>
    [Fact]
    public void TryBeginRefresh_SupersedesCurrentOperation()
    {
        var admission = new CleaningAdmission();
        var first = admission.TryBeginRefresh(CancellationToken.None, out var none)!;
        none.Should().BeNull();

        var second = admission.TryBeginRefresh(CancellationToken.None, out var superseded);

        superseded.Should().BeSameAs(first);
        first.IsSuperseded.Should().BeTrue();
        first.IsCurrent.Should().BeFalse();
        first.Token.IsCancellationRequested.Should().BeTrue();
        second.Should().NotBeNull();
        second!.IsCurrent.Should().BeTrue();
        admission.CurrentRefresh.Should().BeSameAs(second);
    }

    /// <summary>A fence supersedes the current operation without a successor and leaves no current operation.</summary>
    [Fact]
    public void SupersedeRefresh_LeavesNoCurrentOperation()
    {
        var admission = new CleaningAdmission();
        admission.SupersedeRefresh().Should().BeNull("nothing is current before any refresh begins");
        var operation = admission.TryBeginRefresh(CancellationToken.None, out _)!;

        var superseded = admission.SupersedeRefresh();

        superseded.Should().BeSameAs(operation);
        operation.IsSuperseded.Should().BeTrue();
        operation.Token.IsCancellationRequested.Should().BeTrue();
        admission.CurrentRefresh.Should().BeNull();
        admission.SupersedeRefresh().Should().BeNull("a fence supersedes only once");
    }

    /// <summary>Disposal means fully unwound: the operation leaves currency and the drain set.</summary>
    [Fact]
    public async Task Dispose_RemovesOperationFromCurrencyAndDrain()
    {
        var admission = new CleaningAdmission();
        var operation = admission.TryBeginRefresh(CancellationToken.None, out _)!;

        operation.Dispose();
        operation.Dispose();

        operation.Unwound.IsCompleted.Should().BeTrue();
        admission.CurrentRefresh.Should().BeNull();
        admission.SupersedeRefresh().Should().BeNull("a completed refresh has nothing left to fence");
        using var cleaningLease = await admission.EnterCleaningAsync().WaitAsync(Timeout);
    }

    /// <summary>The operation's one token is linked to its caller's cancellation.</summary>
    [Fact]
    public void TryBeginRefresh_LinksCallerCancellation()
    {
        var admission = new CleaningAdmission();
        using var caller = new CancellationTokenSource();
        var operation = admission.TryBeginRefresh(caller.Token, out _)!;

        caller.Cancel();

        operation.Token.IsCancellationRequested.Should().BeTrue();
        operation.IsCurrent.Should().BeFalse();
        operation.IsSuperseded.Should().BeFalse();
        admission.CurrentRefresh.Should().BeSameAs(operation, "a canceled operation stays current until it unwinds");
    }

    /// <summary>Late subscribers receive the current reservation immediately, then each transition.</summary>
    [Fact]
    public async Task CleaningState_ReplaysCurrentReservationThenTransitions()
    {
        var admission = new CleaningAdmission();
        var initial = new List<bool>();
        using (admission.CleaningState.Subscribe(new CallbackObserver<bool>(initial.Add)))
            initial.Should().Equal(false);

        var cleaningLease = await admission.EnterCleaningAsync().WaitAsync(Timeout);
        var observed = new List<bool>();
        using var subscription = admission.CleaningState.Subscribe(new CallbackObserver<bool>(observed.Add));
        observed.Should().Equal(true);

        cleaningLease.Dispose();
        cleaningLease.Dispose();

        observed.Should().Equal(true, false);
    }

    /// <summary>
    ///     Reservation is published synchronously before draining, so observers can cancel their own work and
    ///     let the drain finish; release is published only after the Cleaning lease is disposed.
    /// </summary>
    [Fact]
    public async Task CleaningState_PublishesReservationBeforeDrainAndReleaseAfterLease()
    {
        var admission = new CleaningAdmission();
        using var mutation = await admission.TryEnterSettingsAsync();
        var observed = new List<bool>();
        using var subscription = admission.CleaningState.Subscribe(new CallbackObserver<bool>(observed.Add));

        var cleaning = admission.EnterCleaningAsync();

        observed.Should().Equal(false, true);
        cleaning.IsCompleted.Should().BeFalse();
        mutation!.Dispose();
        var cleaningLease = await cleaning.WaitAsync(Timeout);
        observed.Should().Equal(false, true);

        cleaningLease.Dispose();
        observed.Should().Equal(false, true, false);
    }

    /// <summary>A canceled startup publishes its release so observers never stay locked.</summary>
    [Fact]
    public async Task CleaningState_CanceledStartupPublishesRelease()
    {
        var admission = new CleaningAdmission();
        using var mutation = await admission.TryEnterSettingsAsync();
        var observed = new List<bool>();
        using var subscription = admission.CleaningState.Subscribe(new CallbackObserver<bool>(observed.Add));
        using var cancellation = new CancellationTokenSource();

        var cleaning = admission.EnterCleaningAsync(cancellation.Token);
        await cancellation.CancelAsync();
        await FluentActions.Awaiting(() => cleaning).Should().ThrowAsync<OperationCanceledException>();

        observed.Should().Equal(false, true, false);
    }
}
