using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Services.Monitoring;
using FluentAssertions;
using Moq;

namespace AutoQAC.Tests.Services;

/// <summary>
/// Unit tests for <see cref="HangDetectionService"/> covering observable creation,
/// process exit handling, and constant validation.
/// </summary>
public sealed class HangDetectionServiceTests
{
    private readonly Mock<ILoggingService> _loggerMock;
    private readonly HangDetectionService _service;

    public HangDetectionServiceTests()
    {
        _loggerMock = new Mock<ILoggingService>();
        _service = new HangDetectionService(_loggerMock.Object);
    }

    [Fact]
    public void MonitorProcess_ShouldReturnNonNullObservable()
    {
        // Arrange - use the current process as a stand-in (it's definitely alive)
        var process = Process.GetCurrentProcess();

        // Act
        var observable = _service.MonitorProcess(process);

        // Assert
        observable.Should().NotBeNull();
    }

    [Fact]
    public async Task MonitorProcess_ShouldCompleteForAlreadyExitedProcess()
    {
        // Arrange - start and immediately wait for a short-lived process to exit
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
        await process.WaitForExitAsync();

        // Act - monitor the already-exited process
        var emissions = new List<bool>();
        var observable = _service.MonitorProcess(process);
        await observable.Do(v => emissions.Add(v)).DefaultIfEmpty();

        // Assert - should complete without emitting true (hung)
        emissions.Should().NotContain(true, "an already-exited process should not be flagged as hung");
    }

    [Fact]
    public async Task MonitorProcess_ShortLivedProcess_ShouldCompleteWithoutHungEmission()
    {
        // Arrange - start a process that exits quickly
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c echo short",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            }
        };
        process.Start();

        // Act - monitor the short-lived process
        var emissions = new List<bool>();
        var observable = _service.MonitorProcess(process);

        // Wait for the observable to complete (process will exit within a couple seconds)
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await observable
            .Do(v => emissions.Add(v))
            .DefaultIfEmpty()
            .ToTask(cts.Token);

        // Assert - should not emit true since the process doesn't live long enough for hang threshold
        emissions.Should().NotContain(true,
            "a short-lived process should not trigger hang detection (threshold is 60s)");
    }

    [Fact]
    public void Constants_ShouldHaveExpectedValues()
    {
        // Verify the constants match the design requirements
        HangDetectionService.PollIntervalMs.Should().Be(5_000, "polling every 5 seconds");
        HangDetectionService.HangThresholdMs.Should().Be(60_000, "60 second hang threshold");
        HangDetectionService.CpuThreshold.Should().Be(0.5, "0.5% CPU threshold for near-zero");
    }
}
