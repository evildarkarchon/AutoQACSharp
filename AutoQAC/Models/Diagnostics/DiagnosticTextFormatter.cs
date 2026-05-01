using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AutoQAC.Models.Diagnostics;

/// <summary>
/// Formats shared user-facing diagnostic text while keeping paths, command fragments, and raw exception details out of UI/export copy.
/// </summary>
public static class DiagnosticTextFormatter
{
    /// <summary>
    /// Safe details sentence for user-facing dialogs when technical data was written to the latest AutoQAC log.
    /// </summary>
    public const string LatestLogDetails = "Technical details were written to the latest AutoQAC log.";

    /// <summary>
    /// Export report disclaimer explaining why technical diagnostics are not repeated in report text.
    /// </summary>
    public const string ReportDisclaimer = "Technical details are intentionally kept in AutoQAC logs and are not repeated in this report.";

    private const string LatestLogShort = "See the latest AutoQAC log.";

    private static readonly char[] ExplicitUnsafeNameCharacters = ['\'', '"', '`', '|', '&', ';', '<', '>'];
    private static readonly HashSet<char> InvalidFileNameCharacters = Path.GetInvalidFileNameChars().ToHashSet();
    private static readonly Regex DriveRootedPathPattern = new(@"[A-Za-z]:[\\/]", RegexOptions.Compiled);
    private static readonly Regex NamespaceStackFramePattern = new(@"\bat\s+[A-Za-z_][\w]*(\.[A-Za-z_][\w]*)+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ExeCommandMarkerPattern = new(@"\.exe(?=$|[\s""'`])", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Builds operation-specific unexpected-failure copy with latest-log guidance and no raw technical detail.
    /// </summary>
    /// <param name="operation">The user-facing operation name, such as Cleaning or Preview.</param>
    /// <returns>Safe failure text suitable for dialogs and status text.</returns>
    public static string OperationFailed(string operation)
    {
        var safeOperation = SanitizeDisplayName(operation, "Operation");
        return $"{safeOperation} failed. See the latest AutoQAC log for technical details.";
    }

    /// <summary>
    /// Formats a setting/resource label with a sanitized basename from a path while omitting the containing directory.
    /// </summary>
    /// <param name="label">The setting or resource label to show to the user.</param>
    /// <param name="path">The raw file path or filename candidate.</param>
    /// <param name="fallbackName">The safe basename fallback when the path has no displayable filename.</param>
    /// <returns>Safe text in the form <c>Label (basename)</c>.</returns>
    public static string SafeFileIdentifier(string label, string? path, string fallbackName)
    {
        var safeLabel = SanitizeDisplayName(label, "File");
        var fileName = Path.GetFileName(path);
        var safeName = SanitizeDisplayName(fileName, fallbackName);
        return $"{safeLabel} ({safeName})";
    }

    /// <summary>
    /// Builds safe folder-problem copy using the game/folder label instead of a full local folder path.
    /// </summary>
    /// <param name="gameDisplayName">The safe game display name.</param>
    /// <param name="folderLabel">The folder role to include, defaulting to data folder.</param>
    /// <returns>Safe folder issue text with an actionable reset/choose instruction.</returns>
    public static string SafeFolderIssue(string gameDisplayName, string folderLabel = "data folder")
    {
        var safeGame = SanitizeDisplayName(gameDisplayName, "Selected game");
        var safeFolderLabel = SanitizeDisplayName(folderLabel, "data folder");
        return $"{safeGame} {safeFolderLabel} is unavailable. Choose a valid Data folder or reset the override.";
    }

    /// <summary>
    /// Extracts and sanitizes a plugin filename or plugin-name candidate for user-facing diagnostic rows.
    /// </summary>
    /// <param name="pluginNameOrPath">The raw plugin filename or path candidate.</param>
    /// <param name="fallbackName">The safe fallback when no displayable plugin name remains.</param>
    /// <returns>A sanitized plugin display name preserving useful filename characters such as periods and dashes.</returns>
    public static string SafePluginName(string? pluginNameOrPath, string fallbackName = "selected plugin")
    {
        var fileName = Path.GetFileName(pluginNameOrPath);
        return SanitizeDisplayName(fileName, fallbackName);
    }

    /// <summary>
    /// Builds the safe failed-cleaning row copy for a plugin result.
    /// </summary>
    /// <param name="pluginNameOrPath">The raw plugin filename or path candidate.</param>
    /// <returns>Safe failed-cleaning text preserving only the sanitized plugin filename and latest-log guidance.</returns>
    public static string CleaningFailedForPlugin(string? pluginNameOrPath) =>
        $"{SafePluginName(pluginNameOrPath)}: Cleaning failed. {LatestLogShort}";

    /// <summary>
    /// Builds the safe xEdit exception-log row copy for a plugin result without repeating exception-log content.
    /// </summary>
    /// <param name="pluginNameOrPath">The raw plugin filename or path candidate.</param>
    /// <returns>Safe xEdit failure text preserving only the sanitized plugin filename and latest-log guidance.</returns>
    public static string XEditReportedError(string? pluginNameOrPath) =>
        $"xEdit reported an error for {SafePluginName(pluginNameOrPath)}. {LatestLogShort}";

    /// <summary>
    /// Returns a trimmed candidate summary only when it lacks known unsafe path, command, exception, and stack-frame details.
    /// </summary>
    /// <param name="candidate">The proposed user-facing failure summary.</param>
    /// <param name="fallback">The safe fallback to use when the candidate is empty or unsafe.</param>
    /// <returns>The trimmed candidate if safe; otherwise the fallback.</returns>
    public static string SafeFailureSummary(string? candidate, string fallback)
    {
        var safeFallback = string.IsNullOrWhiteSpace(fallback) ? "Operation failed. See the latest AutoQAC log." : fallback.Trim();

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return safeFallback;
        }

        var trimmed = candidate.Trim();
        return ContainsUnsafeDetail(trimmed) ? safeFallback : trimmed;
    }

    private static bool ContainsUnsafeDetail(string candidate) =>
        candidate.Contains("System.", StringComparison.OrdinalIgnoreCase)
        || candidate.Contains("Exception", StringComparison.OrdinalIgnoreCase)
        || candidate.Contains("UnauthorizedAccess", StringComparison.OrdinalIgnoreCase)
        || candidate.Contains("IOException", StringComparison.OrdinalIgnoreCase)
        || candidate.Contains("Access denied", StringComparison.OrdinalIgnoreCase)
        || candidate.Contains(" at AutoQAC.", StringComparison.OrdinalIgnoreCase)
        || candidate.Contains("\\\\", StringComparison.Ordinal)
        || candidate.Contains("-QAC", StringComparison.OrdinalIgnoreCase)
        || candidate.Contains("-autoload", StringComparison.OrdinalIgnoreCase)
        || candidate.IndexOfAny(['\n', '\r', '\t']) >= 0
        || DriveRootedPathPattern.IsMatch(candidate)
        || NamespaceStackFramePattern.IsMatch(candidate)
        || ExeCommandMarkerPattern.IsMatch(candidate);

    private static string SanitizeDisplayName(string? candidate, string fallback)
    {
        var source = string.IsNullOrWhiteSpace(candidate) ? fallback : candidate;
        var sanitized = new string(source
            .Where(ch => !char.IsControl(ch)
                && !InvalidFileNameCharacters.Contains(ch)
                && !ExplicitUnsafeNameCharacters.Contains(ch))
            .ToArray())
            .Trim();

        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }
}
