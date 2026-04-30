using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;

namespace AutoQAC.Services.Cleaning;

public sealed class PluginResultFinalizer(
    IXEditLogFileService logFileService,
    IXEditOutputParser outputParser,
    ILoggingService logger)
    : IPluginResultFinalizer
{
    /// <inheritdoc />
    public async Task<PluginCleaningResult> FinalizeAsync(
        PluginInfo plugin,
        GameType gameType,
        string xEditDir,
        PluginRunnerOutput runnerOutput,
        TerminationFinalizeContext terminationContext,
        CancellationToken ct)
    {
        var result = runnerOutput.LastAttemptResult;
        CleaningStatistics? logStats = null;
        string? logParseWarning = null;
        var finalStatus = result.Status;

        // Guard: only read logs if process was not killed/cancelled (per D-04)
        if (!terminationContext.ProcessMayStillBeRunning && !terminationContext.StopWasRequested && result.Status != CleaningStatus.Skipped)
        {
            var logResult = await logFileService.ReadLogContentAsync(
                xEditDir, gameType, runnerOutput.MainLogOffset, runnerOutput.ExceptionLogOffset, ct).ConfigureAwait(false);

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
                // Gap CR-02 fix: only a SUCCESSFUL cleaned runner result is eligible for AlreadyClean
                // promotion. A failed xEdit attempt whose log slice happens to contain a completion line
                // and zero parsed stats must remain Failed -- otherwise a real failure is hidden as
                // AlreadyClean and CleaningSessionResult miscounts the session as successful.
                if (result.Success
                    && result.Status == CleaningStatus.Cleaned
                    && logResult.LogLines.Any(outputParser.IsCompletionLine)
                    && logStats is { ItemsRemoved: 0, ItemsUndeleted: 0, ItemsSkipped: 0, PartialFormsCreated: 0 })
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
        else if (terminationContext.StopWasRequested || terminationContext.ProcessMayStillBeRunning)
        {
            logParseWarning = "xEdit was terminated -- no log available";
        }

        // Gap WR-01 fix: derive Success from the final status after log-parse overrides so that
        // exception-log failures (finalStatus = Failed) cannot be returned with Success = true.
        // This keeps Status and Success internally consistent regardless of how a downstream
        // consumer chooses to read the row. AlreadyClean is treated as success-equivalent because
        // it represents "no work needed, finished cleanly".
        var finalSuccess = finalStatus is CleaningStatus.Cleaned or CleaningStatus.AlreadyClean;

        return new PluginCleaningResult
        {
            PluginName = plugin.FileName,
            Status = finalStatus,
            Success = finalSuccess,
            Message = result.TimedOut && runnerOutput.ReachedMaxRetryAttempts
                ? $"Cleaning timed out after {runnerOutput.AttemptCount} attempts."
                : result.Message,
            Duration = runnerOutput.Duration,
            Statistics = logStats,
            LogParseWarning = logParseWarning
        };
    }
}
