using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Models.Diagnostics;
using AutoQAC.Services.GameDetection;
using AutoQAC.Services.Process;
using AutoQAC.Services.State;

namespace AutoQAC.Services.Cleaning;

public sealed class CleaningService(
    IGameDetectionService gameDetection,
    IStateService stateService,
    ILoggingService logger,
    IProcessExecutionService processService,
    IXEditCommandBuilder commandBuilder)
    : ICleaningService
{
    public async Task<CleaningResult> CleanPluginAsync(
        PluginInfo plugin,
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
            // Snapshot state once so launch-mode messaging matches the mode used to build the command.
            var state = stateService.CurrentState;
            // Determine game type from state if available, otherwise detect
            var gameType = state.CurrentGameType;
            if (gameType == GameType.Unknown)
            {
                // Fallback or error? Orchestrator usually sets this.
                gameType = plugin.DetectedGameType;
            }

            var command = commandBuilder.BuildCommand(plugin, gameType);
            if (command == null)
            {
                var buildFailureLaunchMode = state.Mo2ModeEnabled ? "MO2" : "direct xEdit";
                logger.Warning(
                    "Failed to build {LaunchMode} launch command for {Plugin}; no process was started.",
                    buildFailureLaunchMode,
                    plugin.FileName);

                return new CleaningResult
                {
                    Success = false,
                    Status = CleaningStatus.Failed,
                    Message = $"Could not build {buildFailureLaunchMode} launch command for {plugin.FileName}. No process was started. See logs for technical details.",
                    Duration = sw.Elapsed
                };
            }

            // 3. Execute
            // Get timeout from settings
            var timeoutSeconds = state.CleaningTimeout;
            var timeout = TimeSpan.FromSeconds(timeoutSeconds > 0 ? timeoutSeconds : 300);
            var gameDisplayName = gameDetection.GetGameDisplayName(gameType);
            var launchMode = state.Mo2ModeEnabled ? "MO2" : "direct xEdit";
            var safePluginName = DiagnosticTextFormatter.SafePluginName(plugin.FileName);
            var argumentCount = GetArgumentCount(command);

            logger.Information(
                "Starting {Operation} launch: mode={LaunchMode}, game={Game}, plugin={Plugin}, argumentCount={ArgumentCount}, status={Status}",
                "QuickAutoClean",
                launchMode,
                gameDisplayName,
                safePluginName,
                argumentCount,
                "Starting");

            var result = await processService.ExecuteAsync(command, timeout, ct, onProcessStarted).ConfigureAwait(false);

            sw.Stop();

            if (result.TimedOut)
            {
                logger.Warning(
                    "Completed {Operation} launch: mode={LaunchMode}, game={Game}, plugin={Plugin}, argumentCount={ArgumentCount}, status={Status}, reason={Reason}",
                    "QuickAutoClean",
                    launchMode,
                    gameDisplayName,
                    safePluginName,
                    argumentCount,
                    "Failed",
                    "TimedOut");

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
                logger.Warning(
                    "Completed {Operation} launch: mode={LaunchMode}, game={Game}, plugin={Plugin}, argumentCount={ArgumentCount}, status={Status}, reason={Reason}",
                    "QuickAutoClean",
                    launchMode,
                    gameDisplayName,
                    safePluginName,
                    argumentCount,
                    "Failed",
                    "ExitCode");

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
            logger.Information(
                "Completed {Operation} launch: mode={LaunchMode}, game={Game}, plugin={Plugin}, argumentCount={ArgumentCount}, status={Status}, reason={Reason}",
                "QuickAutoClean",
                launchMode,
                gameDisplayName,
                safePluginName,
                argumentCount,
                "Succeeded",
                "ProcessExited");

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
                Message = $"Cleaning failed for {plugin.FileName}. See logs for technical details.",
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

    /// <summary>
    /// Counts the launch arguments without reconstructing or logging the command payload.
    /// </summary>
    private static int GetArgumentCount(ProcessStartInfo startInfo) =>
        startInfo.ArgumentList.Count > 0
            ? startInfo.ArgumentList.Count
            : string.IsNullOrWhiteSpace(startInfo.Arguments) ? 0 : 1;
}
