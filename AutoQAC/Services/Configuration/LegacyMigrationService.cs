using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models.Configuration;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AutoQAC.Services.Configuration;

/// <summary>
/// Migrates legacy Python-era configuration files with proper backup-then-delete order.
/// Migration only runs when no C# config exists (one-time bootstrap, not a merge).
/// </summary>
public sealed class LegacyMigrationService : ILegacyMigrationService
{
    private readonly ILoggingService _logger;
    private readonly string _configDirectory;
    private const string LegacyConfigFile = "AutoQAC Config.yaml";
    private const string CurrentConfigFile = "AutoQAC Settings.yaml";
    private const string BackupSubdirectory = "migration_backup";

    private readonly ISerializer _serializer;
    private readonly IDeserializer _deserializer;

    public LegacyMigrationService(ILoggingService logger, string? configDirectory = null)
    {
        _logger = logger;
        _configDirectory = configDirectory ?? ResolveConfigDirectory();

        _serializer = new SerializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .Build();
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    private string ResolveConfigDirectory()
    {
        var baseDir = AppContext.BaseDirectory;

#if DEBUG
        var current = new DirectoryInfo(baseDir);
        for (int i = 0; i < 6 && current != null; i++)
        {
            var candidate = Path.Combine(current.FullName, "AutoQAC Data");
            if (Directory.Exists(candidate))
                return candidate;
            current = current.Parent;
        }
#endif

        return Path.Combine(baseDir, "AutoQAC Data");
    }

    public async Task<MigrationResult> MigrateIfNeededAsync(CancellationToken ct = default)
    {
        var legacyPath = Path.Combine(_configDirectory, LegacyConfigFile);
        var currentPath = Path.Combine(_configDirectory, CurrentConfigFile);

        // Step 1: Check if legacy file exists
        if (!File.Exists(legacyPath))
        {
            _logger.Debug("[Migration] No legacy config file found at {Path}", legacyPath);
            return MigrationResult.NotNeeded();
        }

        // Step 2: Check if C# config already exists -- skip migration (one-time bootstrap, not merge)
        if (File.Exists(currentPath))
        {
            _logger.Information("[Migration] C# config already exists at {Path}, skipping migration (bootstrap only, no merge)", currentPath);
            return MigrationResult.NotNeeded();
        }

        _logger.Information("[Migration] Found legacy config at {LegacyPath}, beginning migration", legacyPath);

        // Step 3: Read and validate legacy file
        UserConfiguration migratedConfig;
        try
        {
            var legacyContent = await File.ReadAllTextAsync(legacyPath, ct).ConfigureAwait(false);
            migratedConfig = _deserializer.Deserialize<UserConfiguration>(legacyContent);
            if (migratedConfig == null)
            {
                return new MigrationResult(
                    Attempted: true,
                    Success: false,
                    WarningMessage: "Legacy config file was empty or could not be parsed.",
                    FailedFiles: [LegacyConfigFile]);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "[Migration] Failed to parse legacy config file");
            return new MigrationResult(
                Attempted: true,
                Success: false,
                WarningMessage: $"Failed to parse legacy config: {ex.Message}",
                FailedFiles: [LegacyConfigFile]);
        }

        // Step 4: Write migrated config to current location
        try
        {
            var content = _serializer.Serialize(migratedConfig);
            await File.WriteAllTextAsync(currentPath, content, ct).ConfigureAwait(false);
            _logger.Information("[Migration] Wrote migrated config to {Path}", currentPath);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "[Migration] Failed to write migrated config");
            return new MigrationResult(
                Attempted: true,
                Success: false,
                WarningMessage: $"Failed to write migrated config: {ex.Message}",
                FailedFiles: [LegacyConfigFile]);
        }

        // Step 5: BACKUP legacy file BEFORE deletion (CRITICAL: backup-then-delete order)
        try
        {
            var backupDir = Path.Combine(_configDirectory, BackupSubdirectory);
            Directory.CreateDirectory(backupDir);
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
            var backupFileName = $"{timestamp}_{LegacyConfigFile}";
            var backupPath = Path.Combine(backupDir, backupFileName);
            File.Copy(legacyPath, backupPath, overwrite: true);
            _logger.Information("[Migration] Backed up legacy config to {BackupPath}", backupPath);
        }
        catch (Exception ex)
        {
            _logger.Warning("[Migration] Failed to create backup of legacy config: {Message}. Keeping original file.", ex.Message);
            // Do NOT delete the original if backup failed
            return new MigrationResult(
                Attempted: true,
                Success: false,
                WarningMessage: $"Migration partially succeeded: config was migrated but backup of original failed ({ex.Message}). Original file was kept for safety.",
                MigratedFiles: [LegacyConfigFile]);
        }

        // Step 6: Delete legacy file (only after successful backup)
        try
        {
            File.Delete(legacyPath);
            _logger.Information("[Migration] Deleted legacy config file {Path}", legacyPath);
        }
        catch (Exception ex)
        {
            _logger.Warning("[Migration] Failed to delete legacy config after backup: {Message}", ex.Message);
            // Migration succeeded even if deletion fails -- the backup exists, new config exists
            return new MigrationResult(
                Attempted: true,
                Success: true,
                WarningMessage: $"Migration succeeded but could not remove the original legacy file: {ex.Message}",
                MigratedFiles: [LegacyConfigFile]);
        }

        _logger.Information("[Migration] Migration completed successfully");
        return new MigrationResult(
            Attempted: true,
            Success: true,
            MigratedFiles: [LegacyConfigFile]);
    }
}
