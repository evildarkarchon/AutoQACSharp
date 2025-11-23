using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;

namespace AutoQAC.Services.GameDetection;

public sealed class GameDetectionService : IGameDetectionService
{
    private readonly ILoggingService _logger;

    private static readonly Dictionary<string, GameType> ExecutablePatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        { "fo3edit", GameType.Fallout3 },
        { "fo3edit64", GameType.Fallout3 },
        { "fnvedit", GameType.FalloutNewVegas },
        { "fnvedit64", GameType.FalloutNewVegas },
        { "fo4edit", GameType.Fallout4 },
        { "fo4edit64", GameType.Fallout4 },
        { "sseedit", GameType.SkyrimSpecialEdition },
        { "sseedit64", GameType.SkyrimSpecialEdition },
        { "tes5edit", GameType.SkyrimSpecialEdition },
        { "fo4vredit", GameType.Fallout4VR },
        { "fo4vredit64", GameType.Fallout4VR },
        { "skyrimvredit", GameType.SkyrimVR },
        { "tes5vredit", GameType.SkyrimVR }
    };

    private static readonly Dictionary<string, GameType> MasterFilePatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Skyrim.esm", GameType.SkyrimSpecialEdition },
        { "Fallout3.esm", GameType.Fallout3 },
        { "FalloutNV.esm", GameType.FalloutNewVegas },
        { "Fallout4.esm", GameType.Fallout4 },
        { "Fallout4_VR.esm", GameType.Fallout4VR }, // Assuming VR has a specific master or uses FO4
    };

    public GameDetectionService(ILoggingService logger)
    {
        _logger = logger;
    }

    public GameType DetectFromExecutable(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return GameType.Unknown;

        var fileName = Path.GetFileNameWithoutExtension(executablePath);
        // Remove version numbers or extra bits if necessary? 
        // The reference Python implementation splits by space and takes the first part in some cases,
        // but the patterns usually match the filename directly.
        
        // Try exact match first
        if (ExecutablePatterns.TryGetValue(fileName, out var gameType))
        {
            return gameType;
        }

        // Try partial match (e.g. "SSEEdit 4.0.4" -> "SSEEdit")
        foreach (var kvp in ExecutablePatterns)
        {
            if (fileName.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Value;
            }
        }

        return GameType.Unknown;
    }

    public async Task<GameType> DetectFromLoadOrderAsync(string loadOrderPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(loadOrderPath) || !File.Exists(loadOrderPath))
        {
            return GameType.Unknown;
        }

        try
        {
            var lines = await File.ReadAllLinesAsync(loadOrderPath, ct);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.TrimStart().StartsWith("#")) continue;

                // Clean up the line to get the plugin name
                // Remove leading * (enabled flag in plugins.txt)
                var pluginName = line.Trim();
                if (pluginName.StartsWith("*"))
                {
                    pluginName = pluginName.Substring(1);
                }

                // Check if this plugin is a known master
                if (MasterFilePatterns.TryGetValue(pluginName, out var gameType))
                {
                    return gameType;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Failed to read load order file: {loadOrderPath}");
        }

        return GameType.Unknown;
    }

    public bool IsValidGameType(GameType gameType)
    {
        return gameType != GameType.Unknown;
    }

    public string GetGameDisplayName(GameType gameType) => gameType switch
    {
        GameType.Fallout3 => "Fallout 3",
        GameType.FalloutNewVegas => "Fallout: New Vegas",
        GameType.Fallout4 => "Fallout 4",
        GameType.SkyrimSpecialEdition => "Skyrim Special Edition",
        GameType.Fallout4VR => "Fallout 4 VR",
        GameType.SkyrimVR => "Skyrim VR",
        _ => "Unknown"
    };

    public string GetDefaultLoadOrderFileName(GameType gameType)
    {
        // Usually plugins.txt or loadorder.txt
        // But if we strictly need to know what file xEdit uses, it depends on the game mode.
        // For AutoQAC, we usually ask user to provide the file.
        // This method might be used for hints.
        return "plugins.txt";
    }
}
