using System;
using System.Collections.Generic;
using System.Linq;
using YamlDotNet.Serialization;

namespace AutoQAC.Models.Configuration;

public sealed class UserConfiguration
{
    [YamlMember(Alias = "Selected_Game")] public string SelectedGame { get; set; } = "Unknown";

    [YamlMember(Alias = "Load_Order")] public LoadOrderConfig LoadOrder { get; set; } = new();

    [YamlMember(Alias = "Load_Order_Files")]
    public Dictionary<string, string> LoadOrderFileOverrides { get; set; } = new();

    [YamlMember(Alias = "Mod_Organizer")] public ModOrganizerConfig ModOrganizer { get; set; } = new();

    [YamlMember(Alias = "xEdit")] public XEditConfig XEdit { get; set; } = new();

    [YamlMember(Alias = "AutoQAC_Settings")]
    public AutoQacSettings Settings { get; set; } = new();

    [YamlMember(Alias = "Skip_Lists")] public Dictionary<string, List<string>> SkipLists { get; set; } = new();

    [YamlMember(Alias = "Game_Data_Folders")]
    public Dictionary<string, string> GameDataFolderOverrides { get; set; } = new();

    [YamlMember(Alias = "Mo2_Instance_Overrides")]
    public Dictionary<string, string> Mo2InstanceOverrides { get; set; } = new();

    [YamlMember(Alias = "Mo2_Profile_Selections")]
    public Dictionary<string, string> Mo2ProfileSelections { get; set; } = new();

    [YamlMember(Alias = "Log_Retention")] public RetentionSettings LogRetention { get; set; } = new();

    [YamlMember(Alias = "Backup")] public BackupSettings Backup { get; set; } = new();

    /// <summary>
    /// Returns a deep copy of this configuration with independent mutable containers and
    /// non-null defaults for every nested config object and collection. Manual copy is intentional
    /// (Phase 10 D-40..D-46): YamlDotNet serialization is reserved for actual disk persistence
    /// (UserConfigFileStore in Plan 02) and MUST NOT be used in normal in-memory clone paths
    /// (PERF-03). Behavior parity with the prior YAML round-trip clone is verified by the
    /// `Copy_BehaviorMatchesYamlRoundTrip_FullyPopulatedGraph` test in AutoQAC.Tests.
    /// </summary>
    public UserConfiguration Copy() => new()
    {
        SelectedGame = SelectedGame,
        LoadOrder = (LoadOrder).Copy(),
        LoadOrderFileOverrides =
            new Dictionary<string, string>(LoadOrderFileOverrides),
        ModOrganizer = (ModOrganizer).Copy(),
        XEdit = (XEdit).Copy(),
        Settings = (Settings).Copy(),
        SkipLists = (SkipLists)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToList(), StringComparer.Ordinal),
        GameDataFolderOverrides =
            new Dictionary<string, string>(GameDataFolderOverrides),
        Mo2InstanceOverrides = new Dictionary<string, string>(Mo2InstanceOverrides),
        Mo2ProfileSelections = new Dictionary<string, string>(Mo2ProfileSelections),
        LogRetention = (LogRetention).Copy(),
        Backup = (Backup).Copy()
    };
}

public sealed class LoadOrderConfig
{
    [YamlMember(Alias = "File")] public string? File { get; set; }

    /// <summary>Deep copy of LoadOrderConfig (no YAML round-trip; see Phase 10 D-43).</summary>
    public LoadOrderConfig Copy() => new() { File = File };
}

public sealed class ModOrganizerConfig
{
    [YamlMember(Alias = "Binary")] public string? Binary { get; set; }

    /// <summary>Deep copy of ModOrganizerConfig (no YAML round-trip; see Phase 10 D-43).</summary>
    public ModOrganizerConfig Copy() => new() { Binary = Binary };
}

public sealed class XEditConfig
{
    [YamlMember(Alias = "Binary")] public string? Binary { get; set; }

    /// <summary>Deep copy of XEditConfig (no YAML round-trip; see Phase 10 D-43).</summary>
    public XEditConfig Copy() => new() { Binary = Binary };
}

public sealed class AutoQacSettings
{
    [YamlMember(Alias = "Journal_Expiration")]
    public int JournalExpiration { get; set; } = 7;

    [YamlMember(Alias = "Cleaning_Timeout")]
    public int CleaningTimeout { get; set; } = 300;

    [YamlMember(Alias = "CPU_Threshold")] public int CpuThreshold { get; set; } = 5;

    [YamlMember(Alias = "MO2Mode")] public bool Mo2Mode { get; set; }

    [YamlMember(Alias = "Disable_Skip_Lists")]
    public bool DisableSkipLists { get; set; }

    /// <summary>Deep copy of AutoQacSettings preserving all scalar fields (no YAML round-trip; D-43).</summary>
    public AutoQacSettings Copy() => new()
    {
        JournalExpiration = JournalExpiration,
        CleaningTimeout = CleaningTimeout,
        CpuThreshold = CpuThreshold,
        Mo2Mode = Mo2Mode,
        DisableSkipLists = DisableSkipLists
    };
}
