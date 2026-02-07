using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.GameDetection;
using FluentAssertions;
using NSubstitute;

namespace AutoQAC.Tests.Services;

public sealed class GameDetectionServiceTests
{
    [Theory]
    [InlineData("TES4Edit.exe", GameType.Oblivion)]
    [InlineData("TES5Edit.exe", GameType.SkyrimLe)]
    [InlineData("SSEEdit.exe", GameType.SkyrimSe)]
    [InlineData("SkyrimVREdit.exe", GameType.SkyrimVr)]
    [InlineData("TES5VREdit.exe", GameType.SkyrimVr)]
    [InlineData("FO3Edit.exe", GameType.Fallout3)]
    [InlineData("FNVEdit.exe", GameType.FalloutNewVegas)]
    [InlineData("FO4Edit64.exe", GameType.Fallout4)]
    [InlineData("FO4VREdit.exe", GameType.Fallout4Vr)]
    [InlineData("xEdit.exe", GameType.Unknown)]
    [InlineData("NotAGame.exe", GameType.Unknown)]
    [InlineData("", GameType.Unknown)]
    public void DetectFromExecutable_ShouldReturnCorrectGameType(
        string executable,
        GameType expected)
    {
        // Arrange
        var service = new GameDetectionService(Substitute.For<ILoggingService>());

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

        var service = new GameDetectionService(Substitute.For<ILoggingService>());

        try
        {
            // Act
            var result = await service.DetectFromLoadOrderAsync(tempFile);

            // Assert
            result.Should().Be(GameType.SkyrimSe);
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

        var service = new GameDetectionService(Substitute.For<ILoggingService>());

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

        var service = new GameDetectionService(Substitute.For<ILoggingService>());

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

    #region Edge Case Tests

    /// <summary>
    /// Verifies that DetectFromLoadOrderAsync returns Unknown for an empty load order file.
    /// </summary>
    [Fact]
    public async Task DetectFromLoadOrder_WithEmptyFile_ShouldReturnUnknown()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, "");

        var service = new GameDetectionService(Substitute.For<ILoggingService>());

        try
        {
            // Act
            var result = await service.DetectFromLoadOrderAsync(tempFile);

            // Assert
            result.Should().Be(GameType.Unknown, "empty file should result in Unknown game type");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Verifies that DetectFromLoadOrderAsync returns Unknown for a file with only whitespace.
    /// </summary>
    [Fact]
    public async Task DetectFromLoadOrder_WithOnlyWhitespace_ShouldReturnUnknown()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, "   \n\n  \t  \n   ");

        var service = new GameDetectionService(Substitute.For<ILoggingService>());

        try
        {
            // Act
            var result = await service.DetectFromLoadOrderAsync(tempFile);

            // Assert
            result.Should().Be(GameType.Unknown, "whitespace-only file should result in Unknown");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Verifies that DetectFromLoadOrderAsync returns Unknown when the load order file
    /// does not exist.
    /// </summary>
    [Fact]
    public async Task DetectFromLoadOrder_WhenFileNotFound_ShouldReturnUnknown()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "plugins.txt");
        var service = new GameDetectionService(Substitute.For<ILoggingService>());

        // Act
        var result = await service.DetectFromLoadOrderAsync(nonExistentPath);

        // Assert
        result.Should().Be(GameType.Unknown, "non-existent file should result in Unknown");
    }

    /// <summary>
    /// Verifies that DetectFromLoadOrderAsync handles empty path correctly.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DetectFromLoadOrder_WithInvalidPath_ShouldReturnUnknown(string? path)
    {
        // Arrange
        var service = new GameDetectionService(Substitute.For<ILoggingService>());

        // Act
        var result = await service.DetectFromLoadOrderAsync(path!);

        // Assert
        result.Should().Be(GameType.Unknown, "invalid path should result in Unknown");
    }

    /// <summary>
    /// Verifies that DetectFromLoadOrderAsync correctly handles files with only comments.
    /// </summary>
    [Fact]
    public async Task DetectFromLoadOrder_WithOnlyComments_ShouldReturnUnknown()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, "# This is a comment\n# Another comment\n# No plugins here");

        var service = new GameDetectionService(Substitute.For<ILoggingService>());

        try
        {
            // Act
            var result = await service.DetectFromLoadOrderAsync(tempFile);

            // Assert
            result.Should().Be(GameType.Unknown, "file with only comments should result in Unknown");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Verifies that when multiple game masters are present, the first one determines
    /// the game type (first-match wins behavior).
    /// </summary>
    [Fact]
    public async Task DetectFromLoadOrder_WithMultipleGameMasters_ShouldReturnFirstMatch()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        // Mix of game masters - Skyrim appears first
        await File.WriteAllTextAsync(tempFile, @"
# Load Order
Skyrim.esm
Fallout4.esm
Fallout3.esm
SomeMod.esp
");

        var service = new GameDetectionService(Substitute.For<ILoggingService>());

        try
        {
            // Act
            var result = await service.DetectFromLoadOrderAsync(tempFile);

            // Assert
            result.Should().Be(GameType.SkyrimSe, "first game master encountered should determine game type");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Verifies that DetectFromLoadOrderAsync correctly handles load order files
    /// where plugins have the enabled flag prefix (*).
    /// </summary>
    [Fact]
    public async Task DetectFromLoadOrder_WithEnabledFlagPrefix_ShouldDetectCorrectly()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, "*Fallout4.esm\n*DLCCoast.esm\n*SomeMod.esp");

        var service = new GameDetectionService(Substitute.For<ILoggingService>());

        try
        {
            // Act
            var result = await service.DetectFromLoadOrderAsync(tempFile);

            // Assert
            result.Should().Be(GameType.Fallout4, "should detect game even with * prefix");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Verifies that the service correctly identifies Fallout New Vegas from its master file.
    /// </summary>
    [Fact]
    public async Task DetectFromLoadOrder_WithFalloutNewVegas_ShouldDetectCorrectly()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, "FalloutNV.esm\nSomeMod.esp");

        var service = new GameDetectionService(Substitute.For<ILoggingService>());

        try
        {
            // Act
            var result = await service.DetectFromLoadOrderAsync(tempFile);

            // Assert
            result.Should().Be(GameType.FalloutNewVegas);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    #endregion

    #region DetectFromExecutable Edge Cases

    /// <summary>
    /// Verifies that DetectFromExecutable handles partial executable name matches.
    /// </summary>
    [Theory]
    [InlineData("SSEEdit 4.0.4.exe", GameType.SkyrimSe)]
    [InlineData("sseedit_backup.exe", GameType.SkyrimSe)]
    [InlineData("FO4Edit (copy).exe", GameType.Fallout4)]
    public void DetectFromExecutable_ShouldHandleVersionedNames(string fileName, GameType expected)
    {
        // Arrange
        var service = new GameDetectionService(Substitute.For<ILoggingService>());

        // Act
        var result = service.DetectFromExecutable(fileName);

        // Assert
        result.Should().Be(expected, $"'{fileName}' should be recognized as {expected}");
    }

    /// <summary>
    /// Verifies that DetectFromExecutable handles full file paths correctly.
    /// </summary>
    [Fact]
    public void DetectFromExecutable_ShouldExtractFileNameFromPath()
    {
        // Arrange
        var service = new GameDetectionService(Substitute.For<ILoggingService>());
        var fullPath = @"C:\Games\xEdit\SSEEdit.exe";

        // Act
        var result = service.DetectFromExecutable(fullPath);

        // Assert
        result.Should().Be(GameType.SkyrimSe);
    }

    /// <summary>
    /// Verifies that DetectFromExecutable handles null input correctly.
    /// </summary>
    [Fact]
    public void DetectFromExecutable_WithNull_ShouldReturnUnknown()
    {
        // Arrange
        var service = new GameDetectionService(Substitute.For<ILoggingService>());

        // Act
        var result = service.DetectFromExecutable(null!);

        // Assert
        result.Should().Be(GameType.Unknown);
    }

    #endregion

    #region DetectVariant Tests

    /// <summary>
    /// TTW: FNV base game with TaleOfTwoWastelands.esm should return TTW variant.
    /// </summary>
    [Fact]
    public void DetectVariant_ShouldReturnTTW_WhenFNVWithTTWPlugin()
    {
        // Arrange
        var service = new GameDetectionService(Substitute.For<ILoggingService>());
        var plugins = new List<string> { "FalloutNV.esm", "TaleOfTwoWastelands.esm", "SomeMod.esp" };

        // Act
        var result = service.DetectVariant(GameType.FalloutNewVegas, plugins);

        // Assert
        result.Should().Be(GameVariant.TTW);
    }

    /// <summary>
    /// TTW detection should be case-insensitive.
    /// </summary>
    [Fact]
    public void DetectVariant_ShouldBeCaseInsensitive_ForTTW()
    {
        // Arrange
        var service = new GameDetectionService(Substitute.For<ILoggingService>());
        var plugins = new List<string> { "FalloutNV.esm", "taleoftwowastelands.esm" };

        // Act
        var result = service.DetectVariant(GameType.FalloutNewVegas, plugins);

        // Assert
        result.Should().Be(GameVariant.TTW);
    }

    /// <summary>
    /// TTW only applies when base game is FNV. FO3 base game with TTW plugin should return None.
    /// </summary>
    [Fact]
    public void DetectVariant_ShouldReturnNone_WhenTTWPluginButWrongBaseGame()
    {
        // Arrange
        var service = new GameDetectionService(Substitute.For<ILoggingService>());
        var plugins = new List<string> { "Fallout3.esm", "TaleOfTwoWastelands.esm" };

        // Act
        var result = service.DetectVariant(GameType.Fallout3, plugins);

        // Assert
        result.Should().Be(GameVariant.None, "TTW only applies when base game is FNV");
    }

    /// <summary>
    /// Enderal: SSE base game with Enderal - Forgotten Stories.esm should return Enderal variant.
    /// </summary>
    [Fact]
    public void DetectVariant_ShouldReturnEnderal_WhenSSEWithEnderalPlugin()
    {
        // Arrange
        var service = new GameDetectionService(Substitute.For<ILoggingService>());
        var plugins = new List<string> { "Skyrim.esm", "Enderal - Forgotten Stories.esm", "SomeMod.esp" };

        // Act
        var result = service.DetectVariant(GameType.SkyrimSe, plugins);

        // Assert
        result.Should().Be(GameVariant.Enderal);
    }

    /// <summary>
    /// Enderal only applies when base game is SSE. FO4 with Enderal plugin should return None.
    /// </summary>
    [Fact]
    public void DetectVariant_ShouldReturnNone_WhenEnderalPluginButWrongBaseGame()
    {
        // Arrange
        var service = new GameDetectionService(Substitute.For<ILoggingService>());
        var plugins = new List<string> { "Fallout4.esm", "Enderal - Forgotten Stories.esm" };

        // Act
        var result = service.DetectVariant(GameType.Fallout4, plugins);

        // Assert
        result.Should().Be(GameVariant.None, "Enderal only applies when base game is SSE");
    }

    /// <summary>
    /// No variant marker plugins: should return None.
    /// </summary>
    [Fact]
    public void DetectVariant_ShouldReturnNone_WhenNoVariantPlugins()
    {
        // Arrange
        var service = new GameDetectionService(Substitute.For<ILoggingService>());
        var plugins = new List<string> { "Skyrim.esm", "Update.esm", "Mod.esp" };

        // Act
        var result = service.DetectVariant(GameType.SkyrimSe, plugins);

        // Assert
        result.Should().Be(GameVariant.None);
    }

    /// <summary>
    /// Empty load order: should return None.
    /// </summary>
    [Fact]
    public void DetectVariant_ShouldReturnNone_WhenEmptyLoadOrder()
    {
        // Arrange
        var service = new GameDetectionService(Substitute.For<ILoggingService>());
        var plugins = new List<string>();

        // Act
        var result = service.DetectVariant(GameType.FalloutNewVegas, plugins);

        // Assert
        result.Should().Be(GameVariant.None);
    }

    #endregion

    #region Utility Method Tests

    /// <summary>
    /// Verifies that IsValidGameType correctly identifies valid and invalid game types.
    /// </summary>
    [Theory]
    [InlineData(GameType.SkyrimLe, true)]
    [InlineData(GameType.SkyrimSe, true)]
    [InlineData(GameType.SkyrimVr, true)]
    [InlineData(GameType.Fallout3, true)]
    [InlineData(GameType.FalloutNewVegas, true)]
    [InlineData(GameType.Fallout4, true)]
    [InlineData(GameType.Fallout4Vr, true)]
    [InlineData(GameType.Oblivion, true)]
    [InlineData(GameType.Unknown, false)]
    public void IsValidGameType_ShouldReturnCorrectResult(GameType gameType, bool expected)
    {
        // Arrange
        var service = new GameDetectionService(Substitute.For<ILoggingService>());

        // Act
        var result = service.IsValidGameType(gameType);

        // Assert
        result.Should().Be(expected);
    }

    /// <summary>
    /// Verifies that GetGameDisplayName returns meaningful display names.
    /// </summary>
    [Theory]
    [InlineData(GameType.Oblivion, "The Elder Scrolls IV: Oblivion")]
    [InlineData(GameType.SkyrimLe, "Skyrim (Legendary Edition)")]
    [InlineData(GameType.SkyrimSe, "Skyrim Special Edition")]
    [InlineData(GameType.Fallout4, "Fallout 4")]
    [InlineData(GameType.FalloutNewVegas, "Fallout: New Vegas")]
    [InlineData(GameType.Unknown, "Unknown")]
    public void GetGameDisplayName_ShouldReturnCorrectName(GameType gameType, string expected)
    {
        // Arrange
        var service = new GameDetectionService(Substitute.For<ILoggingService>());

        // Act
        var result = service.GetGameDisplayName(gameType);

        // Assert
        result.Should().Be(expected);
    }

    /// <summary>
    /// Verifies that GetDefaultLoadOrderFileName returns a valid file name.
    /// </summary>
    [Fact]
    public void GetDefaultLoadOrderFileName_ShouldReturnValidFileName()
    {
        // Arrange
        var service = new GameDetectionService(Substitute.For<ILoggingService>());

        // Act
        var result = service.GetDefaultLoadOrderFileName(GameType.SkyrimSe);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().EndWith(".txt", "load order files are typically .txt files");
    }

    #endregion
}
