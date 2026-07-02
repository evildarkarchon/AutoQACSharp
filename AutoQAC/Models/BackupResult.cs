namespace AutoQAC.Models;

/// <summary>
/// Result of a single plugin backup operation.
/// </summary>
public sealed class BackupResult
{
    public bool Success { get; private init; }
    public long FileSizeBytes { get; private init; }
    public string? Error { get; private init; }

    private BackupResult()
    {
    }

    /// <summary>
    /// Creates a successful backup result.
    /// </summary>
    public static BackupResult Ok(long fileSizeBytes) => new()
    {
        Success = true,
        FileSizeBytes = fileSizeBytes
    };

    /// <summary>
    /// Creates a failed backup result with an error message.
    /// </summary>
    public static BackupResult Failure(string error) => new()
    {
        Success = false,
        Error = error
    };
}

/// <summary>
/// User choice when a backup operation fails for a plugin.
/// </summary>
public enum BackupFailureChoice
{
    /// <summary>Skip this plugin and continue with the next one.</summary>
    SkipPlugin,

    /// <summary>Abort the entire cleaning session.</summary>
    AbortSession,

    /// <summary>Continue cleaning this plugin without a backup.</summary>
    ContinueWithoutBackup
}
