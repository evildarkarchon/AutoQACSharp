using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.State;
using AutoQAC.ViewModels;
using FluentAssertions;
using Moq;
using ReactiveUI;

namespace AutoQAC.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="ProgressViewModel"/> covering progress tracking,
/// state synchronization, and edge cases like division by zero.
/// </summary>
public sealed class ProgressViewModelTests
{
    private readonly Mock<IStateService> _stateServiceMock;
    private readonly Mock<ICleaningOrchestrator> _orchestratorMock;
    private readonly BehaviorSubject<AppState> _stateSubject;
    private readonly Subject<(string plugin, CleaningStatus status)> _pluginProcessedSubject;

    /// <summary>
    /// Initializes test fixtures with default mock configurations.
    /// </summary>
    public ProgressViewModelTests()
    {
        // Force immediate execution for tests
        RxApp.MainThreadScheduler = Scheduler.Immediate;

        _stateSubject = new BehaviorSubject<AppState>(new AppState());
        _pluginProcessedSubject = new Subject<(string, CleaningStatus)>();

        _stateServiceMock = new Mock<IStateService>();
        _stateServiceMock.Setup(s => s.StateChanged).Returns(_stateSubject);
        _stateServiceMock.Setup(s => s.PluginProcessed).Returns(_pluginProcessedSubject);
        _stateServiceMock.Setup(s => s.CurrentState).Returns(new AppState());

        _orchestratorMock = new Mock<ICleaningOrchestrator>();
    }

    /// <summary>
    /// Creates a new ViewModel instance using the test fixtures.
    /// </summary>
    private ProgressViewModel CreateViewModel()
    {
        return new ProgressViewModel(_stateServiceMock.Object, _orchestratorMock.Object);
    }

    [Fact]
    public void ShouldUpdateProperties_WhenStateChanges()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        var newState = new AppState
        {
            CurrentPlugin = "Test.esp",
            Progress = 5,
            TotalPlugins = 10
        };
        _stateSubject.OnNext(newState);

        // Assert
        vm.CurrentPlugin.Should().Be("Test.esp");
        vm.Progress.Should().Be(5);
        vm.Total.Should().Be(10);
        vm.ProgressText.Should().Be("5 / 10 (50%)");
    }

    [Fact]
    public void StopCommand_ShouldCallOrchestratorStop()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        vm.StopCommand.Execute().Subscribe();

        // Assert
        _orchestratorMock.Verify(x => x.StopCleaning(), Times.Once);
    }

    #region Edge Case Tests

    /// <summary>
    /// Verifies that ProgressText handles division by zero when TotalPlugins is 0.
    /// This tests the edge case where cleaning starts with an empty plugin list.
    /// </summary>
    [Fact]
    public void ProgressText_ShouldHandleZeroTotal()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act - Set Total to 0 (edge case)
        vm.Total = 0;
        vm.Progress = 0;

        // Assert
        // Should not throw and should show some reasonable text
        vm.ProgressText.Should().NotBeNull("progress text should never be null");
        vm.ProgressText.Should().Contain("0", "should show zero progress");
        // The formula (current * 100 / total) would divide by zero, so implementation
        // should handle this case - returns "0 / 0 (0%)"
    }

    /// <summary>
    /// Verifies that ProgressText shows 100% when progress equals total.
    /// </summary>
    [Fact]
    public void ProgressText_ShouldShow100Percent_WhenComplete()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        vm.Total = 10;
        vm.Progress = 10;

        // Assert
        vm.ProgressText.Should().Contain("100%", "should show 100% when complete");
        vm.ProgressText.Should().Contain("10 / 10");
    }

    /// <summary>
    /// Verifies that rapid state updates are handled correctly without
    /// causing race conditions or missed updates.
    /// </summary>
    [Fact]
    public async Task ViewModel_ShouldHandleRapidStateUpdates()
    {
        // Arrange
        var vm = CreateViewModel();
        var updates = new List<int>();

        // Track progress changes
        vm.WhenAnyValue(x => x.Progress)
            .Subscribe(p => updates.Add(p));

        // Act - Simulate rapid state updates
        for (int i = 0; i <= 100; i++)
        {
            var state = new AppState
            {
                Progress = i,
                TotalPlugins = 100,
                CurrentPlugin = $"Plugin{i}.esp"
            };
            _stateSubject.OnNext(state);
        }

        // Small delay to allow updates to propagate
        await Task.Delay(50);

        // Assert
        vm.Progress.Should().Be(100, "final progress should be 100");
        updates.Should().Contain(100, "should have received the final update");
    }

    /// <summary>
    /// Verifies that OnPluginProcessed correctly appends to log output.
    /// </summary>
    [Fact]
    public void OnPluginProcessed_ShouldAppendToLogOutput()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        _pluginProcessedSubject.OnNext(("Plugin1.esp", CleaningStatus.Cleaned));
        _pluginProcessedSubject.OnNext(("Plugin2.esp", CleaningStatus.Skipped));
        _pluginProcessedSubject.OnNext(("Plugin3.esp", CleaningStatus.Failed));

        // Assert
        vm.LogOutput.Should().Contain("Plugin1.esp");
        vm.LogOutput.Should().Contain("Plugin2.esp");
        vm.LogOutput.Should().Contain("Plugin3.esp");
        vm.LogOutput.Should().Contain("Cleaned");
        vm.LogOutput.Should().Contain("Skipped");
        vm.LogOutput.Should().Contain("Failed");
    }

    /// <summary>
    /// Verifies that state counts are correctly updated from AppState.
    /// </summary>
    [Fact]
    public void ViewModel_ShouldUpdateCounts_FromState()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        var state = new AppState
        {
            CleanedPlugins = new HashSet<string> { "a.esp", "b.esp", "c.esp" },
            SkippedPlugins = new HashSet<string> { "d.esp", "e.esp" },
            FailedPlugins = new HashSet<string> { "f.esp" }
        };
        _stateSubject.OnNext(state);

        // Assert
        vm.CleanedCount.Should().Be(3);
        vm.SkippedCount.Should().Be(2);
        vm.FailedCount.Should().Be(1);
    }

    /// <summary>
    /// Verifies that CurrentPlugin is updated from state changes.
    /// </summary>
    [Fact]
    public void CurrentPlugin_ShouldUpdateFromState()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        var state = new AppState { CurrentPlugin = "TestPlugin.esp" };
        _stateSubject.OnNext(state);

        // Assert
        vm.CurrentPlugin.Should().Be("TestPlugin.esp");
    }

    /// <summary>
    /// Verifies that ProgressText updates reactively when Progress or Total changes.
    /// </summary>
    [Fact]
    public void ProgressText_ShouldUpdateReactively()
    {
        // Arrange
        var vm = CreateViewModel();
        var progressTextChanges = new List<string>();

        vm.WhenAnyValue(x => x.ProgressText)
            .Subscribe(text => progressTextChanges.Add(text));

        // Act
        vm.Total = 10;
        vm.Progress = 5;

        // Assert
        progressTextChanges.Should().Contain(t => t.Contains("5 / 10"),
            "should have emitted progress text with 5 / 10");
        progressTextChanges.Should().Contain(t => t.Contains("50%"),
            "should show 50% progress");
    }

    /// <summary>
    /// Verifies proper cleanup when ViewModel is disposed.
    /// </summary>
    [Fact]
    public void Dispose_ShouldCleanupSubscriptions()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act & Assert
        FluentActions.Invoking(() => vm.Dispose())
            .Should().NotThrow("disposal should complete without exception");

        // After disposal, state changes should not cause errors
        _stateSubject.OnNext(new AppState { Progress = 99 });
        // If subscriptions are not cleaned up, this might cause issues
    }

    /// <summary>
    /// Verifies that LogOutput handles very long plugin names.
    /// </summary>
    [Fact]
    public void LogOutput_ShouldHandleLongPluginNames()
    {
        // Arrange
        var vm = CreateViewModel();
        var longPluginName = new string('A', 500) + ".esp";

        // Act
        _pluginProcessedSubject.OnNext((longPluginName, CleaningStatus.Cleaned));

        // Assert
        vm.LogOutput.Should().Contain(longPluginName,
            "long plugin names should be handled without truncation");
    }

    /// <summary>
    /// Verifies that ProgressText handles edge case of Progress > Total.
    /// </summary>
    [Fact]
    public void ProgressText_ShouldHandleProgressGreaterThanTotal()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act - This shouldn't happen in practice, but we should handle it
        vm.Total = 5;
        vm.Progress = 10;

        // Assert
        // Should not throw or produce nonsensical output
        vm.ProgressText.Should().NotBeNullOrEmpty();
        // Percentage might be > 100%, which is technically correct for the inputs
    }

    #endregion

    #region State Synchronization Tests

    /// <summary>
    /// Verifies that initial state is correctly loaded into ViewModel.
    /// </summary>
    [Fact]
    public void Constructor_ShouldLoadInitialState()
    {
        // Arrange
        var initialState = new AppState
        {
            Progress = 3,
            TotalPlugins = 10,
            CurrentPlugin = "InitialPlugin.esp",
            CleanedPlugins = new HashSet<string> { "a.esp", "b.esp" },
            SkippedPlugins = new HashSet<string> { "c.esp" },
            FailedPlugins = new HashSet<string>()
        };
        _stateServiceMock.Setup(s => s.CurrentState).Returns(initialState);

        // Create a new subject with initial state
        var stateSubject = new BehaviorSubject<AppState>(initialState);
        _stateServiceMock.Setup(s => s.StateChanged).Returns(stateSubject);

        // Act
        var vm = new ProgressViewModel(_stateServiceMock.Object, _orchestratorMock.Object);

        // Assert
        vm.Progress.Should().Be(3);
        vm.Total.Should().Be(10);
        vm.CurrentPlugin.Should().Be("InitialPlugin.esp");
        vm.CleanedCount.Should().Be(2);
        vm.SkippedCount.Should().Be(1);
        vm.FailedCount.Should().Be(0);
    }

    #endregion
}
