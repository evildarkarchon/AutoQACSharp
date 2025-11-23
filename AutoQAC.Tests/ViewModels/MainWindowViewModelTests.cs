using System.Reactive.Concurrency;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using AutoQAC.ViewModels;
using FluentAssertions;
using Moq;
using ReactiveUI;
using Xunit;
using System.Reactive.Linq;

namespace AutoQAC.Tests.ViewModels;

public sealed class MainWindowViewModelTests
{
    private readonly Mock<IConfigurationService> _configServiceMock;
    private readonly Mock<IStateService> _stateServiceMock;
    private readonly Mock<ICleaningOrchestrator> _orchestratorMock;
    private readonly Mock<ILoggingService> _loggerMock;
    private readonly Mock<IFileDialogService> _fileDialogMock;

    public MainWindowViewModelTests()
    {
        _configServiceMock = new Mock<IConfigurationService>();
        _stateServiceMock = new Mock<IStateService>();
        _orchestratorMock = new Mock<ICleaningOrchestrator>();
        _loggerMock = new Mock<ILoggingService>();
        _fileDialogMock = new Mock<IFileDialogService>();

        RxApp.MainThreadScheduler = Scheduler.Immediate;
    }

    [Fact]
    public async Task StartCleaningCommand_ShouldCallOrchestrator_WhenCanStart()
    {
        // Arrange
        var stateSubject = new BehaviorSubject<AppState>(new AppState());
        _stateServiceMock.Setup(s => s.StateChanged).Returns(stateSubject);
        _stateServiceMock.Setup(s => s.CurrentState).Returns(new AppState());

        var vm = new MainWindowViewModel(
            _configServiceMock.Object,
            _stateServiceMock.Object,
            _orchestratorMock.Object,
            _loggerMock.Object,
            _fileDialogMock.Object);

        // Manually set properties to satisfy CanExecute
        vm.LoadOrderPath = "plugins.txt";
        vm.XEditPath = "xedit.exe";
        
        // Act
        // Verify CanExecute is true
        // var canExec = await vm.StartCleaningCommand.CanExecute.FirstAsync();
        // canExec.Should().BeTrue(); 
        
        await vm.StartCleaningCommand.Execute();

        // Assert
        _orchestratorMock.Verify(x => x.StartCleaningAsync(default), Times.Once);
    }
}
