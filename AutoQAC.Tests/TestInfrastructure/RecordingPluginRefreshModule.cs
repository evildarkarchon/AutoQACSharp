using System.Reactive.Subjects;
using AutoQAC.Models;
using AutoQAC.Services.GameCapability;
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
        CurrentPublication = CreatePublication(CurrentSnapshot);
        _snapshots = new BehaviorSubject<PluginRefreshSnapshot>(CurrentSnapshot);
    }

    public List<PluginRefreshIntent> Intents { get; } = [];

    public PluginRefreshSnapshot CurrentSnapshot { get; private set; }

    public PluginRefreshPublication CurrentPublication { get; set; }

    public Func<PluginRefreshIntent, CancellationToken, Task<PluginRefreshSnapshot>>? ExecuteHandler { get; set; }

    public Func<CancellationToken, Task<PluginRefreshPublication>>? PublicationHandler { get; set; }

    public IObservable<PluginRefreshSnapshot> Snapshots => _snapshots;

    public Task<PluginRefreshSnapshot> ExecuteAsync(
        PluginRefreshIntent intent,
        CancellationToken cancellationToken = default)
    {
        Intents.Add(intent);
        return ExecuteHandler?.Invoke(intent, cancellationToken) ?? Task.FromResult(CurrentSnapshot);
    }

    public Task<PluginRefreshPublication> GetCurrentPublicationAsync(CancellationToken cancellationToken = default) =>
        PublicationHandler?.Invoke(cancellationToken) ?? Task.FromResult(CurrentPublication);

    public void Publish(PluginRefreshSnapshot snapshot)
    {
        CurrentSnapshot = snapshot;
        CurrentPublication = CreatePublication(snapshot);
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

    public static PluginRefreshPublication CreatePublication(
        PluginRefreshSnapshot? snapshot = null,
        IReadOnlyList<PluginRefreshPublishedRow>? rows = null,
        PluginRefreshFreshness? freshness = null,
        AutoQAC.Services.GameCapability.PluginRefreshDiscoveryPlan? discoveryPlan = null)
    {
        var currentSnapshot = snapshot ?? CreateSnapshot();
        return new PluginRefreshPublication(
            currentSnapshot.Generation,
            currentSnapshot.GameType,
            discoveryPlan,
            currentSnapshot.Configuration,
            freshness ?? PluginRefreshFreshness.Missing,
            rows ?? [],
            currentSnapshot.Rows,
            currentSnapshot.Activity,
            currentSnapshot.Commands,
            currentSnapshot.StatusText);
    }

    public static PluginRefreshPublication CreateFreshPublication(
        GameType gameType = GameType.SkyrimSe,
        IReadOnlyList<PluginRefreshPublishedRow>? rows = null,
        PluginRefreshDiscoveryPlan? discoveryPlan = null,
        PluginRefreshConfigurationProjection? configuration = null,
        string statusText = "Ready")
    {
        configuration ??= CreateConfiguration();
        discoveryPlan ??= CreateDiscoveryPlan(gameType, configuration: configuration);
        rows ??=
        [
            CreatePublishedRow(new PluginInfo
            {
                FileName = "NeedsCleaning.esp",
                FullPath = @"C:\Game\Data\NeedsCleaning.esp",
                DetectedGameType = gameType
            })
        ];
        var visibleRows = rows
            .Where(row => row.IsVisible)
            .Select(row => new PluginRefreshRow(
                row.Plugin.FileName,
                row.Plugin.FullPath,
                row.Plugin.DetectedGameType,
                row.IsSelected,
                row.IsSkippedByPolicy,
                row.Plugin.Approximation))
            .ToList();
        var snapshot = CreateSnapshot(gameType, visibleRows, configuration, statusText: statusText);
        return CreatePublication(snapshot, rows, PluginRefreshFreshness.Fresh, discoveryPlan);
    }

    public static PluginRefreshConfigurationProjection CreateConfiguration(
        string? loadOrderPath = null,
        string? xEditPath = null,
        string? mo2Path = null,
        bool mo2ModeEnabled = false,
        string? mo2InstancePath = null,
        string? selectedProfile = null) =>
        new(
            LoadOrderPath: loadOrderPath,
            GameDataFolder: null,
            HasGameDataFolderOverride: false,
            XEditPath: xEditPath,
            Mo2Path: mo2Path,
            Mo2ModeEnabled: mo2ModeEnabled,
            Mo2InstancePath: mo2InstancePath,
            IsMo2InstanceOverride: false,
            IsMo2InstanceValid: string.IsNullOrWhiteSpace(mo2InstancePath) ? null : true,
            AvailableProfiles: string.IsNullOrWhiteSpace(selectedProfile) ? [] : [selectedProfile],
            SelectedProfile: selectedProfile,
            CleaningTimeout: 300);

    public static PluginRefreshDiscoveryPlan CreateDiscoveryPlan(
        GameType gameType = GameType.SkyrimSe,
        PluginRefreshDiscoveryMode mode = PluginRefreshDiscoveryMode.DirectAutomatic,
        PluginRefreshConfigurationProjection? configuration = null,
        string? loadOrderPath = null,
        string? mo2LoadOrderPath = null) =>
        new(
            gameType,
            mode,
            configuration ?? CreateConfiguration(loadOrderPath: loadOrderPath),
            DisableSkipLists: false,
            CanAttemptIssueApproximation: true,
            DataFolderPath: null,
            LoadOrderPath: loadOrderPath,
            Mo2LoadOrderPath: mo2LoadOrderPath,
            Mo2PathMap: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Mo2BaseDataFolder: null);

    public static PluginRefreshPublishedRow CreatePublishedRow(
        PluginInfo plugin,
        bool isVisible = true,
        bool isSelected = true,
        bool isSkippedByPolicy = false) =>
        new(
            plugin,
            isVisible,
            isSelected,
            isSkippedByPolicy,
            new PluginRefreshRowKey(plugin.FileName, plugin.FullPath));

    public void Dispose() => _snapshots.Dispose();
}
