using AutoQAC.Infrastructure.Logging;
using AutoQAC.Services.Configuration;
using FluentAssertions;
using NSubstitute;

namespace AutoQAC.Tests.Services;

/// <summary>
/// Unit tests for <see cref="LegacyMigrationService"/> covering all migration paths:
/// no-legacy-file, C#-config-exists, valid migration, invalid YAML, empty file,
/// write failure, and backup failure.
///
/// Uses the injectable configDirectory constructor parameter with temp directories
/// for file system isolation. This is standard C# unit test practice -- the constructor
/// parameter makes the service's file paths injectable.
/// </summary>
public sealed class LegacyMigrationServiceTests : IDisposable
{
    private readonly ILoggingService _mockLogger;
    private readonly string _testDirectory;

    public LegacyMigrationServiceTests()
    {
        _mockLogger = Substitute.For<ILoggingService>();
        _testDirectory = Path.Combine(Path.GetTempPath(), $"autoqac_migration_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }

    private LegacyMigrationService CreateSut() =>
        new(_mockLogger, _testDirectory);

    private string LegacyConfigPath => Path.Combine(_testDirectory, "AutoQAC Config.yaml");
    private string CurrentConfigPath => Path.Combine(_testDirectory, "AutoQAC Settings.yaml");
    private string BackupSubdirectory => Path.Combine(_testDirectory, "migration_backup");

    #region No Legacy File

    /// <summary>
    /// When no legacy config file exists, migration should not be attempted.
    /// </summary>
    [Fact]
    public async Task MigrateIfNeeded_NoLegacyFile_ReturnsNotNeeded()
    {
        // Arrange -- empty temp directory, no legacy file
        var sut = CreateSut();

        // Act
        var result = await sut.MigrateIfNeededAsync();

        // Assert
        result.Attempted.Should().BeFalse("no legacy file means no migration needed");
        result.Success.Should().BeTrue("not-needed is a success condition");
        result.WarningMessage.Should().BeNull();
        result.MigratedFiles.Should().BeNull();
        result.FailedFiles.Should().BeNull();
    }

    #endregion

    #region C# Config Already Exists

    /// <summary>
    /// When both legacy and current C# config files exist, migration should skip
    /// (one-time bootstrap only, no merge).
    /// </summary>
    [Fact]
    public async Task MigrateIfNeeded_CSharpConfigExists_ReturnsNotNeeded()
    {
        // Arrange -- both legacy and current config files exist
        await File.WriteAllTextAsync(LegacyConfigPath, "XEditPath: C:\\SSEEdit\\SSEEdit.exe");
        await File.WriteAllTextAsync(CurrentConfigPath, "Selected_Game: SSE");
        var sut = CreateSut();

        // Act
        var result = await sut.MigrateIfNeededAsync();

        // Assert
        result.Attempted.Should().BeFalse("migration is one-time bootstrap only, skipped when C# config exists");
        result.Success.Should().BeTrue();

        // Legacy file should NOT be deleted (migration was skipped)
        File.Exists(LegacyConfigPath).Should().BeTrue("legacy file should be untouched when migration is skipped");
    }

    #endregion

    #region Valid Migration

    /// <summary>
    /// When a valid legacy config exists and no current config exists, migration should:
    /// - Parse the legacy YAML
    /// - Write migrated config as AutoQAC Settings.yaml
    /// - Backup the legacy file to migration_backup/
    /// - Delete the original legacy file
    /// </summary>
    [Fact]
    public async Task MigrateIfNeeded_ValidLegacyConfig_MigratesSuccessfully()
    {
        // Arrange -- valid legacy config with typical content
        var legacyContent = @"
xEdit:
  Binary: C:\SSEEdit\SSEEdit.exe
Load_Order:
  File: C:\Users\test\plugins.txt
";
        await File.WriteAllTextAsync(LegacyConfigPath, legacyContent);
        var sut = CreateSut();

        // Act
        var result = await sut.MigrateIfNeededAsync();

        // Assert
        result.Attempted.Should().BeTrue("legacy file was found and migration was attempted");
        result.Success.Should().BeTrue("valid YAML should migrate successfully");
        result.MigratedFiles.Should().Contain("AutoQAC Config.yaml");
        result.FailedFiles.Should().BeNull();

        // Verify migrated config was written
        File.Exists(CurrentConfigPath).Should().BeTrue("migrated config should be written as AutoQAC Settings.yaml");

        // Verify backup was created
        Directory.Exists(BackupSubdirectory).Should().BeTrue("backup subdirectory should be created");
        var backupFiles = Directory.GetFiles(BackupSubdirectory);
        backupFiles.Should().HaveCount(1, "one backup file should be created");
        backupFiles[0].Should().Contain("AutoQAC Config.yaml", "backup file should contain original filename");

        // Verify legacy file was deleted
        File.Exists(LegacyConfigPath).Should().BeFalse("legacy file should be deleted after successful migration and backup");
    }

    #endregion

    #region Invalid YAML

    /// <summary>
    /// When legacy config contains invalid YAML, migration should fail gracefully.
    /// </summary>
    [Fact]
    public async Task MigrateIfNeeded_InvalidYaml_ReturnsFailure()
    {
        // Arrange -- invalid YAML content
        await File.WriteAllTextAsync(LegacyConfigPath, "{{{invalid yaml content not parseable");
        var sut = CreateSut();

        // Act
        var result = await sut.MigrateIfNeededAsync();

        // Assert
        result.Attempted.Should().BeTrue("legacy file was found and migration was attempted");
        result.Success.Should().BeFalse("invalid YAML should cause failure");
        result.FailedFiles.Should().Contain("AutoQAC Config.yaml");
        result.WarningMessage.Should().NotBeNullOrEmpty("warning should explain the parse failure");

        // Legacy file should NOT be deleted (migration failed)
        File.Exists(LegacyConfigPath).Should().BeTrue("legacy file should be preserved on failure");

        // No migrated config should be written
        File.Exists(CurrentConfigPath).Should().BeFalse("no config should be written on parse failure");
    }

    #endregion

    #region Empty Legacy File

    /// <summary>
    /// When legacy config file is empty, deserializer returns null and migration should fail.
    /// </summary>
    [Fact]
    public async Task MigrateIfNeeded_EmptyLegacyFile_ReturnsFailure()
    {
        // Arrange -- empty file
        await File.WriteAllTextAsync(LegacyConfigPath, "");
        var sut = CreateSut();

        // Act
        var result = await sut.MigrateIfNeededAsync();

        // Assert
        result.Attempted.Should().BeTrue("legacy file was found and migration was attempted");
        result.Success.Should().BeFalse("empty file produces null deserialization result");
        result.FailedFiles.Should().Contain("AutoQAC Config.yaml");
        result.WarningMessage.Should().Contain("empty", "warning should mention the file was empty or unparseable");

        // Legacy file should NOT be deleted
        File.Exists(LegacyConfigPath).Should().BeTrue("legacy file should be preserved on failure");
    }

    #endregion

    #region Write Failure

    /// <summary>
    /// When the destination path for the migrated config is blocked (e.g., a directory
    /// with the same name exists), the write should fail and migration should report failure.
    /// </summary>
    [Fact]
    public async Task MigrateIfNeeded_WriteFailure_ReturnsFailure()
    {
        // Arrange -- valid legacy config
        var legacyContent = @"
xEdit:
  Binary: C:\SSEEdit\SSEEdit.exe
";
        await File.WriteAllTextAsync(LegacyConfigPath, legacyContent);

        // Block the write by creating a DIRECTORY with the same name as the target file
        Directory.CreateDirectory(CurrentConfigPath);

        var sut = CreateSut();

        // Act
        var result = await sut.MigrateIfNeededAsync();

        // Assert
        result.Attempted.Should().BeTrue("legacy file was found and migration was attempted");
        result.Success.Should().BeFalse("write should fail when destination is a directory");
        result.FailedFiles.Should().Contain("AutoQAC Config.yaml");
        result.WarningMessage.Should().NotBeNullOrEmpty("warning should describe the write failure");

        // Legacy file should NOT be deleted
        File.Exists(LegacyConfigPath).Should().BeTrue("legacy file should be preserved on write failure");
    }

    #endregion

    #region Backup Failure

    /// <summary>
    /// When the backup subdirectory cannot be created (e.g., a file with the same name
    /// blocks directory creation), migration should report failure and keep the original file.
    /// </summary>
    [Fact]
    public async Task MigrateIfNeeded_BackupFailure_KeepsOriginal()
    {
        // Arrange -- valid legacy config
        var legacyContent = @"
xEdit:
  Binary: C:\SSEEdit\SSEEdit.exe
";
        await File.WriteAllTextAsync(LegacyConfigPath, legacyContent);

        // Block backup by creating a FILE named "migration_backup" (prevents Directory.CreateDirectory)
        await File.WriteAllTextAsync(BackupSubdirectory, "blocker");

        var sut = CreateSut();

        // Act
        var result = await sut.MigrateIfNeededAsync();

        // Assert
        result.Attempted.Should().BeTrue("legacy file was found and migration was attempted");
        result.Success.Should().BeFalse("backup failure should cause migration to fail for safety");
        result.WarningMessage.Should().NotBeNullOrEmpty("warning should mention backup failure");

        // The migrated config WAS written (Step 4 succeeded before Step 5 backup failed)
        File.Exists(CurrentConfigPath).Should().BeTrue(
            "migrated config was written before backup step failed");

        // Original legacy file should still exist (not deleted since backup failed)
        File.Exists(LegacyConfigPath).Should().BeTrue(
            "legacy file must be preserved when backup fails -- backup-then-delete order");
    }

    #endregion
}
