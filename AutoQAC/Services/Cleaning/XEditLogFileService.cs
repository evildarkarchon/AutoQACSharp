using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;

namespace AutoQAC.Services.Cleaning;

/// <summary>
/// Locates and reads xEdit log files using game-aware naming and offset-based reading.
/// xEdit writes its main log to <c>{wbAppName}Edit_log.txt</c> (e.g., <c>SSEEdit_log.txt</c>)
/// and exception log to <c>{wbAppName}EditException.log</c> in its install directory.
/// Both files are appended to across sessions (truncated at 3MB), so offset-based
/// reading isolates only the current session's output.
/// </summary>
public sealed class XEditLogFileService(ILoggingService logger) : IXEditLogFileService
{
    // ────────────────────────────────────────────────────────────────────
    //  Game-aware methods (offset-based API)
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Maps a <see cref="GameType"/> to xEdit's internal <c>wbAppName</c> prefix.
    /// Source: xEdit <c>xeInit.pas</c> lines 798-944.
    /// </summary>
    internal static string GetXEditAppName(GameType gameType) => gameType switch
    {
        GameType.SkyrimLe        => "TES5",
        GameType.SkyrimSe        => "SSE",
        GameType.SkyrimVr        => "TES5VR",
        GameType.Fallout4        => "FO4",
        GameType.Fallout4Vr      => "FO4VR",
        GameType.Fallout3        => "FO3",
        GameType.FalloutNewVegas => "FNV",
        GameType.Oblivion        => "TES4",
        _ => throw new ArgumentOutOfRangeException(nameof(gameType), gameType,
            "Unsupported game type for xEdit log file resolution")
    };

    /// <inheritdoc />
    public string GetLogFilePath(string xEditDirectory, GameType gameType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xEditDirectory);
        return Path.Combine(xEditDirectory, $"{GetXEditAppName(gameType)}Edit_log.txt");
    }

    /// <inheritdoc />
    public string GetExceptionLogFilePath(string xEditDirectory, GameType gameType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xEditDirectory);
        return Path.Combine(xEditDirectory, $"{GetXEditAppName(gameType)}EditException.log");
    }

    /// <inheritdoc />
    public long CaptureOffset(string logFilePath)
    {
        if (!File.Exists(logFilePath))
            return 0;

        return new FileInfo(logFilePath).Length;
    }

    /// <inheritdoc />
    public async Task<LogReadResult> ReadLogContentAsync(
        string xEditDirectory,
        GameType gameType,
        long mainLogOffset,
        long exceptionLogOffset,
        CancellationToken ct = default)
    {
        var mainLogPath = GetLogFilePath(xEditDirectory, gameType);
        var exceptionLogPath = GetExceptionLogFilePath(xEditDirectory, gameType);

        // Read main log
        if (!File.Exists(mainLogPath))
        {
            logger.Debug("[LogFile] Main log file not found after xEdit exit: {Path}", mainLogPath);
            return new LogReadResult
            {
                LogLines = [],
                ExceptionContent = null,
                Warning = $"Main log file not found: {mainLogPath}"
            };
        }

        var mainContent = await ReadFromOffsetWithRetryAsync(mainLogPath, mainLogOffset, ct)
            .ConfigureAwait(false);

        // Split into lines, handling both \r\n and \n
        var logLines = mainContent
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => !string.IsNullOrEmpty(l))
            .ToList();

        // Read exception log (may not exist -- that's normal)
        string? exceptionContent = null;
        if (File.Exists(exceptionLogPath))
        {
            var exContent = await ReadFromOffsetWithRetryAsync(exceptionLogPath, exceptionLogOffset, ct)
                .ConfigureAwait(false);
            if (!string.IsNullOrEmpty(exContent))
                exceptionContent = exContent;
        }

        return new LogReadResult
        {
            LogLines = logLines,
            ExceptionContent = exceptionContent,
            Warning = null
        };
    }

    /// <summary>
    /// Reads file content from the given byte offset with exponential backoff retry on IOException.
    /// Handles file truncation by resetting offset to 0 when offset exceeds file length.
    /// </summary>
    private async Task<string> ReadFromOffsetWithRetryAsync(
        string filePath, long offset, CancellationToken ct)
    {
        if (!File.Exists(filePath))
            return string.Empty;

        const int maxRetries = 3;
        const int baseDelayMs = 100;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                await using var fs = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite);

                if (offset > fs.Length)
                {
                    // File was truncated (xEdit 3MB threshold) -- read entire file
                    logger.Debug("[LogFile] File truncated (offset {Offset} > length {Length}), reading entire file: {Path}",
                        offset, fs.Length, filePath);
                    offset = 0;
                }

                fs.Seek(offset, SeekOrigin.Begin);
                using var reader = new StreamReader(fs);
                return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            }
            catch (IOException) when (attempt < maxRetries)
            {
                var delay = baseDelayMs * (1 << attempt); // 100ms, 200ms, 400ms
                logger.Debug("[LogFile] File contention on {Path}, retry {Attempt} in {Delay}ms",
                    filePath, attempt + 1, delay);
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (IOException ex) when (attempt == maxRetries)
            {
                logger.Warning("[LogFile] Failed to read after {MaxRetries} retries: {Path}: {Error}",
                    maxRetries, filePath, ex.Message);
                return string.Empty;
            }
        }

        // Unreachable, but compiler needs it
        return string.Empty;
    }

    // ────────────────────────────────────────────────────────────────────
    //  Legacy methods (preserved for CleaningOrchestrator compatibility)
    // ────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    [Obsolete("Use GetLogFilePath(string, GameType) instead.")]
    public string GetLogFilePath(string xEditExecutablePath)
    {
        var dir = Path.GetDirectoryName(xEditExecutablePath);
        if (string.IsNullOrEmpty(dir))
            throw new ArgumentException("Invalid xEdit executable path.", nameof(xEditExecutablePath));
        var stem = Path.GetFileNameWithoutExtension(xEditExecutablePath).ToUpperInvariant();
        return Path.Combine(dir, $"{stem}_log.txt");
    }

    /// <inheritdoc />
    [Obsolete("Use ReadLogContentAsync instead.")]
    public async Task<(List<string> lines, string? error)> ReadLogFileAsync(
        string xEditExecutablePath,
        DateTime processStartTime,
        CancellationToken ct = default)
    {
        // Legacy implementation preserved for CleaningOrchestrator compatibility (Phase 3 replaces this call)
#pragma warning disable CS0618 // Obsolete
        var logPath = GetLogFilePath(xEditExecutablePath);
#pragma warning restore CS0618

        if (!File.Exists(logPath))
            return (new List<string>(), $"Log file not found: {logPath}");

        var logModifiedUtc = File.GetLastWriteTimeUtc(logPath);
        if (logModifiedUtc < processStartTime.ToUniversalTime())
            return (new List<string>(), "Log file is stale (predates this cleaning run)");

        try
        {
            var lines = await File.ReadAllLinesAsync(logPath, ct).ConfigureAwait(false);
            return (lines.ToList(), null);
        }
        catch (IOException)
        {
            await Task.Delay(200, ct).ConfigureAwait(false);
            try
            {
                var lines = await File.ReadAllLinesAsync(logPath, ct).ConfigureAwait(false);
                return (lines.ToList(), null);
            }
            catch (IOException secondEx)
            {
                return (new List<string>(), $"Failed to read log file: {secondEx.Message}");
            }
        }
    }
}
