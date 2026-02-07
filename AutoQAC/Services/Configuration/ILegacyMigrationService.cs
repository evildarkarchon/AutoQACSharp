using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AutoQAC.Services.Configuration;

/// <summary>
/// Detects and migrates legacy Python-era configuration files
/// (AutoQAC Config.yaml) to the current C# format (AutoQAC Settings.yaml).
/// </summary>
public interface ILegacyMigrationService
{
    /// <summary>
    /// Checks for legacy config files and migrates them if the C# config does not already exist.
    /// Migration is one-time bootstrap only -- no merge is performed if C# config exists.
    /// </summary>
    Task<MigrationResult> MigrateIfNeededAsync(CancellationToken ct = default);
}

/// <summary>
/// Result of a legacy configuration migration attempt.
/// </summary>
/// <param name="Attempted">Whether legacy files were found and migration was attempted.</param>
/// <param name="Success">Whether migration completed without errors.</param>
/// <param name="WarningMessage">Details on failure; null if success or not attempted.</param>
/// <param name="MigratedFiles">Which files were successfully migrated.</param>
/// <param name="FailedFiles">Which files failed to migrate.</param>
public sealed record MigrationResult(
    bool Attempted,
    bool Success,
    string? WarningMessage = null,
    List<string>? MigratedFiles = null,
    List<string>? FailedFiles = null)
{
    /// <summary>
    /// Convenience factory for the "nothing to do" case.
    /// </summary>
    public static MigrationResult NotNeeded() =>
        new(Attempted: false, Success: true);
}
