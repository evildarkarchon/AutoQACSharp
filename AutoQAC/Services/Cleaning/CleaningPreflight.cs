using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameDetection;
using AutoQAC.Services.MO2;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;

namespace AutoQAC.Services.Cleaning;

/// <summary>
/// Shared preflight/selection pipeline (D-13). Consumed by both StartCleaningAsync and
/// RunDryRunAsync. No cleaning state mutation, no process launch, no backup, no CTS creation (D-14).
/// </summary>
public sealed class CleaningPreflight(
    IConfigurationService configService,
    IGameDetectionService gameDetection,
    IPluginValidationService pluginValidation,
    IMo2ValidationService mo2Validation,
    ICleaningService cleaningService,
    IStateService stateService,
    ILoggingService logger)
    : ICleaningPreflight
{
    /// <inheritdoc />
    public async Task<CleaningPreflightPlan> PrepareAsync(CancellationToken ct = default)
    {
        // Flush any pending config saves before launching xEdit
        // (per user decision: "Always force-flush pending config saves before launching xEdit")
        await configService.FlushPendingSavesAsync(ct).ConfigureAwait(false);

        // 1. Validate configuration
        var isValid = await ValidateConfigurationAsync(ct).ConfigureAwait(false);
        if (!isValid)
        {
            logger.Error(null, "Configuration is invalid, cannot start cleaning.");
            throw new InvalidOperationException("Configuration is invalid");
        }

        // 2. Get plugins from state (already loaded with skip list status)
        var config = stateService.CurrentState;
        var allPlugins = config.PluginsToClean;

        // 3. Detect Game (if unknown) without mutating state; the caller owns mode-specific state updates (D-14).
        var gameType = config.CurrentGameType;
        if (gameType == GameType.Unknown)
        {
            var detectedGame = gameDetection.DetectFromExecutable(config.XEditExecutablePath ?? string.Empty);

            if (detectedGame == GameType.Unknown && !string.IsNullOrEmpty(config.LoadOrderPath))
            {
                detectedGame = await gameDetection.DetectFromLoadOrderAsync(config.LoadOrderPath, ct)
                    .ConfigureAwait(false);
            }

            if (detectedGame != GameType.Unknown)
            {
                logger.Information("Detected game type: {GameType}", detectedGame);
                gameType = detectedGame;
            }
            else
            {
                logger.Error(null, "Cannot determine game type. Cleaning blocked for safety -- skip lists cannot be applied without a known game type.");
                throw new InvalidOperationException(
                    "Cannot start cleaning: game type could not be determined. " +
                    "Please select a game type in Settings, or ensure the xEdit executable name matches a supported game.");
            }
        }

        // 3b. Detect game variant for skip list handling
        var pluginNames = allPlugins.Select(p => p.FileName).ToList();
        var gameVariant = gameDetection.DetectVariant(gameType, pluginNames);
        if (gameVariant != GameVariant.None)
        {
            logger.Information("Detected game variant: {Variant}", gameVariant);
        }

        // 4. Apply skip list filtering (respecting DisableSkipLists setting)
        var userConfig = await configService.LoadUserConfigAsync(ct).ConfigureAwait(false);
        var disableSkipLists = userConfig.Settings.DisableSkipLists;
        var isMo2Mode = userConfig.Settings.Mo2Mode;

        // 4a. MO2 configuration validation (early check with actionable error messages)
        if (isMo2Mode)
        {
            var mo2Path = config.Mo2ExecutablePath;

            if (string.IsNullOrEmpty(mo2Path))
            {
                throw new InvalidOperationException(
                    "MO2 mode is enabled but no MO2 executable path is configured. " +
                    "Check MO2 executable path in Settings, or disable MO2 mode if not using Mod Organizer 2.");
            }

            if (!File.Exists(mo2Path) || !await mo2Validation.ValidateMo2ExecutableAsync(mo2Path).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    $"MO2 mode is enabled but MO2 executable not found at '{mo2Path}'. " +
                    "Check MO2 executable path in Settings, or disable MO2 mode if not using Mod Organizer 2.");
            }
        }

        var excluded = config.ExcludedPluginPaths;
        var rows = new List<PreflightPluginRow>();
        HashSet<string>? skipSet = null;
        if (!disableSkipLists && gameType != GameType.Unknown)
        {
            var skipList = await configService.GetSkipListAsync(gameType, gameVariant, ct)
                .ConfigureAwait(false);
            skipSet = new HashSet<string>(skipList, StringComparer.OrdinalIgnoreCase);
        }

        foreach (var plugin in allPlugins)
        {
            ct.ThrowIfCancellationRequested();
            var enrichedPlugin = plugin with
            {
                IsInSkipList = skipSet?.Contains(plugin.FileName) ?? plugin.IsInSkipList,
                DetectedGameType = gameType
            };

            if (excluded.Contains(plugin.FullPath))
            {
                rows.Add(new PreflightPluginRow(enrichedPlugin, PreflightDecision.Skip, PreflightSkipReason.NotSelected));
                continue;
            }

            if (skipSet != null && skipSet.Contains(plugin.FileName))
            {
                rows.Add(new PreflightPluginRow(enrichedPlugin, PreflightDecision.Skip, PreflightSkipReason.InSkipList));
                continue;
            }

            // File-existence validation (skipped in MO2 mode -- MO2 VFS resolves paths at runtime)
            if (!isMo2Mode)
            {
                var warning = pluginValidation.ValidatePluginFile(enrichedPlugin);
                if (warning != PluginWarningKind.None)
                {
                    rows.Add(new PreflightPluginRow(enrichedPlugin, PreflightDecision.Skip, MapPluginWarningToReason(warning)));
                    continue;
                }
            }

            rows.Add(new PreflightPluginRow(enrichedPlugin, PreflightDecision.Clean, SkipReason: null));
        }

        if (!isMo2Mode)
        {
            var pathFailures = rows
                .Where(r => IsFileValidationReason(r.SkipReason))
                .Select(r => $"{r.Plugin.FileName} ({MapReasonToPluginWarningLabel(r.SkipReason!.Value)})")
                .ToList();
            if (pathFailures.Count > 0)
            {
                var summary = $"{pathFailures.Count} plugin(s) not found or unreadable: {string.Join(", ", pathFailures)}";
                logger.Warning(summary);
            }
        }
        else
        {
            logger.Debug("MO2 mode active -- skipping file-existence validation (MO2 VFS resolves paths at xEdit runtime)");
        }

        // Step 7 (R-08): compute XEditDirectory once. Both runner and finalizer consume this.
        var xEditDir = Path.GetDirectoryName(config.XEditExecutablePath ?? string.Empty) ?? string.Empty;

        // Step 8: build the plan.
        return new CleaningPreflightPlan
        {
            DetectedGameType = gameType,
            DetectedGameVariant = gameVariant,
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

    /// <summary>
    /// Validates the xEdit and load-order environment using the existing cleaning service contract.
    /// </summary>
    private async Task<bool> ValidateConfigurationAsync(CancellationToken ct)
    {
        var config = stateService.CurrentState;

        if (string.IsNullOrEmpty(config.XEditExecutablePath))
        {
            return false;
        }

        if (RequiresFileLoadOrder(config.CurrentGameType))
        {
            if (string.IsNullOrWhiteSpace(config.LoadOrderPath) ||
                !File.Exists(config.LoadOrderPath))
            {
                return false;
            }
        }

        return await cleaningService.ValidateEnvironmentAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// True for game types that require file-based load-order parsing (Oblivion, FO3, FNV)
    /// rather than Mutagen-backed plugin discovery.
    /// </summary>
    /// <remarks>
    /// Duplicated in CleaningService, CleaningCommandsViewModel, ConfigurationViewModel, and
    /// PluginLoadingService. Consolidating those copies is REF-02 (Phase 9), NOT Phase 8 (D-11).
    /// </remarks>
    private static bool RequiresFileLoadOrder(GameType gameType) => gameType switch
    {
        GameType.Fallout3 => true,
        GameType.FalloutNewVegas => true,
        GameType.Oblivion => true,
        _ => false
    };
}
