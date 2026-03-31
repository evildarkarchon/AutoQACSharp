using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;

namespace AutoQAC.Services.Cleaning;

/// <summary>
/// Service for locating and reading xEdit log files after plugin cleaning completes.
/// xEdit writes its main log to <c>{wbAppName}Edit_log.txt</c> in its install directory,
/// where <c>wbAppName</c> is a game-mode-specific prefix (e.g., "SSE" for Skyrim SE,
/// "FO4" for Fallout 4). The exception log follows the pattern
/// <c>{wbAppName}EditException.log</c>.
/// </summary>
public interface IXEditLogFileService
{
    // ────────────────────────────────────────────────────────────────────
    //  Legacy methods (preserved for CleaningOrchestrator compatibility)
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes the expected log file path for the given xEdit executable.
    /// Convention: same directory, {STEM_UPPERCASE}_log.txt.
    /// </summary>
    /// <param name="xEditExecutablePath">Full path to the xEdit executable.</param>
    /// <returns>Full path to the expected log file.</returns>
    [Obsolete("Use GetLogFilePath(string, GameType) instead. Will be removed in Phase 4.")]
    string GetLogFilePath(string xEditExecutablePath);

    /// <summary>
    /// Reads the xEdit log file, with staleness detection and IOException retry.
    /// </summary>
    /// <param name="xEditExecutablePath">Full path to the xEdit executable (used to compute log path).</param>
    /// <param name="processStartTime">UTC time when the xEdit process was started, for staleness detection.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A tuple of (lines, error). On success, lines contains the log file content and error is null.
    /// On failure, lines is empty and error describes the problem.
    /// </returns>
    [Obsolete("Use ReadLogContentAsync instead. Will be removed in Phase 4.")]
    Task<(List<string> lines, string? error)> ReadLogFileAsync(
        string xEditExecutablePath,
        DateTime processStartTime,
        CancellationToken ct = default);

    // ────────────────────────────────────────────────────────────────────
    //  Game-aware methods (offset-based API)
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the expected main log file path for the given game type and xEdit directory.
    /// Uses xEdit's <c>{wbAppName}Edit_log.txt</c> naming convention.
    /// </summary>
    /// <param name="xEditDirectory">Directory containing the xEdit executable.</param>
    /// <param name="gameType">The game type to resolve the log filename for.</param>
    /// <returns>Full path to the expected main log file.</returns>
    string GetLogFilePath(string xEditDirectory, GameType gameType);

    /// <summary>
    /// Returns the expected exception log file path for the given game type and xEdit directory.
    /// Uses xEdit's <c>{wbAppName}EditException.log</c> naming convention.
    /// </summary>
    /// <param name="xEditDirectory">Directory containing the xEdit executable.</param>
    /// <param name="gameType">The game type to resolve the exception log filename for.</param>
    /// <returns>Full path to the expected exception log file.</returns>
    string GetExceptionLogFilePath(string xEditDirectory, GameType gameType);

    /// <summary>
    /// Captures the current byte offset of a log file. Returns 0 if the file does not exist.
    /// Call before launching xEdit to establish the baseline for offset-based reading.
    /// </summary>
    /// <param name="logFilePath">Full path to the log file.</param>
    /// <returns>Current file size in bytes, or 0 if the file does not exist.</returns>
    long CaptureOffset(string logFilePath);

    /// <summary>
    /// Reads only the content appended to the log files after the given byte offsets.
    /// Handles file truncation (offset beyond file length) by reading the entire file.
    /// Retries on IOException with exponential backoff.
    /// </summary>
    /// <param name="xEditDirectory">Directory containing the xEdit executable.</param>
    /// <param name="gameType">The game type to resolve log filenames for.</param>
    /// <param name="mainLogOffset">Byte offset captured before xEdit launch for the main log.</param>
    /// <param name="exceptionLogOffset">Byte offset captured before xEdit launch for the exception log.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="LogReadResult"/> containing parsed log lines and any exception content.</returns>
    Task<LogReadResult> ReadLogContentAsync(
        string xEditDirectory,
        GameType gameType,
        long mainLogOffset,
        long exceptionLogOffset,
        CancellationToken ct = default);
}
