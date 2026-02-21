using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Plugin;
using FluentAssertions;
using NSubstitute;
using System.Reflection;

namespace AutoQAC.Tests.Services;

public sealed class PluginLoadingServiceTests
{
    private readonly IPluginValidationService _mockPluginValidation;
    private readonly ILoggingService _mockLogger;
    private readonly PluginLoadingService _sut;

    public PluginLoadingServiceTests()
    {
        _mockPluginValidation = Substitute.For<IPluginValidationService>();
        _mockLogger = Substitute.For<ILoggingService>();
        _sut = new PluginLoadingService(_mockPluginValidation, _mockLogger, _ => null);
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
            .GetPluginsFromLoadOrderAsync(loadOrderPath, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(expectedPlugins);

        // Act
        var result = await _sut.GetPluginsFromFileAsync(loadOrderPath);

        // Assert
        result.Should().BeEquivalentTo(expectedPlugins);
        await _mockPluginValidation.Received(1)
            .GetPluginsFromLoadOrderAsync(loadOrderPath, Arg.Any<string?>(), Arg.Any<CancellationToken>());
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

    [Theory]
    [InlineData(GameType.Fallout3)]
    [InlineData(GameType.FalloutNewVegas)]
    [InlineData(GameType.Oblivion)]
    public void GetGameDataFolder_WithNonMutagenGame_ShouldUseRegistryFallback(GameType gameType)
    {
        // Arrange
        var expectedPath = @"C:\Games\Detected\Data";
        var sut = new PluginLoadingService(_mockPluginValidation, _mockLogger,
            g => g == gameType ? expectedPath : null);

        // Act
        var result = sut.GetGameDataFolder(gameType);

        // Assert
        result.Should().Be(expectedPath,
            "registry fallback should be used when Mutagen is unavailable for a game");
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

    [Fact]
    public void NormalizeDataFolderPath_WithDataFilesDirectory_ShouldReturnNull()
    {
        // Arrange
        var tempRoot = Path.Combine(Path.GetTempPath(), "AutoQACTest_" + Guid.NewGuid());
        var dataFilesPath = Path.Combine(tempRoot, "Data Files");
        Directory.CreateDirectory(dataFilesPath);

        try
        {
            // Act
            var result = InvokeNormalizeDataFolderPath(dataFilesPath);

            // Assert
            result.Should().BeNull("Bethesda games standardize on Data, not Data Files");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void NormalizeDataFolderPath_WithInstallDirectoryContainingData_ShouldReturnDataPath()
    {
        // Arrange
        var tempRoot = Path.Combine(Path.GetTempPath(), "AutoQACTest_" + Guid.NewGuid());
        var installPath = Path.Combine(tempRoot, "SkyrimSE");
        var dataPath = Path.Combine(installPath, "Data");
        Directory.CreateDirectory(dataPath);

        try
        {
            // Act
            var result = InvokeNormalizeDataFolderPath(installPath);

            // Assert
            result.Should().Be(dataPath, "install roots should normalize to their Data directory");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static string? InvokeNormalizeDataFolderPath(string input)
    {
        var method = typeof(PluginLoadingService).GetMethod(
            "NormalizeDataFolderPath",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull("NormalizeDataFolderPath should exist for registry path normalization");

        return method!.Invoke(null, new object?[] { input }) as string;
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
