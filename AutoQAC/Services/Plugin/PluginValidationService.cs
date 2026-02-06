using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;

namespace AutoQAC.Services.Plugin;

public sealed class PluginValidationService : IPluginValidationService
{
    private readonly ILoggingService _logger;

    /// <summary>
    /// Valid plugin file extensions (case-insensitive).
    /// </summary>
    private static readonly HashSet<string> ValidExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".esp", ".esm", ".esl"
    };

    /// <summary>
    /// Prefix characters used by plugins.txt and MO2 load order files.
    /// * = enabled, + = MO2 enabled mod, - = MO2 disabled mod.
    /// </summary>
    private static readonly HashSet<char> PrefixChars = new() { '*', '+', '-' };

    public PluginValidationService(ILoggingService logger)
    {
        _logger = logger;
    }

    public async Task<List<PluginInfo>> GetPluginsFromLoadOrderAsync(
        string loadOrderPath,
        string? dataFolderPath = null,
        CancellationToken ct = default)
    {
        var plugins = new List<PluginInfo>();

        if (string.IsNullOrWhiteSpace(loadOrderPath) || !File.Exists(loadOrderPath))
        {
            _logger.Warning($"Load order file not found or empty path: {loadOrderPath}");
            return plugins;
        }

        try
        {
            var lines = await ReadLinesWithEncodingDetectionAsync(loadOrderPath, ct)
                .ConfigureAwait(false);

            foreach (var line in lines)
            {
                var processed = ProcessLine(line);
                if (processed is null)
                    continue;

                var fullPath = dataFolderPath is not null
                    ? Path.Combine(dataFolderPath, processed)
                    : processed;

                plugins.Add(new PluginInfo
                {
                    FileName = processed,
                    FullPath = fullPath,
                    IsInSkipList = false,
                    DetectedGameType = GameType.Unknown
                });
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Failed to parse load order: {loadOrderPath}");
        }

        return plugins;
    }

    public List<PluginInfo> FilterSkippedPlugins(
        List<PluginInfo> plugins,
        List<string> skipList)
    {
        var skips = new HashSet<string>(skipList, StringComparer.OrdinalIgnoreCase);

        return plugins.Select(p =>
        {
            var isSkipped = skips.Contains(p.FileName);
            return p with { IsInSkipList = isSkipped };
        }).ToList();
    }

    public PluginWarningKind ValidatePluginFile(PluginInfo plugin)
    {
        if (string.IsNullOrEmpty(plugin.FullPath) || !Path.IsPathRooted(plugin.FullPath))
            return PluginWarningKind.NotFound;

        if (!File.Exists(plugin.FullPath))
            return PluginWarningKind.NotFound;

        try
        {
            var info = new FileInfo(plugin.FullPath);
            if (info.Length == 0)
                return PluginWarningKind.ZeroByte;

            // Try opening briefly to verify readability (not locked)
            using var stream = File.Open(
                plugin.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return PluginWarningKind.None;
        }
        catch (UnauthorizedAccessException)
        {
            return PluginWarningKind.Unreadable;
        }
        catch (IOException)
        {
            return PluginWarningKind.Unreadable;
        }
    }

    /// <summary>
    /// Read all lines from a file with BOM auto-detection.
    /// StreamReader with detectEncodingFromByteOrderMarks handles UTF-8 BOM,
    /// UTF-16 LE/BE BOM, and UTF-32 BOM automatically. Falls back to UTF-8 by default.
    /// </summary>
    private static async Task<string[]> ReadLinesWithEncodingDetectionAsync(
        string path, CancellationToken ct)
    {
        using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
        var content = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        return content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
    }

    /// <summary>
    /// Process a single line through the validation pipeline.
    /// Returns the validated plugin filename, or null if the line should be skipped.
    /// </summary>
    private string? ProcessLine(string line)
    {
        // Step 1: Skip blank/whitespace-only lines
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var trimmed = line.Trim();

        // Step 2: Skip comment lines (starting with #)
        if (trimmed.StartsWith('#'))
            return null;

        // Step 3: Strip leading prefix character (* + -) then re-trim
        if (trimmed.Length > 0 && PrefixChars.Contains(trimmed[0]))
        {
            trimmed = trimmed.Substring(1).Trim();
        }

        // Step 4: If empty after stripping prefix, skip (separator line)
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            _logger.Debug("Separator line stripped (empty after prefix removal)");
            return null;
        }

        // Step 5: Check for control characters (any char < 0x20 except whitespace chars that
        // would have been trimmed already -- specifically check for null bytes and other controls)
        if (trimmed.Any(c => c < 0x20 && c != '\t'))
        {
            _logger.Warning($"Skipping malformed load order entry (contains control characters): {SanitizeForLog(trimmed)}");
            return null;
        }

        // Step 6: Check for path separators (/ or \) -- indicates a full path, not a plugin name
        if (trimmed.Contains('\\') || trimmed.Contains('/'))
        {
            _logger.Warning($"Skipping malformed load order entry (contains path separators): {trimmed}");
            return null;
        }

        // Step 7: Validate file extension
        var extension = Path.GetExtension(trimmed);
        if (!ValidExtensions.Contains(extension))
        {
            _logger.Debug($"Skipping non-plugin entry (invalid extension '{extension}'): {trimmed}");
            return null;
        }

        return trimmed;
    }

    /// <summary>
    /// Sanitize a string for logging by replacing control characters with their escape sequences.
    /// </summary>
    private static string SanitizeForLog(string input)
    {
        return string.Concat(input.Select(c => c < 0x20 ? $"\\x{(int)c:X2}" : c.ToString()));
    }
}
