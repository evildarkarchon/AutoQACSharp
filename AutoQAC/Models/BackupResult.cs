using System.Threading.Tasks;

namespace AutoQAC.Models;

/// <summary>
/// Result of a single plugin backup operation.
/// </summary>
public sealed class BackupResult
{
    public bool Success { get; private init; }
    public long FileSizeBytes { get; private init; }
    public string? Error { get; private init; }

    private BackupResult() { }

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

/// <summary>
/// Callback delegate invoked when a plugin backup fails during a cleaning session.
/// </summary>
/// <param name="pluginName">Name of the plugin that failed to back up.</param>
/// <param name="errorMessage">Description of the backup failure.</param>
/// <returns>The user's choice for how to proceed.</returns>
public delegate Task<BackupFailureChoice> BackupFailureCallback(string pluginName, string errorMessage);
