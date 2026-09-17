using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using AutoQAC.Models;
using AutoQAC.Models.Configuration;

namespace AutoQAC.Services.Configuration;

/// <summary>Internal value edits and comparisons for Discovery settings; callers never manipulate edit keys.</summary>
internal static class DiscoverySettingsChanges
{
    /// <summary>Validates a complete intent before it can supersede valid work or submit any persistence.</summary>
    internal static DiscoverySettingsChangeFailure? Validate(DiscoverySettingsIntent intent)
    {
        return intent switch
        {
            DiscoverySettingsIntent.SelectGame game when !Enum.IsDefined(game.GameType) =>
                Failure(DiscoverySettingsChangeFailureKind.InvalidGame, "Choose a supported game."),
            DiscoverySettingsIntent.SetLoadOrderPath path when string.IsNullOrWhiteSpace(path.LoadOrderPath) || !File.Exists(path.LoadOrderPath) =>
                Failure(DiscoverySettingsChangeFailureKind.InvalidLoadOrderPath, "The selected load order file is missing."),
            DiscoverySettingsIntent.SetGameDataFolderOverride folder when !ValidFolder(folder.FolderPath) =>
                Failure(DiscoverySettingsChangeFailureKind.InvalidGameDataFolder, "The selected game data folder is missing."),
            DiscoverySettingsIntent.SetMo2InstanceOverride folder when !ValidFolder(folder.FolderPath) =>
                Failure(DiscoverySettingsChangeFailureKind.InvalidMo2InstanceFolder, "The selected MO2 instance folder is missing."),
            DiscoverySettingsIntent.SetMo2ExecutablePath path when string.IsNullOrWhiteSpace(path.ExecutablePath) || !ValidExecutable(path.ExecutablePath) =>
                Failure(DiscoverySettingsChangeFailureKind.InvalidMo2ExecutablePath, "The selected Mod Organizer executable is missing."),
            DiscoverySettingsIntent.ApplySettings editor => ValidateEditor(editor),
            _ => null
        };
    }

    /// <summary>Applies only requested edits to the latest snapshot and returns their conflict keys.</summary>
    internal static HashSet<string> Apply(DiscoverySettingsIntent intent, UserConfiguration config)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        switch (intent)
        {
            case DiscoverySettingsIntent.SelectGame game:
                config.SelectedGame = game.GameType.ToString(); keys.Add("game"); break;
            case DiscoverySettingsIntent.SetMo2Mode mode:
                config.Settings.Mo2Mode = mode.Enabled; keys.Add("mo2Mode"); break;
            case DiscoverySettingsIntent.SetDisableSkipLists skip:
                config.Settings.DisableSkipLists = skip.Disabled; keys.Add("skipDisabled"); break;
            case DiscoverySettingsIntent.SetMo2ExecutablePath path:
                config.ModOrganizer.Binary = path.ExecutablePath; keys.Add("mo2Executable"); break;
            case DiscoverySettingsIntent.SetLoadOrderPath path:
                Set(config.LoadOrderFileOverrides, path.GameType, path.LoadOrderPath);
                config.LoadOrder.File = path.LoadOrderPath;
                keys.Add("loadOrder"); keys.Add("loadOrder/" + GameKey(path.GameType)); break;
            case DiscoverySettingsIntent.SetGameDataFolderOverride folder:
                Set(config.GameDataFolderOverrides, folder.GameType, folder.FolderPath);
                keys.Add("data/" + GameKey(folder.GameType)); break;
            case DiscoverySettingsIntent.SetMo2InstanceOverride folder:
                Set(config.Mo2InstanceOverrides, folder.GameType, folder.FolderPath);
                keys.Add("instance/" + GameKey(folder.GameType)); break;
            case DiscoverySettingsIntent.SetMo2Profile profile:
                Set(config.Mo2ProfileSelections, profile.GameType, profile.ProfileName);
                keys.Add("profile/" + GameKey(profile.GameType)); break;
            case DiscoverySettingsIntent.SetSkipList skipList:
                config.SkipLists[GameKey(skipList.GameType)] = skipList.Plugins.ToList();
                keys.Add("skip/" + GameKey(skipList.GameType)); break;
            case DiscoverySettingsIntent.Reset:
                var defaults = new UserConfiguration();
                config.SelectedGame = defaults.SelectedGame;
                config.LoadOrder = defaults.LoadOrder;
                config.LoadOrderFileOverrides = defaults.LoadOrderFileOverrides;
                config.ModOrganizer = defaults.ModOrganizer;
                config.XEdit = defaults.XEdit;
                config.Settings = defaults.Settings;
                config.SkipLists = defaults.SkipLists;
                config.GameDataFolderOverrides = defaults.GameDataFolderOverrides;
                config.Mo2InstanceOverrides = defaults.Mo2InstanceOverrides;
                config.Mo2ProfileSelections = defaults.Mo2ProfileSelections;
                config.LogRetention = defaults.LogRetention;
                config.Backup = defaults.Backup;
                keys.Add("reset"); break;
            case DiscoverySettingsIntent.ApplySettings editor:
                ApplyEditor(editor.Baseline, editor.Requested, config, keys); break;
            default:
                throw new ArgumentOutOfRangeException(nameof(intent), intent, "Unknown Discovery settings intent.");
        }
        return keys;
    }

    /// <summary>Merges individual editor fields so unchanged dialog values cannot overwrite concurrent choices.</summary>
    private static void ApplyEditor(UserConfiguration baseline, UserConfiguration edited, UserConfiguration target, HashSet<string> keys)
    {
        void Edit<T>(string key, T before, T after, Action<T> assign)
        {
            if (EqualityComparer<T>.Default.Equals(before, after)) return;
            keys.Add(key);
            assign(after);
        }
        Edit("mo2Mode", baseline.Settings.Mo2Mode, edited.Settings.Mo2Mode, v => target.Settings.Mo2Mode = v);
        Edit("skipDisabled", baseline.Settings.DisableSkipLists, edited.Settings.DisableSkipLists, v => target.Settings.DisableSkipLists = v);
        Edit("mo2Executable", baseline.ModOrganizer.Binary, edited.ModOrganizer.Binary, v => target.ModOrganizer.Binary = v);
        Edit("xEdit", baseline.XEdit.Binary, edited.XEdit.Binary, v => target.XEdit.Binary = v);
        Edit("loadOrder", baseline.LoadOrder.File, edited.LoadOrder.File, v => target.LoadOrder.File = v);
        if (baseline.LoadOrder.File != edited.LoadOrder.File &&
            Enum.TryParse<GameType>(target.SelectedGame, out var selectedGame) && selectedGame != GameType.Unknown)
        {
            // Discovery prefers the selected game's override; updating only the global fallback would accept old rows.
            Set(target.LoadOrderFileOverrides, selectedGame, edited.LoadOrder.File);
            keys.Add("loadOrder/" + GameKey(selectedGame));
        }
        Edit("timeout", baseline.Settings.CleaningTimeout, edited.Settings.CleaningTimeout, v => target.Settings.CleaningTimeout = v);
        Edit("journal", baseline.Settings.JournalExpiration, edited.Settings.JournalExpiration, v => target.Settings.JournalExpiration = v);
        Edit("cpu", baseline.Settings.CpuThreshold, edited.Settings.CpuThreshold, v => target.Settings.CpuThreshold = v);
        Edit("retentionMode", baseline.LogRetention.Mode, edited.LogRetention.Mode, v => target.LogRetention.Mode = v);
        Edit("retentionAge", baseline.LogRetention.MaxAgeDays, edited.LogRetention.MaxAgeDays, v => target.LogRetention.MaxAgeDays = v);
        Edit("retentionCount", baseline.LogRetention.MaxFileCount, edited.LogRetention.MaxFileCount, v => target.LogRetention.MaxFileCount = v);
        Edit("backupEnabled", baseline.Backup.Enabled, edited.Backup.Enabled, v => target.Backup.Enabled = v);
        Edit("backupCount", baseline.Backup.MaxSessions, edited.Backup.MaxSessions, v => target.Backup.MaxSessions = v);
    }

    /// <summary>Compares discovery meaning independently of dictionary insertion order and unrelated editor fields.</summary>
    internal static string Fingerprint(UserConfiguration config)
    {
        var values = new SortedDictionary<string, string?>(StringComparer.Ordinal)
        {
            ["game"] = config.SelectedGame,
            ["mo2Mode"] = config.Settings.Mo2Mode.ToString(CultureInfo.InvariantCulture),
            ["skipDisabled"] = config.Settings.DisableSkipLists.ToString(CultureInfo.InvariantCulture),
            ["mo2Executable"] = config.ModOrganizer.Binary,
            ["loadOrder"] = config.LoadOrder.File
        };
        foreach (var pair in config.LoadOrderFileOverrides) values["loadOrder/" + pair.Key] = pair.Value;
        foreach (var pair in config.GameDataFolderOverrides) values["data/" + pair.Key] = pair.Value;
        foreach (var pair in config.Mo2InstanceOverrides) values["instance/" + pair.Key] = pair.Value;
        foreach (var pair in config.Mo2ProfileSelections) values["profile/" + pair.Key] = pair.Value;
        foreach (var pair in config.SkipLists)
            values["skip/" + pair.Key] = JsonSerializer.Serialize(pair.Value.OrderBy(v => v, StringComparer.OrdinalIgnoreCase));
        return JsonSerializer.Serialize(values);
    }

    /// <summary>Validates changed editor paths only; existing invalid fields are not silently rewritten.</summary>
    private static DiscoverySettingsChangeFailure? ValidateEditor(DiscoverySettingsIntent.ApplySettings editor)
    {
        if (editor.Baseline.ModOrganizer.Binary != editor.Requested.ModOrganizer.Binary && !ValidExecutable(editor.Requested.ModOrganizer.Binary))
            return Failure(DiscoverySettingsChangeFailureKind.InvalidMo2ExecutablePath, "The selected Mod Organizer executable is missing.");
        if (editor.Baseline.LoadOrder.File != editor.Requested.LoadOrder.File && !string.IsNullOrWhiteSpace(editor.Requested.LoadOrder.File) && !File.Exists(editor.Requested.LoadOrder.File))
            return Failure(DiscoverySettingsChangeFailureKind.InvalidLoadOrderPath, "The selected load order file is missing.");
        return null;
    }

    /// <summary>Updates a game-scoped override, treating an empty choice as removing the override.</summary>
    private static void Set(Dictionary<string, string> target, GameType game, string? value)
    {
        var key = GameKey(game);
        if (string.IsNullOrWhiteSpace(value)) target.Remove(key);
        else target[key] = value;
    }

    internal static string GameKey(GameType game) => ConfigurationService.GetGameKey(game);

    /// <summary>Editor-only operational preferences do not invalidate rows or restart Issue approximation.</summary>
    internal static bool RequiresPublication(DiscoverySettingsIntent intent, HashSet<string> keys) =>
        intent is not DiscoverySettingsIntent.ApplySettings || keys.Any(key =>
            key is "mo2Mode" or "skipDisabled" or "mo2Executable" or "loadOrder" || key.StartsWith("loadOrder/", StringComparison.Ordinal));
    private static bool ValidFolder(string? value) => string.IsNullOrWhiteSpace(value) || Directory.Exists(value);
    private static bool ValidExecutable(string? value) => string.IsNullOrWhiteSpace(value) ||
        File.Exists(value) && value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
    private static DiscoverySettingsChangeFailure Failure(DiscoverySettingsChangeFailureKind kind, string message) => new(kind, message);
}
