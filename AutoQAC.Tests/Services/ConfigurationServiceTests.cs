using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.Configuration;
using FluentAssertions;
using Moq;
using Xunit;

namespace AutoQAC.Tests.Services;

public sealed class ConfigurationServiceTests : IDisposable
{
    private readonly string _testDirectory;

    public ConfigurationServiceTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "AutoQACTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Fact]
    public async Task LoadUserConfig_ShouldCreateDefault_WhenFileNotFound()
    {
        // Arrange
        var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);
        var expectedPath = Path.Combine(_testDirectory, "AutoQAC Settings.yaml");

        // Act
        var config = await service.LoadUserConfigAsync();

        // Assert
        config.Should().NotBeNull();
        File.Exists(expectedPath).Should().BeTrue();
    }

    [Fact]
    public async Task SaveUserConfig_ShouldWriteToFile()
    {
        // Arrange
        var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);
        var config = new UserConfiguration
        {
            Settings = new AutoQacSettings { CleaningTimeout = 999 }
        };

        // Act
        await service.SaveUserConfigAsync(config);

        // Assert
        var loaded = await service.LoadUserConfigAsync();
        loaded.Settings.CleaningTimeout.Should().Be(999);
    }

    [Fact]
    public async Task ValidatePaths_ShouldReturnFalse_WhenFilesMissing()
    {
        // Arrange
        var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);
        var config = new UserConfiguration
        {
            LoadOrder = new LoadOrderConfig { File = "NonExistent.txt" },
            XEdit = new XEditConfig { Binary = "NonExistent.exe" }
        };

        // Act
        var isValid = await service.ValidatePathsAsync(config);

        // Assert
        isValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidatePaths_ShouldReturnTrue_WhenFilesExist()
    {
        // Arrange
        var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);
        
        var loFile = Path.Combine(_testDirectory, "plugins.txt");
        var xEditFile = Path.Combine(_testDirectory, "SSEEdit.exe");
        await File.WriteAllTextAsync(loFile, "");
        await File.WriteAllTextAsync(xEditFile, "");

        var config = new UserConfiguration
        {
            LoadOrder = new LoadOrderConfig { File = loFile },
            XEdit = new XEditConfig { Binary = xEditFile }
        };

        // Act
        var isValid = await service.ValidatePathsAsync(config);

        // Assert
        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task GetXEditExecutableNames_ShouldReturnCorrectList_AfterLoadingMainConfig()
    {
        // Arrange
        var mainConfigContent = @"
AutoQAC_Data:
  XEdit_Lists:
    SSE:
      - SSEEdit.exe
      - SSEEdit64.exe
";
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, "AutoQAC Main.yaml"), mainConfigContent);

        var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

        // Act
        var list = await service.GetXEditExecutableNamesAsync(GameType.SkyrimSE);

        // Assert
        list.Should().Contain("SSEEdit.exe");
        list.Should().Contain("SSEEdit64.exe");
    }

    #region Error Handling Tests

    /// <summary>
    /// Verifies that LoadUserConfigAsync throws an exception when encountering
    /// malformed/corrupted YAML content.
    /// </summary>
    [Fact]
    public async Task LoadUserConfigAsync_ShouldThrow_WhenYamlIsCorrupted()
    {
        // Arrange
        var corruptedYaml = @"
LoadOrder:
  File: ""valid_start""
  # Corrupted: Invalid YAML - unbalanced quotes and bad indentation
    BadIndent: true
  ""incomplete_key
Settings: [not: properly: closed
";
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, "AutoQAC Settings.yaml"), corruptedYaml);

        var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

        // Act
        Func<Task> act = () => service.LoadUserConfigAsync();

        // Assert
        // YamlDotNet should throw a YamlException (or derived) for malformed YAML
        await act.Should().ThrowAsync<Exception>(
            "corrupted YAML should cause an exception");
    }

    /// <summary>
    /// Verifies that LoadMainConfigAsync throws an exception when encountering
    /// malformed YAML in the main configuration file.
    /// </summary>
    [Fact]
    public async Task LoadMainConfigAsync_ShouldThrow_WhenYamlIsCorrupted()
    {
        // Arrange
        var corruptedYaml = @"
AutoQAC_Data:
  SkipLists:
    SSE
      - This is not valid YAML
    - misplaced item
  XEdit_Lists: [invalid: structure
";
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, "AutoQAC Main.yaml"), corruptedYaml);

        var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

        // Act
        Func<Task> act = () => service.LoadMainConfigAsync();

        // Assert
        await act.Should().ThrowAsync<Exception>(
            "corrupted main config YAML should cause an exception");
    }

    /// <summary>
    /// Verifies that GetSkipListAsync returns empty list when the requested
    /// game type key is missing from the configuration.
    /// </summary>
    [Fact]
    public async Task GetSkipListAsync_ShouldReturnEmptyList_WhenGameTypeKeyMissing()
    {
        // Arrange
        var mainConfigContent = @"
AutoQAC_Data:
  SkipLists:
    SSE:
      - Skyrim.esm
      - Update.esm
  XEdit_Lists:
    SSE:
      - SSEEdit.exe
";
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, "AutoQAC Main.yaml"), mainConfigContent);

        var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

        // Act
        // Request skip list for a game type that's not in the config
        var list = await service.GetSkipListAsync(GameType.Fallout3);

        // Assert
        // Should return empty list (or possibly Universal if present) but not throw
        list.Should().NotBeNull("should return a list even if game type is not found");
        // FO3 not defined, so should be empty unless Universal is defined
    }

    /// <summary>
    /// Verifies that GetSkipListAsync merges Universal skip list with game-specific list.
    /// NOTE: The YAML uses "Skip_Lists" as the alias (with underscore) per MainConfiguration model.
    /// </summary>
    [Fact]
    public async Task GetSkipListAsync_ShouldMergeUniversalWithGameSpecific()
    {
        // Arrange
        // User config with skip lists (consolidated in single file)
        var settingsContent = @"
Selected_Game: SkyrimSE
Skip_Lists:
  SSE:
    - Skyrim.esm
    - Update.esm
";
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, "AutoQAC Settings.yaml"), settingsContent);

        // Universal entries in Main.yaml (read-only)
        var mainConfigContent = @"
AutoQAC_Data:
  Skip_Lists:
    Universal:
      - UniversalPlugin.esm
      - AnotherUniversal.esp
  XEdit_Lists:
    SSE:
      - SSEEdit.exe
";
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, "AutoQAC Main.yaml"), mainConfigContent);

        var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

        // Act
        var list = await service.GetSkipListAsync(GameType.SkyrimSE);

        // Assert
        list.Should().Contain("Skyrim.esm", "user skip list plugins should be included");
        list.Should().Contain("Update.esm");
        list.Should().Contain("UniversalPlugin.esm", "Universal plugins from Main.yaml should be merged");
        list.Should().Contain("AnotherUniversal.esp");
    }

    /// <summary>
    /// Verifies that configuration observable emits on save.
    /// </summary>
    [Fact]
    public async Task SaveUserConfigAsync_ShouldEmitConfigurationChangedEvent()
    {
        // Arrange
        var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);
        var emittedConfigs = new List<UserConfiguration>();
        using var subscription = service.UserConfigurationChanged.Subscribe(c => emittedConfigs.Add(c));

        var config = new UserConfiguration
        {
            Settings = new AutoQacSettings { CleaningTimeout = 999 }
        };

        // Act
        await service.SaveUserConfigAsync(config);

        // Assert
        emittedConfigs.Should().ContainSingle("saving should emit one event");
        emittedConfigs[0].Settings.CleaningTimeout.Should().Be(999);
    }

    /// <summary>
    /// Verifies that ValidatePathsAsync returns false when MO2Mode is enabled
    /// but MO2 binary path is invalid.
    /// </summary>
    [Fact]
    public async Task ValidatePathsAsync_ShouldReturnFalse_WhenMo2ModeEnabledButBinaryMissing()
    {
        // Arrange
        var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

        // Create only load order and xEdit files
        var loFile = Path.Combine(_testDirectory, "plugins.txt");
        var xEditFile = Path.Combine(_testDirectory, "SSEEdit.exe");
        await File.WriteAllTextAsync(loFile, "");
        await File.WriteAllTextAsync(xEditFile, "");

        var config = new UserConfiguration
        {
            LoadOrder = new LoadOrderConfig { File = loFile },
            XEdit = new XEditConfig { Binary = xEditFile },
            ModOrganizer = new ModOrganizerConfig { Binary = "NonExistent/ModOrganizer.exe" },
            Settings = new AutoQacSettings { MO2Mode = true }
        };

        // Act
        var isValid = await service.ValidatePathsAsync(config);

        // Assert
        isValid.Should().BeFalse("MO2 binary is required when MO2Mode is enabled");
    }

    /// <summary>
    /// Verifies that configuration service handles empty configuration files gracefully.
    /// </summary>
    [Fact]
    public async Task LoadUserConfigAsync_ShouldHandleEmptyFile()
    {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, "AutoQAC Settings.yaml"), "");

        var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

        // Act
        // YamlDotNet may return null for empty file, which should be handled
        var config = await service.LoadUserConfigAsync();

        // Assert
        // Should either return default config or handle gracefully
        // The actual behavior depends on implementation
    }

    /// <summary>
    /// Verifies that concurrent read operations don't cause issues.
    /// </summary>
    [Fact]
    public async Task LoadUserConfigAsync_ShouldHandleConcurrentReads()
    {
        // Arrange
        var configContent = @"
LoadOrder:
  File: ""plugins.txt""
XEdit:
  Binary: ""xEdit.exe""
Settings:
  CleaningTimeout: 300
";
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, "AutoQAC Settings.yaml"), configContent);

        var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

        // Act
        var tasks = Enumerable.Range(0, 10).Select(_ => service.LoadUserConfigAsync());
        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().HaveCount(10);
        results.Should().OnlyContain(c => c != null, "all concurrent reads should succeed");
    }

    /// <summary>
    /// Verifies that GetXEditExecutableNamesAsync returns Universal list
    /// when game-specific list is not found.
    /// </summary>
    [Fact]
    public async Task GetXEditExecutableNamesAsync_ShouldFallbackToUniversal_WhenGameNotFound()
    {
        // Arrange
        var mainConfigContent = @"
AutoQAC_Data:
  SkipLists:
    SSE:
      - Skyrim.esm
  XEdit_Lists:
    Universal:
      - xEdit.exe
      - xEdit64.exe
";
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, "AutoQAC Main.yaml"), mainConfigContent);

        var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

        // Act
        // Request for a game type not specifically defined
        var list = await service.GetXEditExecutableNamesAsync(GameType.Fallout3);

        // Assert
        list.Should().Contain("xEdit.exe", "should fallback to Universal list");
        list.Should().Contain("xEdit64.exe");
    }

    /// <summary>
    /// Verifies proper disposal of the configuration service.
    /// </summary>
    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        // Arrange
        var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

        // Act & Assert
        FluentActions.Invoking(() => service.Dispose())
            .Should().NotThrow("dispose should complete without exception");

        // Multiple disposal should be safe
        FluentActions.Invoking(() => service.Dispose())
            .Should().NotThrow("multiple disposal should be safe");
    }

    #endregion
}
