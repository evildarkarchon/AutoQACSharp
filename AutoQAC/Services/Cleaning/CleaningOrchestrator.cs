using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameDetection;
using AutoQAC.Services.Monitoring;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.Backup;
using AutoQAC.Services.Process;
using AutoQAC.Services.State;

namespace AutoQAC.Services.Cleaning;

public sealed class CleaningOrchestrator(
    ICleaningPreflight preflight,
    ICleaningService cleaningService,
    IStateService stateService,
    ILoggingService logger,
    IProcessExecutionService processService,
    IXEditLogFileService logFileService,
    IXEditOutputParser outputParser,
    IBackupService backupService,
    IHangDetectionService hangDetection)
    : ICleaningOrchestrator, IDisposable
{
    private readonly Subject<bool> _hangDetected = new();
    private readonly object _ctsLock = new();
    private readonly object _processLock = new();
    private readonly object _backupOperationLock = new();

    private CancellationTokenSource? _cleaningCts;
    private CancellationTokenSource? _backupOperationCts;
    private volatile bool _isStopRequested;
    private System.Diagnostics.Process? _currentProcess;
    private TerminationResult? _lastTerminationResult;
    private IDisposable? _hangMonitorSubscription;

    public TerminationResult? LastTerminationResult => _lastTerminationResult;
    public IObservable<bool> HangDetected => _hangDetected.AsObservable();

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

        // Reset stop flags at the start of each cleaning session
        _isStopRequested = false;
        _lastTerminationResult = null;

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
            var backupEnabled = preflightPlan.BackupEnabled;
            var isMo2Mode = preflightPlan.IsMo2ModeActive;

            if (backupEnabled && !isMo2Mode)
            {
                // Derive data folder from the first plugin with a rooted FullPath
                var firstRootedPlugin = pluginsToClean.FirstOrDefault(
                    p => !string.IsNullOrEmpty(p.FullPath) && System.IO.Path.IsPathRooted(p.FullPath));

                if (firstRootedPlugin != null)
                {
                    var dataFolder = System.IO.Path.GetDirectoryName(firstRootedPlugin.FullPath)!;
                    var backupRoot = backupService.GetBackupRoot(dataFolder);
                    sessionDir = backupService.CreateSessionDirectory(backupRoot);
                    logger.Information("Backup session directory created: {SessionDir}", sessionDir);
                }
                else
                {
                    logger.Warning("Backup enabled but no plugins have rooted paths -- skipping backup initialization");
                }
            }
            else if (backupEnabled && isMo2Mode)
            {
                logger.Warning("Backup skipped in MO2 mode -- MO2 manages files through its virtual filesystem");
            }

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
                if (sessionDir != null)
                {
                    var backupResult = await BackupPluginAsync(plugin, sessionDir, cts.Token).ConfigureAwait(false);
                    if (backupResult.Status == BackupOperationStatus.Canceled)
                    {
                        logger.Information("Backup canceled for {Plugin}; skipping xEdit launch for this plugin", plugin.FileName);
                        var skippedResult = new PluginCleaningResult
                        {
                            PluginName = plugin.FileName,
                            Status = CleaningStatus.Skipped,
                            Success = false,
                            Message = "Backup canceled"
                        };
                        pluginResults.Add(skippedResult);
                        stateService.AddDetailedCleaningResult(skippedResult);

                        if (cts.Token.IsCancellationRequested)
                        {
                            wasCancelled = true;
                            break;
                        }

                        continue;
                    }

                    if (backupResult.Status != BackupOperationStatus.Complete)
                    {
                        if (onBackupFailure != null)
                        {
                            var choice = await onBackupFailure(plugin.FileName, GetBackupFailureReasonText(backupResult))
                                .ConfigureAwait(false);
                            switch (choice)
                            {
                                case BackupFailureChoice.SkipPlugin:
                                    logger.Information("User chose to skip plugin after backup failure: {Plugin}", plugin.FileName);
                                    var skippedResult = new PluginCleaningResult
                                    {
                                        PluginName = plugin.FileName,
                                        Status = CleaningStatus.Skipped,
                                        Success = false,
                                        Message = "Backup failed - skipped by user"
                                    };
                                    pluginResults.Add(skippedResult);
                                    stateService.AddDetailedCleaningResult(skippedResult);
                                    stateService.UpdateState(s => s with
                                    {
                                        SkippedPlugins = new HashSet<string>(s.SkippedPlugins)
                                            { plugin.FileName }
                                            .ToFrozenSet(StringComparer.Ordinal)
                                    });
                                    continue;
                                case BackupFailureChoice.AbortSession:
                                    logger.Information("User chose to abort session after backup failure for: {Plugin}", plugin.FileName);
                                    // Write partial metadata before returning
                                    if (backupEntries.Count > 0)
                                    {
                                        var partialSession = new BackupSession
                                        {
                                            Timestamp = DateTime.UtcNow,
                                            GameType = gameType.ToString(),
                                            SessionDirectory = sessionDir,
                                            Plugins = backupEntries
                                        };
                                        await backupService.WriteSessionMetadataAsync(sessionDir, partialSession, cts.Token).ConfigureAwait(false);
                                    }

                                    wasCancelled = true;
                                    var abortSessionResult = new CleaningSessionResult
                                    {
                                        StartTime = startTime,
                                        EndTime = DateTime.Now,
                                        GameType = gameType,
                                        WasCancelled = true,
                                        PluginResults = pluginResults,
                                        BackupCleanup = backupCleanup
                                    };

                                    // Abort exits before the normal end-of-method finalization path, so emit completion state here.
                                    stateService.FinishCleaningWithResults(abortSessionResult);
                                    LogSessionSummary(abortSessionResult);
                                    return;
                                case BackupFailureChoice.ContinueWithoutBackup:
                                    logger.Information("User chose to continue without backup for: {Plugin}", plugin.FileName);
                                    break;
                            }
                        }
                        else
                        {
                            logger.Warning("Backup failed for {Plugin}: {Reason}. No callback, continuing without backup.",
                                plugin.FileName, GetBackupFailureReasonText(backupResult));
                        }
                    }
                    else
                    {
                        backupEntries.Add(new BackupPluginEntry
                        {
                            FileName = plugin.FileName,
                            OriginalPath = plugin.FullPath,
                            FileSizeBytes = backupResult.TotalBytes ?? backupResult.BytesCopied
                        });
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
                            lock (_processLock)
                            {
                                _currentProcess = proc;
                            }

                            StartHangMonitoring(proc);
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

                // Stop hang monitoring and dismiss any visible warning
                _hangMonitorSubscription?.Dispose();
                _hangMonitorSubscription = null;
                _hangDetected.OnNext(false);

                // Clear the current process reference after plugin is done
                lock (_processLock)
                {
                    _currentProcess = null;
                }

                // Read log content using offset-based API (replaces legacy timestamp-based ReadLogFileAsync)
                CleaningStatistics? logStats = null;
                string? logParseWarning = null;
                var finalStatus = result.Status;

                // Guard: only read logs if process was not killed/cancelled (per D-04)
                if (!MayProcessStillBeRunning(_lastTerminationResult) && !_isStopRequested && result.Status != CleaningStatus.Skipped)
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
                else if (_isStopRequested || MayProcessStillBeRunning(_lastTerminationResult))
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
                var backupSession = new BackupSession
                {
                    Timestamp = DateTime.UtcNow,
                    GameType = gameType.ToString(),
                    SessionDirectory = sessionDir,
                    Plugins = backupEntries
                };
                await backupService.WriteSessionMetadataAsync(sessionDir, backupSession, cts.Token).ConfigureAwait(false);

                var backupRoot = System.IO.Path.GetDirectoryName(sessionDir)!;
                backupCleanup = await CleanupOldSessionsAsync(
                        backupRoot,
                        preflightPlan.BackupMaxSessions,
                        sessionDir,
                        cts.Token)
                    .ConfigureAwait(false);
                logger.Information("Backup session complete: {Count} plugins backed up", backupEntries.Count);
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
                try
                {
                    var partialBackupSession = new BackupSession
                    {
                        Timestamp = DateTime.UtcNow,
                        GameType = gameType.ToString(),
                        SessionDirectory = sessionDir,
                        Plugins = backupEntries
                    };
                    await backupService.WriteSessionMetadataAsync(
                        sessionDir,
                        partialBackupSession,
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception backupEx)
                {
                    logger.Warning("Failed to write partial backup metadata after cancellation: {Error}", backupEx.Message);
                }
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
            // Reset stop flags
            _isStopRequested = false;
            stateService.SetTerminating(false);
            _lastTerminationResult = null;

            // Stop hang monitoring
            _hangMonitorSubscription?.Dispose();
            _hangMonitorSubscription = null;
            _hangDetected.OnNext(false);

            // Clear process reference
            lock (_processLock)
            {
                _currentProcess = null;
            }

            lock (_ctsLock)
            {
                _cleaningCts?.Dispose();
                _cleaningCts = null;
            }

            ClearBackupOperationCts();
        }
    }

    /// <inheritdoc />
    public Task CancelBackupOperationAsync()
    {
        lock (_processLock)
        {
            if (_currentProcess is not null)
            {
                return Task.CompletedTask;
            }
        }

        CancellationTokenSource? cts;
        lock (_backupOperationLock)
        {
            cts = _backupOperationCts;
        }

        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The operation completed between reading the CTS and attempting cancellation.
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs a single-plugin backup with a file-operation CTS and publishes progress through state.
    /// </summary>
    private async Task<BackupCreateResult> BackupPluginAsync(
        PluginInfo plugin,
        string sessionDir,
        CancellationToken sessionToken)
    {
        var operationCts = CreateBackupOperationCts(sessionToken);
        var progress = new Progress<BackupCopyProgress>(copyProgress =>
        {
            stateService.SetBackupOperation(new BackupOperationState
            {
                Kind = BackupOperationKind.Backup,
                Label = $"Backing up: {plugin.FileName}",
                FileName = copyProgress.FileName,
                FilesCompleted = 0,
                TotalFiles = 1,
                BytesCopied = copyProgress.BytesCopied,
                TotalBytes = copyProgress.TotalBytes,
                IsActive = true,
                CanCancel = true
            });
        });

        try
        {
            stateService.SetBackupOperation(new BackupOperationState
            {
                Kind = BackupOperationKind.Backup,
                Label = $"Backing up: {plugin.FileName}",
                FileName = plugin.FileName,
                FilesCompleted = 0,
                TotalFiles = 1,
                IsActive = true,
                CanCancel = true
            });

            return await backupService.BackupPluginAsync(plugin, sessionDir, progress, operationCts.Token)
                .ConfigureAwait(false);
        }
        finally
        {
            stateService.ClearBackupOperation();
            ClearBackupOperationCts(operationCts);
        }
    }

    /// <summary>
    /// Runs retention cleanup before final session completion and publishes cleanup progress through state.
    /// </summary>
    private async Task<BackupRetentionCleanupResult> CleanupOldSessionsAsync(
        string backupRoot,
        int maxSessionCount,
        string currentSessionDir,
        CancellationToken sessionToken)
    {
        var operationCts = CreateBackupOperationCts(sessionToken);
        var progress = new Progress<BackupCopyProgress>(copyProgress =>
        {
            stateService.SetBackupOperation(new BackupOperationState
            {
                Kind = BackupOperationKind.RetentionCleanup,
                Label = "Cleaning up old backups",
                FileName = copyProgress.FileName,
                FilesCompleted = copyProgress.FilesCompleted,
                TotalFiles = copyProgress.TotalFiles,
                BytesCopied = copyProgress.BytesCopied,
                TotalBytes = copyProgress.TotalBytes,
                IsActive = true,
                CanCancel = true
            });
        });

        try
        {
            stateService.SetBackupOperation(new BackupOperationState
            {
                Kind = BackupOperationKind.RetentionCleanup,
                Label = "Cleaning up old backups",
                IsActive = true,
                CanCancel = true
            });

            return await backupService.CleanupOldSessionsAsync(
                    backupRoot,
                    maxSessionCount,
                    currentSessionDir,
                    progress,
                    operationCts.Token)
                .ConfigureAwait(false);
        }
        finally
        {
            stateService.ClearBackupOperation();
            ClearBackupOperationCts(operationCts);
        }
    }

    /// <summary>
    /// Creates the current non-xEdit operation CTS linked to the overall cleaning session.
    /// </summary>
    private CancellationTokenSource CreateBackupOperationCts(CancellationToken sessionToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
        lock (_backupOperationLock)
        {
            _backupOperationCts = cts;
        }

        return cts;
    }

    /// <summary>
    /// Clears and disposes the active non-xEdit operation CTS when the owning operation exits.
    /// </summary>
    private void ClearBackupOperationCts(CancellationTokenSource? expected = null)
    {
        CancellationTokenSource? toDispose = null;
        lock (_backupOperationLock)
        {
            if (expected == null || ReferenceEquals(_backupOperationCts, expected))
            {
                toDispose = _backupOperationCts;
                _backupOperationCts = null;
            }
        }

        toDispose?.Dispose();
    }

    private static string GetBackupFailureReasonText(BackupCreateResult result) =>
        result.DisplayReason ?? result.FailureReason?.ToString() ?? "Backup failed";

    public async Task<StopCleaningResult> StopCleaningAsync()
    {
        if (_isStopRequested)
        {
            // Path B: Second click during grace period -- immediate force kill, no prompt
            logger.Information("[Termination] Second stop requested -- escalating to force kill");
            return await ForceStopCleaningAsync().ConfigureAwait(false);
        }

        _isStopRequested = true;
        stateService.SetTerminating(true);
        logger.Information("[Termination] Graceful stop requested");

        // Cancel the CTS (race-safe per PROC-04)
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
            logger.Debug("[Termination] CTS already disposed -- cleaning likely already finished");
            stateService.SetTerminating(false);
            return new StopCleaningResult(null, MayStillBeRunning: false);
        }

        // Attempt graceful termination on the current process
        System.Diagnostics.Process? proc;
        lock (_processLock)
        {
            proc = _currentProcess;
        }

        if (proc != null)
        {
            try
            {
                if (proc.Id == Environment.ProcessId)
                {
                    logger.Error(null, "[Termination] Refusing to terminate the AutoQAC process during stop request");
                    return new StopCleaningResult(null, MayStillBeRunning: false);
                }

                if (!proc.HasExited)
                {
                    var result = await processService.TerminateProcessAsync(proc, forceKill: false, ct: CancellationToken.None)
                        .ConfigureAwait(false);
                    _lastTerminationResult = result;

                    if (result == TerminationResult.GracePeriodExpired)
                    {
                        // Path A: Grace period expired naturally, user hasn't clicked again.
                        // Store result so the ViewModel can react and prompt the user.
                        _lastTerminationResult = result;
                    }

                    return ToStopCleaningResult(result);
                }
            }
            catch (InvalidOperationException)
            {
                logger.Debug("[Termination] Process already exited during graceful stop");
                _lastTerminationResult = TerminationResult.AlreadyExited;
                return ToStopCleaningResult(TerminationResult.AlreadyExited);
            }
        }

        return new StopCleaningResult(_lastTerminationResult, MayProcessStillBeRunning(_lastTerminationResult));
    }

    public async Task<StopCleaningResult> ForceStopCleaningAsync()
    {
        logger.Information("[Termination] Force stop requested -- killing process tree immediately");

        // Cancel the CTS if not already
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

        // Force kill the process tree
        System.Diagnostics.Process? proc;
        lock (_processLock)
        {
            proc = _currentProcess;
        }

        if (proc != null)
        {
            try
            {
                if (proc.Id == Environment.ProcessId)
                {
                    logger.Error(null, "[Termination] Refusing to terminate the AutoQAC process during force stop request");
                    return new StopCleaningResult(null, MayStillBeRunning: false);
                }

                if (!proc.HasExited)
                {
                    var result = await processService.TerminateProcessAsync(proc, forceKill: true, ct: CancellationToken.None)
                        .ConfigureAwait(false);
                    _lastTerminationResult = result;
                    return ToStopCleaningResult(result);
                }

                _lastTerminationResult = TerminationResult.AlreadyExited;
                return ToStopCleaningResult(TerminationResult.AlreadyExited);
            }
            catch (InvalidOperationException)
            {
                logger.Debug("[Termination] Process already exited during force stop");
                _lastTerminationResult = TerminationResult.AlreadyExited;
                return ToStopCleaningResult(TerminationResult.AlreadyExited);
            }
        }

        return new StopCleaningResult(_lastTerminationResult, MayProcessStillBeRunning(_lastTerminationResult));
    }

    public StopCleaningResult MarkLeftRunningByUser()
    {
        _lastTerminationResult = TerminationResult.LeftRunningByUser;
        logger.Information("[Termination] User left xEdit running after declining force termination");
        return ToStopCleaningResult(TerminationResult.LeftRunningByUser);
    }

    private static StopCleaningResult ToStopCleaningResult(TerminationResult? result) =>
        new(result, MayProcessStillBeRunning(result));

    private static bool MayProcessStillBeRunning(TerminationResult? result) =>
        result is TerminationResult.GracePeriodExpired or TerminationResult.LeftRunningByUser or TerminationResult.ForceKillFailed;

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

    private void StartHangMonitoring(System.Diagnostics.Process process)
    {
        // Ensure only one active monitor subscription per xEdit process lifecycle.
        _hangMonitorSubscription?.Dispose();
        _hangMonitorSubscription = hangDetection.MonitorProcess(process)
            .Subscribe(
                isHung => _hangDetected.OnNext(isHung),
                _ => { }, // Error: monitor completed unexpectedly
                () => { } // Completed: process exited
            );
    }

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
        _hangMonitorSubscription?.Dispose();
        _hangDetected.Dispose();

        lock (_ctsLock)
        {
            _cleaningCts?.Dispose();
            _cleaningCts = null;
        }
    }
}
