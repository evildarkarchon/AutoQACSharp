using System;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace AutoQAC.Services.Cleaning;

public sealed class PluginCleaningRunner(
    ICleaningService cleaningService,
    IXEditLogFileService logFileService,
    ILoggingService logger)
    : IPluginCleaningRunner
{
    /// <inheritdoc />
    public async Task<PluginRunnerOutput> RunAsync(
        PluginInfo plugin,
        GameType gameType,
        string xEditDir,
        ICleaningSessionDecisionAdapter decisions,
        int timeoutSeconds,
        int maxRetryAttempts,
        Action<System.Diagnostics.Process> attachProcess,
        Action detachProcess,
        CancellationToken ct)
    {
        var pluginStopwatch = Stopwatch.StartNew();
        CleaningResult result;
        var attemptNumber = 0;
        long mainLogOffset;
        long exceptionLogOffset;

        try
        {
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
                    onProcessStarted: attachProcess,
                    ct: ct).ConfigureAwait(false);

                // If timed out, ask the session decision adapter whether to retry before the retry ceiling.
                if (result.TimedOut && attemptNumber < maxRetryAttempts)
                {
                    var shouldRetry = await decisions
                        .ShouldRetryTimedOutPluginAsync(plugin.FileName, timeoutSeconds, attemptNumber,
                            maxRetryAttempts, ct)
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
                    break; // No timeout or max attempts reached.
                }
            } while (true);
        }
        finally
        {
            // R-02 contract: detach exactly once per plugin AFTER the retry loop (matches
            // CleaningSession once-per-plugin behavior — outside the do-while). The Process
            // object is refreshed per attempt by CleanPluginAsync; the previous attempt's Process
            // has already exited by the time the loop iterates, so a single trailing detach
            // correctly mirrors current behavior.
            detachProcess();
        }

        pluginStopwatch.Stop();

        return new PluginRunnerOutput
        {
            LastAttemptResult = result,
            AttemptCount = attemptNumber,
            MainLogOffset = mainLogOffset,
            ExceptionLogOffset = exceptionLogOffset,
            Duration = pluginStopwatch.Elapsed,
            ReachedMaxRetryAttempts = result.TimedOut && attemptNumber >= maxRetryAttempts
        };
    }
}
