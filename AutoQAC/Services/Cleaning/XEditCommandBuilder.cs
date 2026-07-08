using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using AutoQAC.Models;
using AutoQAC.Services.State;

namespace AutoQAC.Services.Cleaning;

public interface IXEditCommandBuilder
{
    ProcessStartInfo? BuildCommand(PluginInfo plugin, GameType gameType);
}

public sealed class XEditCommandBuilder(IStateService stateService) : IXEditCommandBuilder
{
    /// <summary>
    /// Builds the direct xEdit or MO2-wrapped process start contract for cleaning a single plugin.
    /// Returns <see langword="null"/> when required launch configuration is missing or the game type is unknown.
    /// </summary>
    public ProcessStartInfo? BuildCommand(PluginInfo plugin, GameType gameType)
    {
        if (gameType == GameType.Unknown)
        {
            return null; // Safety: refuse to build command without known game type
        }

        var config = stateService.CurrentState;
        var xEditPath = config.XEditExecutablePath;

        if (string.IsNullOrEmpty(xEditPath)) return null;

        var args = BuildXEditArguments(plugin, gameType, config.PartialFormsEnabled, xEditPath);

        if (config.Mo2ModeEnabled)
        {
            if (string.IsNullOrWhiteSpace(config.Mo2ExecutablePath))
            {
                return null;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = config.Mo2ExecutablePath,
                WorkingDirectory = Path.GetDirectoryName(config.Mo2ExecutablePath),
                UseShellExecute = false
            };

            if (!string.IsNullOrWhiteSpace(config.Mo2Profile))
            {
                startInfo.ArgumentList.Add("-p");
                startInfo.ArgumentList.Add(config.Mo2Profile);
            }

            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add(xEditPath);
            startInfo.ArgumentList.Add("-a");
            startInfo.ArgumentList.Add(BuildMo2NestedPayload(args));

            return startInfo;
        }

        var directStartInfo = new ProcessStartInfo
        {
            FileName = xEditPath,
            WorkingDirectory = Path.GetDirectoryName(xEditPath),
            UseShellExecute = false
        };

        var xEditName = Path.GetFileNameWithoutExtension(xEditPath);
        if (xEditName.StartsWith("xEdit", StringComparison.OrdinalIgnoreCase))
        {
            directStartInfo.ArgumentList.Add(GetGameFlag(gameType));
        }

        directStartInfo.ArgumentList.Add("-QAC");
        directStartInfo.ArgumentList.Add("-autoexit");
        // Lock the parsed argv intent of the old -autoload "Plugin.esp" command string without shell quoting.
        directStartInfo.ArgumentList.Add("-autoload");
        directStartInfo.ArgumentList.Add(plugin.FileName);

        if (!config.PartialFormsEnabled) return directStartInfo;
        directStartInfo.ArgumentList.Add("-iknowwhatimdoing");
        directStartInfo.ArgumentList.Add("-allowmakepartial");

        return directStartInfo;
    }

    private static List<string> BuildXEditArguments(PluginInfo plugin, GameType gameType, bool partialFormsEnabled,
        string xEditPath)
    {
        var args = new List<string>();

        var xEditName = Path.GetFileNameWithoutExtension(xEditPath);
        if (xEditName.StartsWith("xEdit", StringComparison.OrdinalIgnoreCase))
        {
            args.Add(GetGameFlag(gameType));
        }

        args.Add("-QAC");
        args.Add("-autoexit");
        // Lock the parsed argv intent of the old -autoload "Plugin.esp" command string without shell quoting.
        args.Add("-autoload");
        args.Add(plugin.FileName);

        if (!partialFormsEnabled) return args;
        args.Add("-iknowwhatimdoing");
        args.Add("-allowmakepartial");

        return args;
    }

    private static string BuildMo2NestedPayload(List<string> args)
    {
        var formattedArgs = new List<string>(args.Count);
        formattedArgs.AddRange(args.Select(FormatMo2NestedArgument));

        return string.Join(" ", formattedArgs);
    }

    /// <summary>
    /// Formats one xEdit token for MO2's nested <c>-a</c> payload using Microsoft CRT quote/backslash rules.
    /// MO2 owns a second parser boundary, so this is intentionally different from direct mode's raw <see cref="ProcessStartInfo.ArgumentList"/> tokens.
    /// </summary>
    private static string FormatMo2NestedArgument(string argument)
    {
        var formatted = new System.Text.StringBuilder();
        formatted.Append('"');

        var backslashes = 0;
        foreach (var c in argument)
        {
            switch (c)
            {
                case '\\':

                    backslashes++;

                    continue;
                case '"':
                    formatted.Append('\\', backslashes * 2 + 1);

                    formatted.Append('"');
                    backslashes = 0;
                    continue;
            }

            formatted.Append('\\', backslashes);
            backslashes = 0;
            formatted.Append(c);
        }

        formatted.Append('\\', backslashes * 2);
        formatted.Append('"');
        return formatted.ToString();
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
