using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.Configuration;
using FluentAssertions;
using NSubstitute;

namespace AutoQAC.Tests.Services;

public sealed class LogRetentionServicePathTests : IDisposable
{
    private readonly IConfigurationService _configService;
    private readonly ILoggingService _logger;
    private readonly string _simulatedCurrentDirectoryRoot;
    private readonly string _logDirectory;

    public LogRetentionServicePathTests()
    {
        _configService = Substitute.For<IConfigurationService>();
        _logger = Substitute.For<ILoggingService>();

        _simulatedCurrentDirectoryRoot = Path.Combine(Path.GetTempPath(), $"autoqac_logretention_cwd_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_simulatedCurrentDirectoryRoot);

        _logDirectory = Path.Combine(Path.GetTempPath(), $"autoqac_logretention_logs_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_logDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_logDirectory))
        {
            Directory.Delete(_logDirectory, recursive: true);
        }

        if (Directory.Exists(_simulatedCurrentDirectoryRoot))
        {
            Directory.Delete(_simulatedCurrentDirectoryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CleanupAsync_UsesConfiguredLogDirectory_InsteadOfCurrentWorkingDirectory()
    {
        var sut = new LogRetentionService(_configService, _logger, _logDirectory);
        _configService.LoadUserConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new UserConfiguration
            {
                LogRetention = new RetentionSettings
                {
                    Mode = RetentionMode.AgeBased,
                    MaxAgeDays = 7
                }
            });

        var recentConfiguredLog = CreateLogFile(_logDirectory, "autoqac-recent.log", DateTime.UtcNow.AddDays(-1));
        var oldConfiguredLog = CreateLogFile(_logDirectory, "autoqac-old.log", DateTime.UtcNow.AddDays(-10));

        var currentWorkingDirectoryLogs = Path.Combine(_simulatedCurrentDirectoryRoot, "logs");
        var recentCwdLog = CreateLogFile(currentWorkingDirectoryLogs, "autoqac-cwd-recent.log", DateTime.UtcNow.AddDays(-1));
        var oldCwdLog = CreateLogFile(currentWorkingDirectoryLogs, "autoqac-cwd-old.log", DateTime.UtcNow.AddDays(-10));

        await sut.CleanupAsync();

        File.Exists(recentConfiguredLog).Should().BeTrue();
        File.Exists(oldConfiguredLog).Should().BeFalse();
        File.Exists(recentCwdLog).Should().BeTrue();
        File.Exists(oldCwdLog).Should().BeTrue();
    }

    [Fact]
    public void GetLogDirectory_ReturnsLogsDirectoryUnderAppBaseDirectory()
    {
        var expected = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "logs"));

        LogFilePaths.GetLogDirectory().Should().Be(expected);
    }

    private static string CreateLogFile(string directory, string fileName, DateTime lastWriteUtc)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, fileName);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }
}
