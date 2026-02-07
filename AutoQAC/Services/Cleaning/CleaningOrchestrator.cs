using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameDetection;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.Backup;
using AutoQAC.Services.Process;
using AutoQAC.Services.State;

namespace AutoQAC.Services.Cleaning;

public sealed class CleaningOrchestrator : ICleaningOrchestrator, IDisposable
{
    private readonly ICleaningService _cleaningService;
    private readonly IPluginValidationService _pluginService;
    private readonly IGameDetectionService _gameDetectionService;
    private readonly IStateService _stateService;
    private readonly IConfigurationService _configService;
    private readonly ILoggingService _logger;
    private readonly IProcessExecutionService _processService;
    private readonly IBackupService _backupService;
    private readonly IXEditLogFileService _logFileService;
    private readonly IXEditOutputParser _outputParser;
    private readonly object _ctsLock = new();
    private readonly object _processLock = new();

    private CancellationTokenSource? _cleaningCts;
    private volatile bool _isStopRequested;
    private System.Diagnostics.Process? _currentProcess;
    private TerminationResult? _lastTerminationResult;

    public TerminationResult? LastTerminationResult => _lastTerminationResult;

    public CleaningOrchestrator(
        ICleaningService cleaningService,
        IPluginValidationService pluginService,
        IGameDetectionService gameDetectionService,
        IStateService stateService,
        IConfigurationService configService,
        ILoggingService logger,
        IProcessExecutionService processService,
        IXEditLogFileService logFileService,
        IXEditOutputParser outputParser,
        IBackupService backupService)
    {
        _cleaningService = cleaningService;
        _pluginService = pluginService;
        _gameDetectionService = gameDetectionService;
        _stateService = stateService;
        _configService = configService;
        _logger = logger;
        _processService = processService;
        _logFileService = logFileService;
        _outputParser = outputParser;
        _backupService = backupService;
    }

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
            _logger.Information("Starting cleaning workflow");

            // Clean orphaned processes before starting
            await _processService.CleanOrphanedProcessesAsync(ct).ConfigureAwait(false);

            // Flush any pending config saves before launching xEdit
            // (per user decision: "Always force-flush pending config saves before launching xEdit")
            await _configService.FlushPendingSavesAsync(ct).ConfigureAwait(false);

            // 1. Validate configuration
            var isValid = await ValidateConfigurationAsync(ct).ConfigureAwait(false);
            if (!isValid)
            {
                _logger.Error(null, "Configuration is invalid, cannot start cleaning.");
                throw new InvalidOperationException("Configuration is invalid");
            }

            // 2. Get plugins from state (already loaded with skip list status)
            var config = _stateService.CurrentState;
            var allPlugins = config.PluginsToClean;

            // 3. Detect Game (if unknown) and Update State
            if (config.CurrentGameType == GameType.Unknown)
            {
                var detectedGame =
                    _gameDetectionService.DetectFromExecutable(config.XEditExecutablePath ?? string.Empty);

                if (detectedGame == GameType.Unknown && !string.IsNullOrEmpty(config.LoadOrderPath))
                {
                    detectedGame = await _gameDetectionService.DetectFromLoadOrderAsync(config.LoadOrderPath, ct)
                        .ConfigureAwait(false);
                }

                if (detectedGame != GameType.Unknown)
                {
                    _logger.Information($"Detected game type: {detectedGame}");
                    _stateService.UpdateState(s => s with { CurrentGameType = detectedGame });
                    config = _stateService.CurrentState; // Refresh local config
                }
                else
                {
                    _logger.Error(null, "Cannot determine game type. Cleaning blocked for safety -- skip lists cannot be applied without a known game type.");
                    throw new InvalidOperationException(
                        "Cannot start cleaning: game type could not be determined. " +
                        "Please select a game type in Settings, or ensure the xEdit executable name matches a supported game.");
                }
            }

            gameType = config.CurrentGameType;

            // 3b. Detect game variant for skip list handling
            var pluginNames = allPlugins.Select(p => p.FileName).ToList();
            var gameVariant = _gameDetectionService.DetectVariant(gameType, pluginNames);
            if (gameVariant != GameVariant.None)
            {
                _logger.Information("Detected game variant: {Variant}", gameVariant);
            }

            // 4. Apply skip list filtering (respecting DisableSkipLists setting)
            var userConfig = await _configService.LoadUserConfigAsync(ct).ConfigureAwait(false);
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
                _logger.Debug("Skip lists disabled by user setting - cleaning all selected plugins");
                pluginsToClean = allPlugins
                    .Where(p => p.IsSelected)
                    .Select(p => p with { DetectedGameType = gameType })
                    .ToList();
            }
            else if (gameType != GameType.Unknown)
            {
                var skipList = await _configService.GetSkipListAsync(gameType, gameVariant).ConfigureAwait(false) ?? [];
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
                    var warning = _pluginService.ValidatePluginFile(plugin);
                    if (warning != PluginWarningKind.None)
                    {
                        pathFailures.Add($"{plugin.FileName} ({warning})");
                    }
                }

                if (pathFailures.Count > 0)
                {
                    var summary = $"{pathFailures.Count} plugin(s) not found or unreadable: {string.Join(", ", pathFailures)}";
                    _logger.Warning(summary);

                    pluginsToClean = pluginsToClean
                        .Where(p => _pluginService.ValidatePluginFile(p) == PluginWarningKind.None)
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
                _logger.Debug("MO2 mode active -- skipping file-existence validation (MO2 VFS resolves paths at xEdit runtime)");
            }

            // 5. Update state - cleaning started
            _stateService.StartCleaning(pluginsToClean);

            // 6. Create cancellation token (thread-safe)
            CancellationTokenSource cts;
            lock (_ctsLock)
            {
                _cleaningCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts = _cleaningCts;
            }

            // Get timeout for retry prompts
            var timeoutSeconds = _stateService.CurrentState.CleaningTimeout;
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
                    var backupRoot = _backupService.GetBackupRoot(dataFolder);
                    sessionDir = _backupService.CreateSessionDirectory(backupRoot);
                    _logger.Information("Backup session directory created: {SessionDir}", sessionDir);
                }
                else
                {
                    _logger.Warning("Backup enabled but no plugins have rooted paths -- skipping backup initialization");
                }
            }
            else if (backupEnabled && isMo2Mode)
            {
                _logger.Warning("Backup skipped in MO2 mode -- MO2 manages files through its virtual filesystem");
            }

            // 7. Process plugins SEQUENTIALLY (CRITICAL!)
            foreach (var plugin in pluginsToClean)
            {
                if (cts.Token.IsCancellationRequested)
                {
                    _logger.Information("Cleaning cancelled by user");
                    wasCancelled = true;
                    break;
                }

                _logger.Information("Processing plugin: {Plugin}", plugin.FileName);
                _stateService.UpdateState(s => s with
                {
                    CurrentPlugin = plugin.FileName
                });

                // Backup this plugin before xEdit processes it
                if (sessionDir != null)
                {
                    var backupResult = _backupService.BackupPlugin(plugin, sessionDir);
                    if (!backupResult.Success)
                    {
                        if (onBackupFailure != null)
                        {
                            var choice = await onBackupFailure(plugin.FileName, backupResult.Error!).ConfigureAwait(false);
                            switch (choice)
                            {
                                case BackupFailureChoice.SkipPlugin:
                                    _logger.Information("User chose to skip plugin after backup failure: {Plugin}", plugin.FileName);
                                    _stateService.UpdateState(s => s with
                                    {
                                        SkippedPlugins = new HashSet<string>(s.SkippedPlugins) { plugin.FileName }
                                    });
                                    continue;
                                case BackupFailureChoice.AbortSession:
                                    _logger.Information("User chose to abort session after backup failure for: {Plugin}", plugin.FileName);
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
                                        await _backupService.WriteSessionMetadataAsync(sessionDir, partialSession, cts.Token).ConfigureAwait(false);
                                    }
                                    return;
                                case BackupFailureChoice.ContinueWithoutBackup:
                                    _logger.Information("User chose to continue without backup for: {Plugin}", plugin.FileName);
                                    break;
                            }
                        }
                        else
                        {
                            _logger.Warning("Backup failed for {Plugin}: {Error}. No callback, continuing without backup.",
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
                    _logger.Debug("xEdit output: {Output}", output);
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
                        _logger.Information("Retry attempt {Attempt} for plugin: {Plugin}",
                            attemptNumber, plugin.FileName);
                    }

                    result = await _cleaningService.CleanPluginAsync(
                        plugin,
                        progress,
                        cts.Token,
                        onProcessStarted: proc =>
                        {
                            lock (_processLock)
                            {
                                _currentProcess = proc;
                            }
                        }).ConfigureAwait(false);

                    // If timed out and callback provided, ask user if they want to retry
                    if (result.TimedOut && onTimeout != null && attemptNumber < maxRetryAttempts)
                    {
                        var shouldRetry = await onTimeout(plugin.FileName, timeoutSeconds, attemptNumber)
                            .ConfigureAwait(false);

                        if (!shouldRetry)
                        {
                            _logger.Information("User chose not to retry plugin: {Plugin}", plugin.FileName);
                            break;
                        }

                        _logger.Information("User chose to retry plugin: {Plugin}", plugin.FileName);
                    }
                    else
                    {
                        break; // No timeout or no callback or max attempts reached
                    }
                } while (true);

                pluginStopwatch.Stop();

                // Clear the current process reference after plugin is done
                lock (_processLock)
                {
                    _currentProcess = null;
                }

                // Attempt to enrich stats from the xEdit log file (preferred over stdout stats)
                var logStats = result.Statistics;
                string? logParseWarning = null;

                if (result.Success && result.Status == CleaningStatus.Cleaned)
                {
                    var xEditPath = config.XEditExecutablePath ?? string.Empty;
                    var (logLines, logError) = await _logFileService.ReadLogFileAsync(
                        xEditPath, pluginStartTime, cts.Token).ConfigureAwait(false);

                    if (logError != null)
                    {
                        _logger.Warning("Log parse warning for {Plugin}: {Warning}", plugin.FileName, logError);
                        logParseWarning = logError;
                        // Keep stdout-based stats as fallback
                    }
                    else if (logLines.Count > 0)
                    {
                        // Prefer log-file-based stats over stdout stats
                        logStats = _outputParser.ParseOutput(logLines);
                        _logger.Debug("Parsed log file stats for {Plugin}: {Removed} ITM, {Undeleted} UDR",
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
                _stateService.AddDetailedCleaningResult(pluginCleaningResult);

                _logger.Information(
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
                await _backupService.WriteSessionMetadataAsync(sessionDir, backupSession, cts.Token).ConfigureAwait(false);

                var backupRoot = System.IO.Path.GetDirectoryName(sessionDir)!;
                _backupService.CleanupOldSessions(backupRoot, userConfig.Backup.MaxSessions, sessionDir);
                _logger.Information("Backup session complete: {Count} plugins backed up", backupEntries.Count);
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

            _stateService.FinishCleaningWithResults(sessionResult);
            _logger.Information("Cleaning workflow completed");
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not an error -- preserve partial results
            _logger.Information("Cleaning workflow cancelled");
            wasCancelled = true;

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
                    await _backupService.WriteSessionMetadataAsync(sessionDir, partialBackupSession).ConfigureAwait(false);
                }
                catch (Exception backupEx)
                {
                    _logger.Warning("Failed to write partial backup metadata after cancellation: {Error}", backupEx.Message);
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

            _stateService.FinishCleaningWithResults(sessionResult);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during cleaning workflow");

            // Still create a session result even on error
            var sessionResult = new CleaningSessionResult
            {
                StartTime = startTime,
                EndTime = DateTime.Now,
                GameType = gameType,
                WasCancelled = wasCancelled,
                PluginResults = pluginResults
            };

            _stateService.FinishCleaningWithResults(sessionResult);
            throw;
        }
        finally
        {
            // Reset stop flags
            _isStopRequested = false;
            _lastTerminationResult = null;

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
            _logger.Information("[Termination] Second stop requested -- escalating to force kill");
            await ForceStopCleaningAsync().ConfigureAwait(false);
            return;
        }

        _isStopRequested = true;
        _logger.Information("[Termination] Graceful stop requested");

        // Cancel the CTS (race-safe per PROC-04)
        CancellationTokenSource? cts;
        lock (_ctsLock)
        {
            cts = _cleaningCts;
        }

        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            _logger.Debug("[Termination] CTS already disposed -- cleaning likely already finished");
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
                if (!proc.HasExited)
                {
                    var result = await _processService.TerminateProcessAsync(proc, forceKill: false)
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
                _logger.Debug("[Termination] Process already exited during graceful stop");
            }
        }
    }

    public async Task ForceStopCleaningAsync()
    {
        _logger.Information("[Termination] Force stop requested -- killing process tree immediately");

        // Cancel the CTS if not already
        CancellationTokenSource? cts;
        lock (_ctsLock)
        {
            cts = _cleaningCts;
        }

        try
        {
            cts?.Cancel();
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
                if (!proc.HasExited)
                {
                    await _processService.TerminateProcessAsync(proc, forceKill: true)
                        .ConfigureAwait(false);
                }
            }
            catch (InvalidOperationException)
            {
                _logger.Debug("[Termination] Process already exited during force stop");
            }
        }
    }

    public async Task<List<DryRunResult>> RunDryRunAsync(CancellationToken ct = default)
    {
        var results = new List<DryRunResult>();

        _logger.Information("Starting dry-run preview");

        // Flush any pending config saves to ensure config is current
        await _configService.FlushPendingSavesAsync(ct).ConfigureAwait(false);

        // Get plugins from state (same as StartCleaningAsync step 2)
        var config = _stateService.CurrentState;
        var allPlugins = config.PluginsToClean;

        // Detect game type locally (same as StartCleaningAsync step 3) -- do NOT update state
        var gameType = config.CurrentGameType;
        if (gameType == GameType.Unknown)
        {
            var detectedGame =
                _gameDetectionService.DetectFromExecutable(config.XEditExecutablePath ?? string.Empty);

            if (detectedGame == GameType.Unknown && !string.IsNullOrEmpty(config.LoadOrderPath))
            {
                detectedGame = await _gameDetectionService.DetectFromLoadOrderAsync(config.LoadOrderPath, ct)
                    .ConfigureAwait(false);
            }

            if (detectedGame != GameType.Unknown)
            {
                gameType = detectedGame;
            }
            else
            {
                _logger.Error(null,
                    "Cannot determine game type for dry-run preview. Skip lists cannot be applied without a known game type.");
                throw new InvalidOperationException(
                    "Cannot start preview: game type could not be determined. " +
                    "Please select a game type in Settings, or ensure the xEdit executable name matches a supported game.");
            }
        }

        // Detect game variant (same as StartCleaningAsync step 3b)
        var pluginNames = allPlugins.Select(p => p.FileName).ToList();
        var gameVariant = _gameDetectionService.DetectVariant(gameType, pluginNames);

        // Load user config for skip list and MO2 settings (same as StartCleaningAsync step 4)
        var userConfig = await _configService.LoadUserConfigAsync(ct).ConfigureAwait(false);
        var disableSkipLists = userConfig.Settings.DisableSkipLists;
        var isMo2Mode = userConfig.Settings.Mo2Mode;

        // Build skip set
        HashSet<string>? skipSet = null;
        if (!disableSkipLists && gameType != GameType.Unknown)
        {
            var skipList = await _configService.GetSkipListAsync(gameType, gameVariant).ConfigureAwait(false) ?? [];
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
                var warning = _pluginService.ValidatePluginFile(enrichedPlugin);
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

        _logger.Information("Dry-run preview complete: {WillClean} will clean, {WillSkip} will skip",
            results.Count(r => r.Status == DryRunStatus.WillClean),
            results.Count(r => r.Status == DryRunStatus.WillSkip));

        return results;
    }

    private async Task<bool> ValidateConfigurationAsync(CancellationToken ct)
    {
        var config = _stateService.CurrentState;

        if (string.IsNullOrEmpty(config.LoadOrderPath) ||
            string.IsNullOrEmpty(config.XEditExecutablePath))
        {
            return false;
        }

        return await _cleaningService.ValidateEnvironmentAsync(ct).ConfigureAwait(false);
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
