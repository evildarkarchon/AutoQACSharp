using AutoQAC.Models;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;
using AutoQAC.Tests.TestInfrastructure;
using FluentAssertions;
using NSubstitute;

namespace AutoQAC.Tests.Services.Configuration;

/// <summary>Verifies operation-owned acceptance with controllable disk and discovery completion.</summary>
public sealed class DiscoverySettingsAcceptanceTests
{
    /// <summary>An older caller delayed before admission must not restore its choice after Reset or a newer edit.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DelayedInitialRead_CannotOvertakeNewerChoice(bool reset)
    {
        using var fixture = new Fixture();
        var initialRead = new TaskCompletionSource<UserConfiguration>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        fixture.Config.LoadUserConfigAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            Interlocked.Increment(ref reads) == 1 ? initialRead.Task : Task.FromResult(fixture.Saved.Copy()));
        var older = fixture.Module.ExecuteAsync(new DiscoverySettingsIntent.SetMo2Mode(true));
        var newer = await fixture.Module.ExecuteAsync(reset
            ? new DiscoverySettingsIntent.Reset()
            : new DiscoverySettingsIntent.SetMo2Mode(false));
        initialRead.SetResult(new UserConfiguration());

        newer.Status.Should().Be(DiscoverySettingsChangeStatus.Accepted);
        (await older.WaitAsync(TimeSpan.FromSeconds(5))).Status.Should().Be(DiscoverySettingsChangeStatus.Superseded);
        fixture.Saved.Settings.Mo2Mode.Should().BeFalse();
    }

    /// <summary>Operational editor fields must not replace paths resolved by the accepted MO2 discovery plan.</summary>
    [Fact]
    public async Task OperationalEdit_PreservesResolvedPublicationPaths()
    {
        using var fixture = new Fixture();
        fixture.State.UpdateState(s => s with { LoadOrderPath = "resolved-profile/plugins.txt", Mo2Profile = "Chosen" });
        var baseline = fixture.Saved.Copy();
        var edited = baseline.Copy();
        edited.Settings.CleaningTimeout = 900;

        var result = await fixture.Module.ExecuteAsync(new DiscoverySettingsIntent.ApplySettings(baseline, edited));

        result.Status.Should().Be(DiscoverySettingsChangeStatus.Accepted);
        fixture.State.CurrentState.LoadOrderPath.Should().Be("resolved-profile/plugins.txt");
        fixture.State.CurrentState.Mo2Profile.Should().Be("Chosen");
    }

    /// <summary>An unrelated watcher read warning does not contradict a successful explicit disk write.</summary>
    [Fact]
    public async Task SuccessfulDiskWrite_WithUnrelatedWatcherFailure_StillAccepts()
    {
        using var fixture = new Fixture();
        fixture.Config.LastFailure.Returns(new ConfigPersistenceFailure(ConfigPersistenceOperationKind.Watcher,
            ConfigPersistenceFailureKind.ReadFailed, "Could not read settings.", null, 1));

        var result = await fixture.Module.ExecuteAsync(new DiscoverySettingsIntent.SetMo2Mode(true));

        result.Status.Should().Be(DiscoverySettingsChangeStatus.Accepted);
    }

    /// <summary>A shared publication cannot complete a concurrent unrelated edit before its own disk barrier.</summary>
    [Fact]
    public async Task Publication_DoesNotAcceptAnOperationalEditStillWriting()
    {
        using var fixture = new Fixture();
        var rows = new TaskCompletionSource<PluginRefreshCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Refresh.RefreshForSettingsAsync(Arg.Any<GameType>(), Arg.Any<CancellationToken>()).Returns(rows.Task);
        var first = fixture.Module.ExecuteAsync(new DiscoverySettingsIntent.SetMo2Mode(true));
        var disk = new TaskCompletionSource<ConfigPersistenceResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Config.FlushPendingSavesAsync(Arg.Any<CancellationToken>()).Returns(disk.Task);
        var baseline = fixture.Saved.Copy();
        var edited = baseline.Copy();
        edited.Settings.CleaningTimeout = 900;
        var second = fixture.Module.ExecuteAsync(new DiscoverySettingsIntent.ApplySettings(baseline, edited));
        rows.SetResult(Fixture.NoGame);

        (await first.WaitAsync(TimeSpan.FromSeconds(5))).Status.Should().Be(DiscoverySettingsChangeStatus.Accepted);
        second.IsCompleted.Should().BeFalse();
        disk.SetResult(Fixture.Flushed);
        (await second.WaitAsync(TimeSpan.FromSeconds(5))).Status.Should().Be(DiscoverySettingsChangeStatus.Accepted);
    }

    /// <summary>Cancellation settles an already-submitted write before Reset can acquire mutation ownership.</summary>
    [Fact]
    public async Task CancellationDuringWrite_ReportsSavedChoiceAndCannotWriteAfterReset()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var disk = new TaskCompletionSource<ConfigPersistenceResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Config.FlushPendingSavesAsync(Arg.Any<CancellationToken>()).Returns(disk.Task, Task.FromResult(Fixture.Flushed));
        var change = fixture.Module.ExecuteAsync(new DiscoverySettingsIntent.SetMo2Mode(true), cancellation.Token);
        await cancellation.CancelAsync();
        var reset = fixture.Module.ExecuteAsync(new DiscoverySettingsIntent.Reset());

        change.IsCompleted.Should().BeFalse("the disk outcome must be known before cancellation reports saved state");
        reset.IsCompleted.Should().BeFalse("Reset must not race an already-submitted write");
        disk.SetResult(Fixture.Flushed);
        var canceled = await change.WaitAsync(TimeSpan.FromSeconds(5));
        canceled.Status.Should().Be(DiscoverySettingsChangeStatus.Canceled);
        canceled.SettingsSaved.Should().BeTrue();
        (await reset.WaitAsync(TimeSpan.FromSeconds(5))).Status.Should().Be(DiscoverySettingsChangeStatus.Accepted);
        fixture.Saved.Settings.Mo2Mode.Should().BeFalse();
    }

    /// <summary>A canceled queued caller must never submit settings after the mutation slot becomes available.</summary>
    [Fact]
    public async Task CancellationBeforeAdmission_DoesNotSaveChoice()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await fixture.Module.ExecuteAsync(new DiscoverySettingsIntent.SetMo2Mode(true), cancellation.Token);

        result.Status.Should().Be(DiscoverySettingsChangeStatus.Canceled);
        result.SettingsSaved.Should().BeFalse();
        fixture.Saved.Settings.Mo2Mode.Should().BeFalse();
    }

    /// <summary>User Skip list changes share the same durable and cleaning-admission guarantees.</summary>
    [Fact]
    public async Task SkipListChange_PersistsGameScopedListBeforeAccepting()
    {
        using var fixture = new Fixture();
        var result = await fixture.Module.ExecuteAsync(new DiscoverySettingsIntent.SetSkipList(GameType.SkyrimSe, ["Example.esp"]));
        result.Status.Should().Be(DiscoverySettingsChangeStatus.Accepted);
        fixture.Saved.SkipLists["SSE"].Should().Equal("Example.esp");
    }

    /// <summary>A dialog submits only its edits, never stale values for untouched fields.</summary>
    [Fact]
    public async Task DialogSave_MergesChangedFieldsAgainstLatestChoices()
    {
        using var fixture = new Fixture();
        var baseline = fixture.Saved.Copy();
        await fixture.Module.ExecuteAsync(new DiscoverySettingsIntent.SetMo2Mode(true));
        var edited = baseline.Copy();
        edited.Settings.CleaningTimeout = 900;

        var result = await fixture.Module.ExecuteAsync(new DiscoverySettingsIntent.ApplySettings(baseline, edited));

        result.Status.Should().Be(DiscoverySettingsChangeStatus.Accepted);
        fixture.Saved.Settings.Mo2Mode.Should().BeTrue();
        fixture.Saved.Settings.CleaningTimeout.Should().Be(900);
        await fixture.Refresh.Received(1).RefreshForSettingsAsync(Arg.Any<GameType>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A mixed dialog save is validated completely before changing any configuration.</summary>
    [Fact]
    public async Task DialogSave_WithInvalidDiscoveryPath_DoesNotPartiallySaveOtherEdits()
    {
        using var fixture = new Fixture();
        var baseline = fixture.Saved.Copy();
        var edited = baseline.Copy();
        edited.Settings.CleaningTimeout = 900;
        edited.ModOrganizer.Binary = "missing.exe";

        var result = await fixture.Module.ExecuteAsync(new DiscoverySettingsIntent.ApplySettings(baseline, edited));

        result.Status.Should().Be(DiscoverySettingsChangeStatus.Rejected);
        fixture.Saved.Settings.CleaningTimeout.Should().Be(baseline.Settings.CleaningTimeout);
    }

    /// <summary>Acceptance waits for durable persistence rather than the save acknowledgment.</summary>
    [Fact]
    public async Task Acceptance_WaitsForDiskBeforeStartingDiscovery()
    {
        using var fixture = new Fixture();
        var disk = new TaskCompletionSource<ConfigPersistenceResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Config.FlushPendingSavesAsync(Arg.Any<CancellationToken>()).Returns(disk.Task);
        var operation = fixture.Module.ExecuteAsync(new DiscoverySettingsIntent.SetMo2Mode(true));

        operation.IsCompleted.Should().BeFalse();
        await fixture.Refresh.DidNotReceive().RefreshForSettingsAsync(Arg.Any<GameType>(), Arg.Any<CancellationToken>());
        disk.SetResult(Fixture.Flushed);
        (await operation.WaitAsync(TimeSpan.FromSeconds(5))).Status.Should().Be(DiscoverySettingsChangeStatus.Accepted);
    }

    /// <summary>A later same-setting choice owns acceptance even if the older refresh finishes last.</summary>
    [Fact]
    public async Task SameSetting_SupersedesOlderOperationWithoutReplayingItsCompletion()
    {
        using var fixture = new Fixture();
        var oldRefresh = new TaskCompletionSource<PluginRefreshCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Refresh.RefreshForSettingsAsync(Arg.Any<GameType>(), Arg.Any<CancellationToken>())
            .Returns(oldRefresh.Task, Task.FromResult(Fixture.NoGame));
        var older = fixture.Module.ExecuteAsync(new DiscoverySettingsIntent.SetMo2Mode(true));
        var newer = fixture.Module.ExecuteAsync(new DiscoverySettingsIntent.SetMo2Mode(false));

        (await newer.WaitAsync(TimeSpan.FromSeconds(5))).Status.Should().Be(DiscoverySettingsChangeStatus.Accepted);
        (await older.WaitAsync(TimeSpan.FromSeconds(5))).Status.ToString().Should().Be("Superseded");
        oldRefresh.SetResult(Fixture.NoGame);
        fixture.Saved.Settings.Mo2Mode.Should().BeFalse();
    }

    /// <summary>Replacing a refresh does not discard an independent setting represented by its replacement.</summary>
    [Fact]
    public async Task DifferentSettings_ShareAcceptanceAndPreserveBothChoices()
    {
        using var fixture = new Fixture();
        var oldRefresh = new TaskCompletionSource<PluginRefreshCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Refresh.RefreshForSettingsAsync(Arg.Any<GameType>(), Arg.Any<CancellationToken>())
            .Returns(oldRefresh.Task, Task.FromResult(Fixture.NoGame));
        var first = fixture.Module.ExecuteAsync(new DiscoverySettingsIntent.SetMo2Mode(true));
        var second = fixture.Module.ExecuteAsync(new DiscoverySettingsIntent.SetDisableSkipLists(true));

        (await second.WaitAsync(TimeSpan.FromSeconds(5))).Status.Should().Be(DiscoverySettingsChangeStatus.Accepted);
        (await first.WaitAsync(TimeSpan.FromSeconds(5))).Status.Should().Be(DiscoverySettingsChangeStatus.Accepted);
        fixture.Saved.Settings.Mo2Mode.Should().BeTrue();
        fixture.Saved.Settings.DisableSkipLists.Should().BeTrue();
        oldRefresh.SetResult(new PluginRefreshCompletion(PluginRefreshCompletionStatus.Superseded));
    }

    /// <summary>An invalid newer choice must leave the admitted refresh and its acceptance intact.</summary>
    [Fact]
    public async Task InvalidNewChoice_DoesNotSupersedeValidWork()
    {
        using var fixture = new Fixture();
        var refresh = new TaskCompletionSource<PluginRefreshCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Refresh.RefreshForSettingsAsync(Arg.Any<GameType>(), Arg.Any<CancellationToken>()).Returns(refresh.Task);
        var valid = fixture.Module.ExecuteAsync(new DiscoverySettingsIntent.SetMo2Mode(true));
        var invalid = await fixture.Module.ExecuteAsync(new DiscoverySettingsIntent.SetMo2ExecutablePath("missing.exe"));

        invalid.Status.Should().Be(DiscoverySettingsChangeStatus.Rejected);
        valid.IsCompleted.Should().BeFalse();
        refresh.SetResult(Fixture.NoGame);
        (await valid.WaitAsync(TimeSpan.FromSeconds(5))).Status.Should().Be(DiscoverySettingsChangeStatus.Accepted);
    }

    /// <summary>Cancellation cannot claim acceptance or roll back a setting already written to disk.</summary>
    [Fact]
    public async Task CancellationAfterSave_PreservesChoiceAndDoesNotWaitForUncooperativeDiscovery()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var refresh = new TaskCompletionSource<PluginRefreshCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Refresh.RefreshForSettingsAsync(Arg.Any<GameType>(), Arg.Any<CancellationToken>()).Returns(refresh.Task);
        var operation = fixture.Module.ExecuteAsync(new DiscoverySettingsIntent.SetMo2Mode(true), cancellation.Token);
        await cancellation.CancelAsync();

        (await operation.WaitAsync(TimeSpan.FromSeconds(5))).Status.ToString().Should().Be("Canceled");
        fixture.Saved.Settings.Mo2Mode.Should().BeTrue();
        refresh.SetResult(Fixture.NoGame);
    }

    /// <summary>Reset prevents an older refresh from restoring its saved choices.</summary>
    [Fact]
    public async Task Reset_SupersedesEveryPendingChoice()
    {
        using var fixture = new Fixture();
        var oldRefresh = new TaskCompletionSource<PluginRefreshCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Refresh.RefreshForSettingsAsync(Arg.Any<GameType>(), Arg.Any<CancellationToken>())
            .Returns(oldRefresh.Task, Task.FromResult(Fixture.NoGame));
        var older = fixture.Module.ExecuteAsync(new DiscoverySettingsIntent.SetMo2Mode(true));
        var reset = await fixture.Module.ExecuteAsync(new DiscoverySettingsIntent.Reset());

        reset.Status.Should().Be(DiscoverySettingsChangeStatus.Accepted);
        (await older.WaitAsync(TimeSpan.FromSeconds(5))).Status.ToString().Should().Be("Superseded");
        fixture.Saved.Settings.Mo2Mode.Should().BeFalse();
        oldRefresh.SetResult(Fixture.NoGame);
    }

    /// <summary>Failure to establish publication preserves the choice but cannot enable cleaning.</summary>
    [Fact]
    public async Task MissingPublication_ReturnsRefreshFailureAfterSaving()
    {
        using var fixture = new Fixture();
        fixture.Refresh.RefreshForSettingsAsync(Arg.Any<GameType>(), Arg.Any<CancellationToken>())
            .Returns(new PluginRefreshCompletion(PluginRefreshCompletionStatus.Failed));

        var result = await fixture.Module.ExecuteAsync(new DiscoverySettingsIntent.SetMo2Mode(true));

        result.Status.ToString().Should().Be("RefreshFailed");
        fixture.Saved.Settings.Mo2Mode.Should().BeTrue();
    }

    private sealed class Fixture : IDisposable
    {
        public static ConfigPersistenceResult Flushed => new(ConfigPersistenceStatusKind.Success,
            ConfigPersistenceOperationKind.Flush, 1, null);
        public static PluginRefreshCompletion NoGame => new(PluginRefreshCompletionStatus.NoGame,
            RecordingPluginRefreshModule.CreateSnapshot());
        public IConfigurationService Config { get; } = Substitute.For<IConfigurationService>();
        public IPluginRefreshModule Refresh { get; } = Substitute.For<IPluginRefreshModule>();
        public StateService State { get; } = new();
        public UserConfiguration Saved { get; private set; } = new();
        public DiscoverySettingsModule Module { get; }

        /// <summary>The adapters model active configuration and durable save completion independently.</summary>
        public Fixture()
        {
            Config.LoadUserConfigAsync(Arg.Any<CancellationToken>()).Returns(_ => Saved.Copy());
            Config.SaveUserConfigAsync(Arg.Any<UserConfiguration>(), Arg.Any<CancellationToken>())
                .Returns(call => { Saved = call.Arg<UserConfiguration>().Copy(); return Task.CompletedTask; });
            Config.ResetToDefaultsAsync(Arg.Any<CancellationToken>())
                .Returns(_ => { Saved = new UserConfiguration(); return Task.CompletedTask; });
            Config.FlushPendingSavesAsync(Arg.Any<CancellationToken>()).Returns(Flushed);
            Refresh.RefreshForSettingsAsync(Arg.Any<GameType>(), Arg.Any<CancellationToken>()).Returns(NoGame);
            Module = new DiscoverySettingsModule(Config, State, Refresh);
        }

        public void Dispose()
        {
            Module.Dispose();
            State.Dispose();
        }
    }
}
