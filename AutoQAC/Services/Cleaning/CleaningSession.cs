using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Process;

namespace AutoQAC.Services.Cleaning;

/// <summary>
/// Owns the full Cleaning session lifecycle: preflight, backup policy, sequential xEdit launches,
/// cancellation, user decisions, and final state publication.
/// </summary>
public sealed class CleaningSession(
    ICleaningPreflight preflight,
    IBackupSessionCoordinator backupCoordinator,
    ICleaningTerminationCoordinator terminationCoordinator,
    IPluginCleaning pluginCleaning,
    ICleaningSessionStatePublisher statePublisher,
    ICleaningSessionDecisionAdapter decisions,
    ILoggingService logger,
    IProcessExecutionService processService)
    : ICleaningSession, IDisposable
{
    private readonly Lock _ctsLock = new();
    private CancellationTokenSource? _cleaningCts;
    private int _sessionActive;

    /// <inheritdoc />
    public IObservable<bool> HangDetected => terminationCoordinator.HangDetected;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken ct = default)
    {
        EnterSessionOrThrow();
        const int maxRetryAttempts = 3;
        var context = new SessionContext(DateTime.Now, GameType.Unknown);
        string? backupSessionDir = null;
        var backupEntries = new List<BackupPluginEntry>();

        // Reset stop flags at the start of each cleaning session.
        terminationCoordinator.ResetForNewSession();

        try
        {
            logger.Information("Starting cleaning workflow");

            // Create the session CTS before cancellable startup work so stop can cancel orphan cleanup / preflight.
            var cts = CreateSessionCts(ct);

            // Clean orphaned processes before starting.
            await processService.CleanOrphanedProcessesAsync(cts.Token).ConfigureAwait(false);

            var preflightPlan = await preflight.PrepareAsync(cts.Token).ConfigureAwait(false);
            context = context with { GameType = preflightPlan.DetectedGameType };
            var pluginsToClean = preflightPlan.PluginRows.Where(r => r.Decision == PreflightDecision.Clean)
                .Select(r => r.Plugin).ToList();
            ThrowIfNoValidPluginsAfterFileValidation(preflightPlan);

            // Real cleaning mode applies detected game state; dry-run does not.
            statePublisher.PublishDetectedGame(context.GameType);
            statePublisher.PublishStarted(pluginsToClean);

            backupSessionDir =
                await backupCoordinator.BeginSessionAsync(preflightPlan, cts.Token).ConfigureAwait(false);

            // If the user requested Stop during startup or backup session begin, bail out before launching xEdit.
            cts.Token.ThrowIfCancellationRequested();

            // Process plugins SEQUENTIALLY (CRITICAL!) -- xEdit must never run in parallel.
            foreach (var plugin in pluginsToClean)
            {
                if (cts.Token.IsCancellationRequested)
                {
                    logger.Information("Cleaning cancelled by user");
                    context = context with { WasCancelled = true };
                    break;
                }

                var pluginDecision = await ProcessPluginAsync(plugin, preflightPlan, backupSessionDir, backupEntries,
                        maxRetryAttempts, context, cts.Token)
                    .ConfigureAwait(false);

                if (pluginDecision == PluginLoopDecision.ReturnedEarly)
                {
                    return;
                }

                if (pluginDecision == PluginLoopDecision.StopSession || cts.Token.IsCancellationRequested ||
                    terminationCoordinator.IsStopRequested)
                {
                    context = context with { WasCancelled = true };
                    break;
                }
            }

            if (backupSessionDir is not null && backupEntries.Count > 0)
            {
                context = context with
                {
                    BackupCleanup = await backupCoordinator.FinalizeSessionAsync(backupSessionDir, context.GameType,
                            backupEntries, preflightPlan.BackupMaxSessions, cts.Token)
                        .ConfigureAwait(false)
                };
            }

            FinishSession(context);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not an error -- preserve partial results.
            logger.Information("Cleaning workflow cancelled");
            if (backupSessionDir is not null && backupEntries.Count > 0)
            {
                await backupCoordinator
                    .WritePartialMetadataAsync(backupSessionDir, context.GameType, backupEntries,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }

            FinishSession(context with { WasCancelled = true });
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error during cleaning workflow");
            // Still create a session result even on error.
            FinishSession(context);
            throw;
        }
        finally
        {
            terminationCoordinator.CompleteSessionFinalization();
            DisposeSessionCts();
            ExitSession();
        }

        return;

        async Task<PluginLoopDecision> ProcessPluginAsync(PluginInfo plugin, CleaningPreflightPlan plan,
            string? sessionDir,
            List<BackupPluginEntry> backupPluginEntries,
            int retryLimit, SessionContext contextSnapshot, CancellationToken cancellationToken)
        {
            logger.Information("Processing plugin: {Plugin}", plugin.FileName);
            statePublisher.PublishCurrentPlugin(plugin.FileName);

            if (sessionDir is not null)
            {
                var backupResult =
                    await HandleBackupOutcomeAsync(plugin, sessionDir, backupPluginEntries, contextSnapshot,
                        cancellationToken).ConfigureAwait(false);
                switch (backupResult)
                {
                    case PluginLoopDecision.SkipPlugin:
                    case PluginLoopDecision.StopSession:
                    case PluginLoopDecision.ReturnedEarly:
                        return backupResult;
                    case PluginLoopDecision.Continue:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(backupResult), backupResult,
                            "Unexpected backup handling decision.");
                }
            }

            // RunPluginBackupAsync is handled above via HandleBackupOutcomeAsync; keep backup before Plugin cleaning.
            var result = await pluginCleaning.CleanAsync(
                    new PluginCleaningContext(
                        plugin,
                        plan.DetectedGameType,
                        plan.XEditDirectory,
                        plan.CleaningTimeoutSeconds,
                        retryLimit),
                    cancellationToken)
                .ConfigureAwait(false);

            contextSnapshot.Results.Add(result);
            statePublisher.PublishPluginResult(result);
            logger.Information("Plugin {Plugin} processed: {Status} - {Message}", plugin.FileName, result.Status,
                result.Message);
            return PluginLoopDecision.Continue;
        }

        async Task<PluginLoopDecision> HandleBackupOutcomeAsync(PluginInfo plugin, string sessionDir,
            List<BackupPluginEntry> backupPluginEntries,
            SessionContext contextSnapshot,
            CancellationToken cancellationToken)
        {
            var outcome = await backupCoordinator.RunPluginBackupAsync(plugin, sessionDir, decisions,
                    cancellationToken)
                .ConfigureAwait(false);
            switch (outcome.Kind)
            {
                case PluginBackupOutcomeKind.Canceled:
                case PluginBackupOutcomeKind.UserSkipped:
                    if (outcome.SkippedResult is not null)
                    {
                        contextSnapshot.Results.Add(outcome.SkippedResult);
                        statePublisher.PublishPluginResult(outcome.SkippedResult);
                    }

                    if (outcome.Kind == PluginBackupOutcomeKind.UserSkipped)
                    {
                        statePublisher.PublishSkippedPlugin(plugin.FileName);
                    }

                    return cancellationToken.IsCancellationRequested
                        ? PluginLoopDecision.StopSession
                        : PluginLoopDecision.SkipPlugin;

                case PluginBackupOutcomeKind.AbortSession:
                    // Best-effort partial metadata write so the abandoned session has a manifest.
                    await backupCoordinator
                        .WritePartialMetadataAsync(sessionDir, contextSnapshot.GameType, backupPluginEntries,
                            CancellationToken.None).ConfigureAwait(false);
                    var abortedContext = contextSnapshot with { WasCancelled = true };
                    FinishSession(abortedContext);
                    return PluginLoopDecision.ReturnedEarly;

                case PluginBackupOutcomeKind.Succeeded:
                    if (outcome.Entry is not null) backupPluginEntries.Add(outcome.Entry);
                    break;

                case PluginBackupOutcomeKind.ContinueWithoutBackup:
                    // Proceed to xEdit; no entry added.
                    break;
            }

            return PluginLoopDecision.Continue;
        }
    }

    /// <inheritdoc />
    public async Task<CleaningSessionControlResult> ControlAsync(
        CleaningSessionControl control,
        CancellationToken ct = default)
    {
        return control switch
        {
            CleaningSessionControl.RequestStop => await RequestStopAsync(ct).ConfigureAwait(false),
            CleaningSessionControl.ForceStop => await ForceStopAsync().ConfigureAwait(false),
            CleaningSessionControl.CancelBackupOperation => await HandleCancelBackupOperationAsync()
                .ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(control), control, "Unknown Cleaning session control.")
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DryRunResult>> PreviewAsync(CancellationToken ct = default)
    {
        logger.Information("Starting dry-run preview");
        var preflightPlan = await preflight.PrepareAsync(ct).ConfigureAwait(false);
        var results = preflightPlan.PluginRows.Select(ToDryRunResult).ToList();
        logger.Information("Dry-run preview complete: {WillClean} will clean, {WillSkip} will skip",
            results.Count(r => r.Status == DryRunStatus.WillClean),
            results.Count(r => r.Status == DryRunStatus.WillSkip));
        return results;
    }

    /// <summary>Creates the session CTS linked to the caller token and publishes it under the session lock.</summary>
    private CancellationTokenSource CreateSessionCts(CancellationToken ct)
    {
        lock (_ctsLock)
        {
            _cleaningCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            return _cleaningCts;
        }
    }

    /// <summary>
    /// Enters the single active cleaning session slot without blocking competing callers.
    /// This protects CTS ownership and preserves sequential orchestration before startup work mutates session state.
    /// </summary>
    private void EnterSessionOrThrow()
    {
        if (Interlocked.CompareExchange(ref _sessionActive, 1, 0) == 1)
        {
            throw new InvalidOperationException("A cleaning session is already in progress.");
        }
    }

    /// <summary>
    /// Releases the active cleaning session slot after CTS disposal and termination reset complete.
    /// The volatile write makes subsequent StartAsync calls observe the session as idle only after cleanup finishes.
    /// </summary>
    private void ExitSession()
    {
        Volatile.Write(ref _sessionActive, 0);
    }

    /// <summary>Cancels the current session CTS before delegating stop behavior to the termination coordinator.</summary>
    private void CancelSessionCts()
    {
        CancellationTokenSource? cts;
        lock (_ctsLock)
        {
            cts = _cleaningCts;
        }

        try
        {
            if (cts is not null) _ = cts.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed -- fine.
        }
    }

    /// <summary>Disposes and clears the session CTS owned by the session module.</summary>
    private void DisposeSessionCts()
    {
        lock (_ctsLock)
        {
            _cleaningCts?.Dispose();
            _cleaningCts = null;
        }
    }

    /// <summary>Requests graceful stop and resolves grace-period expiry through the decision adapter.</summary>
    private async Task<CleaningSessionControlResult> RequestStopAsync(CancellationToken ct)
    {
        if (!HasControllableSession())
        {
            return new CleaningSessionControlResult(
                CleaningSessionControl.RequestStop,
                CleaningSessionControlStatus.NoActiveSession);
        }

        CancelSessionCts();
        var stopResult = await terminationCoordinator.StopAsync().ConfigureAwait(false);
        var terminationResult = stopResult.TerminationResult ?? terminationCoordinator.LastTerminationResult;

        if (terminationResult != TerminationResult.GracePeriodExpired)
        {
            return new CleaningSessionControlResult(
                CleaningSessionControl.RequestStop,
                MapRequestStopStatus(terminationResult),
                terminationResult);
        }

        var decision = await decisions.ChooseAfterGracePeriodExpiredAsync(terminationResult.Value, ct)
            .ConfigureAwait(false);
        if (decision == CleaningSessionStopDecision.ForceTerminate)
        {
            var forceResult = await ForceStopAsync().ConfigureAwait(false);
            return forceResult with { Control = CleaningSessionControl.RequestStop };
        }

        var leftRunningResult = terminationCoordinator.MarkLeftRunningByUser();
        return new CleaningSessionControlResult(
            CleaningSessionControl.RequestStop,
            CleaningSessionControlStatus.LeftRunningByUser,
            leftRunningResult.TerminationResult);
    }

    /// <summary>Requests immediate force termination for the active or pending xEdit process.</summary>
    private async Task<CleaningSessionControlResult> ForceStopAsync()
    {
        if (!HasControllableSession())
        {
            return new CleaningSessionControlResult(
                CleaningSessionControl.ForceStop,
                CleaningSessionControlStatus.NoActiveSession);
        }

        CancelSessionCts();
        var forceResult = await terminationCoordinator.ForceStopAsync().ConfigureAwait(false);
        var terminationResult = forceResult.TerminationResult ?? terminationCoordinator.LastTerminationResult;
        return new CleaningSessionControlResult(
            CleaningSessionControl.ForceStop,
            MapForceStopStatus(terminationResult),
            terminationResult);
    }

    /// <summary>Cancels active backup or retention file work without terminating xEdit.</summary>
    private async Task<CleaningSessionControlResult> HandleCancelBackupOperationAsync()
    {
        if (!IsSessionActive())
        {
            return new CleaningSessionControlResult(
                CleaningSessionControl.CancelBackupOperation,
                CleaningSessionControlStatus.NoActiveBackupOperation);
        }

        if (terminationCoordinator.HasActiveProcess)
        {
            logger.Information("CancelBackupOperationAsync ignored: xEdit is active");
            return new CleaningSessionControlResult(
                CleaningSessionControl.CancelBackupOperation,
                CleaningSessionControlStatus.NoActiveBackupOperation);
        }

        await backupCoordinator.CancelActiveOperationAsync().ConfigureAwait(false);
        return new CleaningSessionControlResult(
            CleaningSessionControl.CancelBackupOperation,
            CleaningSessionControlStatus.BackupCancellationRequested);
    }

    /// <summary>True while a real Cleaning session is active.</summary>
    private bool IsSessionActive() => Volatile.Read(ref _sessionActive) == 1;

    /// <summary>
    /// True when a control request can still affect session or retained termination state.
    /// GracePeriodExpired may outlive session finalization until the user resolves it.
    /// </summary>
    private bool HasControllableSession() =>
        IsSessionActive() ||
        terminationCoordinator.HasActiveProcess ||
        terminationCoordinator.LastTerminationResult == TerminationResult.GracePeriodExpired;

    /// <summary>Maps a graceful stop path to caller-facing control status.</summary>
    private static CleaningSessionControlStatus MapRequestStopStatus(TerminationResult? terminationResult) =>
        terminationResult switch
        {
            null => CleaningSessionControlStatus.StopRequested,
            TerminationResult.AlreadyExited or TerminationResult.GracefulExit =>
                CleaningSessionControlStatus.GracefullyStopped,
            TerminationResult.ForceKilled => CleaningSessionControlStatus.ForceStopped,
            TerminationResult.ForceKillFailed => CleaningSessionControlStatus.ForceKillFailed,
            TerminationResult.LeftRunningByUser => CleaningSessionControlStatus.LeftRunningByUser,
            _ => CleaningSessionControlStatus.StopRequested
        };

    /// <summary>Maps a force stop path to caller-facing control status.</summary>
    private static CleaningSessionControlStatus MapForceStopStatus(TerminationResult? terminationResult) =>
        terminationResult switch
        {
            TerminationResult.ForceKillFailed => CleaningSessionControlStatus.ForceKillFailed,
            null => CleaningSessionControlStatus.StopRequested,
            _ => CleaningSessionControlStatus.ForceStopped
        };

    /// <summary>Builds the final session result, publishes it, and logs the legacy session summary.</summary>
    private void FinishSession(SessionContext context)
    {
        var session = new CleaningSessionResult
        {
            StartTime = context.StartTime, EndTime = DateTime.Now, GameType = context.GameType,
            WasCancelled = context.WasCancelled, PluginResults = context.Results, BackupCleanup = context.BackupCleanup
        };
        statePublisher.PublishCompleted(session);
        LogSessionSummary(session);
    }

    /// <summary>Converts a shared preflight row to the existing dry-run preview row contract.</summary>
    private static DryRunResult ToDryRunResult(PreflightPluginRow row) => row.Decision == PreflightDecision.Clean
        ? new DryRunResult(row.Plugin.FileName, DryRunStatus.WillClean, "Ready for cleaning")
        : new DryRunResult(row.Plugin.FileName, DryRunStatus.WillSkip, row.SkipReason switch
        {
            PreflightSkipReason.NotSelected => "Not selected", PreflightSkipReason.InSkipList => "In skip list",
            PreflightSkipReason.FileNotFound => "File not found",
            PreflightSkipReason.Unreadable => "File is unreadable",
            PreflightSkipReason.ZeroByte => "Zero-byte file",
            PreflightSkipReason.MalformedEntry => "Malformed file name",
            PreflightSkipReason.InvalidExtension => "Invalid file extension", _ => "Skipped"
        });

    /// <summary>Preserves the legacy real-run failure when all selected plugins fail file validation.</summary>
    private static void ThrowIfNoValidPluginsAfterFileValidation(CleaningPreflightPlan plan)
    {
        if (plan.PluginRows.Any(r => r.Decision == PreflightDecision.Clean)) return;
        var pathFailures = plan.PluginRows.Where(r => IsFileValidationReason(r.SkipReason))
            .Select(r => $"{r.Plugin.FileName} ({MapReasonToPluginWarningLabel(r.SkipReason!.Value)})").ToList();
        if (pathFailures.Count == 0) return;
        throw new InvalidOperationException(
            $"No valid plugins to clean. {pathFailures.Count} plugin(s) not found or unreadable: {string.Join(", ", pathFailures)}");
    }

    /// <summary>True for skip reasons produced by on-disk plugin validation.</summary>
    private static bool IsFileValidationReason(PreflightSkipReason? reason) =>
        reason is PreflightSkipReason.FileNotFound or PreflightSkipReason.Unreadable or PreflightSkipReason.ZeroByte
            or PreflightSkipReason.MalformedEntry or PreflightSkipReason.InvalidExtension;

    /// <summary>Converts a file-validation preflight reason to the legacy warning label used in exception summaries.</summary>
    private static string MapReasonToPluginWarningLabel(PreflightSkipReason reason) => reason switch
    {
        PreflightSkipReason.FileNotFound => nameof(PluginWarningKind.NotFound),
        PreflightSkipReason.Unreadable => nameof(PluginWarningKind.Unreadable),
        PreflightSkipReason.ZeroByte => nameof(PluginWarningKind.ZeroByte),
        PreflightSkipReason.MalformedEntry => nameof(PluginWarningKind.MalformedEntry),
        PreflightSkipReason.InvalidExtension => nameof(PluginWarningKind.InvalidExtension), _ => reason.ToString()
    };

    private void LogSessionSummary(CleaningSessionResult session)
    {
        logger.Information("=== AutoQAC Session Complete ===");
        logger.Information("Duration: {Duration}", session.TotalDuration.ToString(@"hh\:mm\:ss"));
        logger.Information("Plugins processed: {Total} (Cleaned: {Cleaned}, Skipped: {Skipped}, Failed: {Failed})",
            session.TotalPlugins, session.CleanedCount, session.SkippedCount, session.FailedCount);
        logger.Information("ITMs removed: {Itm}, UDRs fixed: {Udr}, Navmeshes: {Nav}",
            session.TotalItemsRemoved, session.TotalItemsUndeleted, session.TotalPartialFormsCreated);
        if (session.WasCancelled) logger.Information("Session was cancelled by user");
    }

    /// <inheritdoc />
    public void Dispose() => DisposeSessionCts();

    private sealed record SessionContext(DateTime StartTime, GameType GameType)
    {
        public bool WasCancelled { get; init; }
        public BackupRetentionCleanupResult? BackupCleanup { get; init; }
        public List<PluginCleaningResult> Results { get; } = [];
    }

    private enum PluginLoopDecision
    {
        Continue,
        SkipPlugin,
        StopSession,
        ReturnedEarly
    }
}
