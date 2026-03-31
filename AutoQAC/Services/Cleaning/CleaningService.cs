using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.GameDetection;
using AutoQAC.Services.Process;
using AutoQAC.Services.State;

namespace AutoQAC.Services.Cleaning;

public sealed class CleaningService(
    IGameDetectionService gameDetection,
    IStateService stateService,
    ILoggingService logger,
    IProcessExecutionService processService,
    IXEditCommandBuilder commandBuilder,
    IXEditOutputParser outputParser)
    : ICleaningService
{
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
            var gameType = stateService.CurrentState.CurrentGameType;
            if (gameType == GameType.Unknown)
            {
                // Fallback or error? Orchestrator usually sets this.
                gameType = plugin.DetectedGameType;
            }

            var command = commandBuilder.BuildCommand(plugin, gameType);
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
            var timeoutSeconds = stateService.CurrentState.CleaningTimeout;
            var timeout = TimeSpan.FromSeconds(timeoutSeconds > 0 ? timeoutSeconds : 300);
            var gameDisplayName = gameDetection.GetGameDisplayName(gameType);

            logger.Information(
                "Cleaning {Plugin} for {Game} with timeout {TimeoutSeconds}s...",
                plugin.FileName,
                gameDisplayName,
                timeout.TotalSeconds);

            var result = await processService.ExecuteAsync(command, progress, timeout, ct, onProcessStarted).ConfigureAwait(false);

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

            // Statistics intentionally omitted -- orchestrator parses from log file (per D-02)
            return new CleaningResult
            {
                Success = true,
                Status = CleaningStatus.Cleaned,
                Message = "Cleaning completed successfully.",
                Duration = sw.Elapsed
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
            logger.Error(ex, "Error cleaning {Plugin}", plugin.FileName);
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
        var config = stateService.CurrentState;
        if (string.IsNullOrEmpty(config.XEditExecutablePath) || !File.Exists(config.XEditExecutablePath))
        {
            return Task.FromResult(false);
        }

        if (RequiresFileLoadOrder(config.CurrentGameType))
        {
            if (string.IsNullOrWhiteSpace(config.LoadOrderPath) || !File.Exists(config.LoadOrderPath))
            {
                return Task.FromResult(false);
            }
        }

        return Task.FromResult(true);
    }

    private static bool RequiresFileLoadOrder(GameType gameType) => gameType switch
    {
        GameType.Fallout3 => true,
        GameType.FalloutNewVegas => true,
        GameType.Oblivion => true,
        _ => false
    };
}
