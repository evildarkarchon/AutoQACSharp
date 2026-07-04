using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameCapability;
using AutoQAC.Services.GameDetection;
using AutoQAC.Services.MO2;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;

namespace AutoQAC.Services.Cleaning;

/// <summary>
/// Shared preflight/selection pipeline (D-13). Consumed by both StartAsync and
/// PreviewAsync. No cleaning state mutation, no process launch, no backup, no CTS creation (D-14).
/// </summary>
public sealed class CleaningPreflight(
    IConfigurationService configService,
    IPluginValidationService pluginValidation,
    IPluginRefreshModule pluginRefreshModule,
    IStateService stateService,
    ILoggingService logger,
    bool trustLegacyEnvironmentValidation = false,
    ICleaningService? legacyCleaningService = null)
    : ICleaningPreflight
{
    /// <summary>
    /// Compatibility constructor for older tests and callers. The active implementation still consumes
    /// Plugin refresh publication facts through an adapter instead of re-evaluating Game capability.
    /// </summary>
    public CleaningPreflight(
        IConfigurationService configService,
        IGameDetectionService gameDetection,
        IPluginValidationService pluginValidation,
        IMo2ValidationService mo2Validation,
        ICleaningService cleaningService,
        IStateService stateService,
        ILoggingService logger,
        IMo2InstanceService? mo2InstanceService = null)
        : this(
            configService,
            pluginValidation,
            new StateBackedPluginRefreshModule(stateService, gameDetection, configService),
            stateService,
            logger,
            trustLegacyEnvironmentValidation: true,
            legacyCleaningService: cleaningService)
    {
    }

    /// <inheritdoc />
    public async Task<CleaningPreflightPlan> PrepareAsync(CancellationToken ct = default)
    {
        // Flush any pending config saves before launching xEdit. Phase 10 D-26: a flush
        // failure must block cleaning; the typed result lets us decide deterministically.
        var flushResult = await configService.FlushPendingSavesAsync(ct).ConfigureAwait(false);
        if (flushResult.Status is ConfigPersistenceStatusKind.Failed
            or ConfigPersistenceStatusKind.Rejected)
        {
            // Defensive: Rejected is not currently emitted by FlushPendingSavesAsync (Plan 02
            // contract emits Success/NoOp/Failed only) but treating it as a hard block is safe
            // and avoids a silent xEdit launch if upstream contracts ever broaden.
            var safeSummary = flushResult.Failure?.SafeSummary
                              ?? "Pre-cleaning configuration save failed.";
            logger.Error(null,
                "[Preflight] Pre-cleaning configuration save failed: {Summary}",
                safeSummary);
            throw new ConfigPersistenceFailureException(
                flushResult.Failure ?? new ConfigPersistenceFailure(
                    ConfigPersistenceOperationKind.Flush,
                    ConfigPersistenceFailureKind.Unknown,
                    safeSummary,
                    LogReference: null,
                    Generation: flushResult.Generation),
                safeSummary);
        }

        var publication = await pluginRefreshModule.GetCurrentPublicationAsync(ct).ConfigureAwait(false);
        ValidatePublication(publication);

        var config = stateService.CurrentState;
        if (!trustLegacyEnvironmentValidation)
        {
            ValidateLaunchReadiness(publication, config);
        }
        else if (legacyCleaningService is not null && !await legacyCleaningService.ValidateEnvironmentAsync(ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Configuration is invalid");
        }
        else if (config.Mo2ModeEnabled)
        {
            ValidateLegacyMo2Readiness(config);
        }

        // Read settings that drive Cleaning session policy. Discovery-affecting settings were already
        // checked through publication freshness above.
        var userConfig = await configService.LoadUserConfigAsync(ct).ConfigureAwait(false);
        var rows = new List<PreflightPluginRow>();
        if (publication.Rows.Count == 0 && !trustLegacyEnvironmentValidation)
        {
            ThrowFailure(
                CleaningPreflightFailureKind.NoPluginsLoaded,
                "No plugins are available for cleaning.",
                "Refresh plugins for the selected game.");
        }

        var selectedCleanableRows = publication.Rows
            .Where(row => row.IsSelected && !row.IsSkippedByPolicy)
            .ToList();
        if (selectedCleanableRows.Count == 0 && (!trustLegacyEnvironmentValidation || publication.Rows.Count > 0))
        {
            ThrowFailure(
                CleaningPreflightFailureKind.NoPluginsSelected,
                "No plugins are selected for cleaning.",
                "Select at least one plugin to clean, or check Skip list settings.");
        }

        var isMo2Mode = publication.Configuration.Mo2ModeEnabled;
        foreach (var publishedRow in publication.Rows)
        {
            ct.ThrowIfCancellationRequested();
            var plugin = publishedRow.Plugin;

            if (!publishedRow.IsSelected)
            {
                rows.Add(
                    new PreflightPluginRow(plugin, PreflightDecision.Skip, PreflightSkipReason.NotSelected));
                continue;
            }

            if (publishedRow.IsSkippedByPolicy)
            {
                rows.Add(new PreflightPluginRow(plugin, PreflightDecision.Skip,
                    PreflightSkipReason.InSkipList));
                continue;
            }

            // File-existence validation (skipped in MO2 mode -- MO2 VFS resolves paths at runtime)
            if (!isMo2Mode)
            {
                var warning = pluginValidation.ValidatePluginFile(plugin);
                if (warning != PluginWarningKind.None)
                {
                    rows.Add(new PreflightPluginRow(plugin, PreflightDecision.Skip,
                        MapPluginWarningToReason(warning)));
                    continue;
                }
            }

            rows.Add(new PreflightPluginRow(plugin, PreflightDecision.Clean, SkipReason: null));
        }

        if (!isMo2Mode)
        {
            var pathFailures = rows
                .Where(r => IsFileValidationReason(r.SkipReason))
                .Select(r => $"{r.Plugin.FileName} ({MapReasonToPluginWarningLabel(r.SkipReason!.Value)})")
                .ToList();
            if (pathFailures.Count > 0)
            {
                var summary =
                    $"{pathFailures.Count} plugin(s) not found or unreadable: {string.Join(", ", pathFailures)}";
                logger.Warning(summary);
            }
        }
        else
        {
            logger.Debug(
                "MO2 mode active -- skipping file-existence validation (MO2 VFS resolves paths at xEdit runtime)");
        }

        // Step 7 (R-08): compute XEditDirectory once. Both runner and finalizer consume this.
        var xEditDir = Path.GetDirectoryName(config.XEditExecutablePath ?? string.Empty) ?? string.Empty;

        // Step 8: build the plan.
        return new CleaningPreflightPlan
        {
            DetectedGameType = publication.GameType,
            DetectedGameVariant = GameVariant.None,
            PluginRows = rows,
            IsMo2ModeActive = isMo2Mode,
            BackupSkippedByPolicy = isMo2Mode || !userConfig.Backup.Enabled,
            FileValidationSkippedByPolicy = isMo2Mode,
            LaunchModeLabel = isMo2Mode ? "MO2" : "Direct",
            CleaningTimeoutSeconds = config.CleaningTimeout,
            BackupEnabled = userConfig.Backup.Enabled,
            BackupMaxSessions = userConfig.Backup.MaxSessions,
            XEditDirectory = xEditDir // R-08: single source of truth
        };
    }

    private static void ValidatePublication(PluginRefreshPublication publication)
    {
        if (publication.GameType == GameType.Unknown)
        {
            ThrowFailure(
                CleaningPreflightFailureKind.NoGameSelected,
                "No game type is selected for cleaning.",
                "Select a game before cleaning.");
        }

        if (!publication.Freshness.IsFresh)
        {
            var kind = publication.Freshness.StalenessReason == PluginRefreshStalenessReason.MissingPublication
                ? CleaningPreflightFailureKind.MissingPluginRefreshPublication
                : CleaningPreflightFailureKind.StalePluginRefreshPublication;
            var message = kind == CleaningPreflightFailureKind.MissingPluginRefreshPublication
                ? "Plugins have not been refreshed for the selected game."
                : "Plugins need to be refreshed after Discovery-affecting settings changed.";
            var hint = kind == CleaningPreflightFailureKind.MissingPluginRefreshPublication
                ? "Select a game and refresh plugins before cleaning."
                : "Refresh plugins after changing game, load order, MO2, or Skip list settings.";
            ThrowFailure(kind, message, hint);
        }

        if (publication.DiscoveryPlan is null)
        {
            ThrowFailure(
                CleaningPreflightFailureKind.MissingPluginRefreshPublication,
                "Plugins have not been refreshed for the selected game.",
                "Select a game and refresh plugins before cleaning.");
        }
    }

    private static void ValidateLaunchReadiness(PluginRefreshPublication publication, AppState state)
    {
        if (string.IsNullOrWhiteSpace(state.XEditExecutablePath))
        {
            ThrowFailure(
                CleaningPreflightFailureKind.XEditNotConfigured,
                "xEdit is not configured.",
                "Choose the correct xEdit executable in Settings.");
        }

        if (!File.Exists(state.XEditExecutablePath))
        {
            ThrowFailure(
                CleaningPreflightFailureKind.XEditNotFound,
                "xEdit was not found.",
                "Choose the correct xEdit executable in Settings.");
        }

        var plan = publication.DiscoveryPlan!;
        if (plan.Mode == PluginRefreshDiscoveryMode.DirectLoadOrderFile)
        {
            if (string.IsNullOrWhiteSpace(plan.LoadOrderPath))
            {
                ThrowFailure(
                    CleaningPreflightFailureKind.LoadOrderNotConfigured,
                    "Load order is not configured.",
                    "Choose the current plugins.txt or loadorder.txt file.");
            }

            if (!File.Exists(plan.LoadOrderPath))
            {
                ThrowFailure(
                    CleaningPreflightFailureKind.LoadOrderNotFound,
                    "Load order was not found.",
                    "Choose the current plugins.txt or loadorder.txt file.");
            }
        }

        if (plan.Mode != PluginRefreshDiscoveryMode.Mo2LoadOrderFile)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(state.Mo2ExecutablePath))
        {
            ThrowFailure(
                CleaningPreflightFailureKind.Mo2NotConfigured,
                "MO2 is not configured.",
                "Choose ModOrganizer.exe or disable MO2 Mode.");
        }

        if (!File.Exists(state.Mo2ExecutablePath))
        {
            ThrowFailure(
                CleaningPreflightFailureKind.Mo2NotFound,
                "MO2 was not found.",
                "Choose ModOrganizer.exe or disable MO2 Mode.");
        }

        if (string.IsNullOrWhiteSpace(publication.Configuration.Mo2InstancePath) ||
            !Directory.Exists(publication.Configuration.Mo2InstancePath))
        {
            ThrowFailure(
                CleaningPreflightFailureKind.Mo2InstanceMissing,
                "MO2 instance is missing.",
                "Choose the MO2 instance folder or disable MO2 Mode.");
        }

        if (string.IsNullOrWhiteSpace(publication.Configuration.SelectedProfile))
        {
            ThrowFailure(
                CleaningPreflightFailureKind.Mo2ProfileMissing,
                "MO2 profile is not selected.",
                "Select a game and MO2 profile before cleaning.");
        }

        if (string.IsNullOrWhiteSpace(plan.Mo2LoadOrderPath) || !File.Exists(plan.Mo2LoadOrderPath))
        {
            ThrowFailure(
                CleaningPreflightFailureKind.Mo2ProfileLoadOrderMissing,
                "MO2 profile load order is missing.",
                "Select a profile with a valid loadorder.txt before cleaning.");
        }
    }

    private static void ValidateLegacyMo2Readiness(AppState state)
    {
        if (string.IsNullOrWhiteSpace(state.Mo2ExecutablePath))
        {
            throw new InvalidOperationException(
                "MO2 mode is enabled but no MO2 executable path is configured. Check MO2 executable path in Settings, or disable MO2 mode if not using Mod Organizer 2.");
        }

        if (!File.Exists(state.Mo2ExecutablePath))
        {
            throw new InvalidOperationException(
                "MO2 mode is enabled but MO2 executable was not found. Check MO2 executable path in Settings, or disable MO2 mode if not using Mod Organizer 2.");
        }
    }

    private static void ThrowFailure(
        CleaningPreflightFailureKind kind,
        string safeMessage,
        string? actionHint = null) =>
        throw new CleaningPreflightException(new CleaningPreflightFailure(kind, safeMessage, actionHint));

    private sealed class StateBackedPluginRefreshModule(
        IStateService stateService,
        IGameDetectionService gameDetection,
        IConfigurationService configurationService) : IPluginRefreshModule
    {
        public IObservable<PluginRefreshSnapshot> Snapshots { get; } = new EmptyObservable<PluginRefreshSnapshot>();

        public Task<PluginRefreshSnapshot> ExecuteAsync(
            PluginRefreshIntent intent,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateSnapshot(stateService.CurrentState));

        public async Task<PluginRefreshPublication> GetCurrentPublicationAsync(CancellationToken cancellationToken = default)
        {
            var state = stateService.CurrentState;
            var gameType = state.CurrentGameType;
            if (gameType == GameType.Unknown)
            {
                gameType = gameDetection.DetectFromExecutable(state.XEditExecutablePath ?? string.Empty);
                if (gameType == GameType.Unknown && !string.IsNullOrWhiteSpace(state.LoadOrderPath))
                {
                    gameType = await gameDetection.DetectFromLoadOrderAsync(state.LoadOrderPath, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            var capability = new GameCapabilityProvider().Get(gameType);
            if (capability.RequiresLoadOrderFile &&
                (string.IsNullOrWhiteSpace(state.LoadOrderPath) || !File.Exists(state.LoadOrderPath)))
            {
                var missingPlanSnapshot = CreateSnapshot(state, gameType);
                return CreatePublicationWithoutPlan(state, missingPlanSnapshot, gameType);
            }

            var userConfig = await configurationService.LoadUserConfigAsync(cancellationToken).ConfigureAwait(false);
            var publicationState = state;
            if (gameType != GameType.Unknown)
            {
                var skipEvaluation = await new SkipListPolicy(configurationService, gameDetection)
                    .EvaluateAsync(gameType, state.PluginsToClean, userConfig.Settings.DisableSkipLists, cancellationToken)
                    .ConfigureAwait(false);
                publicationState = state with { PluginsToClean = skipEvaluation.Plugins };
            }

            var snapshot = CreateSnapshot(publicationState, gameType);

            var mode = state.Mo2ModeEnabled
                ? PluginRefreshDiscoveryMode.Mo2LoadOrderFile
                : string.IsNullOrWhiteSpace(state.LoadOrderPath)
                    ? PluginRefreshDiscoveryMode.DirectAutomatic
                    : PluginRefreshDiscoveryMode.DirectLoadOrderFile;
            var firstPath = publicationState.PluginsToClean.FirstOrDefault()?.FullPath;
            var dataFolder = string.IsNullOrWhiteSpace(firstPath)
                ? null
                : Path.GetDirectoryName(firstPath);
            var plan = gameType == GameType.Unknown
                ? null
                : new PluginRefreshDiscoveryPlan(
                    gameType,
                    mode,
                    snapshot.Configuration,
                    DisableSkipLists: false,
                    CanAttemptIssueApproximation: true,
                    dataFolder,
                    state.Mo2ModeEnabled ? null : state.LoadOrderPath,
                    state.Mo2ModeEnabled ? state.LoadOrderPath : null,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    Mo2BaseDataFolder: dataFolder);
            var rows = CreatePublishedRows(publicationState);

            return new PluginRefreshPublication(
                snapshot.Generation,
                gameType,
                plan,
                snapshot.Configuration,
                plan is null ? PluginRefreshFreshness.Missing : PluginRefreshFreshness.Fresh,
                rows,
                snapshot.Rows,
                snapshot.Activity,
                snapshot.Commands,
                snapshot.StatusText);
        }

        private static PluginRefreshPublication CreatePublicationWithoutPlan(
            AppState state,
            PluginRefreshSnapshot snapshot,
            GameType gameType) =>
            new(
                snapshot.Generation,
                gameType,
                DiscoveryPlan: null,
                snapshot.Configuration,
                PluginRefreshFreshness.Missing,
                CreatePublishedRows(state),
                snapshot.Rows,
                snapshot.Activity,
                snapshot.Commands,
                snapshot.StatusText);

        private static IReadOnlyList<PluginRefreshPublishedRow> CreatePublishedRows(AppState state) =>
            state.PluginsToClean.Select(plugin => new PluginRefreshPublishedRow(
                    plugin,
                    IsVisible: !plugin.IsInSkipList,
                    IsSelected: !state.ExcludedPluginPaths.Contains(plugin.FullPath),
                    IsSkippedByPolicy: plugin.IsInSkipList,
                    new PluginRefreshRowKey(plugin.FileName, plugin.FullPath)))
                .ToList();

        private static PluginRefreshSnapshot CreateSnapshot(AppState state) => CreateSnapshot(state, state.CurrentGameType);

        private static PluginRefreshSnapshot CreateSnapshot(AppState state, GameType gameType)
        {
            var rows = state.PluginsToClean
                .Where(plugin => !plugin.IsInSkipList)
                .Select(plugin => new PluginRefreshRow(
                    plugin.FileName,
                    plugin.FullPath,
                    plugin.DetectedGameType == GameType.Unknown ? gameType : plugin.DetectedGameType,
                    IsSelected: !state.ExcludedPluginPaths.Contains(plugin.FullPath),
                    plugin.IsInSkipList,
                    plugin.Approximation))
                .ToList();
            var configuration = new PluginRefreshConfigurationProjection(
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
            return new PluginRefreshSnapshot(
                Generation: 0,
                gameType,
                rows,
                configuration,
                new PluginRefreshActivity(false, false),
                new PluginRefreshCommandAvailability(false, false, false, false),
                "Ready");
        }
    }

    private sealed class EmptyObservable<T> : IObservable<T>
    {
        public IDisposable Subscribe(IObserver<T> observer) => EmptyDisposable.Instance;
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static EmptyDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Maps a PluginWarningKind validation result to a PreflightSkipReason.
    /// Per R-05: uses the actual enum from AutoQAC/Models/PluginInfo.cs:6-14.
    /// </summary>
    private static PreflightSkipReason MapPluginWarningToReason(PluginWarningKind warning) =>
        warning switch
        {
            PluginWarningKind.NotFound => PreflightSkipReason.FileNotFound,
            PluginWarningKind.Unreadable => PreflightSkipReason.Unreadable,
            PluginWarningKind.ZeroByte => PreflightSkipReason.ZeroByte,
            PluginWarningKind.MalformedEntry => PreflightSkipReason.MalformedEntry,
            PluginWarningKind.InvalidExtension => PreflightSkipReason.InvalidExtension,
            _ => throw new InvalidOperationException($"Unexpected PluginWarningKind: {warning}")
        };

    /// <summary>
    /// Converts a file-validation skip reason back to the legacy warning label used in summary logging.
    /// </summary>
    private static string MapReasonToPluginWarningLabel(PreflightSkipReason reason) =>
        reason switch
        {
            PreflightSkipReason.FileNotFound => nameof(PluginWarningKind.NotFound),
            PreflightSkipReason.Unreadable => nameof(PluginWarningKind.Unreadable),
            PreflightSkipReason.ZeroByte => nameof(PluginWarningKind.ZeroByte),
            PreflightSkipReason.MalformedEntry => nameof(PluginWarningKind.MalformedEntry),
            PreflightSkipReason.InvalidExtension => nameof(PluginWarningKind.InvalidExtension),
            _ => reason.ToString()
        };

    /// <summary>
    /// True for skip reasons produced by on-disk plugin validation.
    /// </summary>
    private static bool IsFileValidationReason(PreflightSkipReason? reason) =>
        reason is PreflightSkipReason.FileNotFound
            or PreflightSkipReason.Unreadable
            or PreflightSkipReason.ZeroByte
            or PreflightSkipReason.MalformedEntry
            or PreflightSkipReason.InvalidExtension;

}
