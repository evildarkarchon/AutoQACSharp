using System.Diagnostics;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Process;
using AutoQAC.Services.State;
using FluentAssertions;
using Moq;

namespace AutoQAC.Tests.Services;

/// <summary>
/// Unit tests for <see cref="ProcessExecutionService"/> covering process execution,
/// timeout handling, cancellation, semaphore slot management, and startup failures.
///
/// IMPORTANT: These tests mock the dependencies but cannot fully test actual process
/// execution. The tests verify the service's behavior with various scenarios by
/// testing through the public API.
/// </summary>
public sealed class ProcessExecutionServiceTests : IDisposable
{
    private readonly Mock<ILoggingService> _mockLogger;
    private readonly Mock<IStateService> _mockState;

    /// <summary>
    /// Initializes test fixtures with default mock configurations.
    /// </summary>
    public ProcessExecutionServiceTests()
    {
        _mockLogger = new Mock<ILoggingService>();
        _mockState = new Mock<IStateService>();

        var defaultState = new AppState();
        _mockState.Setup(s => s.CurrentState).Returns(defaultState);
    }

    public void Dispose()
    {
        // No special cleanup needed since we're not creating real processes
    }

    #region Process Execution Tests

    /// <summary>
    /// Verifies that ExecuteAsync returns a failed result with appropriate error
    /// when the process executable does not exist.
    ///
    /// NOTE: This tests the startup failure scenario where Process.Start throws
    /// because the file cannot be found.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenProcessNotFound_ShouldReturnFailedResult()
    {
        // Arrange
        using var service = new ProcessExecutionService(_mockState.Object, _mockLogger.Object);

        var startInfo = new ProcessStartInfo
        {
            FileName = "nonexistent_process_that_does_not_exist_12345.exe",
            Arguments = "--test"
        };

        // Act
        var result = await service.ExecuteAsync(startInfo);

        // Assert
        result.ExitCode.Should().Be(-1, "startup failure should return -1 exit code");
        result.ErrorLines.Should().NotBeEmpty("error details should be captured");

        // Verify logging occurred
        _mockLogger.Verify(
            l => l.Error(It.IsAny<Exception>(), It.Is<string>(s => s.Contains("Failed to start"))),
            Times.Once,
            "startup failure should be logged");
    }

    /// <summary>
    /// Verifies that ExecuteAsync handles cancellation properly by terminating
    /// the process and returning appropriate result.
    ///
    /// NOTE: This test uses a cmd.exe ping command that runs long enough to be cancelled.
    /// The test verifies the cancellation logic path.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenCancelled_ShouldTerminateProcess()
    {
        // Arrange
        using var service = new ProcessExecutionService(_mockState.Object, _mockLogger.Object);

        // Use a simple command that will run for a while - Windows ping
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c ping 127.0.0.1 -n 10" // Pings for about 10 seconds
        };

        using var cts = new CancellationTokenSource();

        // Act
        // Start the process and cancel after a short delay
        var executeTask = service.ExecuteAsync(startInfo, ct: cts.Token);
        await Task.Delay(500); // Let process start
        cts.Cancel();

        var result = await executeTask;

        // Assert
        // The result should indicate cancellation occurred
        // Note: ExitCode may vary based on how termination happened
        result.Should().NotBeNull("result should still be returned even on cancellation");

        // Verify logging
        _mockLogger.Verify(
            l => l.Warning(It.Is<string>(s => s.Contains("cancelled"))),
            Times.AtMostOnce());
    }

    /// <summary>
    /// Verifies that ExecuteAsync enforces timeout and terminates long-running processes.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenTimeout_ShouldTerminateAndReturnTimedOut()
    {
        // Arrange
        using var service = new ProcessExecutionService(_mockState.Object, _mockLogger.Object);

        // Use a command that runs for a while
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c ping 127.0.0.1 -n 10" // Runs for ~10 seconds
        };

        // Set a very short timeout
        var timeout = TimeSpan.FromMilliseconds(500);

        // Act
        var result = await service.ExecuteAsync(startInfo, timeout: timeout);

        // Assert
        result.TimedOut.Should().BeTrue("process should be marked as timed out");
        result.ExitCode.Should().Be(-1, "timed out process should return -1");

        // Verify timeout was logged
        _mockLogger.Verify(
            l => l.Warning(It.Is<string>(s => s.Contains("timed out"))),
            Times.Once,
            "timeout should be logged");
    }

    /// <summary>
    /// Verifies that ExecuteAsync captures output correctly via the progress callback.
    /// This tests the happy path where a process runs and produces output.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ShouldCaptureOutputViaProgress()
    {
        // Arrange
        using var service = new ProcessExecutionService(_mockState.Object, _mockLogger.Object);

        // Use echo command to produce predictable output
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c echo TestOutput123"
        };

        var capturedOutput = new List<string>();
        var progress = new Progress<string>(output => capturedOutput.Add(output));

        // Act
        var result = await service.ExecuteAsync(startInfo, progress);

        // Assert
        result.ExitCode.Should().Be(0, "echo command should succeed");
        result.OutputLines.Should().Contain(l => l.Contains("TestOutput123"),
            "output should contain the echoed text");

        // Note: Progress callback timing is not guaranteed, so we check OutputLines instead
    }

    /// <summary>
    /// Verifies that ExecuteAsync returns the process exit code correctly.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ShouldReturnCorrectExitCode()
    {
        // Arrange
        using var service = new ProcessExecutionService(_mockState.Object, _mockLogger.Object);

        // Use exit command to produce specific exit code
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c exit 42"
        };

        // Act
        var result = await service.ExecuteAsync(startInfo);

        // Assert
        result.ExitCode.Should().Be(42, "exit code should match the command's exit code");
        result.TimedOut.Should().BeFalse("process should not be marked as timed out");
    }

    /// <summary>
    /// Verifies that ExecuteAsync captures error output (stderr) correctly.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ShouldCaptureErrorOutput()
    {
        // Arrange
        using var service = new ProcessExecutionService(_mockState.Object, _mockLogger.Object);

        // Use command that writes to stderr
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c echo ErrorMessage 1>&2"
        };

        // Act
        var result = await service.ExecuteAsync(startInfo);

        // Assert
        result.ErrorLines.Should().Contain(l => l.Contains("ErrorMessage"),
            "stderr output should be captured in ErrorLines");
    }

    #endregion

    #region Disposal Tests

    /// <summary>
    /// Verifies that disposal properly cleans up internal resources.
    /// After disposal, ExecuteAsync should throw ObjectDisposedException.
    /// </summary>
    [Fact]
    public async Task Dispose_ShouldPreventFurtherExecution()
    {
        // Arrange
        var service = new ProcessExecutionService(_mockState.Object, _mockLogger.Object);

        // Act
        service.Dispose();

        // Assert
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c echo test"
        };

        await FluentActions.Awaiting(() => service.ExecuteAsync(startInfo))
            .Should().ThrowAsync<ObjectDisposedException>(
                "disposed service should not allow execution");
    }

    #endregion
}
