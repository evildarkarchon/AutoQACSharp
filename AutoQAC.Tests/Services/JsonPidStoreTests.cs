using AutoQAC.Models;
using AutoQAC.Services.Process;
using FluentAssertions;

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
}
