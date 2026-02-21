using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.Configuration;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AutoQAC.Tests.Services;

public sealed class AppShutdownDisposalTests : IDisposable
{
    private readonly string _testDirectory;

    public AppShutdownDisposalTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "AutoQACShutdownTests_" + Guid.NewGuid());
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
                // Ignore cleanup failures.
            }
        }
    }

    [Fact]
    public async Task ServiceProviderDispose_ShouldCascadeToConfigurationService_AndFlushPendingSave()
    {
        // Arrange
        var logger = Substitute.For<ILoggingService>();
        var services = new ServiceCollection();
        services.AddSingleton(logger);
        services.AddSingleton<IConfigurationService>(_ => new ConfigurationService(logger, _testDirectory));
        var provider = services.BuildServiceProvider();
        var configService = provider.GetRequiredService<IConfigurationService>();

        await configService.SaveUserConfigAsync(new UserConfiguration
        {
            Settings = new AutoQacSettings { CleaningTimeout = 777 }
        });

        // Act
        ((IDisposable)provider).Dispose();

        // Assert: disposed service should no longer accept calls
        await FluentActions.Awaiting(() => configService.LoadUserConfigAsync())
            .Should().ThrowAsync<ObjectDisposedException>();

        // Assert: pending save was flushed during disposal cascade
        using var reloadedService = new ConfigurationService(logger, _testDirectory);
        var reloaded = await reloadedService.LoadUserConfigAsync();
        reloaded.Settings.CleaningTimeout.Should().Be(777);
    }
}
