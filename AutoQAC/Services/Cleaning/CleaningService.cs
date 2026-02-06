using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameDetection;
using AutoQAC.Services.Process;
using AutoQAC.Services.State;

namespace AutoQAC.Services.Cleaning;

public sealed class CleaningService : ICleaningService
{
    private readonly IConfigurationService _configService;
    private readonly IGameDetectionService _gameDetection;
    private readonly IStateService _stateService;
    private readonly ILoggingService _logger;
    private readonly IProcessExecutionService _processService;
    private readonly IXEditCommandBuilder _commandBuilder;
    private readonly IXEditOutputParser _outputParser;

    public CleaningService(
        IConfigurationService configService,
        IGameDetectionService gameDetection,
        IStateService stateService,
        ILoggingService logger,
        IProcessExecutionService processService,
        IXEditCommandBuilder commandBuilder,
        IXEditOutputParser outputParser)
    {
        _configService = configService;
        _gameDetection = gameDetection;
        _stateService = stateService;
        _logger = logger;
        _processService = processService;
        _commandBuilder = commandBuilder;
        _outputParser = outputParser;
    }

    public async Task<CleaningResult> CleanPluginAsync(
        PluginInfo plugin,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        Action<System.Diagnostics.Process>? onProcessStarted = null)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            // 1. Validation
            if (plugin.IsInSkipList)
            {
                return new CleaningResult 
                { 
                    Success = true, 
                    Status = CleaningStatus.Skipped, 
                    Message = "Plugin is in skip list.",
                    Duration = sw.Elapsed 
                };
            }

            // 2. Build Command
            // Determine game type from state if available, otherwise detect
            var gameType = _stateService.CurrentState.CurrentGameType;
            if (gameType == GameType.Unknown)
            {
                 // Fallback or error? Orchestrator usually sets this.
                 gameType = plugin.DetectedGameType;
            }

            var command = _commandBuilder.BuildCommand(plugin, gameType);
            if (command == null)
            {
                return new CleaningResult
                {
                    Success = false,
                    Status = CleaningStatus.Failed,
                    Message = "Failed to build xEdit command.",
                    Duration = sw.Elapsed
                };
            }

            // 3. Execute
            // Get timeout from settings
            var timeoutSeconds = _stateService.CurrentState.CleaningTimeout;
            var timeout = TimeSpan.FromSeconds(timeoutSeconds > 0 ? timeoutSeconds : 300);

            _logger.Information($"Cleaning {plugin.FileName} with timeout {timeout.TotalSeconds}s...");

            var result = await _processService.ExecuteAsync(command, progress, timeout, ct, onProcessStarted).ConfigureAwait(false);

            sw.Stop();

            if (result.TimedOut)
            {
                return new CleaningResult
                {
                    Success = false,
                    Status = CleaningStatus.Failed,
                    Message = "Cleaning timed out.",
                    Duration = sw.Elapsed,
                    TimedOut = true
                };
            }

            if (result.ExitCode != 0)
            {
                // xEdit might exit with non-zero on error, check output
                 return new CleaningResult
                {
                    Success = false,
                    Status = CleaningStatus.Failed,
                    Message = $"xEdit exited with code {result.ExitCode}",
                    Duration = sw.Elapsed
                };
            }

            // 4. Parse Output (Offload to thread pool)
            var stats = await Task.Run(() => _outputParser.ParseOutput(result.OutputLines), ct).ConfigureAwait(false);

            return new CleaningResult
            {
                Success = true,
                Status = CleaningStatus.Cleaned,
                Message = "Cleaning completed successfully.",
                Duration = sw.Elapsed,
                Statistics = stats
            };

        }
        catch (OperationCanceledException)
        {
            return new CleaningResult
            {
                Success = false,
                Status = CleaningStatus.Skipped,
                Message = "Operation cancelled.",
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Error cleaning {plugin.FileName}");
            return new CleaningResult
            {
                Success = false,
                Status = CleaningStatus.Failed,
                Message = ex.Message,
                Duration = sw.Elapsed
            };
        }
    }

    public Task<bool> ValidateEnvironmentAsync(CancellationToken ct = default)
    {
        var config = _stateService.CurrentState;
        if (string.IsNullOrEmpty(config.XEditExecutablePath) || !File.Exists(config.XEditExecutablePath))
        {
            return Task.FromResult(false);
        }
        if (string.IsNullOrEmpty(config.LoadOrderPath) || !File.Exists(config.LoadOrderPath))
        {
            return Task.FromResult(false);
        }
        return Task.FromResult(true);
    }
}
