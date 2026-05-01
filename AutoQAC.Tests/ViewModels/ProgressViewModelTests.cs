using System.ComponentModel;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using AutoQAC.Tests.TestInfrastructure;
using AutoQAC.ViewModels;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AutoQAC.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="ProgressViewModel"/> covering progress tracking,
/// state synchronization, per-plugin stats, results summary mode, and edge cases.
/// </summary>
public sealed class ProgressViewModelTests
{
    private readonly IStateService _stateServiceMock;
    private readonly ICleaningOrchestrator _orchestratorMock;
    private readonly BehaviorSubject<AppState> _stateSubject;
    private readonly Subject<(string plugin, CleaningStatus status)> _pluginProcessedSubject;
    private readonly Subject<PluginCleaningResult> _detailedPluginResultSubject;
    private readonly Subject<CleaningSessionResult> _cleaningCompletedSubject;
    private readonly BehaviorSubject<bool> _isTerminatingSubject;
    private readonly Subject<bool> _hangDetectedSubject;
    private readonly IMessageDialogService _messageDialogMock;
    private readonly IUiDispatcher _uiDispatcher;

    /// <summary>
    /// Initializes test fixtures with default mock configurations.
    /// </summary>
    public ProgressViewModelTests()
    {
        _stateSubject = new BehaviorSubject<AppState>(new AppState());
        _pluginProcessedSubject = new Subject<(string, CleaningStatus)>();
        _detailedPluginResultSubject = new Subject<PluginCleaningResult>();
        _cleaningCompletedSubject = new Subject<CleaningSessionResult>();
        _isTerminatingSubject = new BehaviorSubject<bool>(false);
        _hangDetectedSubject = new Subject<bool>();
        _uiDispatcher = new SynchronousUiDispatcher();

        _stateServiceMock = Substitute.For<IStateService>();
        _stateServiceMock.StateChanged.Returns(_stateSubject);
        _stateServiceMock.PluginProcessed.Returns(_pluginProcessedSubject);
        _stateServiceMock.DetailedPluginResult.Returns(_detailedPluginResultSubject);
        _stateServiceMock.CleaningCompleted.Returns(_cleaningCompletedSubject);
        _stateServiceMock.IsTerminatingChanged.Returns(_isTerminatingSubject);
        _stateServiceMock.CurrentState.Returns(new AppState());

        _orchestratorMock = Substitute.For<ICleaningOrchestrator>();
        _orchestratorMock.HangDetected.Returns(_hangDetectedSubject);

        _messageDialogMock = Substitute.For<IMessageDialogService>();
    }

    /// <summary>
    /// Creates a new ViewModel instance using the test fixtures.
    /// </summary>
    private ProgressViewModel CreateViewModel()
    {
        return new ProgressViewModel(_stateServiceMock, _orchestratorMock, _messageDialogMock, _uiDispatcher);
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
        _orchestratorMock.StopCleaningAsync().Returns(new StopCleaningResult(null, false));
        var vm = CreateViewModel();
        // Activate IsCleaning so the StopCommand CanExecute is true.
        _stateSubject.OnNext(new AppState { IsCleaning = true });

        // Act
        await vm.StopCommand.ExecuteAsync(null);

        // Assert
        await _orchestratorMock.Received(1).StopCleaningAsync();
    }

    [Fact]
    public async Task StopCommand_WhenGracePeriodExpires_ShouldPromptBeforeForceStop()
    {
        // Arrange
        var choiceCompletion = new TaskCompletionSource<MessageDialogResult>();
        _orchestratorMock.StopCleaningAsync()
            .Returns(new StopCleaningResult(TerminationResult.GracePeriodExpired, true));
        _orchestratorMock.ForceStopCleaningAsync()
            .Returns(new StopCleaningResult(TerminationResult.ForceKilled, false));
        _messageDialogMock.ShowChoiceAsync(
                StopTerminationDialogContent.ConfirmationTitle,
                StopTerminationDialogContent.ConfirmationMessage,
                StopTerminationDialogContent.ForceTerminateButton,
                StopTerminationDialogContent.LeaveRunningButton,
                MessageDialogIcon.Question,
                Arg.Any<string?>())
            .Returns(choiceCompletion.Task);

        var vm = CreateViewModel();
        _stateSubject.OnNext(new AppState { IsCleaning = true });

        // Act
        var stopTask = vm.StopCommand.ExecuteAsync(null);

        // Assert
        await _messageDialogMock.Received(1).ShowChoiceAsync(
            StopTerminationDialogContent.ConfirmationTitle,
            StopTerminationDialogContent.ConfirmationMessage,
            StopTerminationDialogContent.ForceTerminateButton,
            StopTerminationDialogContent.LeaveRunningButton,
            MessageDialogIcon.Question,
            Arg.Any<string?>());
        await _orchestratorMock.DidNotReceive().ForceStopCleaningAsync();

        choiceCompletion.SetResult(MessageDialogResult.Yes);
        await stopTask;
        await _orchestratorMock.Received(1).ForceStopCleaningAsync();
    }

    [Fact]
    public async Task StopCommand_WhenForceTerminationDeclined_ShouldMarkLeftRunningAndPersistWarning()
    {
        // Arrange
        _orchestratorMock.StopCleaningAsync()
            .Returns(new StopCleaningResult(TerminationResult.GracePeriodExpired, true));
        _messageDialogMock.ShowChoiceAsync(
                StopTerminationDialogContent.ConfirmationTitle,
                StopTerminationDialogContent.ConfirmationMessage,
                StopTerminationDialogContent.ForceTerminateButton,
                StopTerminationDialogContent.LeaveRunningButton,
                MessageDialogIcon.Question,
                Arg.Any<string?>())
            .Returns(MessageDialogResult.No);

        var vm = CreateViewModel();
        _stateSubject.OnNext(new AppState { IsCleaning = true });

        // Act
        await vm.StopCommand.ExecuteAsync(null);

        // Assert
        _orchestratorMock.Received(1).MarkLeftRunningByUser();
        await _orchestratorMock.DidNotReceive().ForceStopCleaningAsync();
        vm.StopOutcomeWarningText.Should().Be(StopTerminationDialogContent.LeftRunningMessage);
        vm.HasStopOutcomeWarning.Should().BeTrue();
    }

    [Fact]
    public async Task StopCommand_WhenConfirmedForceTerminationFails_ShouldShowSharedFailureAndPersistWarning()
    {
        // Arrange
        _orchestratorMock.StopCleaningAsync()
            .Returns(new StopCleaningResult(TerminationResult.GracePeriodExpired, true));
        _orchestratorMock.ForceStopCleaningAsync()
            .Returns(new StopCleaningResult(TerminationResult.ForceKillFailed, true));
        _messageDialogMock.ShowChoiceAsync(
                StopTerminationDialogContent.ConfirmationTitle,
                StopTerminationDialogContent.ConfirmationMessage,
                StopTerminationDialogContent.ForceTerminateButton,
                StopTerminationDialogContent.LeaveRunningButton,
                MessageDialogIcon.Question,
                Arg.Any<string?>())
            .Returns(MessageDialogResult.Yes);

        var vm = CreateViewModel();
        _stateSubject.OnNext(new AppState { IsCleaning = true });

        // Act
        await vm.StopCommand.ExecuteAsync(null);

        // Assert
        await _orchestratorMock.Received(1).ForceStopCleaningAsync();
        await _messageDialogMock.Received(1).ShowErrorAsync(
            StopTerminationDialogContent.ForceFailureTitle,
            StopTerminationDialogContent.ForceFailureMessage,
            Arg.Any<string?>());
        vm.StopOutcomeWarningText.Should().Be(StopTerminationDialogContent.ForceFailureMessage);
        vm.HasStopOutcomeWarning.Should().BeTrue();
    }

    [Fact]
    public async Task StopCommand_WhenStopThrows_ShouldPersistFailureWarningAndShowSharedFailure()
    {
        // Arrange
        _orchestratorMock.StopCleaningAsync()
            .ThrowsAsync(new InvalidOperationException("stop failed"));
        _messageDialogMock.ShowErrorAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>())
            .Returns(Task.CompletedTask);

        var vm = CreateViewModel();
        _stateSubject.OnNext(new AppState { IsCleaning = true });

        // Act
        await vm.StopCommand.ExecuteAsync(null);

        // Assert
        vm.StopOutcomeWarningText.Should().Be(StopTerminationDialogContent.ForceFailureMessage);
        vm.HasStopOutcomeWarning.Should().BeTrue();
        await _messageDialogMock.Received(1).ShowErrorAsync(
            StopTerminationDialogContent.ForceFailureTitle,
            StopTerminationDialogContent.ForceFailureMessage,
            Arg.Any<string?>());
    }

    [Fact]
    public async Task StopCommand_WhenFailureDialogThrows_ShouldPersistWarningAndComplete()
    {
        // Arrange
        _orchestratorMock.StopCleaningAsync()
            .Returns(new StopCleaningResult(TerminationResult.GracePeriodExpired, true));
        _orchestratorMock.ForceStopCleaningAsync()
            .Returns(new StopCleaningResult(TerminationResult.ForceKillFailed, true));
        _messageDialogMock.ShowChoiceAsync(
                StopTerminationDialogContent.ConfirmationTitle,
                StopTerminationDialogContent.ConfirmationMessage,
                StopTerminationDialogContent.ForceTerminateButton,
                StopTerminationDialogContent.LeaveRunningButton,
                MessageDialogIcon.Question,
                Arg.Any<string?>())
            .Returns(MessageDialogResult.Yes);
        _messageDialogMock.ShowErrorAsync(
                StopTerminationDialogContent.ForceFailureTitle,
                StopTerminationDialogContent.ForceFailureMessage,
                Arg.Any<string?>())
            .ThrowsAsync(new ApplicationException("dialog failed"));

        var vm = CreateViewModel();
        _stateSubject.OnNext(new AppState { IsCleaning = true });

        // Act
        await vm.StopCommand.ExecuteAsync(null);

        // Assert
        vm.StopOutcomeWarningText.Should().Be(StopTerminationDialogContent.ForceFailureMessage);
        vm.HasStopOutcomeWarning.Should().BeTrue();
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
    public void ViewModel_ShouldHandleRapidStateUpdates()
    {
        // Arrange
        var vm = CreateViewModel();
        var updates = new List<int>();

        // Track progress changes via INotifyPropertyChanged.
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProgressViewModel.Progress))
            {
                updates.Add(vm.Progress);
            }
        };

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

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProgressViewModel.ProgressText))
            {
                progressTextChanges.Add(vm.ProgressText);
            }
        };

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

    #region Termination Spinner Tests

    /// <summary>
    /// Verifies that IsTerminating tracks the IsTerminatingChanged observable.
    /// </summary>
    [Fact]
    public void IsTerminating_ShouldTrackStateServiceObservable()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.IsTerminating.Should().BeFalse("initially not terminating");

        // Act
        _isTerminatingSubject.OnNext(true);

        // Assert
        vm.IsTerminating.Should().BeTrue("should reflect terminating state");

        // Act - reset
        _isTerminatingSubject.OnNext(false);

        // Assert
        vm.IsTerminating.Should().BeFalse("should reflect non-terminating state");
    }

    /// <summary>
    /// Verifies that StopCommand is disabled while IsTerminating is true.
    /// </summary>
    [Fact]
    public void StopCommand_ShouldBeDisabled_WhenTerminating()
    {
        // Arrange
        var vm = CreateViewModel();

        // Start cleaning so Stop button would normally be enabled
        _stateSubject.OnNext(new AppState { IsCleaning = true });
        vm.IsCleaning.Should().BeTrue();
        vm.StopCommand.CanExecute(null).Should().BeTrue("Stop should be enabled while cleaning is active");

        // Act - begin termination
        _isTerminatingSubject.OnNext(true);

        // Assert
        vm.StopCommand.CanExecute(null).Should().BeFalse("Stop should be disabled while terminating");
    }

    [Fact]
    public void BackupOperation_ShowsCancelBackup()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        _stateSubject.OnNext(new AppState
        {
            IsCleaning = true,
            BackupOperation = new BackupOperationState
            {
                Kind = BackupOperationKind.Backup,
                Label = "Backing up: Update.esm",
                FileName = "Update.esm",
                FilesCompleted = 0,
                TotalFiles = 1,
                BytesCopied = 40 * 1024 * 1024,
                TotalBytes = 100 * 1024 * 1024,
                IsActive = true,
                CanCancel = true
            }
        });

        // Assert
        vm.ActiveOperationLabel.Should().Be("Backing up: Update.esm");
        vm.IsBackupOperationActive.Should().BeTrue();
        vm.IsBackupCancelVisible.Should().BeTrue();
        vm.IsCleanupCancelVisible.Should().BeFalse();
        vm.BackupOperationProgressText.Should().Be("0 / 1 files — 41.9 MB / 104.9 MB");
    }

    [Fact]
    public void CleanupOperation_ShowsCancelCleanup()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        _stateSubject.OnNext(new AppState
        {
            IsCleaning = true,
            BackupOperation = new BackupOperationState
            {
                Kind = BackupOperationKind.RetentionCleanup,
                Label = "Cleaning up old backups",
                FilesCompleted = 2,
                TotalFiles = 5,
                IsActive = true,
                CanCancel = true
            }
        });

        // Assert
        vm.ActiveOperationLabel.Should().Be("Cleaning up old backups");
        vm.IsBackupOperationActive.Should().BeTrue();
        vm.IsBackupCancelVisible.Should().BeFalse();
        vm.IsCleanupCancelVisible.Should().BeTrue();
        vm.BackupOperationProgressText.Should().Be("2 / 5 files");
    }

    [Fact]
    public async Task CancelBackupOperationCommand_CallsOrchestratorOnce()
    {
        // Arrange
        var vm = CreateViewModel();
        _stateSubject.OnNext(new AppState
        {
            IsCleaning = true,
            BackupOperation = new BackupOperationState
            {
                Kind = BackupOperationKind.Backup,
                Label = "Backing up: Update.esm",
                IsActive = true,
                CanCancel = true
            }
        });

        // Act
        await vm.CancelBackupOperationCommand.ExecuteAsync(null);

        // Assert
        await _orchestratorMock.Received(1).CancelBackupOperationAsync();
    }

    [Fact]
    public void Dispose_RemovesBackupOperationSubscription()
    {
        // Arrange
        var vm = CreateViewModel();
        _stateSubject.OnNext(new AppState
        {
            IsCleaning = true,
            BackupOperation = new BackupOperationState
            {
                Kind = BackupOperationKind.Backup,
                Label = "Backing up: BeforeDispose.esm",
                IsActive = true,
                CanCancel = true
            }
        });
        vm.ActiveOperationLabel.Should().Be("Backing up: BeforeDispose.esm");

        // Act
        vm.Dispose();
        _stateSubject.OnNext(new AppState
        {
            IsCleaning = true,
            BackupOperation = new BackupOperationState
            {
                Kind = BackupOperationKind.RetentionCleanup,
                Label = "Cleaning up old backups",
                IsActive = true,
                CanCancel = true
            }
        });

        // Assert
        vm.ActiveOperationLabel.Should().Be("Backing up: BeforeDispose.esm");
        vm.IsCleanupCancelVisible.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that IsTerminating is reset when a new cleaning session starts.
    /// </summary>
    [Fact]
    public void NewCleaningSession_ShouldResetIsTerminating()
    {
        // Arrange
        var vm = CreateViewModel();

        // Simulate a terminated state
        _isTerminatingSubject.OnNext(true);
        vm.IsTerminating.Should().BeTrue();

        // Manually set it (as if from previous session leftover)
        vm.IsTerminating = true;

        // Act: start new session (IsCleaning transitions from false to true)
        _stateSubject.OnNext(new AppState { IsCleaning = true, TotalPlugins = 5 });

        // Assert
        vm.IsTerminating.Should().BeFalse("should be reset for new session");
    }

    [Fact]
    public void OnHangDetected_ShouldNotReopenAfterDismiss_UntilResetEventThenAllowReopen()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.IsHangWarningVisible.Should().BeFalse();

        // Act - first hang shows warning
        _hangDetectedSubject.OnNext(true);

        // Assert
        vm.IsHangWarningVisible.Should().BeTrue();

        // Act - user dismisses warning
        vm.DismissHangWarningCommand.Execute(null);

        // Assert
        vm.IsHangWarningVisible.Should().BeFalse("dismiss command should hide warning");

        // Act - repeated hang signal should remain suppressed in same cycle
        _hangDetectedSubject.OnNext(true);

        // Assert
        vm.IsHangWarningVisible.Should().BeFalse("dismissed warning should stay hidden until reset");

        // Act - reset cycle then emit hang again
        _hangDetectedSubject.OnNext(false);
        _hangDetectedSubject.OnNext(true);

        // Assert
        vm.IsHangWarningVisible.Should().BeTrue("warning should reappear after non-hung reset event");
    }

    [Fact]
    public async Task KillHungProcessCommand_ShouldForceStopDirectlyWithoutConfirmation()
    {
        // Arrange
        _orchestratorMock.ForceStopCleaningAsync()
            .Returns(new StopCleaningResult(TerminationResult.ForceKilled, false));
        var vm = CreateViewModel();
        vm.IsHangWarningVisible = true;

        // Act
        await vm.KillHungProcessCommand.ExecuteAsync(null);

        // Assert
        await _orchestratorMock.Received(1).ForceStopCleaningAsync();
        await _messageDialogMock.DidNotReceive().ShowChoiceAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<MessageDialogIcon>(),
            Arg.Any<string?>());
        vm.IsHangWarningVisible.Should().BeFalse();
    }

    [Fact]
    public async Task KillHungProcessCommand_WhenForceKillFails_ShouldShowSharedFailureAndPersistWarning()
    {
        // Arrange
        _orchestratorMock.ForceStopCleaningAsync()
            .Returns(new StopCleaningResult(TerminationResult.ForceKillFailed, true));
        var vm = CreateViewModel();
        vm.IsHangWarningVisible = true;

        // Act
        await vm.KillHungProcessCommand.ExecuteAsync(null);

        // Assert
        await _messageDialogMock.Received(1).ShowErrorAsync(
            StopTerminationDialogContent.ForceFailureTitle,
            StopTerminationDialogContent.ForceFailureMessage,
            Arg.Any<string?>());
        vm.StopOutcomeWarningText.Should().Be(StopTerminationDialogContent.ForceFailureMessage);
        vm.HasStopOutcomeWarning.Should().BeTrue();
    }

    [Fact]
    public async Task KillHungProcessCommand_WhenForceStopThrows_ShouldPersistFailureWarningAndShowSharedFailure()
    {
        // Arrange
        _orchestratorMock.ForceStopCleaningAsync()
            .ThrowsAsync(new InvalidOperationException("force stop failed"));
        _messageDialogMock.ShowErrorAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>())
            .Returns(Task.CompletedTask);
        var vm = CreateViewModel();
        vm.IsHangWarningVisible = true;

        // Act
        await vm.KillHungProcessCommand.ExecuteAsync(null);

        // Assert
        vm.IsHangWarningVisible.Should().BeFalse();
        vm.StopOutcomeWarningText.Should().Be(StopTerminationDialogContent.ForceFailureMessage);
        vm.HasStopOutcomeWarning.Should().BeTrue();
        await _messageDialogMock.Received(1).ShowErrorAsync(
            StopTerminationDialogContent.ForceFailureTitle,
            StopTerminationDialogContent.ForceFailureMessage,
            Arg.Any<string?>());
    }

    [Fact]
    public async Task KillHungProcessCommand_WhenForceKillSucceeds_ShouldNotSetSuccessWarningCopy()
    {
        // Arrange
        _orchestratorMock.ForceStopCleaningAsync()
            .Returns(new StopCleaningResult(TerminationResult.ForceKilled, false));
        var vm = CreateViewModel();
        vm.IsHangWarningVisible = true;

        // Act
        await vm.KillHungProcessCommand.ExecuteAsync(null);

        // Assert
        await _messageDialogMock.DidNotReceive().ShowErrorAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>());
        vm.StopOutcomeWarningText.Should().BeNull();
        vm.HasStopOutcomeWarning.Should().BeFalse();
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
        _stateServiceMock.CurrentState.Returns(initialState);

        // Create a new subject with initial state
        var stateSubject = new BehaviorSubject<AppState>(initialState);
        _stateServiceMock.StateChanged.Returns(stateSubject);

        // Act
        var vm = new ProgressViewModel(_stateServiceMock, _orchestratorMock, _messageDialogMock, _uiDispatcher);

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
