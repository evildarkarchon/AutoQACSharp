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
            var lines = await File.ReadAllLinesAsync(loadOrderPath, ct).ConfigureAwait(false);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.TrimStart().StartsWith("#")) continue;

                var rawName = line.Trim();
                var fileName = rawName;

                // Handle plugins.txt style with * for enabled
                if (fileName.StartsWith("*"))
                {
                    fileName = fileName.Substring(1);
                }

                plugins.Add(new PluginInfo
                {
                    FileName = fileName,
                    FullPath = fileName, // Placeholder -- not using dataFolderPath yet
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
        // Stub: old dual-path logic, returns wrong values for new tests
        if (Path.IsPathRooted(plugin.FullPath))
        {
            return File.Exists(plugin.FullPath) ? PluginWarningKind.None : PluginWarningKind.NotFound;
        }

        // Non-rooted: old behavior returned true (exists).
        // New tests expect NotFound for non-rooted paths.
        return PluginWarningKind.None;
    }
}
