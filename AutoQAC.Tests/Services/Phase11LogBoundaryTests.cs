using System.Diagnostics;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Process;
using AutoQAC.Tests.Helpers;
using FluentAssertions;
using NSubstitute;

namespace AutoQAC.Tests.Services;

/// <summary>
/// Phase 11 regression guards for process and startup diagnostic log disclosure boundaries.
/// </summary>
public sealed class Phase11LogBoundaryTests
{
    /// <summary>
    /// Captures ProcessExecutionService logger calls and verifies raw executable/argv payloads are not logged.
    /// </summary>
    [Fact]
    public async Task ProcessExecutionService_WhenStartFails_ShouldLogSafeFieldsWithoutSharedSentinels()
    {
        // Arrange
        var logger = Substitute.For<ILoggingService>();
        var service = new ProcessExecutionService(
            logger,
            new InMemoryPidStore(),
            new StaticProcessSessionIdProvider());
        var startInfo = new ProcessStartInfo
        {
            FileName = @"C:\Users\Alice\Tools\SSEEdit.exe",
            WorkingDirectory = @"C:\Games\Skyrim Special Edition"
        };
        startInfo.ArgumentList.Add("-QAC");
        startInfo.ArgumentList.Add("-autoload");
        startInfo.ArgumentList.Add(@"C:\ModOrganizer\ModOrganizer.exe");

        // Act
        var result = await service.ExecuteAsync(startInfo);

        // Assert
        result.ExitCode.Should().Be(-1);
        logger.Received(1).Debug(
            "Starting external process for {Operation}: status={Status}, argumentCount={ArgumentCount}",
            "ExternalProcess",
            "Starting",
            3);
        logger.Received(1).Error(
            Arg.Any<Exception>(),
            "Failed to start external process for {Operation}: status={Status}, reason={Reason}, argumentCount={ArgumentCount}",
            "ExternalProcess",
            "Failed",
            "StartFailed",
            3);

        var capturedText = CaptureNonExceptionLogArguments(logger);
        foreach (var sentinel in DiagnosticSentinels.UnsafeDiagnosticSentinels)
        {
            capturedText.Should().NotContain(sentinel);
        }
    }

    /// <summary>
    /// Successful legacy-Arguments starts must not leak raw launch text into logs or PID tracking entries.
    /// </summary>
    [Fact]
    public async Task ProcessExecutionService_WhenLegacyArgumentsStartSucceeds_ShouldTrackAndLogSafeExternalProcessLabel()
    {
        // Arrange
        var logger = Substitute.For<ILoggingService>();
        var pidStore = new InMemoryPidStore();
        var service = new ProcessExecutionService(
            logger,
            pidStore,
            new StaticProcessSessionIdProvider(),
            new RealProcessExitWaiter());
        IReadOnlyList<TrackedProcess> trackedAtStart = [];
        var startInfo = new ProcessStartInfo
        {
            FileName = DotNetHostPath,
            WorkingDirectory = Environment.CurrentDirectory,
            Arguments = "--info"
        };

        // Act
        var result = await service.ExecuteAsync(
            startInfo,
            onProcessStarted: _ => trackedAtStart = pidStore.LoadAsync().GetAwaiter().GetResult());

        // Assert
        result.ExitCode.Should().Be(0);
        trackedAtStart.Should().ContainSingle();
        trackedAtStart[0].PluginName.Should().Be("ExternalProcess");

        var capturedText = CaptureNonExceptionLogArguments(logger);
        foreach (var sentinel in DiagnosticSentinels.UnsafeDiagnosticSentinels.Concat(["--info"]))
        {
            capturedText.Should().NotContain(sentinel);
            trackedAtStart[0].PluginName.Should().NotContain(sentinel);
        }
    }

    /// <summary>
    /// Guards known bad process/startup templates and raw argument logging patterns from returning to production source.
    /// </summary>
    [Fact]
    public void ProductionDiagnosticsSource_ShouldNotContainForbiddenPhase11Templates()
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();
        var sources = string.Join(Environment.NewLine, new[]
        {
            File.ReadAllText(Path.Combine(repositoryRoot, "AutoQAC", "Services", "Process", "ProcessExecutionService.cs")),
            File.ReadAllText(Path.Combine(repositoryRoot, "AutoQAC", "Services", "Cleaning", "CleaningService.cs")),
            File.ReadAllText(Path.Combine(repositoryRoot, "AutoQAC", "App.axaml.cs"))
        });
        var forbiddenTemplates = new[]
        {
            "Starting process: {FileName} {Arguments}",
            "Failed to start process: {FileName}",
            "xEdit Path: {XEditPath}",
            "Legacy config migration failed unexpectedly: {ex.Message}",
            "command.Arguments",
            "raw ArgumentList payload logging"
        };

        // Act / Assert
        sources.Should().Contain("DiagnosticTextFormatter.SafeFileIdentifier(\"xEdit Path\"");
        sources.Should().Contain("argumentCount={ArgumentCount}");
        foreach (var forbiddenTemplate in forbiddenTemplates)
        {
            sources.Should().NotContain(forbiddenTemplate);
        }
    }

    /// <summary>
    /// Captures log templates and structured argument values while excluding exception objects that local logs may render separately.
    /// </summary>
    private static string CaptureNonExceptionLogArguments(ILoggingService logger) => string.Join(
        Environment.NewLine,
        logger.ReceivedCalls()
            .SelectMany(call => call.GetArguments())
            .Where(argument => argument is not Exception)
            .Select(argument => argument?.ToString() ?? string.Empty));

    /// <summary>
    /// Finds the repository root from the test output directory so source guards work under dotnet test.
    /// </summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "AutoQAC")) &&
                Directory.Exists(Path.Combine(directory.FullName, "AutoQAC.Tests")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root for Phase 11 source guards.");
    }

    private sealed class StaticProcessSessionIdProvider : IProcessSessionIdProvider
    {
        public string CurrentSessionId => "phase-11-test";
    }

    private static string DotNetHostPath => OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";

    private sealed class RealProcessExitWaiter : IProcessExitWaiter
    {
        public Task WaitForExitAsync(Process process, CancellationToken ct) => process.WaitForExitAsync(ct);
    }

    private sealed class InMemoryPidStore : IPidStore
    {
        private IReadOnlyList<TrackedProcess> _entries = [];

        public Task<IReadOnlyList<TrackedProcess>> LoadAsync(CancellationToken ct = default) => Task.FromResult(_entries);

        public Task UpdateAsync(
            Func<IReadOnlyList<TrackedProcess>, IReadOnlyList<TrackedProcess>> update,
            CancellationToken ct = default)
        {
            _entries = update(_entries);
            return Task.CompletedTask;
        }
    }
}
