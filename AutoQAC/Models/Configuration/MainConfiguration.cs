using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace AutoQAC.Models.Configuration;

public sealed class MainConfiguration
{
    [YamlMember(Alias = "PACT_Data")]
    public PactData Data { get; set; } = new();
}

public sealed class PactData
{
    [YamlMember(Alias = "version")]
    public string Version { get; set; } = string.Empty;

    [YamlMember(Alias = "XEdit_Lists")]
    public Dictionary<string, List<string>> XEditLists { get; set; } = new();

    [YamlMember(Alias = "Skip_Lists")]
    public Dictionary<string, List<string>> SkipLists { get; set; } = new();
}
