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
        if (config.MO2ModeEnabled && !string.IsNullOrEmpty(config.MO2ExecutablePath))
        {
            // ModOrganizer.exe run "path/to/xedit" -a "args"
            var mo2Args = $"run \"{xEditPath}\" -a \"{xEditArgs}\"";
            
            return new ProcessStartInfo
            {
                FileName = config.MO2ExecutablePath,
                Arguments = mo2Args,
                WorkingDirectory = Path.GetDirectoryName(config.MO2ExecutablePath)
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
        GameType.SkyrimSpecialEdition => "-SSE",
        GameType.Fallout4VR => "-FO4VR",
        GameType.SkyrimVR => "-SkyrimVR",
        _ => string.Empty
    };
}
