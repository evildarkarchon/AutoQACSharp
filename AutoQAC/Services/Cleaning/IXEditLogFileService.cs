using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AutoQAC.Services.Cleaning;

/// <summary>
/// Service for locating and reading xEdit log files after plugin cleaning completes.
/// xEdit writes a log file in the same directory as its executable,
/// named {STEM_UPPERCASE}_log.txt (e.g., SSEEdit.exe -> SSEEDIT_log.txt).
/// </summary>
public interface IXEditLogFileService
{
    /// <summary>
    /// Computes the expected log file path for the given xEdit executable.
    /// Convention: same directory, {STEM_UPPERCASE}_log.txt.
    /// </summary>
    /// <param name="xEditExecutablePath">Full path to the xEdit executable.</param>
    /// <returns>Full path to the expected log file.</returns>
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
    Task<(List<string> lines, string? error)> ReadLogFileAsync(
        string xEditExecutablePath,
        DateTime processStartTime,
        CancellationToken ct = default);
}
