using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Process;
using AutoQAC.Services.State;

namespace AutoQAC.Services.Cleaning;

public sealed class CleaningOrchestrator(
    ICleaningPreflight preflight,
    IBackupSessionCoordinator backupCoordinator,
    ICleaningTerminationCoordinator terminationCoordinator,
    IPluginCleaningRunner runner,
    IPluginResultFinalizer finalizer,
    IStateService stateService,
    ILoggingService logger,
    IProcessExecutionService processService)
    : ICleaningOrchestrator, IDisposable
{
    private readonly Lock _ctsLock = new();
    private CancellationTokenSource? _cleaningCts;
    private int _sessionActive;

    /// <inheritdoc />
    public TerminationResult? LastTerminationResult => terminationCoordinator.LastTerminationResult;

    /// <inheritdoc />
    public IObservable<bool> HangDetected => terminationCoordinator.HangDetected;

    /// <inheritdoc />
    public Task StartCleaningAsync(CancellationToken ct = default) => StartCleaningAsync(null, null, ct);

    /// <inheritdoc />
    public Task StartCleaningAsync(TimeoutRetryCallback? onTimeout, CancellationToken ct = default) =>
        StartCleaningAsync(onTimeout, null, ct);

    /// <inheritdoc />
    public async Task StartCleaningAsync(TimeoutRetryCallback? onTimeout, BackupFailureCallback? onBackupFailure,
        CancellationToken ct = default)
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

            // Gap CR-01 fix: create the session CTS BEFORE any cancellable startup work so that
            // StopCleaningAsync (which calls CancelSessionCts) can cancel orphan cleanup / preflight.
            // Previously the CTS was created only after preflight returned, so a stop click during
            // preflight was a no-op and xEdit could still launch.
            var cts = CreateSessionCts(ct);

            // Clean orphaned processes before starting.
            await processService.CleanOrphanedProcessesAsync(cts.Token).ConfigureAwait(false);

            var preflightPlan = await preflight.PrepareAsync(cts.Token).ConfigureAwait(false);
            context = context with { GameType = preflightPlan.DetectedGameType };
            var pluginsToClean = preflightPlan.PluginRows.Where(r => r.Decision == PreflightDecision.Clean)
                .Select(r => r.Plugin).ToList();
            ThrowIfNoValidPluginsAfterFileValidation(preflightPlan);

            // D-14: real cleaning mode applies detected game state; dry-run does not.
            var context1 = context;
            stateService.UpdateState(s => s with { CurrentGameType = context1.GameType });
            stateService.StartCleaning(pluginsToClean);

            backupSessionDir =
                await backupCoordinator.BeginSessionAsync(preflightPlan, cts.Token).ConfigureAwait(false);

            // Gap CR-01 fix: if the user requested Stop during orphan cleanup, preflight, state
            // initialization, or backup session begin, bail out cleanly before launching xEdit.
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
                        onTimeout,
                        onBackupFailure, maxRetryAttempts, context, cts.Token)
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
            List<BackupPluginEntry> backupPluginEntries, TimeoutRetryCallback? timeoutRetryCallback,
            BackupFailureCallback? backupFailureCallback,
            int retryLimit, SessionContext contextSnapshot, CancellationToken cancellationToken)
        {
            logger.Information("Processing plugin: {Plugin}", plugin.FileName);
            stateService.UpdateState(s => s with { CurrentPlugin = plugin.FileName });

            if (sessionDir is not null)
            {
                var backupResult =
                    await HandleBackupOutcomeAsync(plugin, sessionDir, backupPluginEntries, backupFailureCallback,
                        contextSnapshot, cancellationToken).ConfigureAwait(false);
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

            // RunPluginBackupAsync is handled above via HandleBackupOutcomeAsync; keep backup before runner.RunAsync.
            // R-01: maxRetryAttempts = 3 matches the original retry ceiling defined for this method.
            // R-08: runner and finalizer both consume plan.XEditDirectory; the facade does not re-derive it.
            var runnerOutput = await runner.RunAsync(
                plugin, plan.DetectedGameType, plan.XEditDirectory, timeoutRetryCallback,
                plan.CleaningTimeoutSeconds, retryLimit,
                attachProcess: terminationCoordinator.AttachProcess,
                detachProcess: terminationCoordinator.DetachProcess, cancellationToken).ConfigureAwait(false);

            // Snapshot termination context AFTER detach so ProcessMayStillBeRunning reflects final state (Research Open Question #3).
            var terminationContext = new TerminationFinalizeContext(terminationCoordinator.ProcessMayStillBeRunning,
                terminationCoordinator.IsStopRequested);
            var result = await finalizer.FinalizeAsync(plugin, plan.DetectedGameType,
                    plan.XEditDirectory, runnerOutput, terminationContext, cancellationToken)
                .ConfigureAwait(false);

            contextSnapshot.Results.Add(result);
            stateService.AddDetailedCleaningResult(result);
            logger.Information("Plugin {Plugin} processed: {Status} - {Message}", plugin.FileName, result.Status,
                result.Message);
            return PluginLoopDecision.Continue;
        }

        async Task<PluginLoopDecision> HandleBackupOutcomeAsync(PluginInfo plugin, string sessionDir,
            List<BackupPluginEntry> backupPluginEntries, BackupFailureCallback? backupFailureCallback,
            SessionContext contextSnapshot,
            CancellationToken cancellationToken)
        {
            var outcome = await backupCoordinator.RunPluginBackupAsync(plugin, sessionDir, backupFailureCallback,
                    cancellationToken)
                .ConfigureAwait(false);
            switch (outcome.Kind)
            {
                case PluginBackupOutcomeKind.Canceled:
                case PluginBackupOutcomeKind.UserSkipped:
                    if (outcome.SkippedResult is not null)
                    {
                        contextSnapshot.Results.Add(outcome.SkippedResult);
                        stateService.AddDetailedCleaningResult(outcome.SkippedResult);
                    }

                    if (outcome.Kind == PluginBackupOutcomeKind.UserSkipped)
                    {
                        stateService.UpdateState(s => s with
                        {
                            SkippedPlugins =
                            new HashSet<string>(s.SkippedPlugins) { plugin.FileName }.ToFrozenSet(StringComparer
                                .Ordinal)
                        });
                    }

                    return cancellationToken.IsCancellationRequested
                        ? PluginLoopDecision.StopSession
                        : PluginLoopDecision.SkipPlugin;

                case PluginBackupOutcomeKind.AbortSession:
                    // R-09: full AbortSession branch -- verbatim from CleaningOrchestrator.cs:333-362.
                    // Step 1: best-effort partial metadata write so the abandoned session has a manifest.
                    await backupCoordinator
                        .WritePartialMetadataAsync(sessionDir, contextSnapshot.GameType, backupPluginEntries,
                            CancellationToken.None).ConfigureAwait(false);
                    // Step 2: mark the session as cancelled so finalization classifies correctly.
                    var abortedContext = contextSnapshot with { WasCancelled = true };
                    // Steps 3-5: build CleaningSessionResult, publish to state, then log the legacy summary.
                    FinishSession(abortedContext);
                    // Step 6: exit the StartCleaningAsync method.
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
    public async Task CancelBackupOperationAsync()
    {
        if (terminationCoordinator.HasActiveProcess)
        {
            logger.Information("CancelBackupOperationAsync ignored: xEdit is active");
            return;
        }

        await backupCoordinator.CancelActiveOperationAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<StopCleaningResult> StopCleaningAsync()
    {
        CancelSessionCts();
        return await terminationCoordinator.StopAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<StopCleaningResult> ForceStopCleaningAsync()
    {
        CancelSessionCts();
        return await terminationCoordinator.ForceStopAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public StopCleaningResult MarkLeftRunningByUser() => terminationCoordinator.MarkLeftRunningByUser();

    /// <inheritdoc />
    public async Task<List<DryRunResult>> RunDryRunAsync(CancellationToken ct = default)
    {
        logger.Information("Starting dry-run preview");
        var preflightPlan = await preflight.PrepareAsync(ct).ConfigureAwait(false);
        var results = preflightPlan.PluginRows.Select(ToDryRunResult).ToList();
        logger.Information("Dry-run preview complete: {WillClean} will clean, {WillSkip} will skip",
            results.Count(r => r.Status == DryRunStatus.WillClean),
            results.Count(r => r.Status == DryRunStatus.WillSkip));
        return results;
    }

    /// <summary>Creates the session CTS linked to the caller token and publishes it under the facade lock.</summary>
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
    /// This protects _cleaningCts ownership and preserves sequential orchestration before startup work mutates session state.
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
    /// The volatile write makes subsequent StartCleaningAsync calls observe the facade as idle only after cleanup finishes.
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
            // Already disposed -- fine
        }
    }

    /// <summary>Disposes and clears the session CTS owned by the facade.</summary>
    private void DisposeSessionCts()
    {
        lock (_ctsLock)
        {
            _cleaningCts?.Dispose();
            _cleaningCts = null;
        }
    }

    /// <summary>Builds the final session result, publishes it, and logs the legacy session summary.</summary>
    private void FinishSession(SessionContext context)
    {
        var session = new CleaningSessionResult
        {
            StartTime = context.StartTime, EndTime = DateTime.Now, GameType = context.GameType,
            WasCancelled = context.WasCancelled, PluginResults = context.Results, BackupCleanup = context.BackupCleanup
        };
        stateService.FinishCleaningWithResults(session);
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
