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
/// state synchronization, per-plugin stats, results summary mode, and edge cases.
/// </summary>
public sealed class ProgressViewModelTests
{
    private readonly Mock<IStateService> _stateServiceMock;
    private readonly Mock<ICleaningOrchestrator> _orchestratorMock;
    private readonly BehaviorSubject<AppState> _stateSubject;
    private readonly Subject<(string plugin, CleaningStatus status)> _pluginProcessedSubject;
    private readonly Subject<PluginCleaningResult> _detailedPluginResultSubject;
    private readonly Subject<CleaningSessionResult> _cleaningCompletedSubject;

    /// <summary>
    /// Initializes test fixtures with default mock configurations.
    /// </summary>
    public ProgressViewModelTests()
    {
        // Force immediate execution for tests
        RxApp.MainThreadScheduler = Scheduler.Immediate;

        _stateSubject = new BehaviorSubject<AppState>(new AppState());
        _pluginProcessedSubject = new Subject<(string, CleaningStatus)>();
        _detailedPluginResultSubject = new Subject<PluginCleaningResult>();
        _cleaningCompletedSubject = new Subject<CleaningSessionResult>();

        _stateServiceMock = new Mock<IStateService>();
        _stateServiceMock.Setup(s => s.StateChanged).Returns(_stateSubject);
        _stateServiceMock.Setup(s => s.PluginProcessed).Returns(_pluginProcessedSubject);
        _stateServiceMock.Setup(s => s.DetailedPluginResult).Returns(_detailedPluginResultSubject);
        _stateServiceMock.Setup(s => s.CleaningCompleted).Returns(_cleaningCompletedSubject);
        _stateServiceMock.Setup(s => s.CurrentState).Returns(new AppState());

        _orchestratorMock = new Mock<ICleaningOrchestrator>();
        _orchestratorMock.Setup(o => o.HangDetected).Returns(new Subject<bool>());
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
    public async Task StopCommand_ShouldCallOrchestratorStop()
    {
        // Arrange
        _orchestratorMock.Setup(x => x.StopCleaningAsync()).Returns(Task.CompletedTask);
        var vm = CreateViewModel();

        // Act
        await vm.StopCommand.Execute();

        // Assert
        _orchestratorMock.Verify(x => x.StopCleaningAsync(), Times.Once);
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
    /// Verifies that DetailedPluginResult adds results to CompletedPlugins collection.
    /// </summary>
    [Fact]
    public void OnDetailedResult_ShouldAddToCompletedPlugins()
    {
        // Arrange
        var vm = CreateViewModel();

        var result1 = new PluginCleaningResult
        {
            PluginName = "Plugin1.esp",
            Status = CleaningStatus.Cleaned,
            Success = true,
            Statistics = new CleaningStatistics { ItemsRemoved = 5, ItemsUndeleted = 2 }
        };
        var result2 = new PluginCleaningResult
        {
            PluginName = "Plugin2.esp",
            Status = CleaningStatus.Skipped,
            Success = true
        };
        var result3 = new PluginCleaningResult
        {
            PluginName = "Plugin3.esp",
            Status = CleaningStatus.Failed,
            Success = false,
            Message = "Timeout"
        };

        // Act
        _detailedPluginResultSubject.OnNext(result1);
        _detailedPluginResultSubject.OnNext(result2);
        _detailedPluginResultSubject.OnNext(result3);

        // Assert
        vm.CompletedPlugins.Should().HaveCount(3);
        vm.CompletedPlugins[0].PluginName.Should().Be("Plugin1.esp");
        vm.CompletedPlugins[1].PluginName.Should().Be("Plugin2.esp");
        vm.CompletedPlugins[2].PluginName.Should().Be("Plugin3.esp");
    }

    /// <summary>
    /// Verifies that per-plugin counter badges update from DetailedPluginResult.
    /// </summary>
    [Fact]
    public void OnDetailedResult_ShouldUpdateCurrentCounterBadges()
    {
        // Arrange
        var vm = CreateViewModel();

        var result = new PluginCleaningResult
        {
            PluginName = "Plugin1.esp",
            Status = CleaningStatus.Cleaned,
            Success = true,
            Statistics = new CleaningStatistics
            {
                ItemsRemoved = 15,
                ItemsUndeleted = 3,
                PartialFormsCreated = 1
            }
        };

        // Act
        _detailedPluginResultSubject.OnNext(result);

        // Assert
        vm.CurrentItmCount.Should().Be(15);
        vm.CurrentUdrCount.Should().Be(3);
        vm.CurrentNavCount.Should().Be(1);
        vm.HasCurrentPluginStats.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that HasCurrentPluginStats is false when statistics are null.
    /// </summary>
    [Fact]
    public void OnDetailedResult_ShouldSetHasCurrentPluginStatsFalse_WhenNoStatistics()
    {
        // Arrange
        var vm = CreateViewModel();

        var result = new PluginCleaningResult
        {
            PluginName = "Skipped.esp",
            Status = CleaningStatus.Skipped,
            Success = true,
            Statistics = null
        };

        // Act
        _detailedPluginResultSubject.OnNext(result);

        // Assert
        vm.HasCurrentPluginStats.Should().BeFalse();
        vm.CurrentItmCount.Should().Be(0);
        vm.CurrentUdrCount.Should().Be(0);
    }

    /// <summary>
    /// Verifies that session-wide totals accumulate across multiple plugin results.
    /// </summary>
    [Fact]
    public void OnDetailedResult_ShouldAccumulateSessionWideTotals()
    {
        // Arrange
        var vm = CreateViewModel();

        var result1 = new PluginCleaningResult
        {
            PluginName = "Plugin1.esp",
            Status = CleaningStatus.Cleaned,
            Success = true,
            Statistics = new CleaningStatistics { ItemsRemoved = 10, ItemsUndeleted = 2 }
        };
        var result2 = new PluginCleaningResult
        {
            PluginName = "Plugin2.esp",
            Status = CleaningStatus.Cleaned,
            Success = true,
            Statistics = new CleaningStatistics { ItemsRemoved = 5, ItemsUndeleted = 1, PartialFormsCreated = 3 }
        };

        // Act
        _detailedPluginResultSubject.OnNext(result1);
        _detailedPluginResultSubject.OnNext(result2);

        // Assert
        vm.TotalItmCount.Should().Be(15);
        vm.TotalUdrCount.Should().Be(3);
        vm.TotalNavCount.Should().Be(3);
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

    #region Results Summary Tests

    /// <summary>
    /// Verifies that CleaningCompleted transitions to results summary mode.
    /// </summary>
    [Fact]
    public void OnCleaningCompleted_ShouldTransitionToResultsSummary()
    {
        // Arrange
        var vm = CreateViewModel();

        var session = new CleaningSessionResult
        {
            StartTime = DateTime.Now.AddMinutes(-5),
            EndTime = DateTime.Now,
            GameType = GameType.SkyrimSe,
            WasCancelled = false,
            PluginResults = new[]
            {
                new PluginCleaningResult
                {
                    PluginName = "Cleaned.esp",
                    Status = CleaningStatus.Cleaned,
                    Success = true,
                    Statistics = new CleaningStatistics { ItemsRemoved = 10, ItemsUndeleted = 2 }
                }
            }
        };

        // Act
        _cleaningCompletedSubject.OnNext(session);

        // Assert
        vm.IsShowingResults.Should().BeTrue();
        vm.SessionResult.Should().Be(session);
        vm.WasCancelled.Should().BeFalse();
        vm.IsCleaning.Should().BeFalse();
        vm.SessionSummaryText.Should().NotBeNullOrEmpty();
        vm.TotalItmCount.Should().Be(10);
        vm.TotalUdrCount.Should().Be(2);
    }

    /// <summary>
    /// Verifies that cancelled session shows cancelled indication.
    /// </summary>
    [Fact]
    public void OnCleaningCompleted_ShouldShowCancelledIndication_WhenCancelled()
    {
        // Arrange
        var vm = CreateViewModel();

        var session = new CleaningSessionResult
        {
            StartTime = DateTime.Now.AddMinutes(-2),
            EndTime = DateTime.Now,
            GameType = GameType.SkyrimSe,
            WasCancelled = true,
            PluginResults = new[]
            {
                new PluginCleaningResult
                {
                    PluginName = "Cleaned.esp",
                    Status = CleaningStatus.Cleaned,
                    Success = true
                }
            }
        };

        // Act
        _cleaningCompletedSubject.OnNext(session);

        // Assert
        vm.WasCancelled.Should().BeTrue();
        vm.IsShowingResults.Should().BeTrue();
        vm.SessionSummaryText.Should().Contain("Cancelled");
    }

    /// <summary>
    /// Verifies that new cleaning session resets all state.
    /// </summary>
    [Fact]
    public void NewCleaningSession_ShouldResetState()
    {
        // Arrange
        var vm = CreateViewModel();

        // First: add some results from a previous session
        _detailedPluginResultSubject.OnNext(new PluginCleaningResult
        {
            PluginName = "Old.esp",
            Status = CleaningStatus.Cleaned,
            Success = true,
            Statistics = new CleaningStatistics { ItemsRemoved = 5 }
        });
        vm.CompletedPlugins.Should().HaveCount(1);

        // Set up as if we completed
        vm.IsShowingResults = true;
        vm.SessionSummaryText = "Previous session";

        // Act: start new session (IsCleaning transitions from false to true)
        var cleaningState = new AppState { IsCleaning = true, TotalPlugins = 10 };
        _stateSubject.OnNext(cleaningState);

        // Assert
        vm.CompletedPlugins.Should().BeEmpty("should be cleared for new session");
        vm.IsShowingResults.Should().BeFalse("should not show results during active cleaning");
        vm.CurrentItmCount.Should().Be(0);
        vm.CurrentUdrCount.Should().Be(0);
        vm.CurrentNavCount.Should().Be(0);
        vm.TotalItmCount.Should().Be(0);
        vm.TotalUdrCount.Should().Be(0);
        vm.TotalNavCount.Should().Be(0);
        vm.SessionSummaryText.Should().BeEmpty();
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
