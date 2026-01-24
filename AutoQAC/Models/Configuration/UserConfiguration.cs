using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace AutoQAC.Models.Configuration;

public sealed class UserConfiguration
{
    [YamlMember(Alias = "Selected_Game")] public string SelectedGame { get; set; } = "Unknown";

    [YamlMember(Alias = "Load_Order")] public LoadOrderConfig LoadOrder { get; set; } = new();

    [YamlMember(Alias = "Mod_Organizer")] public ModOrganizerConfig ModOrganizer { get; set; } = new();

    [YamlMember(Alias = "xEdit")] public XEditConfig XEdit { get; set; } = new();

    [YamlMember(Alias = "AutoQAC_Settings")]
    public AutoQacSettings Settings { get; set; } = new();

    [YamlMember(Alias = "Skip_Lists")] public Dictionary<string, List<string>> SkipLists { get; set; } = new();

    [YamlMember(Alias = "Game_Data_Folders")]
    public Dictionary<string, string> GameDataFolderOverrides { get; set; } = new();
}

public sealed class LoadOrderConfig
{
    [YamlMember(Alias = "File")] public string? File { get; set; }
}

public sealed class ModOrganizerConfig
{
    [YamlMember(Alias = "Binary")] public string? Binary { get; set; }

    [YamlMember(Alias = "Install_Path")] public string? InstallPath { get; set; }
}

public sealed class XEditConfig
{
    [YamlMember(Alias = "Binary")] public string? Binary { get; set; }

    [YamlMember(Alias = "Install_Path")] public string? InstallPath { get; set; }
}

public sealed class AutoQacSettings
{
    [YamlMember(Alias = "Journal_Expiration")]
    public int JournalExpiration { get; set; } = 7;

    [YamlMember(Alias = "Cleaning_Timeout")]
    public int CleaningTimeout { get; set; } = 300;

    [YamlMember(Alias = "CPU_Threshold")] public int CpuThreshold { get; set; } = 5;

    [YamlMember(Alias = "MO2Mode")] public bool Mo2Mode { get; set; }

    [YamlMember(Alias = "Max_Concurrent_Subprocesses")]
    public int MaxConcurrentSubprocesses { get; set; } = 3;

    [YamlMember(Alias = "Disable_Skip_Lists")]
    public bool DisableSkipLists { get; set; }
}
