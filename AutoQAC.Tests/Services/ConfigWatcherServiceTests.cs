using AutoQAC.Infrastructure.Logging;
using AutoQAC.Services.Configuration;
using NSubstitute;

namespace AutoQAC.Tests.Services;

/// <summary>
/// Watcher tests are smoke-only after Phase 10 D-33. Race coverage lives in
/// <c>ConfigPersistenceCoordinatorTests</c>, where ordering is deterministic and does not depend on OS event timing.
/// </summary>
public sealed class ConfigWatcherServiceTests : IDisposable
{
    private readonly string _testDirectory;

    public ConfigWatcherServiceTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "AutoQACConfigWatcherTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testDirectory);
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
    public async Task FileSystemEvent_TriggersCoordinatorNotification_Smoke()
    {
        // Arrange
        var coordinator = Substitute.For<IConfigPersistenceCoordinator>();
        var logger = Substitute.For<ILoggingService>();
        using var watcher = new ConfigWatcherService(coordinator, logger, _testDirectory);
        watcher.StartWatching();

        // Act: a bounded wait is acceptable here because Phase 10 D-33 keeps only smoke coverage on FileSystemWatcher.
        var path = Path.Combine(_testDirectory, "AutoQAC Settings.yaml");
        await File.WriteAllTextAsync(path, "Selected_Game: Unknown\n");
        await Task.Delay(2000);

        // Assert
        coordinator.Received().NotifySettingsFileChanged(Arg.Any<ConfigFileSignalKind>());
    }
}
