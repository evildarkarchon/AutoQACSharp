using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using AutoQAC.Tests.TestInfrastructure;
using AutoQAC.ViewModels;
using AutoQAC.ViewModels.MainWindow;
using FluentAssertions;
using NSubstitute;
using ReactiveUI;

namespace AutoQAC.Tests.ViewModels;

[Collection(RxAppSchedulerCollection.Name)]
public sealed class MainWindowThreadingTests
{
    [Fact]
    public async Task MainWindowViewModel_ShouldMarshalCleaningCommandStateChangesToMainThreadScheduler()
    {
        using var mainThreadScheduler = new RxAppEventLoopMainThreadSchedulerScope();

        var mainThreadId = await WaitForSignalAsync(mainThreadScheduler.ThreadIdTask);
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
            pluginLoadingService);

        try
        {
            var observedThread = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var subscription = viewModel.Commands.WhenAnyValue(x => x.IsCleaning)
                .Skip(1)
                .Subscribe(_ => observedThread.TrySetResult(Environment.CurrentManagedThreadId));

            await Task.Run(() =>
            {
                currentState = currentState with { IsCleaning = true };
                stateSubject.OnNext(currentState);
            });

            (await WaitForSignalAsync(observedThread.Task)).Should().Be(mainThreadId);
        }
        finally
        {
            viewModel.Dispose();
        }
    }

    [Fact]
    public async Task PluginListViewModel_CommandCanExecute_ShouldMarshalStateChangesToMainThreadScheduler()
    {
        using var mainThreadScheduler = new RxAppEventLoopMainThreadSchedulerScope();

        var mainThreadId = await WaitForSignalAsync(mainThreadScheduler.ThreadIdTask);
        var currentState = new AppState
        {
            PluginsToClean =
            [
                new PluginInfo { FileName = "Test.esp", FullPath = "Test.esp", IsSelected = true }
            ]
        };
        var stateSubject = new BehaviorSubject<AppState>(currentState);
        var stateService = Substitute.For<IStateService>();
        stateService.StateChanged.Returns(stateSubject);
        stateService.CurrentState.Returns(_ => currentState);

        var viewModel = new PluginListViewModel(stateService);

        try
        {
            var observedThread = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var subscription = viewModel.SelectAllCommand.CanExecute
                .Skip(1)
                .Subscribe(_ => observedThread.TrySetResult(Environment.CurrentManagedThreadId));

            await Task.Run(() =>
            {
                currentState = currentState with { IsCleaning = true };
                stateSubject.OnNext(currentState);
            });

            (await WaitForSignalAsync(observedThread.Task)).Should().Be(mainThreadId);
        }
        finally
        {
            viewModel.Dispose();
        }
    }

    private static async Task<T> WaitForSignalAsync<T>(Task<T> signalTask)
    {
        var completedTask = await Task.WhenAny(signalTask, Task.Delay(TimeSpan.FromSeconds(2)));
        completedTask.Should().Be(signalTask, "expected asynchronous test signal to be observed");
        return await signalTask;
    }
}
