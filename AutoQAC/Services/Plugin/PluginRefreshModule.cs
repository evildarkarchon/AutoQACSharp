using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameCapability;
using AutoQAC.Services.MO2;
using AutoQAC.Services.State;

namespace AutoQAC.Services.Plugin;

/// <summary>
/// Deep Plugin refresh module that accepts refresh/selection/cancellation intents and emits whole visible snapshots.
/// </summary>
public sealed class PluginRefreshModule : IPluginRefreshModule, IDisposable
{
    private static readonly PluginRefreshActivity IdleActivity = new(false, false);
    private static readonly PluginRefreshCommandAvailability EmptyCommands = new(false, false, false, false);

    private readonly IPluginLoadingService _pluginLoadingService;
    private readonly IPluginIssueApproximationService _pluginIssueApproximationService;
    private readonly IStateService _stateService;
    private readonly IGameCapabilityProvider _gameCapabilityProvider;
    private readonly IConfigurationService _configurationService;
    private readonly ISkipListPolicy _skipListPolicy;
    private readonly IMo2InstanceService _mo2InstanceService;
    private readonly ILoggingService? _logger;
    private readonly BehaviorSubject<PluginRefreshSnapshot> _snapshots;
    private readonly IDisposable _stateSubscription;
    private readonly Lock _snapshotLock = new();

    private PluginRefreshSnapshot _currentSnapshot;
    private CancellationTokenSource? _activeRefreshCts;
    private long _activeGeneration;
    private bool _lastIsCleaning;
    private bool _disposed;

    /// <summary>
    /// Initializes a Plugin refresh module with the adapters needed for context assembly, publication, and AppState compatibility.
    /// </summary>
    public PluginRefreshModule(
        IPluginLoadingService pluginLoadingService,
        IPluginIssueApproximationService pluginIssueApproximationService,
        IStateService stateService,
        IGameCapabilityProvider gameCapabilityProvider,
        IConfigurationService configurationService,
        ISkipListPolicy skipListPolicy,
        IMo2InstanceService mo2InstanceService,
        ILoggingService? logger = null)
    {
        _pluginLoadingService = pluginLoadingService;
        _pluginIssueApproximationService = pluginIssueApproximationService;
        _stateService = stateService;
        _gameCapabilityProvider = gameCapabilityProvider;
        _configurationService = configurationService;
        _skipListPolicy = skipListPolicy;
        _mo2InstanceService = mo2InstanceService;
        _logger = logger;

        _lastIsCleaning = _stateService.CurrentState.IsCleaning;
        _currentSnapshot = CreateInitialSnapshot(_stateService.CurrentState);
        _snapshots = new BehaviorSubject<PluginRefreshSnapshot>(_currentSnapshot);
        _stateSubscription = _stateService.StateChanged.Subscribe(new StateChangedObserver(OnAppStateChanged));
    }

    /// <inheritdoc />
    public IObservable<PluginRefreshSnapshot> Snapshots => _snapshots;

    /// <inheritdoc />
    public Task<PluginRefreshSnapshot> ExecuteAsync(
        PluginRefreshIntent intent,
        CancellationToken cancellationToken = default) =>
        intent switch
        {
            PluginRefreshIntent.RefreshGame refresh => RefreshGameAsync(
                refresh.GameType,
                refresh.SelectedLoadOrderPath,
                cancellationToken),
            PluginRefreshIntent.RefreshSelectedIssueApproximations => RefreshSelectedIssueApproximationsAsync(
                cancellationToken),
            PluginRefreshIntent.ChangeSelection selection => Task.FromResult(ApplySelectionChange(selection.Change)),
            PluginRefreshIntent.Cancel cancel => Task.FromResult(CancelActiveRefresh(cancel.Reason)),
            _ => throw new ArgumentOutOfRangeException(nameof(intent), intent, "Unknown Plugin refresh intent.")
        };

    /// <summary>
    /// Releases observable and active cancellation resources owned by this singleton module.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        _stateSubscription.Dispose();
        var cts = Interlocked.Exchange(ref _activeRefreshCts, null);
        cts?.Cancel();
        cts?.Dispose();
        _snapshots.Dispose();
    }

    private async Task<PluginRefreshSnapshot> RefreshGameAsync(
        GameType gameType,
        string? selectedLoadOrderPath,
        CancellationToken cancellationToken)
    {
        using var linkedCts = CreateAndActivateGeneration(cancellationToken, out var generation);
        var token = linkedCts.Token;
        var configuration = GetCurrentSnapshot().Configuration;

        PublishSnapshot(new PluginRefreshSnapshot(
            generation,
            gameType,
            gameType == GetCurrentSnapshot().GameType ? GetCurrentSnapshot().Rows : [],
            configuration,
            new PluginRefreshActivity(IsPluginRefreshRunning: true, IsIssueApproximationRefreshRunning: false),
            EmptyCommands,
            gameType == GameType.Unknown ? "No game selected" : $"Loading plugins for {gameType}..."));

        try
        {
            if (gameType == GameType.Unknown)
            {
                PublishNoGameSelected(generation);
                return GetCurrentSnapshot();
            }

            var userConfig = await _configurationService.LoadUserConfigAsync(token).ConfigureAwait(false);
            var contextResult = await CreateContextAsync(gameType, selectedLoadOrderPath, userConfig, token)
                .ConfigureAwait(false);
            configuration = contextResult.Configuration;
            if (!IsVisible(generation, token)) return GetCurrentSnapshot();

            PublishRuntimeConfiguration(userConfig, configuration, contextResult.Context, gameType);
            PublishSnapshotFromState(
                generation,
                gameType,
                configuration,
                new PluginRefreshActivity(IsPluginRefreshRunning: true, IsIssueApproximationRefreshRunning: false),
                $"Loading plugins for {gameType}...");

            if (contextResult.Context is null)
            {
                _stateService.SetPluginsToClean([]);
                PublishSnapshotFromState(
                    generation,
                    gameType,
                    configuration,
                    IdleActivity,
                    contextResult.StatusText ?? GetNoPluginsFoundMessage(gameType));
                return GetCurrentSnapshot();
            }

            var context = contextResult.Context;
            var loadedPlugins = await LoadPluginsAsync(context, token).ConfigureAwait(false);
            if (!IsVisible(generation, token)) return GetCurrentSnapshot();

            if (loadedPlugins.Count == 0)
            {
                _stateService.SetPluginsToClean([]);
                PublishSnapshotFromState(
                    generation,
                    gameType,
                    configuration,
                    IdleActivity,
                    GetNoPluginsFoundMessage(context.GameType));
                return GetCurrentSnapshot();
            }

            var skipEvaluation = await _skipListPolicy.EvaluateAsync(
                    context.GameType,
                    loadedPlugins,
                    context.DisableSkipLists,
                    token)
                .ConfigureAwait(false);
            if (!IsVisible(generation, token)) return GetCurrentSnapshot();

            var capability = _gameCapabilityProvider.Get(context.GameType);
            var initialApproximation = capability.SupportsIssueApproximation
                ? PluginIssueApproximation.Pending
                : PluginIssueApproximation.Unavailable;
            var rows = skipEvaluation.Decisions.Select(decision => decision.Plugin with
            {
                Approximation = initialApproximation
            }).ToList();

            _stateService.SetPluginsToClean(rows);
            PublishSnapshotFromState(
                generation,
                context.GameType,
                configuration,
                new PluginRefreshActivity(IsPluginRefreshRunning: true, IsIssueApproximationRefreshRunning: false),
                $"Loading plugins for {context.GameType}...");

            if (!capability.SupportsIssueApproximation)
            {
                PublishSnapshotFromState(
                    generation,
                    context.GameType,
                    configuration,
                    IdleActivity,
                    "Approximation refresh is not available for this game.");
                return GetCurrentSnapshot();
            }

            var dataFolder = ResolveDataFolder(context, rows);
            var targets = skipEvaluation.Decisions
                .Where(decision => !decision.ShouldSkipByPolicy)
                .Select(decision => new PluginRefreshRowKey(
                    decision.Plugin.FileName,
                    decision.Plugin.FullPath))
                .ToList();
            var issueActivity = new PluginRefreshActivity(
                IsPluginRefreshRunning: true,
                IsIssueApproximationRefreshRunning: true);
            PublishSnapshotFromState(
                generation,
                context.GameType,
                configuration,
                issueActivity,
                targets.Count == 0 ? "Refreshed 0 plugin approximations." : $"Analyzing 0 of {targets.Count} selected plugins.");

            try
            {
                var updatedCount = 0;
                await AnalyzeTargetsAsync(
                        context,
                        dataFolder,
                        targets,
                        result => PublishApproximationResult(
                            generation,
                            token,
                            context.GameType,
                            configuration,
                            issueActivity,
                            targets,
                            result,
                            ref updatedCount),
                        token)
                    .ConfigureAwait(false);

                if (IsVisible(generation, token))
                {
                    PublishSnapshotFromState(
                        generation,
                        context.GameType,
                        configuration,
                        IdleActivity,
                        $"Refreshed {Volatile.Read(ref updatedCount)} plugin approximations.");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.Error(ex, "Failed to refresh plugin issue approximations");
                PublishApproximationFailure(
                    generation,
                    token,
                    context.GameType,
                    configuration,
                    targets,
                    "Approximation refresh failed.");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Superseded and lifecycle cancellations are ordinary Plugin refresh control flow.
        }
        finally
        {
            ReleaseGeneration(linkedCts);
        }

        return GetCurrentSnapshot();
    }

    private async Task<PluginRefreshSnapshot> RefreshSelectedIssueApproximationsAsync(CancellationToken cancellationToken)
    {
        var acceptedSnapshot = GetCurrentSnapshot();
        var selectedTargets = acceptedSnapshot.Rows
            .Where(row => row.IsSelected)
            .Select(row => row.Key)
            .ToList();

        if (selectedTargets.Count == 0)
        {
            return PublishSnapshot(acceptedSnapshot with
            {
                Activity = IdleActivity,
                StatusText = "Select plugins to refresh."
            });
        }

        using var linkedCts = CreateAndActivateGeneration(cancellationToken, out var generation);
        var token = linkedCts.Token;

        try
        {
            var contextResult = await GetContextForSelectedRefreshAsync(token).ConfigureAwait(false);
            if (contextResult.Context is null)
            {
                PublishSnapshot(acceptedSnapshot with
                {
                    Generation = generation,
                    Activity = IdleActivity,
                    Configuration = contextResult.Configuration,
                    StatusText = contextResult.StatusText ?? "Approximation refresh is not available for this game."
                });
                return GetCurrentSnapshot();
            }

            var context = contextResult.Context;
            var configuration = contextResult.Configuration;
            if (!_gameCapabilityProvider.Get(context.GameType).SupportsIssueApproximation)
            {
                PublishSnapshotFromState(
                    generation,
                    context.GameType,
                    configuration,
                    IdleActivity,
                    "Approximation refresh is not available for this game.");
                return GetCurrentSnapshot();
            }

            MarkTargetsPending(context.GameType, selectedTargets);
            var issueActivity = new PluginRefreshActivity(
                IsPluginRefreshRunning: false,
                IsIssueApproximationRefreshRunning: true);
            PublishSnapshotFromState(
                generation,
                context.GameType,
                configuration,
                issueActivity,
                $"Analyzing 0 of {selectedTargets.Count} selected plugins.");

            var dataFolder = ResolveDataFolder(context, CreateRowsFromTargets(context.GameType, selectedTargets));
            var updatedCount = 0;
            await AnalyzeTargetsAsync(
                    context,
                    dataFolder,
                    selectedTargets,
                    result => PublishApproximationResult(
                        generation,
                        token,
                        context.GameType,
                        configuration,
                        issueActivity,
                        selectedTargets,
                        result,
                        ref updatedCount),
                    token)
                .ConfigureAwait(false);

            if (IsVisible(generation, token))
            {
                PublishSnapshotFromState(
                    generation,
                    context.GameType,
                    configuration,
                    IdleActivity,
                    $"Updated {Volatile.Read(ref updatedCount)} selected plugin approximations.");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Manual cancellation publishes the visible canceled snapshot in CancelActiveRefresh.
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to refresh selected plugin issue approximations");
            var snapshot = GetCurrentSnapshot();
            PublishApproximationFailure(
                generation,
                token,
                snapshot.GameType,
                snapshot.Configuration,
                selectedTargets,
                "Approximation refresh failed.");
        }
        finally
        {
            ReleaseGeneration(linkedCts);
        }

        return GetCurrentSnapshot();
    }

    private PluginRefreshSnapshot ApplySelectionChange(PluginSelectionChange change)
    {
        var snapshot = GetCurrentSnapshot();
        var visibleRows = snapshot.Rows.ToList();
        if (visibleRows.Count == 0)
        {
            return snapshot;
        }

        switch (change)
        {
            case PluginSelectionChange.SelectAllVisible:
            {
                var visiblePaths = visibleRows.Select(row => row.FullPath).ToList();
                _stateService.UpdateExcludedPlugins(current =>
                {
                    if (current.Count == 0)
                    {
                        return current;
                    }

                    var next = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
                    foreach (var path in visiblePaths)
                    {
                        next.Remove(path);
                    }

                    return next.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
                });
                break;
            }

            case PluginSelectionChange.DeselectAllVisible:
            {
                var visiblePaths = visibleRows.Select(row => row.FullPath).ToList();
                _stateService.UpdateExcludedPlugins(current =>
                {
                    var next = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
                    foreach (var path in visiblePaths)
                    {
                        next.Add(path);
                    }

                    return next.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
                });
                break;
            }

            case PluginSelectionChange.SetOne setOne:
            {
                var row = visibleRows.FirstOrDefault(visible => IsMatch(visible, setOne.Row));
                if (row is null)
                {
                    return snapshot;
                }

                _stateService.UpdateExcludedPlugins(current =>
                {
                    var alreadyExcluded = current.Contains(row.FullPath);
                    if (setOne.IsSelected ? !alreadyExcluded : alreadyExcluded)
                    {
                        return current;
                    }

                    var next = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
                    if (setOne.IsSelected)
                    {
                        next.Remove(row.FullPath);
                    }
                    else
                    {
                        next.Add(row.FullPath);
                    }

                    return next.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
                });
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(change), change, "Unknown Plugin selection change.");
        }

        return PublishSnapshotFromState(
            snapshot.Generation,
            snapshot.GameType,
            snapshot.Configuration,
            snapshot.Activity,
            snapshot.StatusText);
    }

    private PluginRefreshSnapshot CancelActiveRefresh(PluginRefreshCancelReason reason)
    {
        var cts = Volatile.Read(ref _activeRefreshCts);
        if (cts is not null)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The active generation may have completed between read and cancel; cancellation is best-effort.
            }
        }

        var snapshot = GetCurrentSnapshot();
        if (reason == PluginRefreshCancelReason.Disposed)
        {
            return snapshot;
        }

        return PublishSnapshot(snapshot with
        {
            Activity = IdleActivity,
            StatusText = reason == PluginRefreshCancelReason.Manual
                ? "Approximation refresh canceled."
                : snapshot.StatusText
        });
    }

    private void PublishNoGameSelected(long generation)
    {
        _stateService.UpdateState(state => state with { CurrentGameType = GameType.Unknown });
        _stateService.SetPluginsToClean([]);
        PublishSnapshotFromState(
            generation,
            GameType.Unknown,
            CreateConfigurationProjectionFromState(_stateService.CurrentState),
            IdleActivity,
            "No game selected");
    }

    private CancellationTokenSource CreateAndActivateGeneration(
        CancellationToken externalToken,
        out long generation)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        generation = Interlocked.Increment(ref _activeGeneration);
        var previous = Interlocked.Exchange(ref _activeRefreshCts, cts);
        previous?.Cancel(); // do not dispose another generation's live token source
        return cts;
    }

    private void ReleaseGeneration(CancellationTokenSource cts)
    {
        // A newer refresh may have swapped in its own CTS while this async generation was unwinding.
        // Only clear the active slot when this exact token source is still active; the using scope disposes it.
        Interlocked.CompareExchange(ref _activeRefreshCts, null, cts);
    }

    private bool IsVisible(long generation, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && generation == Volatile.Read(ref _activeGeneration);

    private async Task<PluginRefreshContextResult> GetContextForSelectedRefreshAsync(CancellationToken ct)
    {
        var currentGame = _stateService.CurrentState.CurrentGameType;
        if (currentGame == GameType.Unknown)
        {
            return new PluginRefreshContextResult(
                null,
                GetCurrentSnapshot().Configuration,
                "Approximation refresh is not available for this game.");
        }

        var userConfig = await _configurationService.LoadUserConfigAsync(ct).ConfigureAwait(false);
        var contextResult = await CreateContextAsync(
                currentGame,
                _stateService.CurrentState.LoadOrderPath,
                userConfig,
                ct)
            .ConfigureAwait(false);

        // Selected refreshes rebuild context so same-game MO2/profile/path changes are never cached stale.
        return contextResult;
    }

    private async Task<PluginRefreshContextResult> CreateContextAsync(
        GameType gameType,
        string? selectedLoadOrderPath,
        UserConfiguration userConfig,
        CancellationToken ct)
    {
        if (userConfig.Settings.Mo2Mode && gameType != GameType.Unknown)
        {
            return await CreateMo2ContextAsync(gameType, userConfig, ct).ConfigureAwait(false);
        }

        return await CreateDirectContextAsync(gameType, selectedLoadOrderPath, userConfig, ct)
            .ConfigureAwait(false);
    }

    private async Task<PluginRefreshContextResult> CreateDirectContextAsync(
        GameType gameType,
        string? selectedLoadOrderPath,
        UserConfiguration userConfig,
        CancellationToken ct)
    {
        var customDataFolder = gameType == GameType.Unknown
            ? null
            : await _configurationService.GetGameDataFolderOverrideAsync(gameType, ct).ConfigureAwait(false);
        var dataFolder = gameType == GameType.Unknown
            ? null
            : _pluginLoadingService.GetGameDataFolder(gameType, customDataFolder);
        var hasDataFolderOverride = !string.IsNullOrWhiteSpace(customDataFolder);
        var loadOrderPath = string.IsNullOrWhiteSpace(selectedLoadOrderPath)
            ? null
            : selectedLoadOrderPath;

        var capability = _gameCapabilityProvider.Get(gameType);
        if (string.IsNullOrWhiteSpace(loadOrderPath) && capability.RequiresLoadOrderFile)
        {
            loadOrderPath = await _configurationService.GetGameLoadOrderOverrideAsync(gameType, ct)
                                .ConfigureAwait(false)
                            ?? _pluginLoadingService.GetDefaultLoadOrderPath(gameType);
        }

        var configuration = new PluginRefreshConfigurationProjection(
            LoadOrderPath: loadOrderPath,
            GameDataFolder: dataFolder,
            HasGameDataFolderOverride: hasDataFolderOverride,
            XEditPath: userConfig.XEdit.Binary,
            Mo2Path: userConfig.ModOrganizer.Binary,
            Mo2ModeEnabled: userConfig.Settings.Mo2Mode,
            Mo2InstancePath: null,
            IsMo2InstanceOverride: false,
            IsMo2InstanceValid: null,
            AvailableProfiles: [],
            SelectedProfile: null,
            CleaningTimeout: userConfig.Settings.CleaningTimeout);

        if (capability.RequiresLoadOrderFile && string.IsNullOrWhiteSpace(loadOrderPath))
        {
            return new PluginRefreshContextResult(
                null,
                configuration,
                $"No load order file found for {gameType}. Browse to plugins.txt or loadorder.txt.");
        }

        var context = new PluginRefreshContext(
            gameType,
            dataFolder,
            loadOrderPath,
            userConfig.Settings.DisableSkipLists,
            Mo2Mode: false,
            Mo2LoadOrderPath: null,
            Mo2PathMap: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Mo2BaseDataFolder: null);
        return new PluginRefreshContextResult(context, configuration, null);
    }

    private async Task<PluginRefreshContextResult> CreateMo2ContextAsync(
        GameType gameType,
        UserConfiguration userConfig,
        CancellationToken ct)
    {
        var customDataFolder = await _configurationService.GetGameDataFolderOverrideAsync(gameType, ct)
            .ConfigureAwait(false);
        var dataFolder = _pluginLoadingService.GetGameDataFolder(gameType, customDataFolder);
        var hasDataFolderOverride = !string.IsNullOrWhiteSpace(customDataFolder);
        var instanceOverride = await _configurationService.GetMo2InstanceOverrideAsync(gameType, ct)
            .ConfigureAwait(false);
        var isInstanceOverride = !string.IsNullOrWhiteSpace(instanceOverride);

        var instance = await _mo2InstanceService.ResolveInstanceAsync(
                gameType,
                userConfig.ModOrganizer.Binary,
                instanceOverride,
                ct)
            .ConfigureAwait(false);
        if (instance is null)
        {
            var configuration = new PluginRefreshConfigurationProjection(
                LoadOrderPath: null,
                GameDataFolder: dataFolder,
                HasGameDataFolderOverride: hasDataFolderOverride,
                XEditPath: userConfig.XEdit.Binary,
                Mo2Path: userConfig.ModOrganizer.Binary,
                Mo2ModeEnabled: userConfig.Settings.Mo2Mode,
                Mo2InstancePath: instanceOverride,
                IsMo2InstanceOverride: isInstanceOverride,
                IsMo2InstanceValid: string.IsNullOrWhiteSpace(instanceOverride) ? null : Directory.Exists(instanceOverride),
                AvailableProfiles: [],
                SelectedProfile: null,
                CleaningTimeout: userConfig.Settings.CleaningTimeout);
            return new PluginRefreshContextResult(
                null,
                configuration,
                $"No MO2 instance found for {gameType}. Browse to the instance folder.");
        }

        var profiles = await Task.Run(() => _mo2InstanceService.GetProfiles(instance), ct).ConfigureAwait(false);
        var persistedProfile = await _configurationService.GetMo2ProfileAsync(gameType, ct).ConfigureAwait(false);
        var profile = _mo2InstanceService.ChooseProfile(instance, profiles, persistedProfile);
        var baseConfiguration = new PluginRefreshConfigurationProjection(
            LoadOrderPath: null,
            GameDataFolder: dataFolder,
            HasGameDataFolderOverride: hasDataFolderOverride,
            XEditPath: userConfig.XEdit.Binary,
            Mo2Path: userConfig.ModOrganizer.Binary,
            Mo2ModeEnabled: userConfig.Settings.Mo2Mode,
            Mo2InstancePath: instance.BaseDirectory,
            IsMo2InstanceOverride: isInstanceOverride,
            IsMo2InstanceValid: Directory.Exists(instance.BaseDirectory),
            AvailableProfiles: profiles,
            SelectedProfile: profile,
            CleaningTimeout: userConfig.Settings.CleaningTimeout);

        if (string.IsNullOrWhiteSpace(profile))
        {
            return new PluginRefreshContextResult(
                null,
                baseConfiguration,
                $"No MO2 profiles with loadorder.txt were found for {gameType}.");
        }

        var mo2LoadOrderPath = _mo2InstanceService.GetLoadOrderPath(instance, profile);
        if (string.IsNullOrWhiteSpace(mo2LoadOrderPath) || !File.Exists(mo2LoadOrderPath))
        {
            return new PluginRefreshContextResult(
                null,
                baseConfiguration,
                $"MO2 profile '{profile}' does not contain a loadorder.txt.");
        }

        var pathMap = await Task.Run(
                () => _mo2InstanceService.BuildPluginPathMap(instance, profile, dataFolder),
                ct)
            .ConfigureAwait(false);
        var context = new PluginRefreshContext(
            gameType,
            dataFolder,
            LoadOrderPath: null,
            userConfig.Settings.DisableSkipLists,
            Mo2Mode: true,
            Mo2LoadOrderPath: mo2LoadOrderPath,
            Mo2PathMap: pathMap,
            Mo2BaseDataFolder: dataFolder);
        return new PluginRefreshContextResult(context, baseConfiguration, null);
    }

    private void PublishRuntimeConfiguration(
        UserConfiguration userConfig,
        PluginRefreshConfigurationProjection configuration,
        PluginRefreshContext? context,
        GameType gameType)
    {
        _stateService.UpdateConfigurationPaths(
            context is { Mo2Mode: false } ? configuration.LoadOrderPath : null,
            userConfig.ModOrganizer.Binary,
            userConfig.XEdit.Binary,
            configuration.SelectedProfile);
        _stateService.UpdateState(state => state with
        {
            CurrentGameType = gameType,
            Mo2ModeEnabled = userConfig.Settings.Mo2Mode,
            CleaningTimeout = userConfig.Settings.CleaningTimeout
        });
    }

    private async Task<IReadOnlyList<PluginInfo>> LoadPluginsAsync(PluginRefreshContext context, CancellationToken ct)
    {
        if (context.Mo2Mode)
        {
            if (string.IsNullOrWhiteSpace(context.Mo2LoadOrderPath))
            {
                return [];
            }

            var plugins = await _pluginLoadingService.GetPluginsFromFileAsync(context.Mo2LoadOrderPath, null, ct)
                .ConfigureAwait(false);
            if (context.Mo2PathMap.Count == 0)
            {
                return plugins;
            }

            return plugins.Select(plugin => context.Mo2PathMap.TryGetValue(plugin.FileName, out var fullPath)
                    ? plugin with { FullPath = fullPath }
                    : plugin)
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(context.LoadOrderPath))
        {
            return await _pluginLoadingService
                .GetPluginsFromFileAsync(context.LoadOrderPath, context.DataFolderPath, ct)
                .ConfigureAwait(false);
        }

        if (!_gameCapabilityProvider.Get(context.GameType).SupportsAutomaticPluginDiscovery)
        {
            return [];
        }

        var loadResult = await _pluginLoadingService.TryGetPluginsAsync(
                context.GameType,
                context.DataFolderPath,
                ct)
            .ConfigureAwait(false);
        return loadResult.Status == PluginLoadingStatus.Success
            ? loadResult.Plugins
            : [];
    }

    private async Task AnalyzeTargetsAsync(
        PluginRefreshContext context,
        string? dataFolder,
        IReadOnlyList<PluginRefreshRowKey> targets,
        Action<PluginIssueApproximationResult> onApproximationReady,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dataFolder) || targets.Count == 0)
        {
            return;
        }

        if (context.Mo2Mode)
        {
            await _pluginIssueApproximationService.GetApproximationsAsync(
                new PluginIssueApproximationRequest(
                    context.GameType,
                    new PluginIssueApproximationSource.ResolvedLoadOrder(
                        dataFolder,
                        targets.Select(target => target.FileName).ToList(),
                        context.Mo2PathMap)),
                onApproximationReady,
                ct).ConfigureAwait(false);
        }
        else
        {
            await _pluginIssueApproximationService.GetApproximationsAsync(
                new PluginIssueApproximationRequest(
                    context.GameType,
                    new PluginIssueApproximationSource.DirectDataFolder(dataFolder)),
                onApproximationReady,
                ct).ConfigureAwait(false);
        }
    }

    private void PublishApproximationResult(
        long generation,
        CancellationToken token,
        GameType gameType,
        PluginRefreshConfigurationProjection configuration,
        PluginRefreshActivity activity,
        IReadOnlyList<PluginRefreshRowKey> targets,
        PluginIssueApproximationResult result,
        ref int updatedCount)
    {
        if (!IsVisible(generation, token) || !targets.Any(target => IsMatch(target, result)))
        {
            return;
        }

        var matchedRow = false;
        _stateService.UpdateState(state =>
        {
            if (!IsVisible(generation, token) || state.PluginsToClean.Count == 0)
            {
                return state;
            }

            var rows = state.PluginsToClean.Select(plugin =>
            {
                if (!IsMatch(plugin, result))
                {
                    return plugin;
                }

                matchedRow = true;
                return plugin with { Approximation = result.Approximation };
            }).ToList();

            return state with { PluginsToClean = rows.AsReadOnly() };
        });

        if (!matchedRow || !IsVisible(generation, token))
        {
            return;
        }

        var updated = Interlocked.Increment(ref updatedCount);
        PublishSnapshotFromState(
            generation,
            gameType,
            configuration,
            activity,
            $"Analyzing {updated} of {targets.Count} selected plugins.");
    }

    private void PublishApproximationFailure(
        long generation,
        CancellationToken token,
        GameType gameType,
        PluginRefreshConfigurationProjection configuration,
        IReadOnlyList<PluginRefreshRowKey> targets,
        string message)
    {
        if (!IsVisible(generation, token))
        {
            return;
        }

        MarkTargetsUnavailable(targets, generation, token);
        PublishSnapshotFromState(
            generation,
            gameType,
            configuration,
            IdleActivity,
            message);
    }

    private void MarkTargetsPending(GameType gameType, IReadOnlyList<PluginRefreshRowKey> targets)
    {
        _stateService.UpdateState(state =>
        {
            if (state.PluginsToClean.Count == 0)
            {
                var pendingRows = CreateRowsFromTargets(gameType, targets).ToList();
                return state with { PluginsToClean = pendingRows.AsReadOnly() };
            }

            var rows = state.PluginsToClean.Select(plugin =>
                targets.Any(target => IsMatch(plugin, target))
                    ? plugin with { Approximation = PluginIssueApproximation.Pending }
                    : plugin).ToList();

            return state with { PluginsToClean = rows.AsReadOnly() };
        });
    }

    private void MarkTargetsUnavailable(
        IReadOnlyList<PluginRefreshRowKey> targets,
        long generation,
        CancellationToken token)
    {
        _stateService.UpdateState(state =>
        {
            if (!IsVisible(generation, token) || state.PluginsToClean.Count == 0)
            {
                return state;
            }

            var rows = state.PluginsToClean.Select(plugin =>
                targets.Any(target => IsMatch(plugin, target))
                    ? plugin with { Approximation = PluginIssueApproximation.Unavailable }
                    : plugin).ToList();

            return state with { PluginsToClean = rows.AsReadOnly() };
        });
    }

    private PluginRefreshSnapshot PublishSnapshotFromState(
        long generation,
        GameType gameType,
        PluginRefreshConfigurationProjection configuration,
        PluginRefreshActivity activity,
        string statusText)
    {
        var state = _stateService.CurrentState;
        var rows = CreateVisibleRows(state.PluginsToClean, state.ExcludedPluginPaths);
        return PublishSnapshot(
            new PluginRefreshSnapshot(
                generation,
                gameType,
                rows,
                configuration,
                activity,
                EmptyCommands,
                statusText),
            state);
    }

    private PluginRefreshSnapshot PublishSnapshot(PluginRefreshSnapshot snapshot, AppState? state = null)
    {
        if (_disposed)
        {
            return GetCurrentSnapshot();
        }

        var next = WithCommandAvailability(snapshot, state ?? _stateService.CurrentState);
        lock (_snapshotLock)
        {
            _currentSnapshot = next;
        }

        _snapshots.OnNext(next);
        return next;
    }

    private PluginRefreshSnapshot GetCurrentSnapshot()
    {
        lock (_snapshotLock)
        {
            return _currentSnapshot;
        }
    }

    private PluginRefreshSnapshot WithCommandAvailability(PluginRefreshSnapshot snapshot, AppState state) =>
        snapshot with { Commands = CreateCommandAvailability(snapshot.GameType, snapshot.Rows, snapshot.Activity, state) };

    private PluginRefreshCommandAvailability CreateCommandAvailability(
        GameType gameType,
        IReadOnlyList<PluginRefreshRow> rows,
        PluginRefreshActivity activity,
        AppState state)
    {
        var hasRows = rows.Count > 0;
        var isRunning = activity.IsPluginRefreshRunning || activity.IsIssueApproximationRefreshRunning;
        var canUseRows = hasRows && !state.IsCleaning;
        var canRefreshApproximations = canUseRows &&
                                      !isRunning &&
                                      rows.Any(row => row.IsSelected) &&
                                      gameType != GameType.Unknown &&
                                      _gameCapabilityProvider.Get(gameType).SupportsIssueApproximation;

        return new PluginRefreshCommandAvailability(
            CanSelectAll: canUseRows,
            CanDeselectAll: canUseRows,
            CanRefreshSelectedIssueApproximations: canRefreshApproximations,
            CanCancelRefresh: isRunning);
    }

    private PluginRefreshSnapshot CreateInitialSnapshot(AppState state)
    {
        var rows = CreateVisibleRows(state.PluginsToClean, state.ExcludedPluginPaths);
        var configuration = CreateConfigurationProjectionFromState(state);
        return new PluginRefreshSnapshot(
            Generation: 0,
            GameType: state.CurrentGameType,
            Rows: rows,
            Configuration: configuration,
            Activity: IdleActivity,
            Commands: CreateCommandAvailability(state.CurrentGameType, rows, IdleActivity, state),
            StatusText: "Ready");
    }

    private static PluginRefreshConfigurationProjection CreateConfigurationProjectionFromState(AppState state) =>
        new(
            LoadOrderPath: state.LoadOrderPath,
            GameDataFolder: null,
            HasGameDataFolderOverride: false,
            XEditPath: state.XEditExecutablePath,
            Mo2Path: state.Mo2ExecutablePath,
            Mo2ModeEnabled: state.Mo2ModeEnabled,
            Mo2InstancePath: null,
            IsMo2InstanceOverride: false,
            IsMo2InstanceValid: null,
            AvailableProfiles: [],
            SelectedProfile: state.Mo2Profile,
            CleaningTimeout: state.CleaningTimeout);

    private static IReadOnlyList<PluginRefreshRow> CreateVisibleRows(
        IReadOnlyList<PluginInfo> plugins,
        IReadOnlySet<string> excludedPaths) =>
        plugins.Where(plugin => !plugin.IsInSkipList)
            .Select(plugin => new PluginRefreshRow(
                plugin.FileName,
                plugin.FullPath,
                plugin.DetectedGameType,
                IsSelected: !excludedPaths.Contains(plugin.FullPath),
                plugin.IsInSkipList,
                plugin.Approximation))
            .ToList();

    private void OnAppStateChanged(AppState state)
    {
        if (_disposed || state.IsCleaning == _lastIsCleaning)
        {
            return;
        }

        _lastIsCleaning = state.IsCleaning;
        var snapshot = GetCurrentSnapshot();
        var next = WithCommandAvailability(snapshot, state);
        if (next.Commands == snapshot.Commands)
        {
            return;
        }

        PublishSnapshot(next, state);
    }

    private static string? ResolveDataFolder(PluginRefreshContext context, IReadOnlyList<PluginInfo> rows)
    {
        if (context.Mo2Mode)
        {
            return context.Mo2BaseDataFolder;
        }

        if (!string.IsNullOrWhiteSpace(context.DataFolderPath))
        {
            return context.DataFolderPath;
        }

        var firstPath = rows.FirstOrDefault()?.FullPath;
        return string.IsNullOrWhiteSpace(firstPath) ? null : Path.GetDirectoryName(firstPath);
    }

    private static IReadOnlyList<PluginInfo> CreateRowsFromTargets(
        GameType gameType,
        IReadOnlyList<PluginRefreshRowKey> targets) =>
        targets.Select(target => new PluginInfo
        {
            FileName = target.FileName,
            FullPath = target.FullPath,
            DetectedGameType = gameType,
            Approximation = PluginIssueApproximation.Pending
        }).ToList();

    private string GetNoPluginsFoundMessage(GameType gameType) =>
        _gameCapabilityProvider.Get(gameType).SupportsAutomaticPluginDiscovery
            ? $"No plugins discovered via Mutagen for {gameType}."
            : "No plugins found in the selected load order.";

    private static bool IsMatch(PluginInfo plugin, PluginRefreshRowKey target)
    {
        if (HasUsablePath(plugin.FullPath) && HasUsablePath(target.FullPath))
        {
            return string.Equals(plugin.FullPath, target.FullPath, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(plugin.FileName, target.FileName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMatch(PluginRefreshRow row, PluginRefreshRowKey target)
    {
        if (HasUsablePath(row.FullPath) && HasUsablePath(target.FullPath))
        {
            return string.Equals(row.FullPath, target.FullPath, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(row.FileName, target.FileName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMatch(PluginInfo plugin, PluginIssueApproximationResult result)
    {
        if (HasUsablePath(plugin.FullPath) && HasUsablePath(result.FullPath))
        {
            return string.Equals(plugin.FullPath, result.FullPath, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(plugin.FileName, result.FileName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMatch(PluginRefreshRowKey target, PluginIssueApproximationResult result)
    {
        if (HasUsablePath(target.FullPath) && HasUsablePath(result.FullPath))
        {
            return string.Equals(target.FullPath, result.FullPath, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(target.FileName, result.FileName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasUsablePath(string? path) => !string.IsNullOrWhiteSpace(path);

    private sealed record PluginRefreshContext(
        GameType GameType,
        string? DataFolderPath,
        string? LoadOrderPath,
        bool DisableSkipLists,
        bool Mo2Mode,
        string? Mo2LoadOrderPath,
        IReadOnlyDictionary<string, string> Mo2PathMap,
        string? Mo2BaseDataFolder);

    private sealed record PluginRefreshContextResult(
        PluginRefreshContext? Context,
        PluginRefreshConfigurationProjection Configuration,
        string? StatusText);

    private sealed class StateChangedObserver(Action<AppState> onNext) : IObserver<AppState>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(AppState value) => onNext(value);
    }
}
