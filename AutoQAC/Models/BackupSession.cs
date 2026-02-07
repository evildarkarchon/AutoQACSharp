using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AutoQAC.Models;

/// <summary>
/// Represents a single backup session containing one or more plugin backups.
/// Serialized to session.json in the session directory.
/// </summary>
public sealed record BackupSession
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; }

    [JsonPropertyName("game_type")]
    public string GameType { get; init; } = string.Empty;

    /// <summary>
    /// Absolute path to the session directory on disk.
    /// Not serialized -- populated at load time from the filesystem path.
    /// </summary>
    [JsonIgnore]
    public string SessionDirectory { get; init; } = string.Empty;

    [JsonPropertyName("plugins")]
    public List<BackupPluginEntry> Plugins { get; init; } = new();
}

/// <summary>
/// Represents a single backed-up plugin within a backup session.
/// </summary>
public sealed record BackupPluginEntry
{
    [JsonPropertyName("file_name")]
    public string FileName { get; init; } = string.Empty;

    [JsonPropertyName("original_path")]
    public string OriginalPath { get; init; } = string.Empty;

    [JsonPropertyName("file_size_bytes")]
    public long FileSizeBytes { get; init; }
}
