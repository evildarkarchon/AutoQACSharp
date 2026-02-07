using System;
using System.Reactive.Linq;
using AutoQAC.Infrastructure.Logging;

namespace AutoQAC.Services.Monitoring;

/// <summary>
/// CPU-based hang detection for xEdit processes. Polls Process.TotalProcessorTime
/// at regular intervals and flags a process as hung when CPU usage is near-zero
/// for a sustained duration (default 60 seconds).
/// </summary>
public sealed class HangDetectionService : IHangDetectionService
{
    /// <summary>
    /// How often to poll CPU usage, in milliseconds.
    /// </summary>
    public const int PollIntervalMs = 5_000;

    /// <summary>
    /// How long near-zero CPU must persist before flagging as hung, in milliseconds.
    /// </summary>
    public const int HangThresholdMs = 60_000;

    /// <summary>
    /// CPU usage percentage below which activity is considered "near-zero".
    /// Process.TotalProcessorTime delta divided by wall-clock delta * 100.
    /// </summary>
    public const double CpuThreshold = 0.5;

    private readonly ILoggingService _logger;

    public HangDetectionService(ILoggingService logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public IObservable<bool> MonitorProcess(System.Diagnostics.Process process)
    {
        return Observable.Create<bool>(observer =>
        {
            // Early exit if the process has already exited
            try
            {
                if (process.HasExited)
                {
                    observer.OnCompleted();
                    return System.Reactive.Disposables.Disposable.Empty;
                }
            }
            catch (InvalidOperationException)
            {
                observer.OnCompleted();
                return System.Reactive.Disposables.Disposable.Empty;
            }

            TimeSpan lastCpuTime;
            try
            {
                lastCpuTime = process.TotalProcessorTime;
            }
            catch (InvalidOperationException)
            {
                // Process exited between HasExited check and TotalProcessorTime read
                observer.OnCompleted();
                return System.Reactive.Disposables.Disposable.Empty;
            }

            var lastCheckTime = DateTime.UtcNow;
            var nearZeroDuration = TimeSpan.Zero;
            var wasHung = false;

            var subscription = Observable.Interval(TimeSpan.FromMilliseconds(PollIntervalMs))
                .Subscribe(_ =>
                {
                    try
                    {
                        if (process.HasExited)
                        {
                            _logger.Debug("[HangDetection] Process exited, completing monitor");
                            observer.OnCompleted();
                            return;
                        }

                        var currentCpuTime = process.TotalProcessorTime;
                        var now = DateTime.UtcNow;
                        var elapsed = now - lastCheckTime;

                        // Guard against zero/negative elapsed time
                        if (elapsed.TotalMilliseconds <= 0)
                        {
                            lastCheckTime = now;
                            return;
                        }

                        var cpuDelta = (currentCpuTime - lastCpuTime).TotalMilliseconds;
                        var cpuPercent = (cpuDelta / elapsed.TotalMilliseconds) * 100.0;

                        if (cpuPercent < CpuThreshold)
                        {
                            nearZeroDuration += elapsed;

                            if (nearZeroDuration.TotalMilliseconds >= HangThresholdMs && !wasHung)
                            {
                                wasHung = true;
                                _logger.Warning(
                                    "[HangDetection] Process appears hung: near-zero CPU for {Duration}s",
                                    nearZeroDuration.TotalSeconds.ToString("F0"));
                                observer.OnNext(true);
                            }
                        }
                        else
                        {
                            if (wasHung)
                            {
                                wasHung = false;
                                _logger.Information("[HangDetection] Process resumed CPU activity");
                                observer.OnNext(false);
                            }
                            nearZeroDuration = TimeSpan.Zero;
                        }

                        lastCpuTime = currentCpuTime;
                        lastCheckTime = now;
                    }
                    catch (InvalidOperationException)
                    {
                        // Process exited between HasExited check and TotalProcessorTime read
                        _logger.Debug("[HangDetection] Process exited during poll, completing monitor");
                        observer.OnCompleted();
                    }
                });

            return subscription;
        });
    }
}
