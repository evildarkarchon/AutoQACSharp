using System.IO;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.GameDetection;
using FluentAssertions;
using Moq;
using Xunit;

namespace AutoQAC.Tests.Services;

public sealed class GameDetectionServiceTests
{
    [Theory]
    [InlineData("SSEEdit.exe", GameType.SkyrimSpecialEdition)]
    [InlineData("FO4Edit64.exe", GameType.Fallout4)]
    [InlineData("FNVEdit.exe", GameType.FalloutNewVegas)]
    [InlineData("FO3Edit.exe", GameType.Fallout3)]
    [InlineData("FO4VREdit.exe", GameType.Fallout4VR)]
    [InlineData("TES5VREdit.exe", GameType.SkyrimVR)]
    [InlineData("xEdit.exe", GameType.Unknown)]
    [InlineData("NotAGame.exe", GameType.Unknown)]
    [InlineData("", GameType.Unknown)]
    public void DetectFromExecutable_ShouldReturnCorrectGameType(
        string executable,
        GameType expected)
    {
        // Arrange
        var service = new GameDetectionService(Mock.Of<ILoggingService>());

        // Act
        var result = service.DetectFromExecutable(executable);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public async Task DetectFromLoadOrder_WithSkyrimMaster_ShouldReturnSSE()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, "# Load Order\nSkyrim.esm\nUpdate.esm\n");

        var service = new GameDetectionService(Mock.Of<ILoggingService>());

        try
        {
            // Act
            var result = await service.DetectFromLoadOrderAsync(tempFile);

            // Assert
            result.Should().Be(GameType.SkyrimSpecialEdition);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task DetectFromLoadOrder_WithFallout4Master_ShouldReturnFO4()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, "*Fallout4.esm\nDLCCoast.esm\n");

        var service = new GameDetectionService(Mock.Of<ILoggingService>());

        try
        {
            // Act
            var result = await service.DetectFromLoadOrderAsync(tempFile);

            // Assert
            result.Should().Be(GameType.Fallout4);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task DetectFromLoadOrder_WithUnknownMaster_ShouldReturnUnknown()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, "Unknown.esm\nMod.esp\n");

        var service = new GameDetectionService(Mock.Of<ILoggingService>());

        try
        {
            // Act
            var result = await service.DetectFromLoadOrderAsync(tempFile);

            // Assert
            result.Should().Be(GameType.Unknown);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
