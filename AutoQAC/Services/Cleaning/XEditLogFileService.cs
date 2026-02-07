using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;

namespace AutoQAC.Services.Cleaning;

/// <summary>
/// Reads xEdit log files from disk after each plugin's cleaning process exits.
/// Handles missing files, stale logs, and IOExceptions with a single retry after 200ms delay.
/// </summary>
public sealed class XEditLogFileService : IXEditLogFileService
{
    private const int RetryDelayMs = 200;

    private readonly ILoggingService _logger;

    public XEditLogFileService(ILoggingService logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string GetLogFilePath(string xEditExecutablePath)
    {
        var dir = Path.GetDirectoryName(xEditExecutablePath);
        if (string.IsNullOrEmpty(dir))
            throw new ArgumentException("Invalid xEdit executable path: cannot determine directory.", nameof(xEditExecutablePath));
        var stem = Path.GetFileNameWithoutExtension(xEditExecutablePath).ToUpperInvariant();
        return Path.Combine(dir, $"{stem}_log.txt");
    }

    /// <inheritdoc />
    public async Task<(List<string> lines, string? error)> ReadLogFileAsync(
        string xEditExecutablePath,
        DateTime processStartTime,
        CancellationToken ct = default)
    {
        var logPath = GetLogFilePath(xEditExecutablePath);

        // Check existence
        if (!File.Exists(logPath))
        {
            _logger.Debug("Log file not found: {Path}", logPath);
            return (new List<string>(), $"Log file not found: {logPath}");
        }

        // Staleness detection: log file must be newer than the process start time
        var logModifiedUtc = File.GetLastWriteTimeUtc(logPath);
        if (logModifiedUtc < processStartTime.ToUniversalTime())
        {
            _logger.Debug("Log file is stale (modified {Modified}, process started {Started})", logModifiedUtc, processStartTime);
            return (new List<string>(), "Log file is stale (predates this cleaning run)");
        }

        // Read with one retry on IOException (xEdit may briefly hold the file lock after exit)
        try
        {
            var lines = await File.ReadAllLinesAsync(logPath, ct).ConfigureAwait(false);
            return (lines.ToList(), null);
        }
        catch (IOException firstEx)
        {
            _logger.Debug("First read attempt failed for {Path}: {Error}. Retrying in {Delay}ms...",
                logPath, firstEx.Message, RetryDelayMs);

            await Task.Delay(RetryDelayMs, ct).ConfigureAwait(false);

            try
            {
                var lines = await File.ReadAllLinesAsync(logPath, ct).ConfigureAwait(false);
                return (lines.ToList(), null);
            }
            catch (IOException secondEx)
            {
                _logger.Warning("Failed to read log file after retry: {Path}: {Error}", logPath, secondEx.Message);
                return (new List<string>(), $"Failed to read log file: {secondEx.Message}");
            }
        }
    }
}
