using System.Collections.Generic;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Plugin;
using FluentAssertions;
using Moq;
using Xunit;

namespace AutoQAC.Tests.Services;

public sealed class PluginLoadingServiceTests
{
    private readonly Mock<IPluginValidationService> _mockPluginValidation;
    private readonly Mock<ILoggingService> _mockLogger;
    private readonly PluginLoadingService _sut;

    public PluginLoadingServiceTests()
    {
        _mockPluginValidation = new Mock<IPluginValidationService>();
        _mockLogger = new Mock<ILoggingService>();
        _sut = new PluginLoadingService(_mockPluginValidation.Object, _mockLogger.Object);
    }

    #region IsGameSupportedByMutagen Tests

    [Theory]
    [InlineData(GameType.SkyrimLE, true)]
    [InlineData(GameType.SkyrimSE, true)]
    [InlineData(GameType.SkyrimVR, true)]
    [InlineData(GameType.Fallout4, true)]
    [InlineData(GameType.Fallout4VR, true)]
    [InlineData(GameType.Fallout3, false)]
    [InlineData(GameType.FalloutNewVegas, false)]
    [InlineData(GameType.Oblivion, false)]
    [InlineData(GameType.Unknown, false)]
    public void IsGameSupportedByMutagen_ShouldReturnCorrectValue(GameType gameType, bool expected)
    {
        // Act
        var result = _sut.IsGameSupportedByMutagen(gameType);

        // Assert
        result.Should().Be(expected);
    }

    #endregion

    #region GetAvailableGames Tests

    [Fact]
    public void GetAvailableGames_ShouldReturnAllGamesExceptUnknown()
    {
        // Act
        var result = _sut.GetAvailableGames();

        // Assert
        result.Should().NotContain(GameType.Unknown);
        result.Should().Contain(GameType.SkyrimSE);
        result.Should().Contain(GameType.SkyrimLE);
        result.Should().Contain(GameType.SkyrimVR);
        result.Should().Contain(GameType.Fallout3);
        result.Should().Contain(GameType.FalloutNewVegas);
        result.Should().Contain(GameType.Fallout4);
        result.Should().Contain(GameType.Fallout4VR);
        result.Should().Contain(GameType.Oblivion);
    }

    [Fact]
    public void GetAvailableGames_ShouldReturnSortedList()
    {
        // Act
        var result = _sut.GetAvailableGames();

        // Assert
        var sorted = result.OrderBy(g => g.ToString()).ToList();
        result.Should().BeEquivalentTo(sorted, options => options.WithStrictOrdering());
    }

    #endregion

    #region GetPluginsFromFileAsync Tests

    [Fact]
    public async Task GetPluginsFromFileAsync_ShouldDelegateToPluginValidationService()
    {
        // Arrange
        var loadOrderPath = "plugins.txt";
        var expectedPlugins = new List<PluginInfo>
        {
            new() { FileName = "Plugin1.esp", FullPath = "Data/Plugin1.esp" },
            new() { FileName = "Plugin2.esp", FullPath = "Data/Plugin2.esp" }
        };
        _mockPluginValidation
            .Setup(s => s.GetPluginsFromLoadOrderAsync(loadOrderPath, default))
            .ReturnsAsync(expectedPlugins);

        // Act
        var result = await _sut.GetPluginsFromFileAsync(loadOrderPath);

        // Assert
        result.Should().BeEquivalentTo(expectedPlugins);
        _mockPluginValidation.Verify(
            s => s.GetPluginsFromLoadOrderAsync(loadOrderPath, default),
            Times.Once);
    }

    #endregion

    #region GetPluginsAsync Tests

    [Fact]
    public async Task GetPluginsAsync_WithUnknownGameType_ShouldReturnEmptyList()
    {
        // Act
        var result = await _sut.GetPluginsAsync(GameType.Unknown);

        // Assert
        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData(GameType.Fallout3)]
    [InlineData(GameType.FalloutNewVegas)]
    [InlineData(GameType.Oblivion)]
    public async Task GetPluginsAsync_WithNonMutagenGame_ShouldReturnEmptyList(GameType gameType)
    {
        // Act
        var result = await _sut.GetPluginsAsync(gameType);

        // Assert
        result.Should().BeEmpty(
            $"Non-Mutagen game {gameType} should return empty list and require file-based fallback");
    }

    #endregion

    #region GetGameDataFolder Tests

    [Theory]
    [InlineData(GameType.Fallout3)]
    [InlineData(GameType.FalloutNewVegas)]
    [InlineData(GameType.Oblivion)]
    [InlineData(GameType.Unknown)]
    public void GetGameDataFolder_WithNonMutagenGame_ShouldReturnNull(GameType gameType)
    {
        // Act
        var result = _sut.GetGameDataFolder(gameType);

        // Assert
        result.Should().BeNull(
            $"Non-Mutagen game {gameType} should return null for data folder");
    }

    #endregion
}
