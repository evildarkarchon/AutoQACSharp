using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameDetection;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.Process;
using AutoQAC.Services.State;

namespace AutoQAC.Services.Cleaning;

public sealed class CleaningOrchestrator(
    ICleaningPreflight preflight,
    IBackupSessionCoordinator backupCoordinator,
    ICleaningTerminationCoordinator terminationCoordinator,
    ICleaningService cleaningService,
    IStateService stateService,
    ILoggingService logger,
    IProcessExecutionService processService,
    IXEditLogFileService logFileService,
    IXEditOutputParser outputParser)
    : ICleaningOrchestrator, IDisposable
{
    private readonly object _ctsLock = new();

    private CancellationTokenSource? _cleaningCts;

    public TerminationResult? LastTerminationResult => terminationCoordinator.LastTerminationResult;
    public IObservable<bool> HangDetected => terminationCoordinator.HangDetected;

    public Task StartCleaningAsync(CancellationToken ct = default)
    {
        return StartCleaningAsync(null, null, ct);
    }

    public Task StartCleaningAsync(TimeoutRetryCallback? onTimeout, CancellationToken ct = default)
    {
        return StartCleaningAsync(onTimeout, null, ct);
    }

    public async Task StartCleaningAsync(TimeoutRetryCallback? onTimeout, BackupFailureCallback? onBackupFailure, CancellationToken ct = default)
    {
        const int maxRetryAttempts = 3;
        var startTime = DateTime.Now;
        var pluginResults = new List<PluginCleaningResult>();
        var wasCancelled = false;
        var gameType = GameType.Unknown;
        string? sessionDir = null;
        var backupEntries = new List<BackupPluginEntry>();
        BackupRetentionCleanupResult? backupCleanup = null;

        // Reset stop flags at the start of each cleaning session.
        terminationCoordinator.ResetForNewSession();

        try
        {
            logger.Information("Starting cleaning workflow");

            // Clean orphaned processes before starting
            await processService.CleanOrphanedProcessesAsync(ct).ConfigureAwait(false);

            var preflightPlan = await preflight.PrepareAsync(ct).ConfigureAwait(false);
            gameType = preflightPlan.DetectedGameType;
            var pluginsToClean = preflightPlan.PluginRows
                .Where(r => r.Decision == PreflightDecision.Clean)
                .Select(r => r.Plugin)
                .ToList();

            ThrowIfNoValidPluginsAfterFileValidation(preflightPlan);

            // D-14: real cleaning mode applies detected game state; dry-run does not.
            stateService.UpdateState(s => s with { CurrentGameType = gameType });

            // 5. Update state - cleaning started
            stateService.StartCleaning(pluginsToClean);

            // 6. Create cancellation token (thread-safe)
            CancellationTokenSource cts;
            lock (_ctsLock)
            {
                _cleaningCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts = _cleaningCts;
            }

            // Get timeout for retry prompts
            var timeoutSeconds = preflightPlan.CleaningTimeoutSeconds;
            if (timeoutSeconds <= 0) timeoutSeconds = 300;

            // 6b. Initialize backup session if backup is enabled
            sessionDir = await backupCoordinator.BeginSessionAsync(preflightPlan, cts.Token).ConfigureAwait(false);

            // 7. Process plugins SEQUENTIALLY (CRITICAL!)
            foreach (var plugin in pluginsToClean)
            {
                if (cts.Token.IsCancellationRequested)
                {
                    logger.Information("Cleaning cancelled by user");
                    wasCancelled = true;
                    break;
                }

                logger.Information("Processing plugin: {Plugin}", plugin.FileName);
                stateService.UpdateState(s => s with
                {
                    CurrentPlugin = plugin.FileName
                });

                // Backup this plugin before xEdit processes it
                if (sessionDir is not null)
                {
                    var outcome = await backupCoordinator.RunPluginBackupAsync(plugin, sessionDir, onBackupFailure, cts.Token).ConfigureAwait(false);
                    var stopAfterBackupOutcome = false;
                    switch (outcome.Kind)
                    {
                        case PluginBackupOutcomeKind.Canceled:
                        case PluginBackupOutcomeKind.UserSkipped:
                            if (outcome.SkippedResult is not null)
                            {
                                pluginResults.Add(outcome.SkippedResult);
                                stateService.AddDetailedCleaningResult(outcome.SkippedResult);
                            }

                            if (outcome.Kind == PluginBackupOutcomeKind.UserSkipped)
                            {
                                stateService.UpdateState(s => s with
                                {
                                    SkippedPlugins = new HashSet<string>(s.SkippedPlugins)
                                        { plugin.FileName }
                                        .ToFrozenSet(StringComparer.Ordinal)
                                });
                            }

                            if (cts.Token.IsCancellationRequested)
                            {
                                wasCancelled = true;
                                stopAfterBackupOutcome = true;
                                break;
                            }

                            continue;

                        case PluginBackupOutcomeKind.AbortSession:
                            // R-09: full AbortSession branch -- verbatim from CleaningOrchestrator.cs:333-362.
                            // Step 1: best-effort partial metadata write so the abandoned session has a manifest.
                            await backupCoordinator
                                .WritePartialMetadataAsync(sessionDir, gameType, backupEntries, ct: CancellationToken.None)
                                .ConfigureAwait(false);

                            // Step 2: mark the session as cancelled so finalization classifies correctly.
                            wasCancelled = true;

                            // Step 3: build the session result -- preserve every field that the legacy code did
                            // (StartTime, EndTime, GameType, PluginResults, BackupCleanup, WasCancelled).
                            var abortedSessionResult = new CleaningSessionResult
                            {
                                StartTime = startTime,
                                EndTime = DateTime.Now,
                                GameType = gameType,
                                PluginResults = pluginResults,
                                BackupCleanup = backupCleanup,
                                WasCancelled = wasCancelled
                            };

                            // Step 4: publish the session result to the state service (UI sees final state).
                            stateService.FinishCleaningWithResults(abortedSessionResult);

                            // Step 5: REQUIRED -- log the summary; legacy code does this and tests may pin it.
                            LogSessionSummary(abortedSessionResult);

                            // Step 6: exit the StartCleaningAsync method.
                            return;

                        case PluginBackupOutcomeKind.Succeeded:
                            if (outcome.Entry is not null) backupEntries.Add(outcome.Entry);
                            break;

                        case PluginBackupOutcomeKind.ContinueWithoutBackup:
                            // proceed to xEdit; no entry added
                            break;
                    }

                    if (stopAfterBackupOutcome)
                    {
                        break;
                    }
                }

                // Clean plugin with retry logic for timeouts
                var pluginStopwatch = Stopwatch.StartNew();
                var xEditDir = preflightPlan.XEditDirectory;
                CleaningResult result;
                var attemptNumber = 0;
                long mainLogOffset = 0;
                long exceptionLogOffset = 0;

                do
                {
                    attemptNumber++;

                    if (attemptNumber > 1)
                    {
                        logger.Information("Retry attempt {Attempt} for plugin: {Plugin}",
                            attemptNumber, plugin.FileName);
                    }

                    // Capture log offsets before each xEdit launch (per D-03: per-plugin, inside retry loop)
                    var mainLogPath = logFileService.GetLogFilePath(xEditDir, gameType);
                    var exceptionLogPath = logFileService.GetExceptionLogFilePath(xEditDir, gameType);
                    mainLogOffset = logFileService.CaptureOffset(mainLogPath);
                    exceptionLogOffset = logFileService.CaptureOffset(exceptionLogPath);

                    result = await cleaningService.CleanPluginAsync(
                        plugin,
                        cts.Token,
                        onProcessStarted: proc =>
                        {
                            // Wave 3: keep the inline lambda; Wave 4 hoists it into the runner via delegate.
                            terminationCoordinator.AttachProcess(proc);
                        }).ConfigureAwait(false);

                    // If timed out and callback provided, ask user if they want to retry
                    if (result.TimedOut && onTimeout != null && attemptNumber < maxRetryAttempts)
                    {
                        var shouldRetry = await onTimeout(plugin.FileName, timeoutSeconds, attemptNumber)
                            .ConfigureAwait(false);

                        if (!shouldRetry)
                        {
                            logger.Information("User chose not to retry plugin: {Plugin}", plugin.FileName);
                            break;
                        }

                        logger.Information("User chose to retry plugin: {Plugin}", plugin.FileName);
                    }
                    else
                    {
                        break; // No timeout or no callback or max attempts reached
                    }
                } while (true);

                pluginStopwatch.Stop();

                terminationCoordinator.DetachProcess();

                // Read log content using offset-based API (replaces legacy timestamp-based ReadLogFileAsync)
                CleaningStatistics? logStats = null;
                string? logParseWarning = null;
                var finalStatus = result.Status;

                // Guard: only read logs if process was not killed/cancelled (per D-04)
                if (!terminationCoordinator.ProcessMayStillBeRunning && !terminationCoordinator.IsStopRequested && result.Status != CleaningStatus.Skipped)
                {
                    var logResult = await logFileService.ReadLogContentAsync(
                        xEditDir, gameType, mainLogOffset, exceptionLogOffset, cts.Token).ConfigureAwait(false);

                    if (logResult.Warning != null)
                    {
                        logger.Warning("Log read warning for {Plugin}: {Warning}", plugin.FileName, logResult.Warning);
                        logParseWarning = logResult.Warning;
                    }

                    // PAR-01: Apply existing regex patterns to log file content
                    if (logResult.LogLines.Count > 0)
                    {
                        logStats = outputParser.ParseOutput(logResult.LogLines);
                        logger.Debug("Parsed log file stats for {Plugin}: {Removed} ITM, {Undeleted} UDR",
                            plugin.FileName, logStats.ItemsRemoved, logStats.ItemsUndeleted);

                        // PAR-02: Nothing-to-clean detection (per D-05)
                        var hasCompletionLine = logResult.LogLines.Any(outputParser.IsCompletionLine);
                        if (hasCompletionLine && logStats is { ItemsRemoved: 0, ItemsUndeleted: 0, ItemsSkipped: 0, PartialFormsCreated: 0 })
                        {
                            finalStatus = CleaningStatus.AlreadyClean;
                        }
                    }

                    // PAR-03: Exception log surfacing (per D-06)
                    if (logResult.ExceptionContent != null)
                    {
                        logParseWarning = logResult.ExceptionContent;
                        finalStatus = CleaningStatus.Failed;
                        logger.Warning("xEdit exception log for {Plugin}: {Content}",
                            plugin.FileName, logResult.ExceptionContent);
                    }
                }
                else if (terminationCoordinator.IsStopRequested || terminationCoordinator.ProcessMayStillBeRunning)
                {
                    logParseWarning = "xEdit was terminated -- no log available";
                }

                // Create detailed result
                var pluginCleaningResult = new PluginCleaningResult
                {
                    PluginName = plugin.FileName,
                    Status = finalStatus,
                    Success = result.Success,
                    Message = result.TimedOut && attemptNumber >= maxRetryAttempts
                        ? $"Cleaning timed out after {attemptNumber} attempts."
                        : result.Message,
                    Duration = pluginStopwatch.Elapsed,
                    Statistics = logStats,
                    LogParseWarning = logParseWarning
                };
                pluginResults.Add(pluginCleaningResult);

                // Update detailed results in state
                stateService.AddDetailedCleaningResult(pluginCleaningResult);

                logger.Information(
                    "Plugin {Plugin} processed: {Status} - {Message}",
                    plugin.FileName,
                    result.Status,
                    result.Message);
            }

            // 7b. Write backup session metadata and run retention cleanup
            if (sessionDir != null && backupEntries.Count > 0)
            {
                backupCleanup = await backupCoordinator.FinalizeSessionAsync(
                        sessionDir,
                        gameType,
                        backupEntries,
                        preflightPlan.BackupMaxSessions,
                        cts.Token)
                    .ConfigureAwait(false);
            }

            // 8. Create and store session result
            var sessionResult = new CleaningSessionResult
            {
                StartTime = startTime,
                EndTime = DateTime.Now,
                GameType = gameType,
                WasCancelled = wasCancelled,
                PluginResults = pluginResults,
                BackupCleanup = backupCleanup
            };

            stateService.FinishCleaningWithResults(sessionResult);
            LogSessionSummary(sessionResult);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not an error -- preserve partial results
            logger.Information("Cleaning workflow cancelled");

            // Write partial backup metadata if any backups were made
            if (sessionDir != null && backupEntries.Count > 0)
            {
                await backupCoordinator.WritePartialMetadataAsync(
                        sessionDir,
                        gameType,
                        backupEntries,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }

            var sessionResult = new CleaningSessionResult
            {
                StartTime = startTime,
                EndTime = DateTime.Now,
                GameType = gameType,
                WasCancelled = true,
                PluginResults = pluginResults,
                BackupCleanup = backupCleanup
            };

            stateService.FinishCleaningWithResults(sessionResult);
            LogSessionSummary(sessionResult);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error during cleaning workflow");

            // Still create a session result even on error
            var sessionResult = new CleaningSessionResult
            {
                StartTime = startTime,
                EndTime = DateTime.Now,
                GameType = gameType,
                WasCancelled = wasCancelled,
                PluginResults = pluginResults,
                BackupCleanup = backupCleanup
            };

            stateService.FinishCleaningWithResults(sessionResult);
            LogSessionSummary(sessionResult);
            throw;
        }
        finally
        {
            terminationCoordinator.ResetForNewSession();

            lock (_ctsLock)
            {
                _cleaningCts?.Dispose();
                _cleaningCts = null;
            }
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

    public async Task<StopCleaningResult> StopCleaningAsync()
    {
        // Cancel session CTS first so the foreach loop exits.
        CancellationTokenSource? cts;
        lock (_ctsLock)
        {
            cts = _cleaningCts;
        }

        try
        {
            if (cts is not null)
            {
                _ = cts.CancelAsync();
            }
        }
        catch (ObjectDisposedException)
        {
            // Already disposed -- fine
        }

        return await terminationCoordinator.StopAsync().ConfigureAwait(false);
    }

    public async Task<StopCleaningResult> ForceStopCleaningAsync()
    {
        // Cancel session CTS first so the foreach loop exits.
        CancellationTokenSource? cts;
        lock (_ctsLock)
        {
            cts = _cleaningCts;
        }

        try
        {
            if (cts is not null)
            {
                _ = cts.CancelAsync();
            }
        }
        catch (ObjectDisposedException)
        {
            // Already disposed -- fine
        }

        return await terminationCoordinator.ForceStopAsync().ConfigureAwait(false);
    }

    public StopCleaningResult MarkLeftRunningByUser() => terminationCoordinator.MarkLeftRunningByUser();

    public async Task<List<DryRunResult>> RunDryRunAsync(CancellationToken ct = default)
    {
        var results = new List<DryRunResult>();

        logger.Information("Starting dry-run preview");

        var preflightPlan = await preflight.PrepareAsync(ct).ConfigureAwait(false);

        foreach (var row in preflightPlan.PluginRows)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(ToDryRunResult(row));
        }

        logger.Information("Dry-run preview complete: {WillClean} will clean, {WillSkip} will skip",
            results.Count(r => r.Status == DryRunStatus.WillClean),
            results.Count(r => r.Status == DryRunStatus.WillSkip));

        return results;
    }

    /// <summary>
    /// Converts a shared preflight row to the existing dry-run preview row contract.
    /// </summary>
    private static DryRunResult ToDryRunResult(PreflightPluginRow row)
    {
        if (row.Decision == PreflightDecision.Clean)
        {
            return new DryRunResult(row.Plugin.FileName, DryRunStatus.WillClean, "Ready for cleaning");
        }

        return new DryRunResult(
            row.Plugin.FileName,
            DryRunStatus.WillSkip,
            row.SkipReason switch
            {
                PreflightSkipReason.NotSelected => "Not selected",
                PreflightSkipReason.InSkipList => "In skip list",
                PreflightSkipReason.FileNotFound => "File not found",
                PreflightSkipReason.Unreadable => "File is unreadable",
                PreflightSkipReason.ZeroByte => "Zero-byte file",
                PreflightSkipReason.MalformedEntry => "Malformed file name",
                PreflightSkipReason.InvalidExtension => "Invalid file extension",
                _ => "Skipped"
            });
    }

    /// <summary>
    /// Preserves the legacy real-run failure when all selected plugins fail file validation.
    /// </summary>
    private static void ThrowIfNoValidPluginsAfterFileValidation(CleaningPreflightPlan plan)
    {
        if (plan.PluginRows.Any(r => r.Decision == PreflightDecision.Clean))
        {
            return;
        }

        var pathFailures = plan.PluginRows
            .Where(r => IsFileValidationReason(r.SkipReason))
            .Select(r => $"{r.Plugin.FileName} ({MapReasonToPluginWarningLabel(r.SkipReason!.Value)})")
            .ToList();

        if (pathFailures.Count == 0)
        {
            return;
        }

        var summary = $"{pathFailures.Count} plugin(s) not found or unreadable: {string.Join(", ", pathFailures)}";
        throw new InvalidOperationException($"No valid plugins to clean. {summary}");
    }

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
    /// Converts a file-validation preflight reason to the legacy warning label used in exception summaries.
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

    private void LogSessionSummary(CleaningSessionResult session)
    {
        logger.Information("=== AutoQAC Session Complete ===");
        logger.Information("Duration: {Duration}", session.TotalDuration.ToString(@"hh\:mm\:ss"));
        logger.Information(
            "Plugins processed: {Total} (Cleaned: {Cleaned}, Skipped: {Skipped}, Failed: {Failed})",
            session.TotalPlugins,
            session.CleanedCount,
            session.SkippedCount,
            session.FailedCount);
        logger.Information(
            "ITMs removed: {Itm}, UDRs fixed: {Udr}, Navmeshes: {Nav}",
            session.TotalItemsRemoved,
            session.TotalItemsUndeleted,
            session.TotalPartialFormsCreated);

        if (session.WasCancelled)
        {
            logger.Information("Session was cancelled by user");
        }
    }

    public void Dispose()
    {
        lock (_ctsLock)
        {
            _cleaningCts?.Dispose();
            _cleaningCts = null;
        }
    }
}
