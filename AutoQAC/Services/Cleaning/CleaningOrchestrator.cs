using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameDetection;
using AutoQAC.Services.Plugin;
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
    private readonly object _ctsLock = new();

    private CancellationTokenSource? _cleaningCts;

    public CleaningOrchestrator(
        ICleaningService cleaningService,
        IPluginValidationService pluginService,
        IGameDetectionService gameDetectionService,
        IStateService stateService,
        IConfigurationService configService,
        ILoggingService logger)
    {
        _cleaningService = cleaningService;
        _pluginService = pluginService;
        _gameDetectionService = gameDetectionService;
        _stateService = stateService;
        _configService = configService;
        _logger = logger;
    }

    public async Task StartCleaningAsync(CancellationToken ct = default)
    {
        try
        {
            _logger.Information("Starting cleaning workflow");

            // 1. Validate configuration
            var isValid = await ValidateConfigurationAsync(ct).ConfigureAwait(false);
            if (!isValid)
            {
                _logger.Error(null, "Configuration is invalid, cannot start cleaning.");
                throw new InvalidOperationException("Configuration is invalid");
            }

            // 2. Load plugins from load order
            var config = _stateService.CurrentState;
            var plugins = await _pluginService.GetPluginsFromLoadOrderAsync(
                config.LoadOrderPath!, ct).ConfigureAwait(false);

            // 3. Detect Game (if unknown) and Update State
            if (config.CurrentGameType == GameType.Unknown)
            {
                var detectedGame = _gameDetectionService.DetectFromExecutable(config.XEditExecutablePath ?? string.Empty);

                if (detectedGame == GameType.Unknown && !string.IsNullOrEmpty(config.LoadOrderPath))
                {
                    detectedGame = await _gameDetectionService.DetectFromLoadOrderAsync(config.LoadOrderPath, ct).ConfigureAwait(false);
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

            // 4. Filter skip list
            var skipList = await _configService.GetSkipListAsync(config.CurrentGameType).ConfigureAwait(false);
            var pluginsToClean = _pluginService.FilterSkippedPlugins(plugins, skipList);

            // 5. Update state - cleaning started
            _stateService.StartCleaning(
                pluginsToClean.Select(p => p.FileName).ToList());

            // 6. Create cancellation token (thread-safe)
            CancellationTokenSource cts;
            lock (_ctsLock)
            {
                _cleaningCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts = _cleaningCts;
            }

            // 7. Process plugins SEQUENTIALLY (CRITICAL!)
            foreach (var plugin in pluginsToClean)
            {
                if (cts.Token.IsCancellationRequested)
                {
                    _logger.Information("Cleaning cancelled by user");
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
                    // We could pipe this to UI if needed
                    _logger.Debug("xEdit output: {Output}", output);
                });

                // Clean plugin
                var result = await _cleaningService.CleanPluginAsync(
                    plugin,
                    progress,
                    cts.Token).ConfigureAwait(false);

                // Update results
                _stateService.AddCleaningResult(plugin.FileName, result.Status);

                _logger.Information(
                    "Plugin {Plugin} processed: {Status} - {Message}",
                    plugin.FileName,
                    result.Status,
                    result.Message);
            }

            // 8. Finish cleaning
            _stateService.FinishCleaning();
            _logger.Information("Cleaning workflow completed");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during cleaning workflow");
            _stateService.FinishCleaning();
            throw;
        }
        finally
        {
            lock (_ctsLock)
            {
                _cleaningCts?.Dispose();
                _cleaningCts = null;
            }
        }
    }

    public void StopCleaning()
    {
        _logger.Information("Stop requested");
        lock (_ctsLock)
        {
            _cleaningCts?.Cancel();
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
