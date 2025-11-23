using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Plugin;
using FluentAssertions;
using Moq;
using Xunit;

namespace AutoQAC.Tests.Services;

public sealed class PluginValidationServiceTests
{
    [Fact]
    public async Task GetPluginsFromLoadOrder_ShouldParseFileCorrectly()
    {
        // Arrange
        var content = @"# This is a comment
*Skyrim.esm
Update.esm
*Dawnguard.esm
HearthFires.esm
";
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, content);

        var service = new PluginValidationService(Mock.Of<ILoggingService>());

        try
        {
            // Act
            var plugins = await service.GetPluginsFromLoadOrderAsync(tempFile);

            // Assert
            plugins.Should().HaveCount(4);
            plugins[0].FileName.Should().Be("Skyrim.esm");
            plugins[1].FileName.Should().Be("Update.esm");
            plugins[2].FileName.Should().Be("Dawnguard.esm");
            plugins[3].FileName.Should().Be("HearthFires.esm");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void FilterSkippedPlugins_ShouldMarkSkippedCorrectly()
    {
        // Arrange
        var service = new PluginValidationService(Mock.Of<ILoggingService>());
        var plugins = new List<PluginInfo>
        {
            new() { FileName = "Skyrim.esm", FullPath = "Skyrim.esm", DetectedGameType = GameType.Unknown, IsInSkipList = false },
            new() { FileName = "Update.esm", FullPath = "Update.esm", DetectedGameType = GameType.Unknown, IsInSkipList = false },
            new() { FileName = "Mod.esp", FullPath = "Mod.esp", DetectedGameType = GameType.Unknown, IsInSkipList = false }
        };

        var skipList = new List<string> { "Skyrim.esm", "Update.esm" };

        // Act
        var result = service.FilterSkippedPlugins(plugins, skipList);

        // Assert
        result.Should().HaveCount(3);
        result.Single(p => p.FileName == "Skyrim.esm").IsInSkipList.Should().BeTrue();
        result.Single(p => p.FileName == "Update.esm").IsInSkipList.Should().BeTrue();
        result.Single(p => p.FileName == "Mod.esp").IsInSkipList.Should().BeFalse();
    }
}
