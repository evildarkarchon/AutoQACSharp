using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.State;
using AutoQAC.ViewModels;
using FluentAssertions;
using Moq;
using ReactiveUI;

namespace AutoQAC.Tests.ViewModels;

public sealed class SkipListViewModelTests
{
    private readonly Mock<IConfigurationService> _configServiceMock;
    private readonly Mock<IStateService> _stateServiceMock;
    private readonly Mock<ILoggingService> _loggerMock;
    private readonly BehaviorSubject<AppState> _stateSubject;

    public SkipListViewModelTests()
    {
        _configServiceMock = new Mock<IConfigurationService>();
        _stateServiceMock = new Mock<IStateService>();
        _loggerMock = new Mock<ILoggingService>();
        _stateSubject = new BehaviorSubject<AppState>(new AppState { CurrentGameType = GameType.SkyrimSe });

        // Default setup
        _stateServiceMock.Setup(s => s.StateChanged).Returns(_stateSubject);
        _stateServiceMock.Setup(s => s.CurrentState).Returns(() => _stateSubject.Value);

        RxApp.MainThreadScheduler = Scheduler.Immediate;
    }

    private SkipListViewModel CreateViewModel()
    {
        return new SkipListViewModel(
            _configServiceMock.Object,
            _stateServiceMock.Object,
            _loggerMock.Object);
    }

    #region LoadSkipListAsync Tests

    [Fact]
    public async Task LoadSkipListAsync_ShouldPopulateSkipListEntries()
    {
        // Arrange
        var skipList = new List<string> { "Plugin1.esp", "Plugin2.esm" };
        _configServiceMock.Setup(x => x.GetGameSpecificSkipListAsync(GameType.SkyrimSe, It.IsAny<CancellationToken>()))
            .ReturnsAsync(skipList);

        var vm = CreateViewModel();

        // Act
        await vm.LoadSkipListAsync();

        // Assert
        vm.SkipListEntries.Should().HaveCount(2);
        vm.SkipListEntries.Should().Contain("Plugin1.esp");
        vm.SkipListEntries.Should().Contain("Plugin2.esm");
    }

    [Fact]
    public async Task LoadSkipListAsync_ShouldSetSelectedGameFromState()
    {
        // Arrange
        _stateSubject.OnNext(new AppState { CurrentGameType = GameType.Fallout4 });
        _configServiceMock.Setup(x => x.GetGameSpecificSkipListAsync(GameType.Fallout4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var vm = CreateViewModel();

        // Act
        await vm.LoadSkipListAsync();

        // Assert
        vm.SelectedGame.Should().Be(GameType.Fallout4);
    }

    [Fact]
    public async Task LoadSkipListAsync_ShouldUseFirstGameIfUnknown()
    {
        // Arrange
        _stateSubject.OnNext(new AppState { CurrentGameType = GameType.Unknown });
        _configServiceMock.Setup(x => x.GetGameSpecificSkipListAsync(It.IsAny<GameType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var vm = CreateViewModel();

        // Act
        await vm.LoadSkipListAsync();

        // Assert
        vm.SelectedGame.Should().NotBe(GameType.Unknown);
    }

    [Fact]
    public async Task LoadSkipListAsync_ShouldPopulateAvailablePlugins()
    {
        // Arrange
        var loadedPlugins = new List<PluginInfo>
        {
            new() { FileName = "Mod1.esp", FullPath = "Mod1.esp" },
            new() { FileName = "Mod2.esp", FullPath = "Mod2.esp" },
            new() { FileName = "InSkipList.esp", FullPath = "InSkipList.esp" }
        };
        var skipList = new List<string> { "InSkipList.esp" };

        _stateSubject.OnNext(new AppState
        {
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = loadedPlugins
        });

        _configServiceMock.Setup(x => x.GetGameSpecificSkipListAsync(GameType.SkyrimSe, It.IsAny<CancellationToken>()))
            .ReturnsAsync(skipList);

        var vm = CreateViewModel();

        // Act
        await vm.LoadSkipListAsync();

        // Assert
        vm.AvailablePlugins.Should().HaveCount(2);
        vm.AvailablePlugins.Should().Contain("Mod1.esp");
        vm.AvailablePlugins.Should().Contain("Mod2.esp");
        vm.AvailablePlugins.Should().NotContain("InSkipList.esp", "plugins in skip list should be excluded");
    }

    #endregion

    #region AddSelectedPluginCommand Tests

    [Fact]
    public async Task AddSelectedPluginCommand_ShouldAddToSkipList()
    {
        // Arrange
        var loadedPlugins = new List<PluginInfo>
        {
            new() { FileName = "NewMod.esp", FullPath = "NewMod.esp" }
        };
        _stateSubject.OnNext(new AppState
        {
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = loadedPlugins
        });

        _configServiceMock.Setup(x => x.GetGameSpecificSkipListAsync(GameType.SkyrimSe, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var vm = CreateViewModel();
        await vm.LoadSkipListAsync();

        vm.SelectedPlugin = "NewMod.esp";

        // Act
        await vm.AddSelectedPluginCommand.Execute();

        // Assert
        vm.SkipListEntries.Should().Contain("NewMod.esp");
        vm.AvailablePlugins.Should().NotContain("NewMod.esp");
    }

    [Fact]
    public async Task AddSelectedPluginCommand_ShouldBeDisabled_WhenNoPluginSelected()
    {
        // Arrange
        _configServiceMock.Setup(x => x.GetGameSpecificSkipListAsync(GameType.SkyrimSe, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var vm = CreateViewModel();
        await vm.LoadSkipListAsync();

        vm.SelectedPlugin = null;

        // Assert
        var canExecute = await vm.AddSelectedPluginCommand.CanExecute.FirstAsync();
        canExecute.Should().BeFalse();
    }

    #endregion

    #region AddManualEntryCommand Tests

    [Fact]
    public async Task AddManualEntryCommand_ShouldAddValidEntry()
    {
        // Arrange
        _configServiceMock.Setup(x => x.GetGameSpecificSkipListAsync(GameType.SkyrimSe, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var vm = CreateViewModel();
        await vm.LoadSkipListAsync();

        vm.ManualEntryText = "NewPlugin.esp";

        // Act
        await vm.AddManualEntryCommand.Execute();

        // Assert
        vm.SkipListEntries.Should().Contain("NewPlugin.esp");
        vm.ManualEntryText.Should().BeEmpty();
        vm.ManualEntryError.Should().BeNull();
    }

    [Fact]
    public async Task AddManualEntryCommand_ShouldShowError_WhenInvalidExtension()
    {
        // Arrange
        _configServiceMock.Setup(x => x.GetGameSpecificSkipListAsync(GameType.SkyrimSe, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var vm = CreateViewModel();
        await vm.LoadSkipListAsync();

        vm.ManualEntryText = "InvalidFile.txt";

        // Assert
        var canExecute = await vm.AddManualEntryCommand.CanExecute.FirstAsync();
        canExecute.Should().BeFalse();
        vm.ManualEntryError.Should().Contain("Must end with");
    }

    [Fact]
    public async Task AddManualEntryCommand_ShouldShowError_WhenDuplicate()
    {
        // Arrange
        var skipList = new List<string> { "ExistingPlugin.esp" };
        _configServiceMock.Setup(x => x.GetGameSpecificSkipListAsync(GameType.SkyrimSe, It.IsAny<CancellationToken>()))
            .ReturnsAsync(skipList);

        var vm = CreateViewModel();
        await vm.LoadSkipListAsync();

        vm.ManualEntryText = "ExistingPlugin.esp";
        await vm.AddManualEntryCommand.Execute();

        // Assert
        vm.ManualEntryError.Should().Contain("already in skip list");
        vm.SkipListEntries.Should().HaveCount(1, "duplicate should not be added");
    }

    [Fact]
    public async Task AddManualEntryCommand_ShouldAcceptEsmAndEsl()
    {
        // Arrange
        _configServiceMock.Setup(x => x.GetGameSpecificSkipListAsync(GameType.SkyrimSe, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var vm = CreateViewModel();
        await vm.LoadSkipListAsync();

        // Act - add .esm
        vm.ManualEntryText = "Master.esm";
        await vm.AddManualEntryCommand.Execute();

        // Act - add .esl
        vm.ManualEntryText = "Light.esl";
        await vm.AddManualEntryCommand.Execute();

        // Assert
        vm.SkipListEntries.Should().Contain("Master.esm");
        vm.SkipListEntries.Should().Contain("Light.esl");
    }

    #endregion

    #region RemoveSelectedEntryCommand Tests

    [Fact]
    public async Task RemoveSelectedEntryCommand_ShouldRemoveFromSkipList()
    {
        // Arrange
        var skipList = new List<string> { "ToRemove.esp", "ToKeep.esp" };
        _configServiceMock.Setup(x => x.GetGameSpecificSkipListAsync(GameType.SkyrimSe, It.IsAny<CancellationToken>()))
            .ReturnsAsync(skipList);

        var vm = CreateViewModel();
        await vm.LoadSkipListAsync();

        vm.SelectedEntry = "ToRemove.esp";

        // Act
        await vm.RemoveSelectedEntryCommand.Execute();

        // Assert
        vm.SkipListEntries.Should().NotContain("ToRemove.esp");
        vm.SkipListEntries.Should().Contain("ToKeep.esp");
    }

    [Fact]
    public async Task RemoveSelectedEntryCommand_ShouldAddBackToAvailablePlugins()
    {
        // Arrange
        var loadedPlugins = new List<PluginInfo>
        {
            new() { FileName = "InSkipList.esp", FullPath = "InSkipList.esp" }
        };
        var skipList = new List<string> { "InSkipList.esp" };

        _stateSubject.OnNext(new AppState
        {
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean = loadedPlugins
        });

        _configServiceMock.Setup(x => x.GetGameSpecificSkipListAsync(GameType.SkyrimSe, It.IsAny<CancellationToken>()))
            .ReturnsAsync(skipList);

        var vm = CreateViewModel();
        await vm.LoadSkipListAsync();

        vm.SelectedEntry = "InSkipList.esp";

        // Act
        await vm.RemoveSelectedEntryCommand.Execute();

        // Assert
        vm.AvailablePlugins.Should().Contain("InSkipList.esp");
    }

    [Fact]
    public async Task RemoveSelectedEntryCommand_ShouldBeDisabled_WhenNoEntrySelected()
    {
        // Arrange
        _configServiceMock.Setup(x => x.GetGameSpecificSkipListAsync(GameType.SkyrimSe, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "Plugin.esp" });

        var vm = CreateViewModel();
        await vm.LoadSkipListAsync();

        vm.SelectedEntry = null;

        // Assert
        var canExecute = await vm.RemoveSelectedEntryCommand.CanExecute.FirstAsync();
        canExecute.Should().BeFalse();
    }

    #endregion

    #region HasUnsavedChanges Tests

    [Fact]
    public async Task HasUnsavedChanges_ShouldBeFalse_AfterLoading()
    {
        // Arrange
        _configServiceMock.Setup(x => x.GetGameSpecificSkipListAsync(GameType.SkyrimSe, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "Plugin.esp" });

        var vm = CreateViewModel();
        await vm.LoadSkipListAsync();

        // Assert
        vm.HasUnsavedChanges.Should().BeFalse();
    }

    [Fact]
    public async Task HasUnsavedChanges_ShouldBeTrue_AfterAddingEntry()
    {
        // Arrange
        _configServiceMock.Setup(x => x.GetGameSpecificSkipListAsync(GameType.SkyrimSe, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var vm = CreateViewModel();
        await vm.LoadSkipListAsync();

        // Act
        vm.ManualEntryText = "NewPlugin.esp";
        await vm.AddManualEntryCommand.Execute();

        // Assert
        vm.HasUnsavedChanges.Should().BeTrue();
    }

    [Fact]
    public async Task HasUnsavedChanges_ShouldBeTrue_AfterRemovingEntry()
    {
        // Arrange
        _configServiceMock.Setup(x => x.GetGameSpecificSkipListAsync(GameType.SkyrimSe, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "Plugin.esp" });

        var vm = CreateViewModel();
        await vm.LoadSkipListAsync();

        vm.SelectedEntry = "Plugin.esp";

        // Act
        await vm.RemoveSelectedEntryCommand.Execute();

        // Assert
        vm.HasUnsavedChanges.Should().BeTrue();
    }

    [Fact]
    public async Task HasUnsavedChanges_ShouldBeFalse_WhenChangesReverted()
    {
        // Arrange
        _configServiceMock.Setup(x => x.GetGameSpecificSkipListAsync(GameType.SkyrimSe, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "Original.esp" });

        var vm = CreateViewModel();
        await vm.LoadSkipListAsync();

        // Add then remove
        vm.ManualEntryText = "New.esp";
        await vm.AddManualEntryCommand.Execute();
        vm.SelectedEntry = "New.esp";
        await vm.RemoveSelectedEntryCommand.Execute();

        // Assert
        vm.HasUnsavedChanges.Should().BeFalse();
    }

    #endregion

    #region SaveCommand Tests

    [Fact]
    public async Task SaveCommand_ShouldCallUpdateSkipListAsync()
    {
        // Arrange
        _configServiceMock.Setup(x => x.GetGameSpecificSkipListAsync(GameType.SkyrimSe, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var vm = CreateViewModel();
        await vm.LoadSkipListAsync();

        vm.ManualEntryText = "NewPlugin.esp";
        await vm.AddManualEntryCommand.Execute();

        // Act
        await vm.SaveCommand.Execute();

        // Assert
        _configServiceMock.Verify(
            x => x.UpdateSkipListAsync(
                GameType.SkyrimSe,
                It.Is<List<string>>(l => l.Contains("NewPlugin.esp")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SaveCommand_ShouldReturnTrue_OnSuccess()
    {
        // Arrange
        _configServiceMock.Setup(x => x.GetGameSpecificSkipListAsync(GameType.SkyrimSe, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var vm = CreateViewModel();
        await vm.LoadSkipListAsync();

        // Act
        var result = await vm.SaveCommand.Execute();

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region CancelCommand Tests

    [Fact]
    public async Task CancelCommand_ShouldReturnFalse()
    {
        // Arrange
        _configServiceMock.Setup(x => x.GetGameSpecificSkipListAsync(GameType.SkyrimSe, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var vm = CreateViewModel();
        await vm.LoadSkipListAsync();

        // Act
        var result = await vm.CancelCommand.Execute();

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Validation Tests

    [Theory]
    [InlineData("valid.esp", null)]
    [InlineData("valid.esm", null)]
    [InlineData("valid.esl", null)]
    [InlineData("UPPERCASE.ESP", null)]
    [InlineData("ab.esp", null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("invalid.txt", "Must end with")]
    [InlineData("noextension", "Must end with")]
    [InlineData("a.es", "too short")]
    public async Task ManualEntryValidation_ShouldShowCorrectError(string input, string? expectedErrorContains)
    {
        // Arrange
        _configServiceMock.Setup(x => x.GetGameSpecificSkipListAsync(GameType.SkyrimSe, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var vm = CreateViewModel();
        await vm.LoadSkipListAsync();

        // Act
        vm.ManualEntryText = input;

        // Assert
        if (expectedErrorContains == null)
        {
            vm.ManualEntryError.Should().BeNull();
        }
        else
        {
            vm.ManualEntryError.Should().Contain(expectedErrorContains);
        }
    }

    #endregion

    #region AvailableGames Tests

    [Fact]
    public void AvailableGames_ShouldNotContainUnknown()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert
        vm.AvailableGames.Should().NotContain(GameType.Unknown);
        vm.AvailableGames.Should().NotBeEmpty();
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public async Task Dispose_ShouldNotThrow()
    {
        // Arrange
        _configServiceMock.Setup(x => x.GetGameSpecificSkipListAsync(GameType.SkyrimSe, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var vm = CreateViewModel();
        await vm.LoadSkipListAsync();

        // Act & Assert
        FluentActions.Invoking(() => vm.Dispose())
            .Should().NotThrow();
    }

    #endregion
}
