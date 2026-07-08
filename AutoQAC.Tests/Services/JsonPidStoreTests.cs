using AutoQAC.Models;
using AutoQAC.Infrastructure;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Services.Process;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AutoQAC.Tests.Services;

/// <summary>
/// Tests for the injectable PID store contracts and JSON-backed implementation.
/// </summary>
public sealed class JsonPidStoreTests
{
    /// <summary>
    /// Verifies tracked PID records carry a per-application session identifier without changing existing fields.
    /// </summary>
    [Fact]
    public void TrackedProcess_ShouldExposeSessionIdAlongsideExistingFields()
    {
        var started = DateTime.UtcNow;

        var tracked = new TrackedProcess
        {
            Pid = 1234,
            StartTime = started,
            PluginName = "Example.esp",
            SessionId = "session-1"
        };

        tracked.Pid.Should().Be(1234);
        tracked.StartTime.Should().Be(started);
        tracked.PluginName.Should().Be("Example.esp");
        tracked.SessionId.Should().Be("session-1");
    }

    /// <summary>
    /// Verifies tests can substitute PID storage through public async load/update seams.
    /// </summary>
    [Fact]
    public async Task IPidStore_ShouldExposeAsyncLoadAndUpdateSeams()
    {
        IPidStore store = new InMemoryPidStore();

        await store.UpdateAsync(existing => existing.Append(new TrackedProcess { Pid = 7 }).ToList());
        var loaded = await store.LoadAsync();

        loaded.Should().ContainSingle(p => p.Pid == 7);
    }

    /// <summary>
    /// Verifies JSON updates are persisted to the path supplied by the path provider.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_ShouldSerializeEntriesToInjectedPath()
    {
        using var temp = TempPidStore.Create();
        var store = temp.CreateStore();

        await store.UpdateAsync(_ =>
        [
            new TrackedProcess { Pid = 42, PluginName = "Plugin.esp", SessionId = "session" }
        ]);

        var loaded = await store.LoadAsync();

        loaded.Should().ContainSingle(p => p.Pid == 42 && p.PluginName == "Plugin.esp" && p.SessionId == "session");
        File.ReadAllText(temp.PidFilePath).Should().Contain("Plugin.esp");
    }

    /// <summary>
    /// Verifies concurrent read-modify-write operations retain every update.
    /// </summary>
    [Fact]
    public async Task ConcurrentUpdateAsync_ShouldPreserveAllUpdates()
    {
        using var temp = TempPidStore.Create();
        var store = temp.CreateStore();

        await Task.WhenAll(Enumerable.Range(1, 20).Select(pid => store.UpdateAsync(existing =>
            existing.Append(new TrackedProcess { Pid = pid }).ToList())));

        var loaded = await store.LoadAsync();

        loaded.Select(p => p.Pid).Should().BeEquivalentTo(Enumerable.Range(1, 20));
    }

    /// <summary>
    /// Verifies corrupt PID JSON is preserved before the store is reset to an empty list.
    /// </summary>
    [Fact]
    public async Task LoadAsync_WhenCorruptJson_ShouldPreserveCorruptCopyAndReturnEmptyList()
    {
        using var temp = TempPidStore.Create();
        Directory.CreateDirectory(temp.DirectoryPath);
        await File.WriteAllTextAsync(temp.PidFilePath, "{ not valid json");

        var loaded = await temp.CreateStore().LoadAsync();

        loaded.Should().BeEmpty();
        Directory.GetFiles(temp.DirectoryPath, "autoqac-pids.corrupt-*.json").Should().ContainSingle();
        File.ReadAllText(temp.PidFilePath).Should().Be("[]");
    }

    /// <summary>
    /// Verifies session ID provider instances expose a stable non-empty run identifier.
    /// </summary>
    [Fact]
    public void ProcessSessionIdProvider_SessionId_ShouldBeNonEmptyAndStable()
    {
        var provider = new ProcessSessionIdProvider();

        provider.CurrentSessionId.Should().NotBeNullOrWhiteSpace();
        provider.CurrentSessionId.Should().Be(provider.CurrentSessionId);
    }

    /// <summary>
    /// Verifies production DI keeps the PID store and current session as singleton services.
    /// </summary>
    [Fact]
    public void AddBusinessLogic_ShouldRegisterPidStoreAndSessionAsSingletons()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure();
        services.AddBusinessLogic();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IProcessSessionIdProvider>().Should()
            .BeSameAs(provider.GetRequiredService<IProcessSessionIdProvider>());
        provider.GetRequiredService<IPidStore>().Should()
            .BeSameAs(provider.GetRequiredService<IPidStore>());
    }

    private sealed class InMemoryPidStore : IPidStore
    {
        private IReadOnlyList<TrackedProcess> _entries = [];

        public Task<IReadOnlyList<TrackedProcess>> LoadAsync(CancellationToken ct = default) =>
            Task.FromResult(_entries);

        public Task UpdateAsync(
            Func<IReadOnlyList<TrackedProcess>, IReadOnlyList<TrackedProcess>> update,
            CancellationToken ct = default)
        {
            _entries = update(_entries);
            return Task.CompletedTask;
        }
    }

    private sealed class TempPidStore : IPidStorePathProvider, IDisposable
    {
        private TempPidStore(string directoryPath)
        {
            DirectoryPath = directoryPath;
            PidFilePath = Path.Combine(directoryPath, "autoqac-pids.json");
        }

        public string DirectoryPath { get; }

        public string PidFilePath { get; }

        public static TempPidStore Create() => new(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        public JsonPidStore CreateStore()
        {
            Directory.CreateDirectory(DirectoryPath);
            return new JsonPidStore(this, Substitute.For<ILoggingService>());
        }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
