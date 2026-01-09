using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace AutoQAC.Models.Configuration;

public sealed class MainConfiguration
{
    [YamlMember(Alias = "AutoQAC_Data")]
    public AutoQacData Data { get; set; } = new();
}

public sealed class AutoQacData
{
    [YamlMember(Alias = "version")]
    public string Version { get; set; } = string.Empty;

    [YamlMember(Alias = "XEdit_Lists")]
    public Dictionary<string, List<string>> XEditLists { get; } = new();

    [YamlMember(Alias = "Skip_Lists")]
    public Dictionary<string, List<string>> SkipLists { get; } = new();
}
