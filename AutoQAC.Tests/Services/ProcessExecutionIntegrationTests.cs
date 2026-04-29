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
}
