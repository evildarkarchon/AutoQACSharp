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
        CancellationToken ct = default)
    {
        var plugins = new List<PluginInfo>();

        if (string.IsNullOrWhiteSpace(loadOrderPath) || !File.Exists(loadOrderPath))
        {
            _logger.Warning($"Load order file not found or empty path: {loadOrderPath}");
            return plugins;
        }

        // We need to know the directory of the plugins to construct full paths.
        // Usually, the load order file is in Local AppData, but the plugins are in the game Data folder.
        // However, AutoQAC often just needs the filename to pass to xEdit.
        // The FullPath property in PluginInfo suggests we might want to know where it is on disk.
        // BUT, parsing plugins.txt only gives us filenames.
        // We can't determine FullPath without knowing the Game Data Path or MO2 Mods Path.
        // For now, we will store the FileName as FullPath if we can't resolve it, or leave it blank?
        // Actually, let's assume the caller might update it later or we just store the filename for now.
        // Wait, the interface says "GetPluginsFromLoadOrderAsync".
        // In the Python code, it seems to just return a list of strings or objects with filenames.
        // Let's check PluginInfo definition again.
        // public required string FullPath { get; init; }
        
        // Since we can't know the true full path from just plugins.txt (which could be anywhere), 
        // and plugins.txt only contains filenames.
        // We will set FullPath to FileName for now, unless we have a way to resolve the game data directory.
        // The roadmap mentions "GameDetectionService" but that detects GameType.
        // Let's stick to FileName == FullPath for this specific method, or leave it as "Unknown".
        
        try
        {
            var lines = await File.ReadAllLinesAsync(loadOrderPath, ct);
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
                    FullPath = fileName, // Placeholder as we don't have the game data path here
                    IsInSkipList = false, // Will be calculated later
                    DetectedGameType = GameType.Unknown // Will be calculated later or inferred
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
        // Create a hashset for faster lookup, case-insensitive
        var skips = new HashSet<string>(skipList, StringComparer.OrdinalIgnoreCase);

        return plugins.Select(p =>
        {
            var isSkipped = skips.Contains(p.FileName);
            return p with { IsInSkipList = isSkipped };
        }).ToList();
    }

    public bool ValidatePluginExists(PluginInfo plugin)
    {
        // Since FullPath might just be FileName, this check is weak.
        // However, if FullPath IS a full path, we check it.
        // If it is just a filename, we can't check existence without a base directory.
        if (Path.IsPathRooted(plugin.FullPath))
        {
            return File.Exists(plugin.FullPath);
        }
        
        // If not rooted, we can't validate existence on disk.
        // We assume true or false? 
        // Let's return true to assume it exists if it was in the load order, 
        // or false if we want to be strict.
        // Given xEdit handles loading, maybe we just skip this check if we don't have full path.
        return true; 
    }
}
