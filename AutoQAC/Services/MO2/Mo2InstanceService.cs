using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;

namespace AutoQAC.Services.MO2;

public sealed class Mo2InstanceService(ILoggingService? logger = null, Func<string>? localAppDataResolver = null)
    : IMo2InstanceService
{
    private static readonly string[] PluginPatterns = ["*.esm", "*.esp", "*.esl"];

    private readonly Func<string> _localAppDataResolver = localAppDataResolver ?? (() => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public Task<Mo2InstanceInfo?> ResolveInstanceAsync(
        GameType game,
        string? mo2BinaryPath,
        string? overrideBaseDir,
        CancellationToken ct = default)
    {
        return Task.Run(() => ResolveInstance(game, mo2BinaryPath, overrideBaseDir, ct), ct);
    }

    public IReadOnlyList<string> GetProfiles(Mo2InstanceInfo instance)
    {
        if (!Directory.Exists(instance.ProfilesDirectory))
        {
            return [];
        }

        return Directory.EnumerateDirectories(instance.ProfilesDirectory)
            .Where(dir => File.Exists(Path.Combine(dir, "loadorder.txt")) || File.Exists(Path.Combine(dir, "plugins.txt")))
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string? ChooseProfile(Mo2InstanceInfo instance, IReadOnlyList<string> profiles, string? persistedProfile)
    {
        if (profiles.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(persistedProfile) && profiles.Contains(persistedProfile, StringComparer.OrdinalIgnoreCase))
        {
            return profiles.First(p => string.Equals(p, persistedProfile, StringComparison.OrdinalIgnoreCase));
        }

        var defaultProfile = profiles.FirstOrDefault(p => string.Equals(p, "Default", StringComparison.OrdinalIgnoreCase));
        if (defaultProfile is not null)
        {
            return defaultProfile;
        }

        if (!string.IsNullOrWhiteSpace(instance.IniSelectedProfile) && profiles.Contains(instance.IniSelectedProfile, StringComparer.OrdinalIgnoreCase))
        {
            return profiles.First(p => string.Equals(p, instance.IniSelectedProfile, StringComparison.OrdinalIgnoreCase));
        }

        return profiles[0];
    }

    public string? GetLoadOrderPath(Mo2InstanceInfo instance, string profile)
    {
        if (string.IsNullOrWhiteSpace(profile))
        {
            return null;
        }

        var profileDir = Path.Combine(instance.ProfilesDirectory, profile);
        var loadOrder = Path.Combine(profileDir, "loadorder.txt");
        if (File.Exists(loadOrder))
        {
            return loadOrder;
        }

        var plugins = Path.Combine(profileDir, "plugins.txt");
        return File.Exists(plugins) ? plugins : loadOrder;
    }

    public IReadOnlyDictionary<string, string> BuildPluginPathMap(
        Mo2InstanceInfo instance,
        string profile,
        string? gameDataFolder)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddPluginFiles(map, instance.OverwriteDirectory);

        var enabledMods = GetEnabledMods(instance, profile);
        if (enabledMods.Count == 0)
        {
            logger?.Warning("MO2 modlist.txt missing or empty for profile {Profile}; scanning all mods with degraded priority accuracy", profile);
            enabledMods = Directory.Exists(instance.ModsDirectory)
                ? Directory.EnumerateDirectories(instance.ModsDirectory)
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Cast<string>()
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : [];
        }

        foreach (var modName in enabledMods)
        {
            AddPluginFiles(map, Path.Combine(instance.ModsDirectory, modName));
        }

        AddPluginFiles(map, gameDataFolder);
        return map;
    }

    private Mo2InstanceInfo? ResolveInstance(GameType game, string? mo2BinaryPath, string? overrideBaseDir, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(overrideBaseDir) && Directory.Exists(overrideBaseDir))
        {
            var iniPath = Path.Combine(overrideBaseDir, "ModOrganizer.ini");
            return BuildInstance(overrideBaseDir, File.Exists(iniPath) ? iniPath : null, isAutoDetected: false);
        }

        var candidates = EnumerateCandidateIniFiles(mo2BinaryPath)
            .Select(path => new { Path = path, Values = ParseIni(path) })
            .Where(candidate => MatchesGame(candidate.Values.GetValueOrDefault("gameName"), game))
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        var portableIni = GetPortableIniPath(mo2BinaryPath);
        var selected = portableIni is not null
            ? candidates.FirstOrDefault(c => string.Equals(c.Path, portableIni, StringComparison.OrdinalIgnoreCase))
            : null;

        selected ??= candidates
            .OrderByDescending(c => File.GetLastWriteTimeUtc(c.Path))
            .First();

        return BuildInstance(Path.GetDirectoryName(selected.Path) ?? string.Empty, selected.Path, isAutoDetected: true, selected.Values);
    }

    private IEnumerable<string> EnumerateCandidateIniFiles(string? mo2BinaryPath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var portable = GetPortableIniPath(mo2BinaryPath);
        if (portable is not null && File.Exists(portable) && seen.Add(portable))
        {
            yield return portable;
        }

        var localAppData = _localAppDataResolver();
        var globalRoot = string.IsNullOrWhiteSpace(localAppData)
            ? null
            : Path.Combine(localAppData, "ModOrganizer");
        if (globalRoot is null || !Directory.Exists(globalRoot))
        {
            yield break;
        }

        foreach (var ini in Directory.EnumerateFiles(globalRoot, "ModOrganizer.ini", SearchOption.AllDirectories))
        {
            if (seen.Add(ini))
            {
                yield return ini;
            }
        }
    }

    private static string? GetPortableIniPath(string? mo2BinaryPath)
    {
        if (string.IsNullOrWhiteSpace(mo2BinaryPath))
        {
            return null;
        }

        var dir = Path.GetDirectoryName(mo2BinaryPath);
        return string.IsNullOrWhiteSpace(dir) ? null : Path.Combine(dir, "ModOrganizer.ini");
    }

    private static Mo2InstanceInfo BuildInstance(
        string fallbackBaseDirectory,
        string? iniPath,
        bool isAutoDetected,
        IReadOnlyDictionary<string, string>? iniValues = null)
    {
        iniValues ??= iniPath is not null && File.Exists(iniPath)
            ? ParseIni(iniPath)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var baseDirectory = ResolveMo2Path(
            iniValues.GetValueOrDefault("base_directory"),
            fallbackBaseDirectory,
            fallbackBaseDirectory);
        var modsDirectory = ResolveMo2Path(
            iniValues.GetValueOrDefault("mod_directory"),
            baseDirectory,
            Path.Combine(baseDirectory, "mods"));
        var profilesDirectory = ResolveMo2Path(
            iniValues.GetValueOrDefault("profiles_directory"),
            baseDirectory,
            Path.Combine(baseDirectory, "profiles"));
        var overwriteDirectory = ResolveMo2Path(
            iniValues.GetValueOrDefault("overwrite_directory"),
            baseDirectory,
            Path.Combine(baseDirectory, "overwrite"));

        return new Mo2InstanceInfo(
            baseDirectory,
            modsDirectory,
            profilesDirectory,
            overwriteDirectory,
            DecodeSelectedProfile(iniValues.GetValueOrDefault("selected_profile")),
            iniValues.GetValueOrDefault("gameName"),
            isAutoDetected,
            iniPath);
    }

    private static Dictionary<string, string> ParseIni(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#') || line.StartsWith('['))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        return values;
    }

    private static string ResolveMo2Path(string? value, string baseDirectory, string defaultPath)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultPath;
        }

        var expanded = value.Replace("%BASE_DIR%", baseDirectory, StringComparison.OrdinalIgnoreCase);
        expanded = Environment.ExpandEnvironmentVariables(expanded);
        return Path.IsPathRooted(expanded) ? expanded : Path.GetFullPath(Path.Combine(baseDirectory, expanded));
    }

    private static string? DecodeSelectedProfile(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        const string prefix = "@ByteArray(";
        if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && value.EndsWith(')'))
        {
            return value[prefix.Length..^1];
        }

        return value.StartsWith('@') ? null : value;
    }

    private static bool MatchesGame(string? gameName, GameType game) => game switch
    {
        GameType.SkyrimLe => string.Equals(gameName, "Skyrim", StringComparison.OrdinalIgnoreCase),
        GameType.SkyrimSe => string.Equals(gameName, "Skyrim Special Edition", StringComparison.OrdinalIgnoreCase),
        GameType.SkyrimVr => string.Equals(gameName, "Skyrim VR", StringComparison.OrdinalIgnoreCase),
        GameType.Fallout4 => string.Equals(gameName, "Fallout 4", StringComparison.OrdinalIgnoreCase),
        GameType.Fallout4Vr => string.Equals(gameName, "Fallout 4 VR", StringComparison.OrdinalIgnoreCase),
        GameType.Fallout3 => string.Equals(gameName, "Fallout 3", StringComparison.OrdinalIgnoreCase),
        GameType.FalloutNewVegas => gameName is not null &&
            (string.Equals(gameName, "New Vegas", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(gameName, "Fallout New Vegas", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(gameName, "TTW", StringComparison.OrdinalIgnoreCase)),
        GameType.Oblivion => string.Equals(gameName, "Oblivion", StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    private static List<string> GetEnabledMods(Mo2InstanceInfo instance, string profile)
    {
        var modListPath = Path.Combine(instance.ProfilesDirectory, profile, "modlist.txt");
        if (!File.Exists(modListPath))
        {
            return [];
        }

        var enabled = new List<string>();
        foreach (var rawLine in File.ReadLines(modListPath))
        {
            var line = rawLine.Trim();
            if (line.Length < 2 || line.StartsWith('#') || line.StartsWith("_separator", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line[0] == '+')
            {
                enabled.Add(line[1..]);
            }
        }

        return enabled;
    }

    private static void AddPluginFiles(Dictionary<string, string> map, string? root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return;
        }

        foreach (var pattern in PluginPatterns)
        {
            foreach (var path in Directory.EnumerateFiles(root, pattern, SearchOption.TopDirectoryOnly))
            {
                map.TryAdd(Path.GetFileName(path), path);
            }
        }
    }
}
