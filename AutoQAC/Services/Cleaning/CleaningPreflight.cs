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
    IMo2ValidationService mo2Validation,
    IStateService stateService,
    ILoggingService logger)
    : ICleaningPreflight
{
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
        if (CleaningLaunchBlockerValidator.TryValidatePublication(publication, out var failure))
        {
            ThrowFailure(failure);
        }

        var config = stateService.CurrentState;
        if (CleaningLaunchBlockerValidator.TryValidateLaunchReadiness(publication, config, out failure))
        {
            ThrowFailure(failure);
        }

        await ValidateMo2ExecutableSemanticsAsync(publication, config).ConfigureAwait(false);

        // Read settings that drive Cleaning session policy. Discovery-affecting settings were already
        // checked through publication freshness above.
        var userConfig = await configService.LoadUserConfigAsync(ct).ConfigureAwait(false);
        var rows = new List<PreflightPluginRow>();
        if (CleaningLaunchBlockerValidator.TryValidateSelection(publication, out failure))
        {
            ThrowFailure(failure);
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

    private async Task ValidateMo2ExecutableSemanticsAsync(PluginRefreshPublication publication, AppState state)
    {
        var plan = publication.DiscoveryPlan!;
        if (plan.Mode != PluginRefreshDiscoveryMode.Mo2LoadOrderFile)
        {
            return;
        }

        var mo2Path = state.Mo2ExecutablePath;
        if (string.IsNullOrWhiteSpace(mo2Path))
        {
            return;
        }

        if (!await mo2Validation.ValidateMo2ExecutableAsync(mo2Path).ConfigureAwait(false))
        {
            ThrowFailure(CleaningLaunchBlockerValidator.CreateMo2NotFoundFailure(mo2Path));
        }
    }

    private static void ThrowFailure(CleaningPreflightFailure failure) =>
        throw new CleaningPreflightException(failure);

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
