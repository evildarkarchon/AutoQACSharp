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
        await AssertValueRemainsAsync(() => Volatile.Read(ref reloadCount), expected: 0);

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
        await AssertValueRemainsAsync(() => Volatile.Read(ref reloadCount), expected: 0);

        // Assert deferred while cleaning
        reloadCount.Should().Be(0);

        // End cleaning and emit state transition.
        currentState = currentState with { IsCleaning = false };
        stateSubject.OnNext(currentState);

        await WaitForConditionAsync(() => Volatile.Read(ref reloadCount) > 0);
        reloadCount.Should().Be(1);
    }

    [Fact]
    public async Task StartWatching_ShouldContinueWatching_WhenReloadFromDiskThrows_OnSubsequentValidChange()
    {
        // Arrange
        var configService = Substitute.For<IConfigurationService>();
        var stateService = Substitute.For<IStateService>();
        var logger = Substitute.For<ILoggingService>();
        var reloadAttempts = 0;
        var successfulReloads = 0;

        stateService.CurrentState.Returns(new AppState { IsCleaning = false });
        stateService.StateChanged.Returns(Observable.Empty<AppState>());
        configService.GetLastWrittenHash().Returns((string?)null);
        configService.ReloadFromDiskAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var currentAttempt = Interlocked.Increment(ref reloadAttempts);
                if (currentAttempt == 1)
                {
                    throw new InvalidOperationException("Simulated reload failure");
                }

                Interlocked.Increment(ref successfulReloads);
                return Task.CompletedTask;
            });

        using var watcher = new ConfigWatcherService(configService, stateService, logger, _testDirectory);
        watcher.StartWatching();

        // Act - first change fails to reload
        File.WriteAllText(_configPath, "Selected_Game: Fallout4");
        await WaitForConditionAsync(() => Volatile.Read(ref reloadAttempts) >= 1);

        // Act - second change should still be processed successfully
        File.WriteAllText(_configPath, "Selected_Game: SkyrimVr");
        await WaitForConditionAsync(() => Volatile.Read(ref successfulReloads) >= 1);

        // Assert
        reloadAttempts.Should().BeGreaterThanOrEqualTo(2, "watcher should keep handling changes after one failed reload");
        successfulReloads.Should().BeGreaterThan(0, "a subsequent valid change should reload successfully");
    }

    [Fact]
    public async Task StartWatching_ShouldApplyDeferredChangesOnlyOnce_WhenMultipleNonCleaningStateTransitionsOccur()
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

        // Trigger external change while cleaning -> defer
        File.WriteAllText(_configPath, "Selected_Game: FalloutNewVegas");
        await AssertValueRemainsAsync(() => Volatile.Read(ref reloadCount), expected: 0);
        reloadCount.Should().Be(0, "reload should be deferred while cleaning");

        // First cleaning end transition applies deferred change once
        currentState = currentState with { IsCleaning = false };
        stateSubject.OnNext(currentState);
        await WaitForConditionAsync(() => Volatile.Read(ref reloadCount) == 1);

        // Subsequent non-cleaning transition should not re-apply same deferred change
        currentState = currentState with { IsCleaning = true };
        stateSubject.OnNext(currentState);
        currentState = currentState with { IsCleaning = false };
        stateSubject.OnNext(currentState);
        await AssertValueRemainsAsync(() => Volatile.Read(ref reloadCount), expected: 1, durationMs: 900);

        reloadCount.Should().Be(1, "deferred change flag should be consumed after first apply");
    }

    private static async Task AssertValueRemainsAsync(
        Func<int> valueProvider,
        int expected,
        int durationMs = 1200,
        int pollMs = 50)
    {
        var stopAt = DateTime.UtcNow.AddMilliseconds(durationMs);
        while (DateTime.UtcNow < stopAt)
        {
            valueProvider().Should().Be(expected);
            await Task.Delay(pollMs);
        }
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
