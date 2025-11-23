using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;

namespace AutoQAC.Services.Cleaning;

public sealed class CleaningOrchestrator : ICleaningOrchestrator
{
    private readonly ICleaningService _cleaningService;
    private readonly IPluginValidationService _pluginService;
    private readonly IStateService _stateService;
    private readonly IConfigurationService _configService;
    private readonly ILoggingService _logger;

    private CancellationTokenSource? _cleaningCts;

    public CleaningOrchestrator(
        ICleaningService cleaningService,
        IPluginValidationService pluginService,
        IStateService stateService,
        IConfigurationService configService,
        ILoggingService logger)
    {
        _cleaningService = cleaningService;
        _pluginService = pluginService;
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
            var isValid = await ValidateConfigurationAsync(ct);
            if (!isValid)
            {
                _logger.Error(null, "Configuration is invalid, cannot start cleaning.");
                throw new InvalidOperationException("Configuration is invalid");
            }

            // 2. Load plugins from load order
            var config = _stateService.CurrentState;
            var plugins = await _pluginService.GetPluginsFromLoadOrderAsync(
                config.LoadOrderPath!, ct);

            // 3. Filter skip list
            var skipList = _configService.GetSkipList(config.CurrentGameType);
            var pluginsToClean = _pluginService.FilterSkippedPlugins(plugins, skipList);

            // 4. Update state - cleaning started
            _stateService.StartCleaning(
                pluginsToClean.Select(p => p.FileName).ToList());

            // 5. Create cancellation token
            _cleaningCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            // 6. Process plugins SEQUENTIALLY (CRITICAL!)
            foreach (var plugin in pluginsToClean)
            {
                if (_cleaningCts.Token.IsCancellationRequested)
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
                    _cleaningCts.Token);

                // Update results
                _stateService.AddCleaningResult(plugin.FileName, result.Status);

                _logger.Information(
                    "Plugin {Plugin} processed: {Status} - {Message}",
                    plugin.FileName,
                    result.Status,
                    result.Message);
            }

            // 7. Finish cleaning
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
            _cleaningCts?.Dispose();
            _cleaningCts = null;
        }
    }

    public void StopCleaning()
    {
        _logger.Information("Stop requested");
        _cleaningCts?.Cancel();
        _cleaningService.StopCurrentOperation();
    }

    private async Task<bool> ValidateConfigurationAsync(CancellationToken ct)
    {
        var config = _stateService.CurrentState;

        if (string.IsNullOrEmpty(config.LoadOrderPath) ||
            string.IsNullOrEmpty(config.XEditExecutablePath))
        {
            return false;
        }

        return await _cleaningService.ValidateEnvironmentAsync(ct);
    }
}
