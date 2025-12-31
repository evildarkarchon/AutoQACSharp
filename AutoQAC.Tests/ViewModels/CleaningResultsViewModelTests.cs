using System.Reactive.Concurrency;
using System.Reactive.Linq;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.UI;
using AutoQAC.ViewModels;
using FluentAssertions;
using Moq;
using ReactiveUI;

namespace AutoQAC.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="CleaningResultsViewModel"/>.
/// </summary>
public sealed class CleaningResultsViewModelTests
{
    private readonly Mock<ILoggingService> _loggerMock;
    private readonly Mock<IFileDialogService> _fileDialogMock;

    public CleaningResultsViewModelTests()
    {
        // Force immediate execution for tests
        RxApp.MainThreadScheduler = Scheduler.Immediate;

        _loggerMock = new Mock<ILoggingService>();
        _fileDialogMock = new Mock<IFileDialogService>();
    }

    private static CleaningSessionResult CreateTestSessionResult(
        int cleaned = 2,
        int skipped = 1,
        int failed = 0,
        bool wasCancelled = false)
    {
        var results = new List<PluginCleaningResult>();

        for (int i = 0; i < cleaned; i++)
        {
            results.Add(new PluginCleaningResult
            {
                PluginName = $"Cleaned{i + 1}.esp",
                Status = CleaningStatus.Cleaned,
                Success = true,
                Duration = TimeSpan.FromSeconds(30),
                Statistics = new CleaningStatistics
                {
                    ItemsRemoved = 10 + i,
                    ItemsUndeleted = 2 + i
                }
            });
        }

        for (int i = 0; i < skipped; i++)
        {
            results.Add(new PluginCleaningResult
            {
                PluginName = $"Skipped{i + 1}.esp",
                Status = CleaningStatus.Skipped
            });
        }

        for (int i = 0; i < failed; i++)
        {
            results.Add(new PluginCleaningResult
            {
                PluginName = $"Failed{i + 1}.esp",
                Status = CleaningStatus.Failed,
                Message = "Timeout"
            });
        }

        return new CleaningSessionResult
        {
            StartTime = new DateTime(2024, 1, 15, 10, 0, 0),
            EndTime = new DateTime(2024, 1, 15, 10, 5, 30),
            GameType = GameType.SkyrimSe,
            WasCancelled = wasCancelled,
            PluginResults = results
        };
    }

    private CleaningResultsViewModel CreateViewModel(CleaningSessionResult? sessionResult = null)
    {
        return new CleaningResultsViewModel(
            sessionResult ?? CreateTestSessionResult(),
            _loggerMock.Object,
            _fileDialogMock.Object);
    }

    #region Initialization Tests

    [Fact]
    public void Constructor_ShouldPopulatePluginResults()
    {
        // Arrange & Act
        var vm = CreateViewModel();

        // Assert
        vm.PluginResults.Should().HaveCount(3);
        vm.PluginResults.Should().Contain(r => r.PluginName == "Cleaned1.esp");
        vm.PluginResults.Should().Contain(r => r.PluginName == "Cleaned2.esp");
        vm.PluginResults.Should().Contain(r => r.PluginName == "Skipped1.esp");
    }

    [Fact]
    public void Constructor_ShouldSetSessionResult()
    {
        // Arrange
        var sessionResult = CreateTestSessionResult();

        // Act
        var vm = new CleaningResultsViewModel(sessionResult, _loggerMock.Object, _fileDialogMock.Object);

        // Assert
        vm.SessionResult.Should().BeSameAs(sessionResult);
    }

    #endregion

    #region WindowTitle Tests

    [Fact]
    public void WindowTitle_WhenSuccessful_ShouldBeCleaningCompleted()
    {
        // Arrange
        var vm = CreateViewModel(CreateTestSessionResult(cleaned: 2, failed: 0, wasCancelled: false));

        // Assert
        vm.WindowTitle.Should().Be("Cleaning Completed");
    }

    [Fact]
    public void WindowTitle_WhenCancelled_ShouldBeCleaningCancelled()
    {
        // Arrange
        var vm = CreateViewModel(CreateTestSessionResult(wasCancelled: true));

        // Assert
        vm.WindowTitle.Should().Be("Cleaning Cancelled");
    }

    [Fact]
    public void WindowTitle_WhenHasErrors_ShouldIndicateErrors()
    {
        // Arrange
        var vm = CreateViewModel(CreateTestSessionResult(cleaned: 1, failed: 1));

        // Assert
        vm.WindowTitle.Should().Be("Cleaning Completed with Errors");
    }

    #endregion

    #region Summary Properties Tests

    [Fact]
    public void TotalPlugins_ShouldReturnCorrectCount()
    {
        // Arrange
        var vm = CreateViewModel(CreateTestSessionResult(cleaned: 3, skipped: 2, failed: 1));

        // Assert
        vm.TotalPlugins.Should().Be(6);
    }

    [Fact]
    public void CleanedCount_ShouldReturnCorrectCount()
    {
        // Arrange
        var vm = CreateViewModel(CreateTestSessionResult(cleaned: 5));

        // Assert
        vm.CleanedCount.Should().Be(5);
    }

    [Fact]
    public void FailedCount_ShouldReturnCorrectCount()
    {
        // Arrange
        var vm = CreateViewModel(CreateTestSessionResult(failed: 3));

        // Assert
        vm.FailedCount.Should().Be(3);
    }

    [Fact]
    public void SkippedCount_ShouldReturnCorrectCount()
    {
        // Arrange
        var vm = CreateViewModel(CreateTestSessionResult(skipped: 4));

        // Assert
        vm.SkippedCount.Should().Be(4);
    }

    [Fact]
    public void TotalItms_ShouldSumAllItms()
    {
        // Arrange - 2 cleaned plugins with 10+0=10 and 10+1=11 ITMs
        var vm = CreateViewModel(CreateTestSessionResult(cleaned: 2));

        // Assert
        vm.TotalItms.Should().Be(21); // 10 + 11
    }

    [Fact]
    public void TotalUdrs_ShouldSumAllUdrs()
    {
        // Arrange - 2 cleaned plugins with 2+0=2 and 2+1=3 UDRs
        var vm = CreateViewModel(CreateTestSessionResult(cleaned: 2));

        // Assert
        vm.TotalUdrs.Should().Be(5); // 2 + 3
    }

    [Fact]
    public void DurationText_ShouldFormatCorrectly()
    {
        // Arrange - session is 5:30
        var vm = CreateViewModel();

        // Assert
        vm.DurationText.Should().Be("05:30");
    }

    [Fact]
    public void IsSuccess_ShouldReflectSessionResult()
    {
        // Arrange
        var successVm = CreateViewModel(CreateTestSessionResult(cleaned: 2, failed: 0));
        var failedVm = CreateViewModel(CreateTestSessionResult(cleaned: 1, failed: 1));

        // Assert
        successVm.IsSuccess.Should().BeTrue();
        failedVm.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void HasFailures_ShouldBeTrue_WhenFailedCountIsPositive()
    {
        // Arrange
        var vm = CreateViewModel(CreateTestSessionResult(failed: 1));

        // Assert
        vm.HasFailures.Should().BeTrue();
    }

    [Fact]
    public void HasFailures_ShouldBeFalse_WhenNoFailures()
    {
        // Arrange
        var vm = CreateViewModel(CreateTestSessionResult(failed: 0));

        // Assert
        vm.HasFailures.Should().BeFalse();
    }

    [Fact]
    public void WasCancelled_ShouldReflectSessionResult()
    {
        // Arrange
        var cancelledVm = CreateViewModel(CreateTestSessionResult(wasCancelled: true));
        var completedVm = CreateViewModel(CreateTestSessionResult(wasCancelled: false));

        // Assert
        cancelledVm.WasCancelled.Should().BeTrue();
        completedVm.WasCancelled.Should().BeFalse();
    }

    [Fact]
    public void HasPartialForms_ShouldBeFalse_WhenNoPartialForms()
    {
        // Arrange - test session doesn't have partial forms
        var vm = CreateViewModel();

        // Assert
        vm.HasPartialForms.Should().BeFalse();
    }

    [Fact]
    public void HasPartialForms_ShouldBeTrue_WhenPartialFormsExist()
    {
        // Arrange
        var sessionResult = new CleaningSessionResult
        {
            PluginResults = new List<PluginCleaningResult>
            {
                new()
                {
                    PluginName = "Test.esp",
                    Status = CleaningStatus.Cleaned,
                    Statistics = new CleaningStatistics { PartialFormsCreated = 5 }
                }
            }
        };
        var vm = CreateViewModel(sessionResult);

        // Assert
        vm.HasPartialForms.Should().BeTrue();
        vm.TotalPartialForms.Should().Be(5);
    }

    #endregion

    #region Command Tests

    [Fact]
    public void CloseCommand_ShouldBeExecutable()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act & Assert
        vm.CloseCommand.CanExecute.Subscribe(canExecute =>
        {
            canExecute.Should().BeTrue();
        });
    }

    [Fact]
    public async Task ExportReportCommand_WhenUserCancels_ShouldNotWriteFile()
    {
        // Arrange
        _fileDialogMock.Setup(f => f.SaveFileDialogAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()))
            .ReturnsAsync((string?)null);

        var vm = CreateViewModel();

        // Act
        await vm.ExportReportCommand.Execute().FirstAsync();

        // Assert - No file should be written
        _loggerMock.Verify(
            l => l.Information(It.IsAny<string>(), It.IsAny<object[]>()),
            Times.Never);
    }

    [Fact]
    public async Task ExportReportCommand_WhenUserSelectsPath_ShouldLogSuccess()
    {
        // Arrange
        var testPath = "C:\\test\\report.txt";
        _fileDialogMock.Setup(f => f.SaveFileDialogAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()))
            .ReturnsAsync(testPath);

        var vm = CreateViewModel();

        // Act
        await vm.ExportReportCommand.Execute().FirstAsync();

        // Assert
        _loggerMock.Verify(
            l => l.Information(It.IsAny<string>(), It.Is<object[]>(args => args[0].ToString() == testPath)),
            Times.Once);
    }

    [Fact]
    public async Task ExportReportCommand_ShouldUseCorrectDefaultFileName()
    {
        // Arrange
        var sessionResult = CreateTestSessionResult();
        var expectedPrefix = $"AutoQAC_Report_{sessionResult.StartTime:yyyyMMdd_HHmmss}";

        _fileDialogMock.Setup(f => f.SaveFileDialogAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.Is<string>(name => name.StartsWith(expectedPrefix)),
            It.IsAny<string>()))
            .ReturnsAsync((string?)null);

        var vm = CreateViewModel(sessionResult);

        // Act
        await vm.ExportReportCommand.Execute().FirstAsync();

        // Assert
        _fileDialogMock.Verify(f => f.SaveFileDialogAsync(
            "Save Cleaning Report",
            It.IsAny<string>(),
            It.Is<string>(name => name.Contains("AutoQAC_Report")),
            It.IsAny<string>()),
            Times.Once);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void WithEmptyResults_ShouldNotThrow()
    {
        // Arrange
        var emptySession = new CleaningSessionResult
        {
            StartTime = DateTime.Now,
            EndTime = DateTime.Now,
            PluginResults = Array.Empty<PluginCleaningResult>()
        };

        // Act & Assert
        FluentActions.Invoking(() => CreateViewModel(emptySession))
            .Should().NotThrow();

        var vm = CreateViewModel(emptySession);
        vm.TotalPlugins.Should().Be(0);
        vm.PluginResults.Should().BeEmpty();
    }

    #endregion
}
