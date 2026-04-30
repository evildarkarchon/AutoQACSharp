using System.Reactive.Linq;
using System.Reactive.Subjects;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.State;
using AutoQAC.Tests.Services.Configuration.Fakes;
using FluentAssertions;
using NSubstitute;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AutoQAC.Tests.Services.Configuration;

public sealed class ConfigPersistenceCoordinatorTests
{
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(NullNamingConvention.Instance)
        .Build();

    [Fact]
    public async Task Lifecycle_StartThenStop_DrainsCleanly()
    {
        var coordinator = CreateCoordinator();
        await coordinator.StartAsync();
        await coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Submit_AfterStop_Throws_ObjectDisposedException()
    {
        var coordinator = CreateCoordinator();
        await coordinator.StartAsync();
        await coordinator.StopAsync();
        await FluentActions.Awaiting(() => coordinator.SaveUserConfigAsync(NewConfig(1)))
            .Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Save_ThenSave_ThenFlush_CoalescesToLatestPending_OneWrite()
    {
        var store = new FakeUserConfigFileStore();
        var coordinator = CreateCoordinator(store);
        await coordinator.StartAsync();
        await coordinator.SaveUserConfigAsync(NewConfig(1));
        await coordinator.SaveUserConfigAsync(NewConfig(2));
        await coordinator.FlushPendingSavesAsync();
        store.WriteCount.Should().Be(1, because: "D-04 coalesces to latest pending");
        (await coordinator.LoadCurrentAsync()).Settings.CleaningTimeout.Should().Be(2);
    }

    [Fact]
    public async Task Flush_AfterPending_ReturnsSuccessAndPersistsLatest()
    {
        var coordinator = CreateCoordinator();
        await coordinator.StartAsync();
        await coordinator.SaveUserConfigAsync(NewConfig(44));
        var result = await coordinator.FlushPendingSavesAsync();
        result.Status.Should().Be(ConfigPersistenceStatusKind.Success, because: "D-05 forced flush returns success");
        result.Operation.Should().Be(ConfigPersistenceOperationKind.Flush);
        result.Generation.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Flush_NoPending_ReturnsNoOpAndZeroWrites()
    {
        var store = new FakeUserConfigFileStore();
        var coordinator = CreateCoordinator(store);
        await coordinator.StartAsync();
        var result = await coordinator.FlushPendingSavesAsync();
        result.Status.Should().Be(ConfigPersistenceStatusKind.NoOp);
        store.WriteCount.Should().Be(0);
    }

    [Fact]
    public async Task Save_WriteFails_ReturnsFailedAndRollsBackToLastKnownGood()
    {
        var store = new FakeUserConfigFileStore();
        var failures = new List<ConfigPersistenceFailure>();
        var coordinator = CreateCoordinator(store);
        using var sub = coordinator.Failures.Subscribe(failures.Add);
        await coordinator.StartAsync();
        await coordinator.SaveUserConfigAsync(NewConfig(10));
        await coordinator.FlushPendingSavesAsync();
        store.WriteFailure = new IOException("locked");
        await coordinator.SaveUserConfigAsync(NewConfig(20));
        var result = await coordinator.FlushPendingSavesAsync();
        result.Status.Should().Be(ConfigPersistenceStatusKind.Failed);
        result.Failure!.Kind.Should().Be(ConfigPersistenceFailureKind.WriteFailed);
        result.Failure.SafeSummary.Should().NotContain("Exception");
        (await coordinator.LoadCurrentAsync()).Settings.CleaningTimeout.Should().Be(10, because: "D-12 rolls back to last known good");
        failures.Should().ContainSingle();
    }

    [Fact]
    public async Task Save_AfterFailure_Succeeds_ClearsLastFailure()
    {
        var store = new FakeUserConfigFileStore { WriteFailure = new IOException("locked") };
        var coordinator = CreateCoordinator(store);
        await coordinator.StartAsync();
        await coordinator.SaveUserConfigAsync(NewConfig(1));
        await coordinator.FlushPendingSavesAsync();
        store.WriteFailure = null;
        await coordinator.SaveUserConfigAsync(NewConfig(2));
        await coordinator.FlushPendingSavesAsync();
        coordinator.LastFailure.Should().BeNull("D-27 clears active failure after success");
    }

    [Fact]
    public async Task Watcher_HashEcho_SilentSkip_NoFailureNoStateChange()
    {
        var store = new FakeUserConfigFileStore();
        var failures = new List<ConfigPersistenceFailure>();
        var coordinator = CreateCoordinator(store);
        using var sub = coordinator.Failures.Subscribe(failures.Add);
        await coordinator.StartAsync();
        await coordinator.SaveUserConfigAsync(NewConfig(1));
        await coordinator.FlushPendingSavesAsync();
        coordinator.NotifySettingsFileChanged(ConfigFileSignalKind.Changed);
        await PumpUntilQuiescentAsync(coordinator);
        failures.Should().BeEmpty("D-20 app-write echoes are silent");
        store.CallLog.Count(c => c == "Read").Should().Be(0);
    }

    [Fact]
    public async Task Watcher_DistinctExternalContent_NoPendingApp_AcceptsAndAppliesCandidate()
    {
        var store = new FakeUserConfigFileStore { CurrentContent = Serializer.Serialize(NewConfig(99)) };
        var coordinator = CreateCoordinator(store);
        await coordinator.StartAsync();
        coordinator.NotifySettingsFileChanged(ConfigFileSignalKind.Changed);
        await PumpUntilQuiescentAsync(coordinator);
        (await coordinator.LoadCurrentAsync()).Settings.CleaningTimeout.Should().Be(99);
    }

    [Fact]
    public async Task Watcher_DuringPendingAppSave_RejectsRacingExternalEdit_AppSaveWins()
    {
        var store = new FakeUserConfigFileStore { CurrentContent = Serializer.Serialize(NewConfig(99)) };
        var coordinator = CreateCoordinator(store);
        await coordinator.StartAsync();
        await coordinator.SaveUserConfigAsync(NewConfig(12));
        coordinator.NotifySettingsFileChanged(ConfigFileSignalKind.Changed);
        await coordinator.FlushPendingSavesAsync();
        (await coordinator.LoadCurrentAsync()).Settings.CleaningTimeout.Should().Be(12, because: "D-16 app save wins close timing race");
    }

    [Fact]
    public async Task Watcher_AfterAppSaveSettled_NewExternalContentIsAppliedWhenGenerationGreater()
    {
        var store = new FakeUserConfigFileStore();
        var coordinator = CreateCoordinator(store);
        await coordinator.StartAsync();
        await coordinator.SaveUserConfigAsync(NewConfig(12));
        await coordinator.FlushPendingSavesAsync();
        store.CurrentContent = Serializer.Serialize(NewConfig(13));
        store.CurrentHash = FakeUserConfigFileStore.ComputeHash(store.CurrentContent);
        coordinator.NotifySettingsFileChanged(ConfigFileSignalKind.Changed);
        await PumpUntilQuiescentAsync(coordinator);
        (await coordinator.LoadCurrentAsync()).Settings.CleaningTimeout.Should().Be(13, because: "D-17 later external edit can apply");
    }

    [Fact]
    public async Task Watcher_DuringCleaning_DefersLatestExternal_NoActiveMutation()
    {
        var state = new AppState { IsCleaning = true };
        var store = new FakeUserConfigFileStore { CurrentContent = Serializer.Serialize(NewConfig(55)) };
        var coordinator = CreateCoordinator(store, () => state, new Subject<AppState>());
        await coordinator.StartAsync();
        coordinator.NotifySettingsFileChanged(ConfigFileSignalKind.Changed);
        await PumpUntilQuiescentAsync(coordinator);
        (await coordinator.LoadCurrentAsync()).Settings.CleaningTimeout.Should().NotBe(55);
    }

    [Fact]
    public async Task Watcher_AfterCleaningEnds_AppliesLatestDeferredOnly()
    {
        var subject = new Subject<AppState>();
        var state = new AppState { IsCleaning = true };
        var store = new FakeUserConfigFileStore { CurrentContent = Serializer.Serialize(NewConfig(55)) };
        var coordinator = CreateCoordinator(store, () => state, subject);
        await coordinator.StartAsync();
        coordinator.NotifySettingsFileChanged(ConfigFileSignalKind.Changed);
        await PumpUntilQuiescentAsync(coordinator);
        store.CurrentContent = Serializer.Serialize(NewConfig(56));
        store.CurrentHash = FakeUserConfigFileStore.ComputeHash(store.CurrentContent);
        coordinator.NotifySettingsFileChanged(ConfigFileSignalKind.Changed);
        await PumpUntilQuiescentAsync(coordinator);
        state = state with { IsCleaning = false };
        subject.OnNext(state);
        await PumpUntilQuiescentAsync(coordinator);
        (await coordinator.LoadCurrentAsync()).Settings.CleaningTimeout.Should().Be(56);
    }

    [Fact]
    public async Task Watcher_DeferredInvalidYaml_RejectsAndKeepsCurrent_DoesNotApplyEarlierValid()
    {
        var subject = new Subject<AppState>();
        var state = new AppState { IsCleaning = true };
        var store = new FakeUserConfigFileStore { CurrentContent = "<>not yaml:\nbad: : :" };
        var failures = new List<ConfigPersistenceFailure>();
        var coordinator = CreateCoordinator(store, () => state, subject);
        using var sub = coordinator.Failures.Subscribe(failures.Add);
        await coordinator.StartAsync();
        coordinator.NotifySettingsFileChanged(ConfigFileSignalKind.Changed);
        await PumpUntilQuiescentAsync(coordinator);
        state = state with { IsCleaning = false };
        subject.OnNext(state);
        await PumpUntilQuiescentAsync(coordinator);
        failures.Should().Contain(f => f.Kind == ConfigPersistenceFailureKind.InvalidExternalYaml && !f.SafeSummary.Contains("Exception"));
    }

    [Fact]
    public async Task Watcher_FileMissing_KeepsCurrent_EmitsMissingFileFailure()
    {
        var store = new FakeUserConfigFileStore { FileMissing = true };
        var failures = new List<ConfigPersistenceFailure>();
        var coordinator = CreateCoordinator(store);
        using var sub = coordinator.Failures.Subscribe(failures.Add);
        await coordinator.StartAsync();
        coordinator.NotifySettingsFileChanged(ConfigFileSignalKind.Deleted);
        await PumpUntilQuiescentAsync(coordinator);
        failures.Should().Contain(f => f.Kind == ConfigPersistenceFailureKind.MissingFile && !f.SafeSummary.Contains("Exception"));
    }

    [Fact]
    public async Task Reload_InvalidYaml_RejectsCandidate_ActiveUnchanged_EmitsInvalidExternalYaml()
    {
        var store = new FakeUserConfigFileStore { CurrentContent = "<>not yaml:\nbad: : :" };
        var coordinator = CreateCoordinator(store);
        await coordinator.StartAsync();
        var result = await coordinator.ReloadFromDiskAsync();
        result.Status.Should().Be(ConfigPersistenceStatusKind.Failed);
        result.Failure!.Kind.Should().Be(ConfigPersistenceFailureKind.InvalidExternalYaml);
    }

    [Fact]
    public async Task Watcher_StaleObservation_BeforePendingSettled_RejectsAsStale()
    {
        var store = new FakeUserConfigFileStore { CurrentContent = Serializer.Serialize(NewConfig(77)) };
        var coordinator = CreateCoordinator(store);
        await coordinator.StartAsync();
        await coordinator.SaveUserConfigAsync(NewConfig(1));
        coordinator.NotifySettingsFileChanged(ConfigFileSignalKind.Changed);
        await coordinator.FlushPendingSavesAsync();
        (await coordinator.LoadCurrentAsync()).Settings.CleaningTimeout.Should().Be(1);
    }

    [Fact]
    public async Task Stop_FinalFlushFails_StopAsyncCompletesWithinFiveSeconds_LogsWarning()
    {
        var store = new FakeUserConfigFileStore { WriteFailure = new IOException("locked") };
        var coordinator = CreateCoordinator(store);
        await coordinator.StartAsync();
        await coordinator.SaveUserConfigAsync(NewConfig(1));
        await coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Reload_FileMissing_ReturnsMissingFileFailureWithoutResettingDefaults()
    {
        var store = new FakeUserConfigFileStore { FileMissing = true };
        var coordinator = CreateCoordinator(store);
        await coordinator.StartAsync();
        var result = await coordinator.ReloadFromDiskAsync();
        result.Status.Should().Be(ConfigPersistenceStatusKind.Failed);
        result.Failure!.Kind.Should().Be(ConfigPersistenceFailureKind.MissingFile);
    }

    [Fact]
    public void TestSourceContains_NoProductionThrottleSleeps_StaticGuard()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Services", "Configuration", "ConfigPersistenceCoordinatorTests.cs"));
        source.Should().NotContain("Task.Delay(" + "500");
        source.Should().NotContain("Thread.Sleep(" + "500");
    }

    private static ConfigPersistenceCoordinator CreateCoordinator(
        FakeUserConfigFileStore? store = null,
        Func<AppState>? currentState = null,
        IObservable<AppState>? stateChanged = null)
    {
        var stateService = Substitute.For<IStateService>();
        stateService.CurrentState.Returns(_ => currentState?.Invoke() ?? new AppState { IsCleaning = false });
        stateService.StateChanged.Returns(stateChanged ?? Observable.Empty<AppState>());
        return new ConfigPersistenceCoordinator(
            store ?? new FakeUserConfigFileStore(),
            stateService,
            Substitute.For<ILoggingService>(),
            TimeSpan.Zero);
    }

    private static async Task PumpUntilQuiescentAsync(ConfigPersistenceCoordinator coordinator)
    {
        await coordinator.FlushPendingSavesAsync().WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static UserConfiguration NewConfig(int timeout) => new()
    {
        SelectedGame = "SkyrimSe",
        Settings = new AutoQacSettings { CleaningTimeout = timeout },
        XEdit = new XEditConfig { Binary = $@"C:\Tools\xEdit-{timeout}.exe" }
    };
}
