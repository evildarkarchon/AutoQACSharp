using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Configuration;
using FluentAssertions;
using Moq;

namespace AutoQAC.Tests.Services;

/// <summary>
/// Tests for skip list management in ConfigurationService.
/// User skip lists are stored in AutoQAC Settings.yaml (user-writable).
/// Universal entries are read from AutoQAC Main.yaml (read-only).
/// </summary>
public sealed class ConfigurationServiceSkipListTests : IDisposable
{
  private readonly string _testDirectory;

  public ConfigurationServiceSkipListTests()
  {
    _testDirectory = Path.Combine(Path.GetTempPath(), "AutoQACSkipListTests_" + Guid.NewGuid());
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

  /// <summary>
  /// Sets up AutoQAC Main.yaml with Universal entries (read-only in production).
  /// </summary>
  private async Task SetupMainConfigAsync(string content)
  {
    await File.WriteAllTextAsync(Path.Combine(_testDirectory, "AutoQAC Main.yaml"), content);
  }

  /// <summary>
  /// Sets up AutoQAC Settings.yaml with user skip lists (user-writable).
  /// </summary>
  private async Task SetupSettingsConfigAsync(string content)
  {
    await File.WriteAllTextAsync(Path.Combine(_testDirectory, "AutoQAC Settings.yaml"), content);
  }

  #region GetGameSpecificSkipListAsync Tests

  [Fact]
  public async Task GetGameSpecificSkipListAsync_ShouldReturnOnlyUserSkipListEntries()
  {
    // Arrange - user skip list in Settings.yaml, Universal in Main.yaml
    var settingsContent = @"
Skip_Lists:
  SSE:
    - Skyrim.esm
    - Update.esm
";
    var mainConfigContent = @"
AutoQAC_Data:
  Skip_Lists:
    Universal:
      - UniversalPlugin.esm
";
    await SetupSettingsConfigAsync(settingsContent);
    await SetupMainConfigAsync(mainConfigContent);
    var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

    // Act
    var list = await service.GetGameSpecificSkipListAsync(GameType.SkyrimSe);

    // Assert
    list.Should().HaveCount(2);
    list.Should().Contain("Skyrim.esm");
    list.Should().Contain("Update.esm");
    list.Should().NotContain("UniversalPlugin.esm", "Universal entries should not be included");
  }

  [Fact]
  public async Task GetGameSpecificSkipListAsync_ShouldReturnEmptyList_WhenGameNotFound()
  {
    // Arrange - no FO3 entries in Settings.yaml
    var settingsContent = @"
Skip_Lists:
  SSE:
    - Skyrim.esm
";
    await SetupSettingsConfigAsync(settingsContent);
    var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

    // Act
    var list = await service.GetGameSpecificSkipListAsync(GameType.Fallout3);

    // Assert
    list.Should().BeEmpty();
  }

  [Fact]
  public async Task GetGameSpecificSkipListAsync_ShouldReturnEmptyList_WhenNoSettingsFile()
  {
    // Arrange - no Settings.yaml file
    var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

    // Act
    var list = await service.GetGameSpecificSkipListAsync(GameType.SkyrimSe);

    // Assert
    list.Should().BeEmpty();
  }

  [Fact]
  public async Task GetGameSpecificSkipListAsync_ShouldReturnCopy()
  {
    // Arrange
    var settingsContent = @"
Skip_Lists:
  SSE:
    - Skyrim.esm
";
    await SetupSettingsConfigAsync(settingsContent);
    var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

    // Act
    var list1 = await service.GetGameSpecificSkipListAsync(GameType.SkyrimSe);
    list1.Add("ModifiedPlugin.esp");
    var list2 = await service.GetGameSpecificSkipListAsync(GameType.SkyrimSe);

    // Assert
    list2.Should().NotContain("ModifiedPlugin.esp", "returned list should be a copy");
  }

  #endregion

  #region UpdateSkipListAsync Tests

  [Fact]
  public async Task UpdateSkipListAsync_ShouldPersistToSettingsFile()
  {
    // Arrange
    var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);
    var newSkipList = new List<string> { "NewPlugin1.esp", "NewPlugin2.esm" };

    // Act
    await service.UpdateSkipListAsync(GameType.SkyrimSe, newSkipList);
    // Flush debounced save to disk before verifying with a second instance
    await service.FlushPendingSavesAsync();

    // Recreate service to verify file was written
    var service2 = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);
    var loadedList = await service2.GetGameSpecificSkipListAsync(GameType.SkyrimSe);

    // Assert
    loadedList.Should().HaveCount(2);
    loadedList.Should().Contain("NewPlugin1.esp");
    loadedList.Should().Contain("NewPlugin2.esm");
  }

  [Fact]
  public async Task UpdateSkipListAsync_ShouldReplaceExistingEntries()
  {
    // Arrange - pre-existing user skip list
    var settingsContent = @"
Skip_Lists:
  SSE:
    - OldPlugin.esm
";
    await SetupSettingsConfigAsync(settingsContent);
    var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);
    var newSkipList = new List<string> { "NewPlugin1.esp", "NewPlugin2.esm" };

    // Act
    await service.UpdateSkipListAsync(GameType.SkyrimSe, newSkipList);

    // Assert
    var loadedList = await service.GetGameSpecificSkipListAsync(GameType.SkyrimSe);
    loadedList.Should().HaveCount(2);
    loadedList.Should().Contain("NewPlugin1.esp");
    loadedList.Should().Contain("NewPlugin2.esm");
    loadedList.Should().NotContain("OldPlugin.esm");
  }

  [Fact]
  public async Task UpdateSkipListAsync_ShouldEmitSkipListChangedEvent()
  {
    // Arrange
    var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

    var emittedGames = new List<GameType>();
    using var subscription = service.SkipListChanged.Subscribe(emittedGames.Add);

    // Act
    await service.UpdateSkipListAsync(GameType.SkyrimSe, ["Test.esp"]);

    // Assert
    emittedGames.Should().ContainSingle();
    emittedGames[0].Should().Be(GameType.SkyrimSe);
  }

  [Fact]
  public async Task UpdateSkipListAsync_ShouldNotAffectOtherGames()
  {
    // Arrange
    var settingsContent = @"
Skip_Lists:
  SSE:
    - Skyrim.esm
  FO4:
    - Fallout4.esm
";
    await SetupSettingsConfigAsync(settingsContent);
    var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

    // Act
    await service.UpdateSkipListAsync(GameType.SkyrimSe, ["NewSkyrim.esp"]);

    // Assert
    var fo4List = await service.GetGameSpecificSkipListAsync(GameType.Fallout4);
    fo4List.Should().Contain("Fallout4.esm", "Fallout 4 list should be unchanged");
  }

  [Fact]
  public async Task UpdateSkipListAsync_ShouldNotModifyMainConfig()
  {
    // Arrange - Main.yaml with Universal and game-specific entries
    var mainConfigContent = @"
AutoQAC_Data:
  Skip_Lists:
    SSE:
      - MainSkyrim.esm
    Universal:
      - Universal.esm
";
    await SetupMainConfigAsync(mainConfigContent);
    var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

    // Act
    await service.UpdateSkipListAsync(GameType.SkyrimSe, ["UserSkyrim.esp"]);

    // Assert - Main.yaml should be unchanged
    var mainContent = await File.ReadAllTextAsync(Path.Combine(_testDirectory, "AutoQAC Main.yaml"));
    mainContent.Should().Contain("MainSkyrim.esm", "Main.yaml should not be modified");
    mainContent.Should().Contain("Universal.esm", "Universal entries should remain");
  }

  #endregion

  #region AddToSkipListAsync Tests

  [Fact]
  public async Task AddToSkipListAsync_ShouldAddNewEntry()
  {
    // Arrange
    var settingsContent = @"
Skip_Lists:
  SSE:
    - Skyrim.esm
";
    await SetupSettingsConfigAsync(settingsContent);
    var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

    // Act
    await service.AddToSkipListAsync(GameType.SkyrimSe, "NewMod.esp");

    // Assert
    var list = await service.GetGameSpecificSkipListAsync(GameType.SkyrimSe);
    list.Should().Contain("NewMod.esp");
    list.Should().Contain("Skyrim.esm");
  }

  [Fact]
  public async Task AddToSkipListAsync_ShouldNotAddDuplicates()
  {
    // Arrange
    var settingsContent = @"
Skip_Lists:
  SSE:
    - Skyrim.esm
";
    await SetupSettingsConfigAsync(settingsContent);
    var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

    // Act
    await service.AddToSkipListAsync(GameType.SkyrimSe, "Skyrim.esm");

    // Assert
    var list = await service.GetGameSpecificSkipListAsync(GameType.SkyrimSe);
    list.Should().HaveCount(1, "duplicate should not be added");
  }

  [Fact]
  public async Task AddToSkipListAsync_ShouldBeCaseInsensitive()
  {
    // Arrange
    var settingsContent = @"
Skip_Lists:
  SSE:
    - Skyrim.esm
";
    await SetupSettingsConfigAsync(settingsContent);
    var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

    // Act
    await service.AddToSkipListAsync(GameType.SkyrimSe, "SKYRIM.ESM");

    // Assert
    var list = await service.GetGameSpecificSkipListAsync(GameType.SkyrimSe);
    list.Should().HaveCount(1, "case-insensitive duplicate should not be added");
  }

  [Fact]
  public async Task AddToSkipListAsync_ShouldThrow_WhenPluginNameEmpty()
  {
    // Arrange
    var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

    // Act
    Func<Task> act = () => service.AddToSkipListAsync(GameType.SkyrimSe, "");

    // Assert
    await act.Should().ThrowAsync<ArgumentException>();
  }

  [Fact]
  public async Task AddToSkipListAsync_ShouldCreateGameKeyIfNotExists()
  {
    // Arrange - Settings.yaml with SSE only
    var settingsContent = @"
Skip_Lists:
  SSE:
    - Skyrim.esm
";
    await SetupSettingsConfigAsync(settingsContent);
    var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

    // Act
    await service.AddToSkipListAsync(GameType.Fallout4, "NewFallout4Mod.esp");

    // Assert
    var list = await service.GetGameSpecificSkipListAsync(GameType.Fallout4);
    list.Should().Contain("NewFallout4Mod.esp");
  }

  [Fact]
  public async Task AddToSkipListAsync_ShouldWorkWithNoExistingSettingsFile()
  {
    // Arrange - no Settings.yaml
    var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

    // Act
    await service.AddToSkipListAsync(GameType.SkyrimSe, "NewMod.esp");

    // Assert
    var list = await service.GetGameSpecificSkipListAsync(GameType.SkyrimSe);
    list.Should().Contain("NewMod.esp");
  }

  #endregion

  #region RemoveFromSkipListAsync Tests

  [Fact]
  public async Task RemoveFromSkipListAsync_ShouldRemoveEntry()
  {
    // Arrange
    var settingsContent = @"
Skip_Lists:
  SSE:
    - Skyrim.esm
    - ToRemove.esp
";
    await SetupSettingsConfigAsync(settingsContent);
    var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

    // Act
    await service.RemoveFromSkipListAsync(GameType.SkyrimSe, "ToRemove.esp");

    // Assert
    var list = await service.GetGameSpecificSkipListAsync(GameType.SkyrimSe);
    list.Should().NotContain("ToRemove.esp");
    list.Should().Contain("Skyrim.esm");
  }

  [Fact]
  public async Task RemoveFromSkipListAsync_ShouldBeCaseInsensitive()
  {
    // Arrange
    var settingsContent = @"
Skip_Lists:
  SSE:
    - ToRemove.esp
";
    await SetupSettingsConfigAsync(settingsContent);
    var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

    // Act
    await service.RemoveFromSkipListAsync(GameType.SkyrimSe, "TOREMOVE.ESP");

    // Assert
    var list = await service.GetGameSpecificSkipListAsync(GameType.SkyrimSe);
    list.Should().BeEmpty();
  }

  [Fact]
  public async Task RemoveFromSkipListAsync_ShouldNotThrow_WhenEntryNotFound()
  {
    // Arrange
    var settingsContent = @"
Skip_Lists:
  SSE:
    - Skyrim.esm
";
    await SetupSettingsConfigAsync(settingsContent);
    var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

    // Act
    Func<Task> act = () => service.RemoveFromSkipListAsync(GameType.SkyrimSe, "NonExistent.esp");

    // Assert
    await act.Should().NotThrowAsync();
    var list = await service.GetGameSpecificSkipListAsync(GameType.SkyrimSe);
    list.Should().HaveCount(1);
  }

  [Fact]
  public async Task RemoveFromSkipListAsync_ShouldDoNothing_WhenPluginNameEmpty()
  {
    // Arrange
    var settingsContent = @"
Skip_Lists:
  SSE:
    - Skyrim.esm
";
    await SetupSettingsConfigAsync(settingsContent);
    var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

    // Act
    await service.RemoveFromSkipListAsync(GameType.SkyrimSe, "");

    // Assert
    var list = await service.GetGameSpecificSkipListAsync(GameType.SkyrimSe);
    list.Should().HaveCount(1);
  }

  #endregion

  #region GetSkipListAsync (Merged) Tests

  [Fact]
  public async Task GetSkipListAsync_ShouldMergeUserAndUniversalEntries()
  {
    // Arrange - user skip list + Universal from Main.yaml
    var settingsContent = @"
Skip_Lists:
  SSE:
    - UserPlugin.esp
";
    var mainConfigContent = @"
AutoQAC_Data:
  Skip_Lists:
    Universal:
      - Universal.esm
";
    await SetupSettingsConfigAsync(settingsContent);
    await SetupMainConfigAsync(mainConfigContent);
    var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

    // Act
    var mergedList = await service.GetSkipListAsync(GameType.SkyrimSe);

    // Assert
    mergedList.Should().Contain("UserPlugin.esp", "user skip list should be included");
    mergedList.Should().Contain("Universal.esm", "Universal should be merged");
  }

  [Fact]
  public async Task GetSkipListAsync_ShouldDeduplicateMergedEntries()
  {
    // Arrange - same entry in both user and Universal
    var settingsContent = @"
Skip_Lists:
  SSE:
    - Skyrim.esm
";
    var mainConfigContent = @"
AutoQAC_Data:
  Skip_Lists:
    Universal:
      - Skyrim.esm
";
    await SetupSettingsConfigAsync(settingsContent);
    await SetupMainConfigAsync(mainConfigContent);
    var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

    // Act
    var mergedList = await service.GetSkipListAsync(GameType.SkyrimSe);

    // Assert
    mergedList.Should().ContainSingle(x => x.Equals("Skyrim.esm", StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public async Task GetSkipListAsync_ShouldReturnUniversalOnly_WhenNoUserSkipList()
  {
    // Arrange - no Settings.yaml, only Main.yaml with Universal
    var mainConfigContent = @"
AutoQAC_Data:
  Skip_Lists:
    Universal:
      - Universal.esm
";
    await SetupMainConfigAsync(mainConfigContent);
    var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

    // Act
    var mergedList = await service.GetSkipListAsync(GameType.SkyrimSe);

    // Assert
    mergedList.Should().ContainSingle();
    mergedList.Should().Contain("Universal.esm");
  }

  [Fact]
  public async Task GetSkipListAsync_ShouldIncludeMainConfigGameSpecificEntries()
  {
    // Arrange - Main.yaml has game-specific entries that protect base game files
    var mainConfigContent = @"
AutoQAC_Data:
  Skip_Lists:
    SSE:
      - MainSkyrim.esm
    Universal:
      - Universal.esm
";
    await SetupMainConfigAsync(mainConfigContent);
    var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

    // Act
    var mergedList = await service.GetSkipListAsync(GameType.SkyrimSe);

    // Assert - both Universal and game-specific from Main.yaml should be included
    mergedList.Should().Contain("Universal.esm");
    mergedList.Should().Contain("MainSkyrim.esm",
      "Main.yaml game-specific entries should be included to protect base game");
  }

  #endregion

  #region TTW/Enderal Variant Skip List Tests

  /// <summary>
  /// TTW variant: GetSkipListAsync for FNV with GameVariant.TTW should include FO3 entries.
  /// </summary>
  [Fact]
  public async Task GetSkipListAsync_TTW_ShouldMergeFO3EntriesIntoFNV()
  {
    // Arrange
    var mainConfigContent = @"
AutoQAC_Data:
  Skip_Lists:
    FNV:
      - FalloutNV.esm
    FO3:
      - Fallout3.esm
      - Anchorage.esm
    Universal:
      - Universal.esm
";
    var settingsContent = @"
Skip_Lists:
  FNV:
    - UserFNVMod.esp
  FO3:
    - UserFO3Mod.esp
";
    await SetupMainConfigAsync(mainConfigContent);
    await SetupSettingsConfigAsync(settingsContent);
    var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

    // Act
    var list = await service.GetSkipListAsync(GameType.FalloutNewVegas, GameVariant.TTW);

    // Assert
    list.Should().Contain("FalloutNV.esm", "FNV entries from Main.yaml should be included");
    list.Should().Contain("UserFNVMod.esp", "FNV entries from user config should be included");
    list.Should().Contain("Fallout3.esm", "FO3 entries from Main.yaml should be merged for TTW");
    list.Should().Contain("Anchorage.esm", "FO3 DLC entries from Main.yaml should be merged for TTW");
    list.Should().Contain("UserFO3Mod.esp", "FO3 entries from user config should be merged for TTW");
    list.Should().Contain("Universal.esm", "Universal entries should still be included");
  }

  /// <summary>
  /// Non-TTW FNV: GetSkipListAsync should NOT include FO3 entries.
  /// </summary>
  [Fact]
  public async Task GetSkipListAsync_NonTTW_ShouldNotIncludeFO3Entries()
  {
    // Arrange
    var mainConfigContent = @"
AutoQAC_Data:
  Skip_Lists:
    FNV:
      - FalloutNV.esm
    FO3:
      - Fallout3.esm
      - Anchorage.esm
    Universal:
      - Universal.esm
";
    await SetupMainConfigAsync(mainConfigContent);
    var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

    // Act - No TTW variant
    var list = await service.GetSkipListAsync(GameType.FalloutNewVegas, GameVariant.None);

    // Assert
    list.Should().Contain("FalloutNV.esm", "FNV entries should be included");
    list.Should().Contain("Universal.esm", "Universal should be included");
    list.Should().NotContain("Fallout3.esm", "FO3 entries must NOT be included without TTW variant");
    list.Should().NotContain("Anchorage.esm", "FO3 DLC must NOT be included without TTW variant");
  }

  /// <summary>
  /// Enderal variant: GetSkipListAsync for SSE with GameVariant.Enderal should use Enderal key.
  /// </summary>
  [Fact]
  public async Task GetSkipListAsync_Enderal_ShouldUseEnderalKey()
  {
    // Arrange
    var mainConfigContent = @"
AutoQAC_Data:
  Skip_Lists:
    SSE:
      - Skyrim.esm
      - Update.esm
    Enderal:
      - Enderal - Forgotten Stories.esm
      - SkyUI_SE.esp
    Universal:
      - Universal.esm
";
    await SetupMainConfigAsync(mainConfigContent);
    var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

    // Act
    var list = await service.GetSkipListAsync(GameType.SkyrimSe, GameVariant.Enderal);

    // Assert
    list.Should().Contain("Enderal - Forgotten Stories.esm", "Enderal-specific entries should be used");
    list.Should().Contain("SkyUI_SE.esp", "Enderal skip list should include all Enderal entries");
    list.Should().Contain("Universal.esm", "Universal should still be included");
    list.Should().NotContain("Skyrim.esm", "SSE entries should NOT be used when Enderal variant is active");
    list.Should().NotContain("Update.esm", "SSE entries should NOT be used when Enderal variant is active");
  }

  #endregion

  #region Integration Tests

  [Fact]
  public async Task SkipListOperations_ShouldWorkWithNoInitialFiles()
  {
    // Arrange - no config files exist
    var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

    // Act
    await service.AddToSkipListAsync(GameType.SkyrimSe, "FirstMod.esp");
    await service.AddToSkipListAsync(GameType.SkyrimSe, "SecondMod.esp");
    await service.RemoveFromSkipListAsync(GameType.SkyrimSe, "FirstMod.esp");

    // Assert
    var list = await service.GetGameSpecificSkipListAsync(GameType.SkyrimSe);
    list.Should().ContainSingle();
    list.Should().Contain("SecondMod.esp");
  }

  [Fact]
  public async Task GetSkipListAsync_ShouldStillMergeUniversal_AfterUpdatingUserSkipList()
  {
    // Arrange
    var mainConfigContent = @"
AutoQAC_Data:
  Skip_Lists:
    Universal:
      - Universal.esm
";
    await SetupMainConfigAsync(mainConfigContent);
    var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

    // Act
    await service.UpdateSkipListAsync(GameType.SkyrimSe, ["NewSkyrim.esp"]);
    var mergedList = await service.GetSkipListAsync(GameType.SkyrimSe);

    // Assert
    mergedList.Should().Contain("NewSkyrim.esp", "user skip list should be updated");
    mergedList.Should().Contain("Universal.esm", "Universal should still be merged");
  }

  [Fact]
  public async Task MultipleGames_ShouldHaveIndependentSkipLists()
  {
    // Arrange
    var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

    // Act
    await service.AddToSkipListAsync(GameType.SkyrimSe, "SkyrimMod.esp");
    await service.AddToSkipListAsync(GameType.Fallout4, "FalloutMod.esp");

    // Assert
    var skyrimList = await service.GetGameSpecificSkipListAsync(GameType.SkyrimSe);
    var falloutList = await service.GetGameSpecificSkipListAsync(GameType.Fallout4);

    skyrimList.Should().Contain("SkyrimMod.esp");
    skyrimList.Should().NotContain("FalloutMod.esp");
    falloutList.Should().Contain("FalloutMod.esp");
    falloutList.Should().NotContain("SkyrimMod.esp");
  }

  #endregion

  #region GameType.Unknown Skip List Tests (TEST-03)

  /// <summary>
  /// When GameType.Unknown is passed to GetSkipListAsync, GetGameKey maps it to "Unknown"
  /// which won't match any key in the skip list config. Only Universal entries should be returned.
  /// </summary>
  [Fact]
  public async Task GetSkipListAsync_UnknownGameType_ReturnsOnlyUniversalEntries()
  {
    // Arrange
    var mainConfigContent = @"
AutoQAC_Data:
  Skip_Lists:
    SSE:
      - Skyrim.esm
    Universal:
      - Update.esm
";
    var settingsContent = @"
Skip_Lists:
  SSE:
    - UserSkyrim.esp
";
    await SetupMainConfigAsync(mainConfigContent);
    await SetupSettingsConfigAsync(settingsContent);
    var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

    // Act
    var list = await service.GetSkipListAsync(GameType.Unknown);

    // Assert
    list.Should().Contain("Update.esm", "Universal entries should always be included");
    list.Should().NotContain("Skyrim.esm", "SSE entries should not be included for Unknown game type");
    list.Should().NotContain("UserSkyrim.esp", "user SSE entries should not be included for Unknown game type");
  }

  /// <summary>
  /// When GameType.Unknown is used and there are no Universal entries, result should be empty.
  /// </summary>
  [Fact]
  public async Task GetSkipListAsync_UnknownGameType_NoUniversal_ReturnsEmpty()
  {
    // Arrange -- no Universal key in config
    var mainConfigContent = @"
AutoQAC_Data:
  Skip_Lists:
    SSE:
      - Skyrim.esm
";
    await SetupMainConfigAsync(mainConfigContent);
    var service = new ConfigurationService(Mock.Of<ILoggingService>(), _testDirectory);

    // Act
    var list = await service.GetSkipListAsync(GameType.Unknown);

    // Assert
    list.Should().BeEmpty("no Universal entries and Unknown doesn't match any game key");
  }

  #endregion
}
