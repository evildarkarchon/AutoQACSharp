using YamlDotNet.Serialization;

namespace AutoQAC.Models.Configuration;

/// <summary>
/// Settings that control plugin backup before cleaning.
/// </summary>
public sealed class BackupSettings
{
    /// <summary>
    /// Whether plugin backup is enabled before cleaning. Enabled by default.
    /// </summary>
    [YamlMember(Alias = "enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Maximum number of backup sessions to retain. Oldest sessions beyond this count are deleted.
    /// </summary>
    [YamlMember(Alias = "max_sessions")]
    public int MaxSessions { get; set; } = 10;
}
