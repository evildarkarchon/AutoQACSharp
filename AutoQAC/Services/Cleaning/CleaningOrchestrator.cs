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
        IProcessExecutionService processService)
    {
        _cleaningService = cleaningService;
        _pluginService = pluginService;
        _gameDetectionService = gameDetectionService;
        _stateService = stateService;
        _configService = configService;
        _logger = logger;
        _processService = processService;
    }

    public Task StartCleaningAsync(CancellationToken ct = default)
    {
        return StartCleaningAsync(null, ct);
    }

    public async Task StartCleaningAsync(TimeoutRetryCallback? onTimeout, CancellationToken ct = default)
    {
        const int maxRetryAttempts = 3;
        var startTime = DateTime.Now;
        var pluginResults = new List<PluginCleaningResult>();
        var wasCancelled = false;
        var gameType = GameType.Unknown;

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
                    _logger.Warning("Could not auto-detect game type. Skip list might be empty.");
                }
            }

            gameType = config.CurrentGameType;

            // 4. Apply skip list filtering (respecting DisableSkipLists setting)
            var userConfig = await _configService.LoadUserConfigAsync(ct).ConfigureAwait(false);
            var disableSkipLists = userConfig.Settings.DisableSkipLists;

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
                var skipList = await _configService.GetSkipListAsync(gameType).ConfigureAwait(false) ?? [];
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

                // Progress reporting
                var progress = new Progress<string>(output =>
                {
                    _logger.Debug("xEdit output: {Output}", output);
                });

                // Clean plugin with retry logic for timeouts
                var pluginStopwatch = Stopwatch.StartNew();
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
                        cts.Token).ConfigureAwait(false);

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
                    Statistics = result.Statistics
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
