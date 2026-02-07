using YamlDotNet.Serialization;

namespace AutoQAC.Models.Configuration;

/// <summary>
/// Controls how old log/journal files are pruned.
/// </summary>
public enum RetentionMode
{
    /// <summary>
    /// Delete files older than <see cref="RetentionSettings.MaxAgeDays"/> days.
    /// </summary>
    AgeBased,

    /// <summary>
    /// Keep at most <see cref="RetentionSettings.MaxFileCount"/> files, deleting oldest first.
    /// </summary>
    CountBased
}

/// <summary>
/// Settings that control log and journal file retention policy.
/// </summary>
public sealed class RetentionSettings
{
    [YamlMember(Alias = "mode")]
    public RetentionMode Mode { get; set; } = RetentionMode.AgeBased;

    [YamlMember(Alias = "max_age_days")]
    public int MaxAgeDays { get; set; } = 30;

    [YamlMember(Alias = "max_file_count")]
    public int MaxFileCount { get; set; } = 50;
}
