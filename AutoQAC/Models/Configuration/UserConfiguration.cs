using YamlDotNet.Serialization;

namespace AutoQAC.Models.Configuration;

public sealed class UserConfiguration
{
    [YamlMember(Alias = "Load_Order")]
    public LoadOrderConfig LoadOrder { get; set; } = new();

    [YamlMember(Alias = "Mod_Organizer")]
    public ModOrganizerConfig ModOrganizer { get; set; } = new();

    [YamlMember(Alias = "xEdit")]
    public XEditConfig XEdit { get; set; } = new();

    [YamlMember(Alias = "PACT_Settings")]
    public PactSettings Settings { get; set; } = new();
}

public sealed class LoadOrderConfig
{
    [YamlMember(Alias = "File")]
    public string? File { get; set; }
}

public sealed class ModOrganizerConfig
{
    [YamlMember(Alias = "Binary")]
    public string? Binary { get; set; }

    [YamlMember(Alias = "Install_Path")]
    public string? InstallPath { get; set; }
}

public sealed class XEditConfig
{
    [YamlMember(Alias = "Binary")]
    public string? Binary { get; set; }

    [YamlMember(Alias = "Install_Path")]
    public string? InstallPath { get; set; }
}

public sealed class PactSettings
{
    [YamlMember(Alias = "Journal_Expiration")]
    public int JournalExpiration { get; set; } = 7;

    [YamlMember(Alias = "Cleaning_Timeout")]
    public int CleaningTimeout { get; set; } = 300;

    [YamlMember(Alias = "CPU_Threshold")]
    public int CpuThreshold { get; set; } = 5;

    [YamlMember(Alias = "MO2Mode")]
    public bool MO2Mode { get; set; }

    [YamlMember(Alias = "Max_Concurrent_Subprocesses")]
    public int MaxConcurrentSubprocesses { get; set; } = 3;
}
