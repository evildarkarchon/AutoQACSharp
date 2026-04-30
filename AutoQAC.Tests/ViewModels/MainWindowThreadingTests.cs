using System.Reactive.Linq;
using System.Reactive.Subjects;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using AutoQAC.Services.UI.Interactions;
using AutoQAC.Tests.TestInfrastructure;
using AutoQAC.ViewModels;
using AutoQAC.ViewModels.MainWindow;
using FluentAssertions;
using NSubstitute;

namespace AutoQAC.Tests.ViewModels;

public sealed class MainWindowThreadingTests
{
    [Fact]
    public async Task MainWindowViewModel_ShouldPostStateChangesThroughIUiDispatcher()
    {
        // After the migration to CommunityToolkit.Mvvm, UI marshaling is delegated to
        // IUiDispatcher rather than RxApp.MainThreadScheduler. This test verifies that
        // the dispatcher is invoked for state-driven updates. It does not claim real UI
        // thread affinity because this test double executes callbacks inline.
        using var captureDispatcher = new ThreadCapturingUiDispatcher();
        var dispatchedFromThread = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var currentState = new AppState();
        var stateSubject = new BehaviorSubject<AppState>(currentState);
        var configService = Substitute.For<IConfigurationService>();
        var stateService = Substitute.For<IStateService>();
        var pluginLoadingService = Substitute.For<IPluginLoadingService>();
        stateService.StateChanged.Returns(stateSubject);
        stateService.CurrentState.Returns(_ => currentState);
        stateService.CleaningCompleted.Returns(Observable.Never<CleaningSessionResult>());
        configService.SkipListChanged.Returns(Observable.Never<GameType>());
        configService.LoadUserConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new global::AutoQAC.Models.Configuration.UserConfiguration
            {
                LoadOrder = new(),
                XEdit = new(),
                ModOrganizer = new(),
                Settings = new()
            });
        configService.GetSelectedGameAsync(Arg.Any<CancellationToken>())
            .Returns(GameType.Unknown);
        pluginLoadingService.GetAvailableGames()
            .Returns(new List<GameType> { GameType.Fallout4 });

        var viewModel = new MainWindowViewModel(
            configService,
            stateService,
            Substitute.For<ICleaningOrchestrator>(),
            Substitute.For<ILoggingService>(),
            Substitute.For<IFileDialogService>(),
            Substitute.For<IMessageDialogService>(),
            Substitute.For<IPluginValidationService>(),
            pluginLoadingService,
            captureDispatcher);

        try
        {
            // Reset capture state after construction (constructor performs the initial
            // OnStateChanged dispatch synchronously on the calling thread).
            captureDispatcher.Reset();

            currentState = currentState with { IsCleaning = true };
            stateSubject.OnNext(currentState);

            // Wait for the dispatcher to be invoked at least once after the state change.
            await captureDispatcher.WaitForNextPostAsync();

            captureDispatcher.LastPostThreadId.Should().NotBe(0,
                "the IUiDispatcher should receive the state-change callback");

            // The actual VM property update must reflect the state change after dispatch.
            viewModel.Commands.IsCleaning.Should().BeTrue(
                "the synchronous dispatcher applies the callback inline so IsCleaning should be set");
        }
        finally
        {
            viewModel.Dispose();
        }
    }

    /// <summary>
    /// Verifies that the child command VM applies state immediately once the parent
    /// has already marshaled the state notification onto the UI dispatcher.
    /// </summary>
    [Fact]
    public void CleaningCommandsViewModel_OnStateChanged_ShouldApplyStateSynchronously()
    {
        var dispatcher = Substitute.For<IUiDispatcher>();
        var pluginLoadingService = Substitute.For<IPluginLoadingService>();
        var viewModel = new CleaningCommandsViewModel(
            Substitute.For<IStateService>(),
            Substitute.For<ICleaningOrchestrator>(),
            Substitute.For<IConfigurationService>(),
            pluginLoadingService,
            Substitute.For<IPluginRefreshCoordinator>(),
            Substitute.For<ILoggingService>(),
            Substitute.For<IMessageDialogService>(),
            dispatcher,
            new Interaction<Unit, Unit>(),
            new Interaction<List<DryRunResult>, Unit>(),
            new Interaction<Unit, bool>(),
            new Interaction<Unit, bool>(),
            new Interaction<Unit, Unit>(),
            new Interaction<Unit, Unit>());

        try
        {
            var state = new AppState
            {
                XEditExecutablePath = @"C:\Tools\xEdit\SSEEdit.exe",
                PluginsToClean =
                [
                    new PluginInfo { FileName = "Test.esp", FullPath = @"C:\Games\Data\Test.esp" }
                ]
            };

            viewModel.OnStateChanged(state);

            viewModel.CanStartCleaning.Should().BeTrue(
                "the parent VM already dispatched state changes before invoking the child VM");
            dispatcher.DidNotReceive().Post(Arg.Any<Action>());
        }
        finally
        {
            viewModel.Dispose();
        }
    }

    [Fact]
    public async Task PluginListViewModel_ShouldUpdateOnStateChanges_ThroughUiDispatcherFlow()
    {
        // Originally this test verified that ReactiveCommand.CanExecute marshaled
        // updates to RxApp.MainThreadScheduler. With CommunityToolkit.Mvvm there is no
        // separate scheduler -- IRelayCommand.CanExecute returns synchronously and the
        // parent VM dispatches OnStateChanged through IUiDispatcher. This test verifies
        // the equivalent contract: when the parent dispatches OnStateChanged, the
        // PluginListViewModel responds and command CanExecute reflects the new state.
        var currentState = new AppState
        {
            PluginsToClean =
            [
                new PluginInfo { FileName = "Test.esp", FullPath = "Test.esp" }
            ]
        };
        var stateSubject = new BehaviorSubject<AppState>(currentState);
        var stateService = Substitute.For<IStateService>();
        stateService.StateChanged.Returns(stateSubject);
        stateService.CurrentState.Returns(_ => currentState);

        var viewModel = new PluginListViewModel(stateService);

        try
        {
            // Initially HasPlugins=true and IsCleaning=false → command CanExecute true.
            viewModel.SelectAllCommand.CanExecute(null).Should().BeTrue();

            // Simulate the parent VM dispatching state changes (this is what IUiDispatcher
            // would call inline in the SynchronousUiDispatcher test setup).
            await Task.Run(() =>
            {
                currentState = currentState with { IsCleaning = true };
            });
            viewModel.OnStateChanged(currentState);

            viewModel.IsCleaning.Should().BeTrue();
            viewModel.SelectAllCommand.CanExecute(null).Should().BeFalse(
                "command should be disabled while cleaning");
        }
        finally
        {
            viewModel.Dispose();
        }
    }

    [Fact]
    public async Task MainWindowViewModel_ShouldPostRefreshStatusChangesThroughIUiDispatcher()
    {
        using var captureDispatcher = new ThreadCapturingUiDispatcher();
        var currentState = new AppState();
        var stateSubject = new BehaviorSubject<AppState>(currentState);
        var refreshStatusSubject = new Subject<PluginRefreshStatus>();
        var configService = Substitute.For<IConfigurationService>();
        var stateService = Substitute.For<IStateService>();
        var pluginLoadingService = Substitute.For<IPluginLoadingService>();
        var refreshCoordinator = Substitute.For<IPluginRefreshCoordinator>();
        stateService.StateChanged.Returns(stateSubject);
        stateService.CurrentState.Returns(_ => currentState);
        stateService.CleaningCompleted.Returns(Observable.Never<CleaningSessionResult>());
        configService.SkipListChanged.Returns(Observable.Never<GameType>());
        configService.LoadUserConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new global::AutoQAC.Models.Configuration.UserConfiguration
            {
                LoadOrder = new(),
                XEdit = new(),
                ModOrganizer = new(),
                Settings = new()
            });
        configService.GetSelectedGameAsync(Arg.Any<CancellationToken>())
            .Returns(GameType.Unknown);
        pluginLoadingService.GetAvailableGames()
            .Returns(new List<GameType> { GameType.Fallout4 });
        refreshCoordinator.StatusChanged.Returns(refreshStatusSubject);

        var viewModel = new MainWindowViewModel(
            configService,
            stateService,
            Substitute.For<ICleaningOrchestrator>(),
            Substitute.For<ILoggingService>(),
            Substitute.For<IFileDialogService>(),
            Substitute.For<IMessageDialogService>(),
            Substitute.For<IPluginValidationService>(),
            pluginLoadingService,
            captureDispatcher,
            pluginRefreshCoordinator: refreshCoordinator);

        try
        {
            captureDispatcher.Reset();

            await Task.Run(() => refreshStatusSubject.OnNext(PluginRefreshStatus.AnalyzingSelected(1, 2)));
            await captureDispatcher.WaitForPostCountAsync(2);

            captureDispatcher.PostCount.Should().BeGreaterThanOrEqualTo(2,
                "both refresh-status subscribers must marshal UI-bound mutations through IUiDispatcher");
            viewModel.Configuration.StatusText.Should().Be("Analyzing 1 of 2 selected plugins.");
            viewModel.PluginList.IsApproximationRefreshRunning.Should().BeTrue();
        }
        finally
        {
            viewModel.Dispose();
            refreshStatusSubject.Dispose();
        }
    }

    [Fact]
    public void PluginListViewModel_OnStateChanged_ShouldKeepUnchangedRows_WhenOneApproximationUpdates()
    {
        var stateService = Substitute.For<IStateService>();
        stateService.StateChanged.Returns(Observable.Never<AppState>());
        stateService.CurrentState.Returns(new AppState());
        var viewModel = new PluginListViewModel(stateService);

        try
        {
            var initialState = new AppState
            {
                PluginsToClean =
                [
                    new PluginInfo
                    {
                        FileName = "One.esp",
                        FullPath = @"C:\Data\One.esp",
                        Approximation = PluginIssueApproximation.Pending
                    },
                    new PluginInfo
                    {
                        FileName = "Two.esp",
                        FullPath = @"C:\Data\Two.esp",
                        Approximation = PluginIssueApproximation.Pending
                    }
                ]
            };

            viewModel.OnStateChanged(initialState);
            var unchangedRow = viewModel.PluginsToClean[1];

            var updatedState = initialState with
            {
                PluginsToClean =
                [
                    initialState.PluginsToClean[0] with
                    {
                        Approximation = PluginIssueApproximation.Available(2, 1, 0)
                    },
                    initialState.PluginsToClean[1]
                ]
            };

            viewModel.OnStateChanged(updatedState);

            viewModel.PluginsToClean.Should().HaveCount(2);
            viewModel.PluginsToClean[1].Should().BeSameAs(unchangedRow);
            viewModel.PluginsToClean[0].Approximation.Status.Should().Be(PluginIssueApproximationStatus.Available);
        }
        finally
        {
            viewModel.Dispose();
        }
    }

    /// <summary>
    /// Test double <see cref="IUiDispatcher"/> that runs callbacks synchronously while
    /// recording the thread id of the most recent post. Replaces the previous
    /// <c>RxAppEventLoopMainThreadSchedulerScope</c> for verifying that VM state changes
    /// are routed through the dispatcher abstraction.
    /// </summary>
    private sealed class ThreadCapturingUiDispatcher : IUiDispatcher, IDisposable
    {
        private TaskCompletionSource<int> _nextPost = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<int> _postTargetReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _targetPostCount = 1;

        public int LastPostThreadId { get; private set; }

        public int PostCount { get; private set; }

        public void Reset()
        {
            LastPostThreadId = 0;
            PostCount = 0;
            _targetPostCount = 1;
            _nextPost = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            _postTargetReached = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Task WaitForNextPostAsync() => _nextPost.Task.WaitAsync(TimeSpan.FromSeconds(2));

        public Task WaitForPostCountAsync(int postCount)
        {
            if (PostCount >= postCount)
            {
                return Task.CompletedTask;
            }

            _targetPostCount = postCount;
            if (_postTargetReached.Task.IsCompleted)
            {
                _postTargetReached = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            return _postTargetReached.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }

        public void Post(Action action)
        {
            try
            {
                action();
            }
            finally
            {
                LastPostThreadId = Environment.CurrentManagedThreadId;
                PostCount++;
                _nextPost.TrySetResult(LastPostThreadId);
                if (PostCount >= _targetPostCount)
                {
                    _postTargetReached.TrySetResult(PostCount);
                }
            }
        }

        public Task InvokeAsync(Func<Task> action) => action();

        public void Dispose()
        {
            _nextPost.TrySetResult(0);
        }
    }
}
