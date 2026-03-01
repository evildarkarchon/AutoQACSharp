using System.Reflection;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Plugin;
using FluentAssertions;
using NSubstitute;

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

    #region TryGetPluginsAsync Tests

    [Fact]
    public async Task TryGetPluginsAsync_WithMutagenSupportedGame_ShouldReturnSuccessWithPlugins()
    {
        // Arrange
        var dataFolder = @"C:\Games\SkyrimSE\Data";
        var sut = new PluginLoadingService(
            _mockPluginValidation,
            _mockLogger,
            _ => null,
            (_, _, _) =>
            (
                dataFolder,
                (IReadOnlyList<string>)new List<string>
                {
                    "Skyrim.esm",
                    "Update.esm",
                    "MyPatch.esp"
                }
            ));

        // Act
        var result = await sut.TryGetPluginsAsync(GameType.SkyrimSe, dataFolder);

        // Assert
        result.Status.Should().Be(PluginLoadingStatus.Success);
        result.Plugins.Should().HaveCount(3);
        result.DataFolder.Should().Be(dataFolder);
        result.Plugins[0].FullPath.Should().Be(Path.Combine(dataFolder, "Skyrim.esm"));
        result.Plugins[1].FullPath.Should().Be(Path.Combine(dataFolder, "Update.esm"));
        result.Plugins[2].FullPath.Should().Be(Path.Combine(dataFolder, "MyPatch.esp"));
    }

    [Theory]
    [InlineData(GameType.Fallout3)]
    [InlineData(GameType.FalloutNewVegas)]
    [InlineData(GameType.Oblivion)]
    [InlineData(GameType.Unknown)]
    public async Task TryGetPluginsAsync_WithUnsupportedGame_ShouldReturnUnsupportedStatus(GameType gameType)
    {
        // Act
        var result = await _sut.TryGetPluginsAsync(gameType);

        // Assert
        result.Status.Should().Be(PluginLoadingStatus.UnsupportedGame);
        result.Plugins.Should().BeEmpty();
    }

    [Fact]
    public async Task TryGetPluginsAsync_WhenMutagenReturnsNoDataFolder_ShouldReturnDataFolderNotFound()
    {
        // Arrange
        var sut = new PluginLoadingService(
            _mockPluginValidation,
            _mockLogger,
            _ => null,
            (_, _, _) => (null, (IReadOnlyList<string>)new List<string>()));

        // Act
        var result = await sut.TryGetPluginsAsync(GameType.SkyrimSe, @"C:\Missing\Data");

        // Assert
        result.Status.Should().Be(PluginLoadingStatus.DataFolderNotFound);
        result.Plugins.Should().BeEmpty();
    }

    [Fact]
    public async Task TryGetPluginsAsync_WhenMutagenReturnsEmptyLoadOrder_ShouldReturnNoPluginsDiscovered()
    {
        // Arrange
        var dataFolder = @"C:\Games\SkyrimSE\Data";
        var sut = new PluginLoadingService(
            _mockPluginValidation,
            _mockLogger,
            _ => null,
            (_, _, _) => (dataFolder, (IReadOnlyList<string>)new List<string>()));

        // Act
        var result = await sut.TryGetPluginsAsync(GameType.SkyrimSe, dataFolder);

        // Assert
        result.Status.Should().Be(PluginLoadingStatus.NoPluginsDiscovered);
        result.Plugins.Should().BeEmpty();
        result.DataFolder.Should().Be(dataFolder);
    }

    [Fact]
    public async Task TryGetPluginsAsync_WhenMutagenThrows_ShouldReturnFailedStatus()
    {
        // Arrange
        var sut = new PluginLoadingService(
            _mockPluginValidation,
            _mockLogger,
            _ => null,
            (_, _, _) => throw new InvalidOperationException("Mutagen failure"));

        // Act
        var result = await sut.TryGetPluginsAsync(GameType.SkyrimSe, @"C:\Games\SkyrimSE\Data");

        // Assert
        result.Status.Should().Be(PluginLoadingStatus.Failed);
        result.Plugins.Should().BeEmpty();
        result.FailureReason.Should().Contain("Mutagen failure");
    }

    [Fact]
    public async Task TryGetPluginsAsync_ShouldReturnTaskBeforeMutagenListingCompletes()
    {
        // Arrange
        var dataFolder = @"C:\Games\SkyrimSE\Data";
        using var providerStarted = new ManualResetEventSlim(false);
        using var providerRelease = new ManualResetEventSlim(false);

        var sut = new PluginLoadingService(
            _mockPluginValidation,
            _mockLogger,
            _ => null,
            (_, _, ct) =>
            {
                providerStarted.Set();
                providerRelease.Wait(ct);
                return (dataFolder, (IReadOnlyList<string>)new List<string> { "Skyrim.esm" });
            });

        // Act
        var outerCallTask = Task.Factory.StartNew(
            () => sut.TryGetPluginsAsync(GameType.SkyrimSe, dataFolder, CancellationToken.None),
            CancellationToken.None,
            TaskCreationOptions.None,
            TaskScheduler.Default);

        Task<PluginLoadingResult>? innerTask = null;

        try
        {
            providerStarted.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue("provider should be invoked");

            var completed = await Task.WhenAny(outerCallTask, Task.Delay(250));
            completed.Should().Be(outerCallTask,
                "TryGetPluginsAsync should return promptly and not block until Mutagen listing completes");

            innerTask = await outerCallTask;
            innerTask.IsCompleted.Should().BeFalse("provider is still blocked and result should not be complete yet");
        }
        finally
        {
            providerRelease.Set();
        }

        var result = await innerTask!;
        result.Status.Should().Be(PluginLoadingStatus.Success);
        result.Plugins.Should().ContainSingle();
    }

    [Fact]
    public void PluginLoadingResult_PluginsProperty_ShouldBeReadOnlyList()
    {
        // Act
        var pluginsProperty = typeof(PluginLoadingResult).GetProperty(nameof(PluginLoadingResult.Plugins));

        // Assert
        pluginsProperty.Should().NotBeNull();
        pluginsProperty!.PropertyType.Should().Be(typeof(IReadOnlyList<PluginInfo>));
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

    [Theory]
    [InlineData(GameType.Oblivion, @"SOFTWARE\WOW6432Node\Bethesda Softworks\Oblivion")]
    [InlineData(GameType.Fallout3, @"SOFTWARE\WOW6432Node\Bethesda Softworks\Fallout3")]
    [InlineData(GameType.FalloutNewVegas, @"SOFTWARE\WOW6432Node\Bethesda Softworks\FalloutNV")]
    [InlineData(GameType.SkyrimLe, @"SOFTWARE\WOW6432Node\Bethesda Softworks\Skyrim")]
    [InlineData(GameType.SkyrimSe, @"SOFTWARE\WOW6432Node\Bethesda Softworks\Skyrim Special Edition")]
    [InlineData(GameType.SkyrimVr, @"SOFTWARE\WOW6432Node\Bethesda Softworks\Skyrim VR")]
    [InlineData(GameType.Fallout4, @"SOFTWARE\WOW6432Node\Bethesda Softworks\Fallout4")]
    [InlineData(GameType.Fallout4Vr, @"SOFTWARE\WOW6432Node\Bethesda Softworks\Fallout 4 VR")]
    public void RegistryInstallPathKeys_ShouldContainWow6432NodeBethesdaKey(GameType gameType, string expectedKey)
    {
        // Arrange
        var registryKeys = GetRegistryInstallPathKeys();

        // Act
        var gameKeys = registryKeys[gameType];

        // Assert
        gameKeys.Should().Contain(expectedKey,
            "Bethesda games should probe HKLM\\SOFTWARE\\WOW6432Node\\Bethesda Softworks first");
    }

    private static string? InvokeNormalizeDataFolderPath(string input)
    {
        var method = typeof(PluginLoadingService).GetMethod(
            "NormalizeDataFolderPath",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull("NormalizeDataFolderPath should exist for registry path normalization");

        return method!.Invoke(null, new object?[] { input }) as string;
    }

    private static Dictionary<GameType, string[]> GetRegistryInstallPathKeys()
    {
        var field = typeof(PluginLoadingService).GetField(
            "RegistryInstallPathKeys",
            BindingFlags.NonPublic | BindingFlags.Static);

        field.Should().NotBeNull("RegistryInstallPathKeys should exist for registry probing");

        return (field!.GetValue(null) as Dictionary<GameType, string[]>)!;
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
