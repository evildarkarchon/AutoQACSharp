using System.Diagnostics;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Process;
using FluentAssertions;
using NSubstitute;

namespace AutoQAC.Tests.Services;

/// <summary>
/// Real child-process tests for process timeout, graceful signaling, force kill, and PID cleanup behavior.
/// </summary>
public sealed class ProcessExecutionIntegrationTests : IDisposable
{
    private readonly TempPidStorePathProvider _pathProvider = new();
    private readonly JsonPidStore _pidStore;
    private readonly ProcessSessionIdProvider _sessionIdProvider = new();
    private readonly ProcessExecutionService _service;

    public ProcessExecutionIntegrationTests()
    {
        _pidStore = new JsonPidStore(_pathProvider, Substitute.For<ILoggingService>());
        _service = new ProcessExecutionService(Substitute.For<ILoggingService>(), _pidStore, _sessionIdProvider);
    }

    [Fact]
    public async Task ExecuteAsync_SleepHelperTimeout_ShouldForceKillAndRemovePidEntry()
    {
        Process? started = null;
        try
        {
            var result = await _service.ExecuteAsync(
                HelperStartInfo("sleep 30000"),
                timeout: TimeSpan.FromMilliseconds(250),
                onProcessStarted: p => started = p,
                pluginName: "Sleep.esp");

            result.TimedOut.Should().BeTrue();
            result.TerminationResult.Should().Be(TerminationResult.ForceKilled);
            (await _pidStore.LoadAsync()).Should().BeEmpty();
        }
        finally
        {
            KillIfRunning(started);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ExitOnStdinHelper_ShouldExitGracefullyWithoutForceKill()
    {
        Process? started = null;
        try
        {
            var startInfo = HelperStartInfo("exit-on-stdin");
            startInfo.RedirectStandardInput = true;
            startInfo.RedirectStandardOutput = true;

            var result = await _service.ExecuteAsync(
                startInfo,
                timeout: TimeSpan.FromSeconds(5),
                onProcessStarted: p =>
                {
                    started = p;
                    _ = Task.Run(async () =>
                    {
                        var ready = await p.StandardOutput.ReadLineAsync();
                        ready.Should().Be("READY");
                        await p.StandardInput.WriteLineAsync("stop");
                    });
                },
                pluginName: "Graceful.esp");

            result.ExitCode.Should().Be(0);
            result.TerminationResult.Should().BeNull();
            (await _pidStore.LoadAsync()).Should().BeEmpty();
        }
        finally
        {
            KillIfRunning(started);
        }
    }

    [Fact]
    public async Task ExecuteAsync_SpawnChildHelperTimeout_ShouldExerciseProcessTreeForceKill()
    {
        Process? started = null;
        try
        {
            var result = await _service.ExecuteAsync(
                HelperStartInfo("spawn-child 30000"),
                timeout: TimeSpan.FromMilliseconds(250),
                onProcessStarted: p => started = p,
                pluginName: "Tree.esp");

            result.TimedOut.Should().BeTrue();
            result.TerminationResult.Should().Be(TerminationResult.ForceKilled);
            (await _pidStore.LoadAsync()).Should().BeEmpty();
        }
        finally
        {
            KillIfRunning(started);
        }
    }

    /// <summary>
    /// Verifies a canceled post-kill wait is reported as force-kill failure instead of success.
    /// </summary>
    [Fact]
    public async Task TerminateProcessAsync_ForceKill_WhenPostKillWaitIsCanceled_ShouldReturnForceKillFailed()
    {
        var waitStrategy = new CancelingProcessExitWaiter();
        using var service = new ProcessExecutionService(
            Substitute.For<ILoggingService>(),
            _pidStore,
            _sessionIdProvider,
            waitStrategy);
        using var process = System.Diagnostics.Process.Start(HelperStartInfo("sleep 30000"));
        process.Should().NotBeNull("the helper process is required for force-kill validation");

        try
        {
            var result = await service.TerminateProcessAsync(process!, forceKill: true);

            process!.WaitForExit(2000).Should().BeTrue("force kill should be invoked before waiting for process exit");
            result.Should().Be(TerminationResult.ForceKillFailed);
            waitStrategy.Calls.Should().Be(1);
        }
        finally
        {
            KillIfRunning(process);
        }
    }

    /// <summary>
    /// Verifies user cancellation reports a grace-period expiration without force-killing the helper or removing PID evidence.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_UserCancellation_ShouldReturnGracePeriodExpiredKeepHelperRunningAndPreservePidEvidence()
    {
        var startedSignal = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var helperPid = 0;
        using var cts = new CancellationTokenSource();

        try
        {
            var executionTask = _service.ExecuteAsync(
                HelperStartInfo("sleep 30000"),
                timeout: TimeSpan.FromSeconds(30),
                ct: cts.Token,
                onProcessStarted: p => startedSignal.TrySetResult(p.Id),
                pluginName: "UserStop.esp");

            helperPid = await startedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await cts.CancelAsync();

            var result = await executionTask.WaitAsync(TimeSpan.FromSeconds(5));

            result.TimedOut.Should().BeFalse("a user stop is not the timeout path");
            result.TerminationResult.Should().Be(TerminationResult.GracePeriodExpired);

            using var helper = Process.GetProcessById(helperPid);
            helper.HasExited.Should().BeFalse("user cancellation must leave the process for caller confirmation/manual follow-up");

            var tracked = await _pidStore.LoadAsync();
            tracked.Should().ContainSingle(entry =>
                entry.Pid == helperPid &&
                entry.PluginName == "UserStop.esp" &&
                entry.SessionId == _sessionIdProvider.CurrentSessionId);
        }
        finally
        {
            KillIfRunning(helperPid);
        }
    }

    public void Dispose()
    {
        _service.Dispose();
        _pathProvider.Dispose();
    }

    private static ProcessStartInfo HelperStartInfo(string arguments)
    {
        var helperPath = Path.Combine(AppContext.BaseDirectory, "AutoQAC.TestProcessHelper.exe");
        File.Exists(helperPath).Should().BeTrue("dotnet test should build and copy the helper executable");
        return new ProcessStartInfo
        {
            FileName = helperPath,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }

    private static void KillIfRunning(Process? process)
    {
        if (process is null)
        {
            return;
        }

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
            // Best-effort cleanup prevents leaked helper processes from affecting later tests.
        }
        finally
        {
            process.Dispose();
        }
    }

    /// <summary>
    /// Best-effort cleanup for a helper whose original <see cref="Process"/> instance may have been disposed by the service.
    /// </summary>
    private static void KillIfRunning(int pid)
    {
        if (pid <= 0)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            KillIfRunning(process);
        }
        catch (ArgumentException)
        {
            // Process already exited before cleanup could attach to it.
        }
        catch (InvalidOperationException)
        {
            // Process exited while cleanup was attaching to it.
        }
    }

    private sealed class TempPidStorePathProvider : IPidStorePathProvider, IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public string PidFilePath => Path.Combine(_directory, "autoqac-pids.json");

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }

    private sealed class CancelingProcessExitWaiter : IProcessExitWaiter
    {
        public int Calls { get; private set; }

        /// <summary>
        /// Simulates cancellation after force kill has been invoked but before the exit wait reports completion.
        /// </summary>
        public Task WaitForExitAsync(System.Diagnostics.Process process, CancellationToken ct)
        {
            Calls++;
            throw new OperationCanceledException(ct);
        }
    }
}
