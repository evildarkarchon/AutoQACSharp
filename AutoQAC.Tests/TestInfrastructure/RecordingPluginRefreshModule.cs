using System.Reactive.Subjects;
using AutoQAC.Models;
using AutoQAC.Services.Plugin;

namespace AutoQAC.Tests.TestInfrastructure;

/// <summary>
/// Test double for <see cref="IPluginRefreshModule"/> that records accepted intents and exposes a pushable snapshot stream.
/// </summary>
public sealed class RecordingPluginRefreshModule : IPluginRefreshModule, IDisposable
{
    private readonly BehaviorSubject<PluginRefreshSnapshot> _snapshots;

    public RecordingPluginRefreshModule(PluginRefreshSnapshot? initialSnapshot = null)
    {
        CurrentSnapshot = initialSnapshot ?? CreateSnapshot();
        _snapshots = new BehaviorSubject<PluginRefreshSnapshot>(CurrentSnapshot);
    }

    public List<PluginRefreshIntent> Intents { get; } = [];

    public PluginRefreshSnapshot CurrentSnapshot { get; private set; }

    public Func<PluginRefreshIntent, CancellationToken, Task<PluginRefreshSnapshot>>? ExecuteHandler { get; set; }

    public IObservable<PluginRefreshSnapshot> Snapshots => _snapshots;

    public Task<PluginRefreshSnapshot> ExecuteAsync(
        PluginRefreshIntent intent,
        CancellationToken cancellationToken = default)
    {
        Intents.Add(intent);
        return ExecuteHandler?.Invoke(intent, cancellationToken) ?? Task.FromResult(CurrentSnapshot);
    }

    public void Publish(PluginRefreshSnapshot snapshot)
    {
        CurrentSnapshot = snapshot;
        _snapshots.OnNext(snapshot);
    }

    public static PluginRefreshSnapshot CreateSnapshot(
        GameType gameType = GameType.Unknown,
        IReadOnlyList<PluginRefreshRow>? rows = null,
        PluginRefreshConfigurationProjection? configuration = null,
        PluginRefreshActivity? activity = null,
        PluginRefreshCommandAvailability? commands = null,
        string statusText = "Ready") =>
        new(
            Generation: 1,
            GameType: gameType,
            Rows: rows ?? [],
            Configuration: configuration ?? new PluginRefreshConfigurationProjection(
                LoadOrderPath: null,
                GameDataFolder: null,
                HasGameDataFolderOverride: false,
                XEditPath: null,
                Mo2Path: null,
                Mo2ModeEnabled: false,
                Mo2InstancePath: null,
                IsMo2InstanceOverride: false,
                IsMo2InstanceValid: null,
                AvailableProfiles: [],
                SelectedProfile: null,
                CleaningTimeout: 300),
            Activity: activity ?? new PluginRefreshActivity(false, false),
            Commands: commands ?? new PluginRefreshCommandAvailability(false, false, false, false),
            StatusText: statusText);

    public void Dispose() => _snapshots.Dispose();
}
