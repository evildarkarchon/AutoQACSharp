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
        // Oblivion
        { "tes4edit", GameType.Oblivion },
        { "tes4edit64", GameType.Oblivion },
        // Skyrim LE - note: TES5Edit can be used for both LE and SE
        // We map to SkyrimLe for the original tool name
        { "tes5edit", GameType.SkyrimLe },
        { "tes5edit64", GameType.SkyrimLe },
        // Skyrim SE - specific SSEEdit tool
        { "sseedit", GameType.SkyrimSe },
        { "sseedit64", GameType.SkyrimSe },
        // Skyrim VR
        { "skyrimvredit", GameType.SkyrimVr },
        { "tes5vredit", GameType.SkyrimVr },
        // Fallout 3
        { "fo3edit", GameType.Fallout3 },
        { "fo3edit64", GameType.Fallout3 },
        // Fallout New Vegas
        { "fnvedit", GameType.FalloutNewVegas },
        { "fnvedit64", GameType.FalloutNewVegas },
        // Fallout 4
        { "fo4edit", GameType.Fallout4 },
        { "fo4edit64", GameType.Fallout4 },
        // Fallout 4 VR
        { "fo4vredit", GameType.Fallout4Vr },
        { "fo4vredit64", GameType.Fallout4Vr }
    };

    private static readonly Dictionary<string, GameType> MasterFilePatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Oblivion.esm", GameType.Oblivion },
        // Skyrim.esm is shared between LE and SE - we default to SE (more common)
        { "Skyrim.esm", GameType.SkyrimSe },
        { "Fallout3.esm", GameType.Fallout3 },
        { "FalloutNV.esm", GameType.FalloutNewVegas },
        { "Fallout4.esm", GameType.Fallout4 },
        { "Fallout4_VR.esm", GameType.Fallout4Vr },
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
            var lines = await File.ReadAllLinesAsync(loadOrderPath, ct).ConfigureAwait(false);
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

    public GameVariant DetectVariant(GameType baseGame, IReadOnlyList<string> pluginNames)
    {
        if (pluginNames == null || pluginNames.Count == 0)
            return GameVariant.None;

        if (baseGame == GameType.FalloutNewVegas)
        {
            if (pluginNames.Any(p => p.Equals("TaleOfTwoWastelands.esm", StringComparison.OrdinalIgnoreCase)))
            {
                _logger.Information("Detected TTW (Tale of Two Wastelands) variant");
                return GameVariant.TTW;
            }
        }

        if (baseGame == GameType.SkyrimSe)
        {
            if (pluginNames.Any(p =>
                p.Equals("Enderal - Forgotten Stories.esm", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("Enderal.esm", StringComparison.OrdinalIgnoreCase)))
            {
                _logger.Information("Detected Enderal variant");
                return GameVariant.Enderal;
            }
        }

        return GameVariant.None;
    }

    public bool IsValidGameType(GameType gameType)
    {
        return gameType != GameType.Unknown;
    }

    public string GetGameDisplayName(GameType gameType) => gameType switch
    {
        GameType.Oblivion => "The Elder Scrolls IV: Oblivion",
        GameType.SkyrimLe => "Skyrim (Legendary Edition)",
        GameType.SkyrimSe => "Skyrim Special Edition",
        GameType.SkyrimVr => "Skyrim VR",
        GameType.Fallout3 => "Fallout 3",
        GameType.FalloutNewVegas => "Fallout: New Vegas",
        GameType.Fallout4 => "Fallout 4",
        GameType.Fallout4Vr => "Fallout 4 VR",
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
