using System.Reactive.Linq;
using System.Reactive.Subjects;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.State;
using FluentAssertions;
using NSubstitute;

namespace AutoQAC.Tests.Services;

public sealed class ConfigWatcherServiceTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly string _configPath;

    public ConfigWatcherServiceTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "AutoQACConfigWatcherTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testDirectory);
        _configPath = Path.Combine(_testDirectory, "AutoQAC Settings.yaml");
        File.WriteAllText(_configPath, "Selected_Game: SkyrimSe");
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors.
            }
        }
    }

    [Fact]
    public async Task StartWatching_ShouldReload_WhenExternalValidChangeDetected()
    {
        // Arrange
        var configService = Substitute.For<IConfigurationService>();
        var stateService = Substitute.For<IStateService>();
        var logger = Substitute.For<ILoggingService>();
        var reloadCount = 0;

        stateService.CurrentState.Returns(new AppState { IsCleaning = false });
        stateService.StateChanged.Returns(Observable.Empty<AppState>());
        configService.GetLastWrittenHash().Returns((string?)null);
        configService.ReloadFromDiskAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref reloadCount);
                return Task.CompletedTask;
            });

        using var watcher = new ConfigWatcherService(configService, stateService, logger, _testDirectory);
        watcher.StartWatching();

        // Act
        File.WriteAllText(_configPath, "Selected_Game: Fallout4");

        // Assert
        await WaitForConditionAsync(() => Volatile.Read(ref reloadCount) > 0);
        reloadCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task StartWatching_ShouldSkipReload_WhenHashMatchesAppWrite()
    {
        // Arrange
        var configService = Substitute.For<IConfigurationService>();
        var stateService = Substitute.For<IStateService>();
        var logger = Substitute.For<ILoggingService>();
        var reloadCount = 0;
        var content = File.ReadAllText(_configPath);
        var currentHash = ComputeFileHash(_configPath);

        stateService.CurrentState.Returns(new AppState { IsCleaning = false });
        stateService.StateChanged.Returns(Observable.Empty<AppState>());
        configService.GetLastWrittenHash().Returns(currentHash);
        configService.ReloadFromDiskAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref reloadCount);
                return Task.CompletedTask;
            });

        using var watcher = new ConfigWatcherService(configService, stateService, logger, _testDirectory);
        watcher.StartWatching();

        // Act
        File.WriteAllText(_configPath, content);
        await Task.Delay(1200);

        // Assert
        reloadCount.Should().Be(0);
    }

    [Fact]
    public async Task StartWatching_ShouldDeferReloadDuringCleaning_AndApplyAfterCleaningEnds()
    {
        // Arrange
        var configService = Substitute.For<IConfigurationService>();
        var stateService = Substitute.For<IStateService>();
        var logger = Substitute.For<ILoggingService>();
        var reloadCount = 0;
        var stateSubject = new Subject<AppState>();
        var currentState = new AppState { IsCleaning = true };

        stateService.CurrentState.Returns(_ => currentState);
        stateService.StateChanged.Returns(stateSubject);
        configService.GetLastWrittenHash().Returns((string?)null);
        configService.ReloadFromDiskAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref reloadCount);
                return Task.CompletedTask;
            });

        using var watcher = new ConfigWatcherService(configService, stateService, logger, _testDirectory);
        watcher.StartWatching();

        // Act
        File.WriteAllText(_configPath, "Selected_Game: FalloutNewVegas");
        await Task.Delay(1200);

        // Assert deferred while cleaning
        reloadCount.Should().Be(0);

        // End cleaning and emit state transition.
        currentState = currentState with { IsCleaning = false };
        stateSubject.OnNext(currentState);

        await WaitForConditionAsync(() => Volatile.Read(ref reloadCount) > 0);
        reloadCount.Should().Be(1);
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, int timeoutMs = 8000)
    {
        var stopAt = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < stopAt)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("Condition was not met before timeout.");
    }

    private static string ComputeFileHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = System.Security.Cryptography.SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }
}
