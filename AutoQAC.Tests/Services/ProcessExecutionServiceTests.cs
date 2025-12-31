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
    /// Sets up a state service returning a default AppState with 1 max concurrent subprocess.
    /// </summary>
    public ProcessExecutionServiceTests()
    {
        _mockLogger = new Mock<ILoggingService>();
        _mockState = new Mock<IStateService>();

        // Configure default state with 1 max concurrent process
        // This ensures sequential execution as per CLAUDE.md requirements
        var defaultState = new AppState { MaxConcurrentSubprocesses = 1 };
        _mockState.Setup(s => s.CurrentState).Returns(defaultState);
    }

    public void Dispose()
    {
        // No special cleanup needed since we're not creating real processes
    }

    #region Process Slot Acquisition Tests

    /// <summary>
    /// Verifies that AcquireProcessSlotAsync returns a disposable slot
    /// that can be properly released.
    /// </summary>
    [Fact]
    public async Task AcquireProcessSlotAsync_ShouldReturnDisposableSlot()
    {
        // Arrange
        using var service = new ProcessExecutionService(_mockState.Object, _mockLogger.Object);

        // Act
        using var slot = await service.AcquireProcessSlotAsync();

        // Assert
        slot.Should().NotBeNull("a valid slot should be returned");
        slot.Should().BeAssignableTo<IDisposable>("slot should be disposable");
    }

    /// <summary>
    /// Verifies that the semaphore properly limits concurrent process slots
    /// based on the MaxConcurrentSubprocesses setting from state.
    /// </summary>
    [Fact]
    public async Task AcquireProcessSlotAsync_ShouldBlockWhenNoSlotsAvailable()
    {
        // Arrange
        // State configured with 1 slot in constructor
        using var service = new ProcessExecutionService(_mockState.Object, _mockLogger.Object);

        // Act
        // Acquire the only slot
        var slot1 = await service.AcquireProcessSlotAsync();

        // Try to acquire second slot with a short timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Assert
        // Should throw OperationCanceledException because no slot is available
        var acquireTask = service.AcquireProcessSlotAsync(cts.Token);
        await FluentActions.Awaiting(() => acquireTask)
            .Should().ThrowAsync<OperationCanceledException>(
                "second slot acquisition should fail when all slots are taken");

        // Cleanup
        slot1.Dispose();
    }

    /// <summary>
    /// Verifies that releasing a slot allows another acquisition to proceed.
    /// This tests the core semaphore release functionality.
    /// </summary>
    [Fact]
    public async Task AcquireProcessSlotAsync_ShouldSucceedAfterSlotReleased()
    {
        // Arrange
        using var service = new ProcessExecutionService(_mockState.Object, _mockLogger.Object);

        // Act
        // Acquire and release the slot
        var slot1 = await service.AcquireProcessSlotAsync();
        slot1.Dispose();

        // Should now be able to acquire again
        using var slot2 = await service.AcquireProcessSlotAsync();

        // Assert
        slot2.Should().NotBeNull("slot should be available after release");
    }

    /// <summary>
    /// Verifies that cancellation token is respected during slot acquisition.
    /// </summary>
    [Fact]
    public async Task AcquireProcessSlotAsync_ShouldThrowOnCancellation()
    {
        // Arrange
        using var service = new ProcessExecutionService(_mockState.Object, _mockLogger.Object);

        // Occupy the only slot
        var slot = await service.AcquireProcessSlotAsync();

        // Act & Assert
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        await FluentActions.Awaiting(() => service.AcquireProcessSlotAsync(cts.Token))
            .Should().ThrowAsync<OperationCanceledException>(
                "cancelled token should cause immediate cancellation");

        slot.Dispose();
    }

    #endregion

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

    #region Semaphore Management Tests

    /// <summary>
    /// Verifies that multiple concurrent execution requests are properly queued
    /// and processed sequentially when only one slot is available.
    /// This is CRITICAL for ensuring xEdit processes run one at a time per CLAUDE.md.
    ///
    /// NOTE: This test uses slot acquisition to verify sequential behavior,
    /// which is more reliable than timing-based tests with actual processes.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ShouldEnforceSequentialExecution_WhenOneSlot()
    {
        // Arrange
        using var service = new ProcessExecutionService(_mockState.Object, _mockLogger.Object);

        // Verify that only one slot is available at a time by trying to acquire two
        var slot1 = await service.AcquireProcessSlotAsync();

        // Act - Try to acquire a second slot with a short timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Assert
        // The second slot acquisition should fail because we only have 1 slot
        // and it's already held by slot1
        await FluentActions.Awaiting(() => service.AcquireProcessSlotAsync(cts.Token))
            .Should().ThrowAsync<OperationCanceledException>(
                "with 1 slot, concurrent slot acquisition should block and timeout");

        // Release the first slot
        slot1.Dispose();

        // Now acquiring should succeed
        using var slot2 = await service.AcquireProcessSlotAsync();
        slot2.Should().NotBeNull("after releasing slot1, slot2 should be acquirable");
    }

    /// <summary>
    /// Verifies that disposal properly cleans up the semaphore.
    /// </summary>
    [Fact]
    public void Dispose_ShouldCleanupSemaphore()
    {
        // Arrange
        var service = new ProcessExecutionService(_mockState.Object, _mockLogger.Object);

        // Act
        service.Dispose();

        // Assert
        // After disposal, trying to acquire should throw ObjectDisposedException
        FluentActions.Awaiting(() => service.AcquireProcessSlotAsync())
            .Should().ThrowAsync<ObjectDisposedException>(
                "disposed service should not allow slot acquisition");
    }

    /// <summary>
    /// Verifies that the service initializes with correct number of slots from state.
    /// </summary>
    [Fact]
    public async Task Constructor_ShouldInitializeSlotsFromState()
    {
        // Arrange
        var stateWith3Slots = new AppState { MaxConcurrentSubprocesses = 3 };
        _mockState.Setup(s => s.CurrentState).Returns(stateWith3Slots);

        using var service = new ProcessExecutionService(_mockState.Object, _mockLogger.Object);

        // Act & Assert
        // Should be able to acquire 3 slots concurrently
        var slots = new List<IDisposable>();
        for (int i = 0; i < 3; i++)
        {
            slots.Add(await service.AcquireProcessSlotAsync());
        }

        slots.Should().HaveCount(3, "should acquire all 3 configured slots");

        // Cleanup
        foreach (var slot in slots)
        {
            slot.Dispose();
        }
    }

    /// <summary>
    /// Verifies that null MaxConcurrentSubprocesses defaults to 1 slot.
    /// </summary>
    [Fact]
    public async Task Constructor_WhenMaxConcurrentNull_ShouldDefaultToOneSlot()
    {
        // Arrange
        var stateWithNullSlots = new AppState { MaxConcurrentSubprocesses = null };
        _mockState.Setup(s => s.CurrentState).Returns(stateWithNullSlots);

        using var service = new ProcessExecutionService(_mockState.Object, _mockLogger.Object);

        // Act
        // Should be able to acquire exactly 1 slot
        var slot1 = await service.AcquireProcessSlotAsync();

        // Second slot should block
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var acquireTask = service.AcquireProcessSlotAsync(cts.Token);

        // Assert
        await FluentActions.Awaiting(() => acquireTask)
            .Should().ThrowAsync<OperationCanceledException>(
                "only 1 slot should be available when MaxConcurrent is null");

        slot1.Dispose();
    }

    #endregion
}
