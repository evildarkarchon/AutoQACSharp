using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using AutoQAC.Models;
using AutoQAC.Services.State;

namespace AutoQAC.Services.Cleaning;

public interface IXEditCommandBuilder
{
    ProcessStartInfo? BuildCommand(PluginInfo plugin, GameType gameType);
}

public sealed class XEditCommandBuilder : IXEditCommandBuilder
{
    private readonly IStateService _stateService;

    public XEditCommandBuilder(IStateService stateService)
    {
        _stateService = stateService;
    }

    public ProcessStartInfo? BuildCommand(PluginInfo plugin, GameType gameType)
    {
        var config = _stateService.CurrentState;
        var xEditPath = config.XEditExecutablePath;
        
        if (string.IsNullOrEmpty(xEditPath)) return null;

        var args = new List<string>();

        // 1. Game Type Flag (if universal)
        var xEditName = Path.GetFileNameWithoutExtension(xEditPath);
        if (xEditName.StartsWith("xEdit", StringComparison.OrdinalIgnoreCase))
        {
             args.Add(GetGameFlag(gameType));
        }
        
        // 2. Core Flags
        args.Add("-QAC");
        args.Add("-autoexit");
        args.Add($"-autoload \"{plugin.FileName}\"");

        // 3. Partial Forms
        if (config.PartialFormsEnabled)
        {
             args.Add("-iknowwhatimdoing");
             args.Add("-allowmakepartial");
        }

        var xEditArgs = string.Join(" ", args);

        // 4. MO2 Wrapping
        if (config.Mo2ModeEnabled && !string.IsNullOrEmpty(config.Mo2ExecutablePath))
        {
            // Escape quotes in xEditArgs for the -a parameter
            // We need to ensure that quotes inside xEditArgs are escaped so they don't break the outer quotes of -a "..."
            var escapedXEditArgs = xEditArgs.Replace("\"", "\\\"");
            var mo2Args = $"run \"{xEditPath}\" -a \"{escapedXEditArgs}\"";
            
            return new ProcessStartInfo
            {
                FileName = config.Mo2ExecutablePath,
                Arguments = mo2Args,
                WorkingDirectory = Path.GetDirectoryName(config.Mo2ExecutablePath)
            };
        }
        else
        {
            return new ProcessStartInfo
            {
                FileName = xEditPath,
                Arguments = xEditArgs,
                WorkingDirectory = Path.GetDirectoryName(xEditPath)
            };
        }
    }

    private static string GetGameFlag(GameType gameType) => gameType switch
    {
        GameType.Fallout3 => "-FO3",
        GameType.FalloutNewVegas => "-FNV",
        GameType.Fallout4 => "-FO4",
        GameType.SkyrimLe => "-TES5",
        GameType.SkyrimSe => "-SSE",
        GameType.Fallout4Vr => "-FO4VR",
        GameType.SkyrimVr => "-SkyrimVR",
        GameType.Oblivion => "-TES4",
        _ => string.Empty
    };
}
