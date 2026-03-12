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
    ICleaningService cleaningService,
    IPluginValidationService pluginService,
    IGameDetectionService gameDetectionService,
    IStateService stateService,
    IConfigurationService configService,
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

    private CancellationTokenSource? _cleaningCts;
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

        // Reset stop flags at the start of each cleaning session
        _isStopRequested = false;
        _lastTerminationResult = null;

        try
        {
            logger.Information("Starting cleaning workflow");

            // Clean orphaned processes before starting
            await processService.CleanOrphanedProcessesAsync(ct).ConfigureAwait(false);

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

            // 3. Detect Game (if unknown) and Update State
            if (config.CurrentGameType == GameType.Unknown)
            {
                var detectedGame =
                    gameDetectionService.DetectFromExecutable(config.XEditExecutablePath ?? string.Empty);

                if (detectedGame == GameType.Unknown && !string.IsNullOrEmpty(config.LoadOrderPath))
                {
                    detectedGame = await gameDetectionService.DetectFromLoadOrderAsync(config.LoadOrderPath, ct)
                        .ConfigureAwait(false);
                }

                if (detectedGame != GameType.Unknown)
                {
                    logger.Information("Detected game type: {GameType}", detectedGame);
                    stateService.UpdateState(s => s with { CurrentGameType = detectedGame });
                    config = stateService.CurrentState; // Refresh local config
                }
                else
                {
                    logger.Error(null, "Cannot determine game type. Cleaning blocked for safety -- skip lists cannot be applied without a known game type.");
                    throw new InvalidOperationException(
                        "Cannot start cleaning: game type could not be determined. " +
                        "Please select a game type in Settings, or ensure the xEdit executable name matches a supported game.");
                }
            }

            gameType = config.CurrentGameType;

            // 3b. Detect game variant for skip list handling
            var pluginNames = allPlugins.Select(p => p.FileName).ToList();
            var gameVariant = gameDetectionService.DetectVariant(gameType, pluginNames);
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

                if (!System.IO.File.Exists(mo2Path))
                {
                    throw new InvalidOperationException(
                        $"MO2 mode is enabled but MO2 executable not found at '{mo2Path}'. " +
                        "Check MO2 executable path in Settings, or disable MO2 mode if not using Mod Organizer 2.");
                }
            }

            List<PluginInfo> pluginsToClean;
            if (disableSkipLists)
            {
                logger.Debug("Skip lists disabled by user setting - cleaning all selected plugins");
                pluginsToClean = allPlugins
                    .Where(p => p.IsSelected)
                    .Select(p => p with { DetectedGameType = gameType })
                    .ToList();
            }
            else if (gameType != GameType.Unknown)
            {
                var skipList = await configService.GetSkipListAsync(gameType, gameVariant, ct)
                    .ConfigureAwait(false);
                var skipSet = new HashSet<string>(skipList, StringComparer.OrdinalIgnoreCase);

                pluginsToClean = allPlugins
                    .Select(p => p with { IsInSkipList = skipSet.Contains(p.FileName), DetectedGameType = gameType })
                    .Where(p => p is { IsInSkipList: false, IsSelected: true })
                    .ToList();
            }
            else
            {
                pluginsToClean = allPlugins.Where(p => p is { IsInSkipList: false, IsSelected: true }).ToList();
            }

            // 4b. File-existence validation (skipped in MO2 mode -- MO2 VFS resolves paths at runtime)
            if (!isMo2Mode)
            {
                var pathFailures = new List<string>();
                foreach (var plugin in pluginsToClean)
                {
                    var warning = pluginService.ValidatePluginFile(plugin);
                    if (warning != PluginWarningKind.None)
                    {
                        pathFailures.Add($"{plugin.FileName} ({warning})");
                    }
                }

                if (pathFailures.Count > 0)
                {
                    var summary = $"{pathFailures.Count} plugin(s) not found or unreadable: {string.Join(", ", pathFailures)}";
                    logger.Warning(summary);

                    pluginsToClean = pluginsToClean
                        .Where(p => pluginService.ValidatePluginFile(p) == PluginWarningKind.None)
                        .ToList();

                    if (pluginsToClean.Count == 0)
                    {
                        throw new InvalidOperationException(
                            $"No valid plugins to clean. {summary}");
                    }
                }
            }
            else
            {
                logger.Debug("MO2 mode active -- skipping file-existence validation (MO2 VFS resolves paths at xEdit runtime)");
            }

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
            var timeoutSeconds = stateService.CurrentState.CleaningTimeout;
            if (timeoutSeconds <= 0) timeoutSeconds = 300;

            // 6b. Initialize backup session if backup is enabled
            var backupEnabled = userConfig.Backup.Enabled;

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
                    var backupResult = backupService.BackupPlugin(plugin, sessionDir);
                    if (!backupResult.Success)
                    {
                        if (onBackupFailure != null)
                        {
                            var choice = await onBackupFailure(plugin.FileName, backupResult.Error!).ConfigureAwait(false);
                            switch (choice)
                            {
                                case BackupFailureChoice.SkipPlugin:
                                    logger.Information("User chose to skip plugin after backup failure: {Plugin}", plugin.FileName);
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
                                    return;
                                case BackupFailureChoice.ContinueWithoutBackup:
                                    logger.Information("User chose to continue without backup for: {Plugin}", plugin.FileName);
                                    break;
                            }
                        }
                        else
                        {
                            logger.Warning("Backup failed for {Plugin}: {Error}. No callback, continuing without backup.",
                                plugin.FileName, backupResult.Error ?? "Unknown error");
                        }
                    }
                    else
                    {
                        backupEntries.Add(new BackupPluginEntry
                        {
                            FileName = plugin.FileName,
                            OriginalPath = plugin.FullPath,
                            FileSizeBytes = backupResult.FileSizeBytes
                        });
                    }
                }

                // Progress reporting
                var progress = new Progress<string>(output =>
                {
                    logger.Debug("xEdit output: {Output}", output);
                });

                // Clean plugin with retry logic for timeouts
                var pluginStopwatch = Stopwatch.StartNew();
                var pluginStartTime = DateTime.UtcNow;
                CleaningResult result;
                var attemptNumber = 0;

                do
                {
                    attemptNumber++;

                    if (attemptNumber > 1)
                    {
                        logger.Information("Retry attempt {Attempt} for plugin: {Plugin}",
                            attemptNumber, plugin.FileName);
                    }

                    result = await cleaningService.CleanPluginAsync(
                        plugin,
                        progress,
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

                // Attempt to enrich stats from the xEdit log file (preferred over stdout stats)
                var logStats = result.Statistics;
                string? logParseWarning = null;

                if (result is { Success: true, Status: CleaningStatus.Cleaned })
                {
                    var xEditPath = config.XEditExecutablePath ?? string.Empty;
                    var (logLines, logError) = await logFileService.ReadLogFileAsync(
                        xEditPath, pluginStartTime, cts.Token).ConfigureAwait(false);

                    if (logError != null)
                    {
                        logger.Warning("Log parse warning for {Plugin}: {Warning}", plugin.FileName, logError);
                        logParseWarning = logError;
                        // Keep stdout-based stats as fallback
                    }
                    else if (logLines.Count > 0)
                    {
                        // Prefer log-file-based stats over stdout stats
                        logStats = outputParser.ParseOutput(logLines);
                        logger.Debug("Parsed log file stats for {Plugin}: {Removed} ITM, {Undeleted} UDR",
                            plugin.FileName, logStats.ItemsRemoved, logStats.ItemsUndeleted);
                    }
                }

                // Create detailed result
                var pluginCleaningResult = new PluginCleaningResult
                {
                    PluginName = plugin.FileName,
                    Status = result.Status,
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
                backupService.CleanupOldSessions(backupRoot, userConfig.Backup.MaxSessions, sessionDir);
                logger.Information("Backup session complete: {Count} plugins backed up", backupEntries.Count);
            }

            // 8. Create and store session result
            var sessionResult = new CleaningSessionResult
            {
                StartTime = startTime,
                EndTime = DateTime.Now,
                GameType = gameType,
                WasCancelled = wasCancelled,
                PluginResults = pluginResults
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
                PluginResults = pluginResults
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
                PluginResults = pluginResults
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
        }
    }

    public async Task StopCleaningAsync()
    {
        if (_isStopRequested)
        {
            // Path B: Second click during grace period -- immediate force kill, no prompt
            logger.Information("[Termination] Second stop requested -- escalating to force kill");
            await ForceStopCleaningAsync().ConfigureAwait(false);
            return;
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
            return;
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
                    return;
                }

                if (!proc.HasExited)
                {
                    var result = await processService.TerminateProcessAsync(proc, forceKill: false, ct: CancellationToken.None)
                        .ConfigureAwait(false);

                    if (result == TerminationResult.GracePeriodExpired)
                    {
                        // Path A: Grace period expired naturally, user hasn't clicked again.
                        // Store result so the ViewModel can react and prompt the user.
                        _lastTerminationResult = result;
                    }
                }
            }
            catch (InvalidOperationException)
            {
                logger.Debug("[Termination] Process already exited during graceful stop");
            }
        }
    }

    public async Task ForceStopCleaningAsync()
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
                    return;
                }

                if (!proc.HasExited)
                {
                    await processService.TerminateProcessAsync(proc, forceKill: true, ct: CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            catch (InvalidOperationException)
            {
                logger.Debug("[Termination] Process already exited during force stop");
            }
        }
    }

    public async Task<List<DryRunResult>> RunDryRunAsync(CancellationToken ct = default)
    {
        var results = new List<DryRunResult>();

        logger.Information("Starting dry-run preview");

        // Flush any pending config saves to ensure config is current
        await configService.FlushPendingSavesAsync(ct).ConfigureAwait(false);

        // Get plugins from state (same as StartCleaningAsync step 2)
        var config = stateService.CurrentState;
        var allPlugins = config.PluginsToClean;

        // Detect game type locally (same as StartCleaningAsync step 3) -- do NOT update state
        var gameType = config.CurrentGameType;
        if (gameType == GameType.Unknown)
        {
            var detectedGame =
                gameDetectionService.DetectFromExecutable(config.XEditExecutablePath ?? string.Empty);

            if (detectedGame == GameType.Unknown && !string.IsNullOrEmpty(config.LoadOrderPath))
            {
                detectedGame = await gameDetectionService.DetectFromLoadOrderAsync(config.LoadOrderPath, ct)
                    .ConfigureAwait(false);
            }

            if (detectedGame != GameType.Unknown)
            {
                gameType = detectedGame;
            }
            else
            {
                logger.Error(null,
                    "Cannot determine game type for dry-run preview. Skip lists cannot be applied without a known game type.");
                throw new InvalidOperationException(
                    "Cannot start preview: game type could not be determined. " +
                    "Please select a game type in Settings, or ensure the xEdit executable name matches a supported game.");
            }
        }

        // Detect game variant (same as StartCleaningAsync step 3b)
        var pluginNames = allPlugins.Select(p => p.FileName).ToList();
        var gameVariant = gameDetectionService.DetectVariant(gameType, pluginNames);

        // Load user config for skip list and MO2 settings (same as StartCleaningAsync step 4)
        var userConfig = await configService.LoadUserConfigAsync(ct).ConfigureAwait(false);
        var disableSkipLists = userConfig.Settings.DisableSkipLists;
        var isMo2Mode = userConfig.Settings.Mo2Mode;

        // Build skip set
        HashSet<string>? skipSet = null;
        if (!disableSkipLists)
        {
            var skipList = await configService.GetSkipListAsync(gameType, gameVariant, ct)
                .ConfigureAwait(false);
            skipSet = new HashSet<string>(skipList, StringComparer.OrdinalIgnoreCase);
        }

        // Evaluate each plugin
        foreach (var plugin in allPlugins)
        {
            ct.ThrowIfCancellationRequested();

            // Not selected
            if (!plugin.IsSelected)
            {
                results.Add(new DryRunResult(plugin.FileName, DryRunStatus.WillSkip, "Not selected"));
                continue;
            }

            // Skip list filtering
            if (skipSet != null && skipSet.Contains(plugin.FileName))
            {
                results.Add(new DryRunResult(plugin.FileName, DryRunStatus.WillSkip, "In skip list"));
                continue;
            }

            // File-existence validation (skipped in MO2 mode)
            if (!isMo2Mode)
            {
                var enrichedPlugin = plugin with { DetectedGameType = gameType };
                var warning = pluginService.ValidatePluginFile(enrichedPlugin);
                if (warning != PluginWarningKind.None)
                {
                    var reason = warning switch
                    {
                        PluginWarningKind.NotFound => "File not found",
                        PluginWarningKind.Unreadable => "File is unreadable",
                        PluginWarningKind.ZeroByte => "Zero-byte file",
                        PluginWarningKind.MalformedEntry => "Malformed file name",
                        PluginWarningKind.InvalidExtension => "Invalid file extension",
                        _ => $"Validation failed ({warning})"
                    };
                    results.Add(new DryRunResult(plugin.FileName, DryRunStatus.WillSkip, reason));
                    continue;
                }
            }

            // Plugin is ready for cleaning
            results.Add(new DryRunResult(plugin.FileName, DryRunStatus.WillClean, "Ready for cleaning"));
        }

        logger.Information("Dry-run preview complete: {WillClean} will clean, {WillSkip} will skip",
            results.Count(r => r.Status == DryRunStatus.WillClean),
            results.Count(r => r.Status == DryRunStatus.WillSkip));

        return results;
    }

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
                !System.IO.File.Exists(config.LoadOrderPath))
            {
                return false;
            }
        }

        return await cleaningService.ValidateEnvironmentAsync(ct).ConfigureAwait(false);
    }

    private static bool RequiresFileLoadOrder(GameType gameType) => gameType switch
    {
        GameType.Fallout3 => true,
        GameType.FalloutNewVegas => true,
        GameType.Oblivion => true,
        _ => false
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
