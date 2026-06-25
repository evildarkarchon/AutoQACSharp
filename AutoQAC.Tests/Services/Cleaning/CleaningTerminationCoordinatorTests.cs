using System.Diagnostics;
using System.Reactive.Subjects;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Monitoring;
using AutoQAC.Services.Process;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using FluentAssertions;
using NSubstitute;

namespace AutoQAC.Tests.Services.Cleaning;

public sealed class CleaningTerminationCoordinatorTests : IDisposable
{
    private readonly IProcessExecutionService _processMock;
    private readonly IHangDetectionService _hangMock;
    private readonly IStateService _stateMock;
    private readonly ICleaningTerminationCoordinator _sut;
    private readonly List<Process> _startedProcesses = [];

    public CleaningTerminationCoordinatorTests()
    {
        _processMock = Substitute.For<IProcessExecutionService>();
        _hangMock = Substitute.For<IHangDetectionService>();
        _stateMock = Substitute.For<IStateService>();
        var loggerMock = Substitute.For<ILoggingService>();
        _sut = new CleaningTerminationCoordinator(_processMock, _hangMock, _stateMock, loggerMock);
    }

    public void Dispose()
    {
        foreach (var process in _startedProcesses)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(2000);
                }
            }
            catch
            {
                // Best-effort cleanup for test helper processes.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    [Fact]
    public async Task StopAsync_FirstCall_AttemptsGraceful_AndStoresGracePeriodExpiredResult()
    {
        // Arrange
        using var process = StartSleeperProcess();
        _sut.AttachProcess(process);
        _processMock.TerminateProcessAsync(process, forceKill: false, Arg.Any<CancellationToken>())
            .Returns(TerminationResult.GracePeriodExpired);

        // Act
        var result = await _sut.StopAsync();

        // Assert
        result.TerminationResult.Should().Be(TerminationResult.GracePeriodExpired);
        result.MayStillBeRunning.Should().BeTrue();
        _sut.IsStopRequested.Should().BeTrue();
        _sut.LastTerminationResult.Should().Be(TerminationResult.GracePeriodExpired);
        await _processMock.Received(1).TerminateProcessAsync(process, forceKill: false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopAsync_SecondCallDuringGrace_EscalatesToForceStop_WithoutPrompt()
    {
        // Arrange
        using var process = StartSleeperProcess();
        _sut.AttachProcess(process);
        _processMock.TerminateProcessAsync(process, forceKill: false, Arg.Any<CancellationToken>())
            .Returns(TerminationResult.GracePeriodExpired);
        _processMock.TerminateProcessAsync(process, forceKill: true, Arg.Any<CancellationToken>())
            .Returns(TerminationResult.ForceKilled);

        // Act
        await _sut.StopAsync();
        var result = await _sut.StopAsync();

        // Assert
        result.TerminationResult.Should().Be(TerminationResult.ForceKilled);
        result.MayStillBeRunning.Should().BeFalse();
        await _processMock.Received(1).TerminateProcessAsync(process, forceKill: false, Arg.Any<CancellationToken>());
        await _processMock.Received(1).TerminateProcessAsync(process, forceKill: true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForceStopAsync_TerminatesActiveProcess_WithForceKillTrue()
    {
        // Arrange
        using var process = StartSleeperProcess();
        _sut.AttachProcess(process);
        _processMock.TerminateProcessAsync(process, forceKill: true, Arg.Any<CancellationToken>())
            .Returns(TerminationResult.ForceKilled);

        // Act
        var result = await _sut.ForceStopAsync();

        // Assert
        result.TerminationResult.Should().Be(TerminationResult.ForceKilled);
        await _processMock.Received(1).TerminateProcessAsync(process, forceKill: true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopAsync_RefusesToTerminate_WhenAttachedProcessIdEqualsCurrentProcess()
    {
        // Arrange
        Environment.ProcessId.Should().Be(Process.GetCurrentProcess().Id);
        _sut.AttachProcess(Process.GetCurrentProcess());

        // Act
        var result = await _sut.StopAsync();

        // Assert
        result.TerminationResult.Should().BeNull();
        result.MayStillBeRunning.Should().BeFalse();
        _stateMock.Received(1).SetTerminating(false);
        await _processMock.DidNotReceive().TerminateProcessAsync(
            Arg.Any<Process>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopAsync_WithNoActiveProcess_ClearsTerminatingFlag()
    {
        // Act
        var result = await _sut.StopAsync();

        // Assert
        result.TerminationResult.Should().BeNull();
        result.MayStillBeRunning.Should().BeFalse();
        _stateMock.Received(1).SetTerminating(true);
        _stateMock.Received(1).SetTerminating(false);
        await _processMock.DidNotReceive().TerminateProcessAsync(
            Arg.Any<Process>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForceStopAsync_RefusesToTerminate_WhenAttachedProcessIdEqualsCurrentProcess()
    {
        // Arrange
        Environment.ProcessId.Should().Be(Process.GetCurrentProcess().Id);
        _sut.AttachProcess(Process.GetCurrentProcess());

        // Act
        var result = await _sut.ForceStopAsync();

        // Assert
        result.TerminationResult.Should().BeNull();
        result.MayStillBeRunning.Should().BeFalse();
        await _processMock.DidNotReceive().TerminateProcessAsync(
            Arg.Any<Process>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void MarkLeftRunningByUser_ReturnsMayStillBeRunning_AndStoresLeftRunningResult()
    {
        // Act
        var result = _sut.MarkLeftRunningByUser();

        // Assert
        result.TerminationResult.Should().Be(TerminationResult.LeftRunningByUser);
        result.MayStillBeRunning.Should().BeTrue();
        _sut.LastTerminationResult.Should().Be(TerminationResult.LeftRunningByUser);
        _sut.ProcessMayStillBeRunning.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteSessionFinalization_AfterForceKillFailed_PreservesUnresolvedTerminationStateUntilReset()
    {
        // Arrange
        using var process = StartSleeperProcess();
        _sut.AttachProcess(process);
        _processMock.TerminateProcessAsync(process, forceKill: true, Arg.Any<CancellationToken>())
            .Returns(TerminationResult.ForceKillFailed);

        // Act
        var stopResult = await _sut.ForceStopAsync();
        _sut.CompleteSessionFinalization();

        // Assert
        stopResult.TerminationResult.Should().Be(TerminationResult.ForceKillFailed);
        stopResult.MayStillBeRunning.Should().BeTrue();
        _sut.LastTerminationResult.Should().Be(TerminationResult.ForceKillFailed);
        _sut.ProcessMayStillBeRunning.Should().BeTrue();

        _sut.ResetForNewSession();
        _sut.LastTerminationResult.Should().BeNull();
        _sut.ProcessMayStillBeRunning.Should().BeFalse();
    }

    [Fact]
    public async Task ResetForNewSession_ClearsStopFlag_AndLastTerminationResult()
    {
        // Arrange
        using var process = StartSleeperProcess();
        _sut.AttachProcess(process);
        _processMock.TerminateProcessAsync(process, forceKill: false, Arg.Any<CancellationToken>())
            .Returns(TerminationResult.GracePeriodExpired);
        await _sut.StopAsync();

        // Act
        _sut.ResetForNewSession();

        // Assert
        _sut.IsStopRequested.Should().BeFalse();
        _sut.LastTerminationResult.Should().BeNull();
        _sut.HasActiveProcess.Should().BeFalse();
        _sut.ProcessMayStillBeRunning.Should().BeFalse();
    }

    [Fact]
    public void ResetForNewSession_EmitsHangDetectedFalse_AndSetsTerminatingFalse()
    {
        // Arrange — subscribe to HangDetected and capture all emissions.
        var emissions = new List<bool>();
        using var sub = _sut.HangDetected.Subscribe(new CallbackObserver<bool>(emissions.Add));

        // Act
        _sut.ResetForNewSession();

        // Assert — per R-03: reset must emit `false` on hang Subject AND call SetTerminating(false).
        emissions.Should().NotBeEmpty();
        emissions[^1].Should().BeFalse("ResetForNewSession must clear stale hang state (D-09)");
        _stateMock.Received(1).SetTerminating(false);
    }

    [Fact]
    public async Task TerminateProcessAsync_IsCalledWithCancellationTokenNone_NotSessionToken()
    {
        // Arrange
        using var process = StartSleeperProcess();
        _sut.AttachProcess(process);
        _processMock.TerminateProcessAsync(process, forceKill: false, Arg.Any<CancellationToken>())
            .Returns(TerminationResult.GracePeriodExpired);

        // Act
        await _sut.StopAsync();

        // Assert
        await _processMock.Received(1).TerminateProcessAsync(
            process,
            forceKill: false,
            Arg.Is<CancellationToken>(ct => ct == CancellationToken.None));
    }

    [Fact]
    public void AttachProcess_StartsHangMonitorSubscription_AndForwardsToHangDetectedObservable()
    {
        // Arrange
        using var process = StartShortLivedProcess();
        var hangSubject = new Subject<bool>();
        _hangMock.MonitorProcess(process).Returns(hangSubject);
        var emissions = new List<bool>();
        using var sub = _sut.HangDetected.Subscribe(new CallbackObserver<bool>(emissions.Add));

        // Act
        _sut.AttachProcess(process);
        hangSubject.OnNext(true);

        // Assert
        _hangMock.Received(1).MonitorProcess(process);
        emissions.Should().Contain(true);
    }

    [Fact]
    public async Task ForceStopAsync_AfterGracePeriodExpiredAndDetach_ForceKillsPendingProcess()
    {
        // Arrange
        using var process = StartSleeperProcess();
        _sut.AttachProcess(process);
        _processMock.TerminateProcessAsync(
                process,
                forceKill: false,
                Arg.Is<CancellationToken>(ct => ct == CancellationToken.None))
            .Returns(TerminationResult.GracePeriodExpired);
        _processMock.TerminateProcessAsync(
                Arg.Any<Process>(),
                forceKill: true,
                Arg.Is<CancellationToken>(ct => ct == CancellationToken.None))
            .Returns(TerminationResult.ForceKilled);

        // Act
        await _sut.StopAsync();
        _sut.DetachProcess();
        var result = await _sut.ForceStopAsync();

        // Assert
        result.TerminationResult.Should().Be(
            TerminationResult.ForceKilled,
            "D-01/D-03 require the coordinator to retain durable pending-target identity after active detach");
        result.MayStillBeRunning.Should().BeFalse("D-06 requires a terminal confirmed force-stop result");
        await _processMock.Received(1).TerminateProcessAsync(
            Arg.Any<Process>(),
            forceKill: true,
            Arg.Is<CancellationToken>(ct => ct == CancellationToken.None));
    }

    [Fact]
    public async Task ForceStopAsync_AfterGracePeriodExpiredAndDetach_KeepsHasActiveProcessFalse()
    {
        // Arrange
        using var process = StartSleeperProcess();
        _sut.AttachProcess(process);
        _processMock.TerminateProcessAsync(process, forceKill: false, Arg.Any<CancellationToken>())
            .Returns(TerminationResult.GracePeriodExpired);
        _processMock.TerminateProcessAsync(Arg.Any<Process>(), forceKill: true, Arg.Any<CancellationToken>())
            .Returns(TerminationResult.ForceKilled);

        // Act
        await _sut.StopAsync();
        _sut.DetachProcess();
        var hasActiveProcessAfterDetach = _sut.HasActiveProcess;
        var result = await _sut.ForceStopAsync();

        // Assert
        hasActiveProcessAfterDetach.Should().BeFalse(
            "D-04 keeps active cleaning semantics separate from pending force-escalation ownership");
        result.TerminationResult.Should().Be(TerminationResult.ForceKilled);
        await _processMock.Received(1).TerminateProcessAsync(Arg.Any<Process>(), forceKill: true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForceStopAsync_AfterGracePeriodExpiredAndDetachedProcessAlreadyExited_ReturnsForceKillFailed()
    {
        // Arrange
        using var process = StartSelfExitingProcess();
        _sut.AttachProcess(process);
        _processMock.TerminateProcessAsync(process, forceKill: false, Arg.Any<CancellationToken>())
            .Returns(TerminationResult.GracePeriodExpired);

        // Act
        process.HasExited.Should().BeFalse("the target must still be alive when StopAsync captures durable pending identity");
        await _sut.StopAsync();
        _sut.DetachProcess();
        process.WaitForExit(5000).Should().BeTrue("the helper process is intentionally short-lived");
        var result = await _sut.ForceStopAsync();

        // Assert
        result.TerminationResult.Should().Be(
            TerminationResult.ForceKillFailed,
            "D-08 requires an unavailable durable pending target to surface explicit force failure");
        result.MayStillBeRunning.Should().BeTrue();
        await _processMock.DidNotReceive().TerminateProcessAsync(process, forceKill: true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForceStopAsync_AfterGracePeriodExpiredAndUnavailablePendingTarget_ReturnsForceKillFailed()
    {
        // Arrange
        using var process = StartSleeperProcess();
        _sut.AttachProcess(process);
        _processMock.TerminateProcessAsync(process, forceKill: false, Arg.Any<CancellationToken>())
            .Returns(TerminationResult.GracePeriodExpired);
        _processMock.TerminateProcessAsync(Arg.Any<Process>(), forceKill: true, Arg.Any<CancellationToken>())
            .Returns(TerminationResult.ForceKillFailed);

        // Act
        await _sut.StopAsync();
        _sut.DetachProcess();
        var result = await _sut.ForceStopAsync();

        // Assert
        result.TerminationResult.Should().Be(
            TerminationResult.ForceKillFailed,
            "D-08 requires unavailable or unprovable confirmed force targets to surface explicit force failure");
        result.TerminationResult.Should().NotBe(TerminationResult.GracePeriodExpired, "D-06 forbids reusing cached grace expiry after confirmation");
        result.MayStillBeRunning.Should().BeTrue();
        await _processMock.Received(1).TerminateProcessAsync(Arg.Any<Process>(), forceKill: true, Arg.Any<CancellationToken>());
    }

    private Process StartSleeperProcess()
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = "-NoProfile -Command \"Start-Sleep -Seconds 30\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });

        process.Should().NotBeNull("a real external process is needed to exercise termination paths safely");
        _startedProcesses.Add(process);
        return process;
    }

    /// <summary>
    /// Starts a process that stays alive long enough for StopAsync to capture PID/start-time identity, then exits before confirmed force stop.
    /// </summary>
    private Process StartSelfExitingProcess()
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = "-NoProfile -Command \"Start-Sleep -Milliseconds 2000\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });

        process.Should().NotBeNull("a real external process is needed to exercise pending target identity safely");
        _startedProcesses.Add(process);
        return process;
    }

    private Process StartShortLivedProcess()
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c echo done",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            }
        };
        process.Start();
        _startedProcesses.Add(process);
        return process;
    }
}
