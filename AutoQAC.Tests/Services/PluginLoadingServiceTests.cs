using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Plugin;
using FluentAssertions;
using Moq;

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
    [InlineData(GameType.SkyrimLe, true)]
    [InlineData(GameType.SkyrimSe, true)]
    [InlineData(GameType.SkyrimVr, true)]
    [InlineData(GameType.Fallout4, true)]
    [InlineData(GameType.Fallout4Vr, true)]
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
        result.Should().Contain(GameType.SkyrimSe);
        result.Should().Contain(GameType.SkyrimLe);
        result.Should().Contain(GameType.SkyrimVr);
        result.Should().Contain(GameType.Fallout3);
        result.Should().Contain(GameType.FalloutNewVegas);
        result.Should().Contain(GameType.Fallout4);
        result.Should().Contain(GameType.Fallout4Vr);
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

    [Fact]
    public void GetGameDataFolder_WithCustomOverride_ShouldReturnOverride()
    {
        // Arrange
        var customPath = @"C:\Custom\SkyrimSE\Data";

        // Act
        var result = _sut.GetGameDataFolder(GameType.SkyrimSe, customPath);

        // Assert
        result.Should().Be(customPath,
            "custom override should be returned when provided");
    }

    [Fact]
    public void GetGameDataFolder_WithEmptyOverride_ShouldNotReturnEmpty()
    {
        // Arrange - pass empty string as override (should behave as no override)
        var emptyOverride = "";

        // Act
        var result = _sut.GetGameDataFolder(GameType.SkyrimSe, emptyOverride);

        // Assert
        // Result should be null (no game installed) or an actual path (if game is installed)
        // But it should NOT be empty string
        if (result != null)
        {
            result.Should().NotBeEmpty("empty override should be treated as no override");
        }
    }

    [Theory]
    [InlineData(GameType.Fallout3)]
    [InlineData(GameType.FalloutNewVegas)]
    [InlineData(GameType.Oblivion)]
    public void GetGameDataFolder_NonMutagenGame_WithOverride_ShouldReturnOverride(GameType gameType)
    {
        // Arrange
        // Even non-Mutagen games should return the override if provided
        var customPath = @"C:\Custom\GameData";

        // Act
        var result = _sut.GetGameDataFolder(gameType, customPath);

        // Assert
        result.Should().Be(customPath,
            "custom override should be returned even for non-Mutagen games");
    }

    #endregion

    #region GetPluginsAsync with CustomDataFolder Tests

    [Fact]
    public async Task GetPluginsAsync_WithUnknownGameType_AndCustomFolder_ShouldReturnEmptyList()
    {
        // Act
        var result = await _sut.GetPluginsAsync(GameType.Unknown, @"C:\Custom\Data");

        // Assert
        result.Should().BeEmpty("Unknown game type should return empty list regardless of custom folder");
    }

    [Theory]
    [InlineData(GameType.Fallout3)]
    [InlineData(GameType.FalloutNewVegas)]
    [InlineData(GameType.Oblivion)]
    public async Task GetPluginsAsync_WithNonMutagenGame_AndCustomFolder_ShouldReturnEmptyList(GameType gameType)
    {
        // Act
        var result = await _sut.GetPluginsAsync(gameType, @"C:\Custom\Data");

        // Assert
        result.Should().BeEmpty(
            $"Non-Mutagen game {gameType} should return empty list and require file-based fallback");
    }

    #endregion
}
