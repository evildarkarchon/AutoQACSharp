using System;
using System.Collections.Generic;
using System.IO;
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
        var expectedPath = Path.Combine(_testDirectory, "AutoQAC Config.yaml");

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
            Settings = new PactSettings { CleaningTimeout = 999 }
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
PACT_Data:
  XEdit_Lists:
    SSE:
      - SSEEdit.exe
      - SSEEdit64.exe
";
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, "AutoQAC Main.yaml"), mainConfigContent);
        
        var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

        // Act
        var list = await service.GetXEditExecutableNamesAsync(GameType.SkyrimSpecialEdition);

        // Assert
        list.Should().Contain("SSEEdit.exe");
        list.Should().Contain("SSEEdit64.exe");
    }
}
