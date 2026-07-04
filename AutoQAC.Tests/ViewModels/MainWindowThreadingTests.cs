using System.Reactive.Linq;
using System.Reactive.Subjects;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameCapability;
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
        using var refreshModule = new RecordingPluginRefreshModule();
        var gameCapabilityProvider = CreateGameCapabilityProvider();

        var viewModel = new MainWindowViewModel(
            configService,
            stateService,
            Substitute.For<ICleaningSession>(),
            Substitute.For<ILoggingService>(),
            Substitute.For<IFileDialogService>(),
            Substitute.For<IMessageDialogService>(),
            Substitute.For<IPluginValidationService>(),
            pluginLoadingService,
            captureDispatcher,
            refreshModule,
            gameCapabilityProvider,
            new CleaningCommandReadiness(refreshModule, stateService));

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
        var readiness = Substitute.For<ICleaningCommandReadiness>();
        readiness.EvaluateAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CleaningCommandReadinessResult.Ready));
        var viewModel = new CleaningCommandsViewModel(
            Substitute.For<IStateService>(),
            Substitute.For<ICleaningSession>(),
            Substitute.For<IConfigurationService>(),
            readiness,
            new RecordingPluginRefreshModule(),
            Substitute.For<ILoggingService>(),
            Substitute.For<IMessageDialogService>(),
            Substitute.For<IAppLifetime>(),
            new Interaction<ICleaningSession, Unit>(),
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
        }
        finally
        {
            viewModel.Dispose();
        }
    }

    [Fact]
    public void PluginListViewModel_ShouldUpdateOnSnapshots()
    {
        var viewModel = new PluginListViewModel(new RecordingPluginRefreshModule());

        try
        {
            viewModel.OnPluginRefreshSnapshot(RecordingPluginRefreshModule.CreateSnapshot(
                gameType: GameType.SkyrimSe,
                rows: [CreateRow("Test.esp")],
                commands: new PluginRefreshCommandAvailability(true, true, true, false)));

            viewModel.SelectAllCommand.CanExecute(null).Should().BeTrue();

            viewModel.OnPluginRefreshSnapshot(RecordingPluginRefreshModule.CreateSnapshot(
                gameType: GameType.SkyrimSe,
                rows: [CreateRow("Test.esp")],
                commands: new PluginRefreshCommandAvailability(false, false, false, false)));

            viewModel.SelectAllCommand.CanExecute(null).Should().BeFalse(
                "command availability comes from the Plugin refresh snapshot");
        }
        finally
        {
            viewModel.Dispose();
        }
    }

    [Fact]
    public async Task MainWindowViewModel_ShouldPostRefreshSnapshotsThroughIUiDispatcher()
    {
        using var captureDispatcher = new ThreadCapturingUiDispatcher();
        var currentState = new AppState();
        var stateSubject = new BehaviorSubject<AppState>(currentState);
        var configService = Substitute.For<IConfigurationService>();
        var stateService = Substitute.For<IStateService>();
        var pluginLoadingService = Substitute.For<IPluginLoadingService>();
        using var refreshModule = new RecordingPluginRefreshModule();
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
        var gameCapabilityProvider = CreateGameCapabilityProvider();

        var viewModel = new MainWindowViewModel(
            configService,
            stateService,
            Substitute.For<ICleaningSession>(),
            Substitute.For<ILoggingService>(),
            Substitute.For<IFileDialogService>(),
            Substitute.For<IMessageDialogService>(),
            Substitute.For<IPluginValidationService>(),
            pluginLoadingService,
            captureDispatcher,
            pluginRefreshModule: refreshModule,
            gameCapabilityProvider: gameCapabilityProvider,
            cleaningCommandReadiness: new CleaningCommandReadiness(refreshModule, stateService));

        try
        {
            captureDispatcher.Reset();

            await Task.Run(() => refreshModule.Publish(RecordingPluginRefreshModule.CreateSnapshot(
                gameType: GameType.SkyrimSe,
                rows: [CreateRow("A.esp")],
                activity: new PluginRefreshActivity(false, true),
                commands: new PluginRefreshCommandAvailability(false, false, false, true),
                statusText: "Analyzing 1 of 2 selected plugins.")));
            await captureDispatcher.WaitForNextPostAsync();

            captureDispatcher.PostCount.Should().BeGreaterThanOrEqualTo(1,
                "Plugin refresh snapshots must marshal UI-bound mutations through IUiDispatcher");
            viewModel.Configuration.StatusText.Should().Be("Analyzing 1 of 2 selected plugins.");
            viewModel.PluginList.IsApproximationRefreshRunning.Should().BeTrue();
        }
        finally
        {
            viewModel.Dispose();
        }
    }

    [Fact]
    public void PluginListViewModel_OnStateChanged_ShouldKeepUnchangedRows_WhenOneApproximationUpdates()
    {
        var viewModel = new PluginListViewModel(new RecordingPluginRefreshModule());

        try
        {
            var initialRows = new[]
            {
                CreateRow("One.esp", @"C:\Data\One.esp", PluginIssueApproximation.Pending),
                CreateRow("Two.esp", @"C:\Data\Two.esp", PluginIssueApproximation.Pending)
            };

            viewModel.OnPluginRefreshSnapshot(RecordingPluginRefreshModule.CreateSnapshot(
                gameType: GameType.SkyrimSe,
                rows: initialRows));
            var unchangedRow = viewModel.PluginsToClean[1];

            var updatedRows = new[]
            {
                initialRows[0] with { Approximation = PluginIssueApproximation.Available(2, 1, 0) },
                initialRows[1]
            };

            viewModel.OnPluginRefreshSnapshot(RecordingPluginRefreshModule.CreateSnapshot(
                gameType: GameType.SkyrimSe,
                rows: updatedRows));

            viewModel.PluginsToClean.Should().HaveCount(2);
            viewModel.PluginsToClean[1].Should().BeSameAs(unchangedRow);
            viewModel.PluginsToClean[0].Approximation.Status.Should().Be(PluginIssueApproximationStatus.Available);
        }
        finally
        {
            viewModel.Dispose();
        }
    }

    private static PluginRefreshRow CreateRow(
        string fileName,
        string? fullPath = null,
        PluginIssueApproximation? approximation = null) =>
        new(
            fileName,
            fullPath ?? fileName,
            GameType.SkyrimSe,
            IsSelected: true,
            IsInSkipList: false,
            approximation ?? PluginIssueApproximation.Unavailable);

    private static IGameCapabilityProvider CreateGameCapabilityProvider() => new GameCapabilityProvider();

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
