using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.State;
using AutoQAC.ViewModels;
using FluentAssertions;
using Moq;
using ReactiveUI;
using System.Reactive.Concurrency;
using Xunit;

namespace AutoQAC.Tests.ViewModels;

public sealed class ProgressViewModelTests
{
    [Fact]
    public void ShouldUpdateProperties_WhenStateChanges()
    {
        // Arrange
        RxApp.MainThreadScheduler = Scheduler.Immediate; // Force immediate execution

        var stateSubject = new BehaviorSubject<AppState>(new AppState());
        var pluginSubject = new Subject<(string, CleaningStatus)>();

        var stateServiceMock = new Mock<IStateService>();
        stateServiceMock.Setup(s => s.StateChanged).Returns(stateSubject);
        stateServiceMock.Setup(s => s.PluginProcessed).Returns(pluginSubject);
        stateServiceMock.Setup(s => s.CurrentState).Returns(new AppState());

        var orchestratorMock = new Mock<ICleaningOrchestrator>();

        var vm = new ProgressViewModel(stateServiceMock.Object, orchestratorMock.Object);

        // Act
        var newState = new AppState
        {
            CurrentPlugin = "Test.esp",
            Progress = 5,
            TotalPlugins = 10
        };
        stateSubject.OnNext(newState);

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
        RxApp.MainThreadScheduler = Scheduler.Immediate;
        
        var stateServiceMock = new Mock<IStateService>();
        stateServiceMock.Setup(s => s.StateChanged).Returns(Observable.Return(new AppState()));
        stateServiceMock.Setup(s => s.PluginProcessed).Returns(Observable.Never<(string, CleaningStatus)>());
        stateServiceMock.Setup(s => s.CurrentState).Returns(new AppState());

        var orchestratorMock = new Mock<ICleaningOrchestrator>();

        var vm = new ProgressViewModel(stateServiceMock.Object, orchestratorMock.Object);

        // Act
        vm.StopCommand.Execute().Subscribe();

        // Assert
        orchestratorMock.Verify(x => x.StopCleaning(), Times.Once);
    }
}
