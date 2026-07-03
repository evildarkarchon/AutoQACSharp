using AutoQAC.Infrastructure;
using AutoQAC.Models;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameCapability;
using AutoQAC.Services.Plugin;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AutoQAC.Tests.Integration;

/// <summary>
/// Integration tests for game selection and Game capability.
/// </summary>
public sealed class GameSelectionIntegrationTests
{
    [Fact]
    public void GameCapabilityProvider_ShouldReportCorrectAutomaticDiscoverySupport()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddInfrastructure();
        services.AddConfiguration();
        services.AddBusinessLogic();
        var provider = services.BuildServiceProvider();

        var gameCapabilityProvider = provider.GetRequiredService<IGameCapabilityProvider>();

        // Act & Assert - automatic discovery games
        gameCapabilityProvider.Get(GameType.SkyrimSe).SupportsAutomaticPluginDiscovery.Should().BeTrue();
        gameCapabilityProvider.Get(GameType.SkyrimLe).SupportsAutomaticPluginDiscovery.Should().BeTrue();
        gameCapabilityProvider.Get(GameType.SkyrimVr).SupportsAutomaticPluginDiscovery.Should().BeTrue();
        gameCapabilityProvider.Get(GameType.Fallout4).SupportsAutomaticPluginDiscovery.Should().BeTrue();
        gameCapabilityProvider.Get(GameType.Fallout4Vr).SupportsAutomaticPluginDiscovery.Should().BeTrue();

        // File-load-order or unsupported games
        gameCapabilityProvider.Get(GameType.Fallout3).SupportsAutomaticPluginDiscovery.Should().BeFalse();
        gameCapabilityProvider.Get(GameType.FalloutNewVegas).SupportsAutomaticPluginDiscovery.Should().BeFalse();
        gameCapabilityProvider.Get(GameType.Oblivion).SupportsAutomaticPluginDiscovery.Should().BeFalse();
        gameCapabilityProvider.Get(GameType.Unknown).SupportsAutomaticPluginDiscovery.Should().BeFalse();
    }

    [Fact]
    public void GameCapabilityProvider_ShouldReturnAllAvailableGames()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddInfrastructure();
        services.AddConfiguration();
        services.AddBusinessLogic();
        var provider = services.BuildServiceProvider();

        var gameCapabilityProvider = provider.GetRequiredService<IGameCapabilityProvider>();

        // Act
        var availableGames = gameCapabilityProvider.GetAvailableGames();

        // Assert
        availableGames.Should().NotBeEmpty();
        availableGames.Should().NotContain(GameType.Unknown);
        availableGames.Should().Contain(GameType.SkyrimSe);
        availableGames.Should().Contain(GameType.Fallout4);
        availableGames.Should().Contain(GameType.Fallout3);
        availableGames.Should().Contain(GameType.FalloutNewVegas);
    }

    [Fact]
    public async Task ConfigurationService_ShouldPersistAndLoadSelectedGame()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"AutoQAC_Test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var services = new ServiceCollection();
            services.AddInfrastructure();

            // Use temp directory for config
            services.AddSingleton<IConfigurationService>(sp =>
                new ConfigurationService(
                    sp.GetRequiredService<Infrastructure.Logging.ILoggingService>(),
                    tempDir));

            var provider = services.BuildServiceProvider();
            var configService = provider.GetRequiredService<IConfigurationService>();

            // Act - Set game
            await configService.SetSelectedGameAsync(GameType.SkyrimSe);
            // Flush debounced save to disk before verifying with a second instance
            await configService.FlushPendingSavesAsync();

            // Create new instance to verify persistence
            var configService2 = new ConfigurationService(
                provider.GetRequiredService<Infrastructure.Logging.ILoggingService>(),
                tempDir);

            var loadedGame = await configService2.GetSelectedGameAsync();

            // Assert
            loadedGame.Should().Be(GameType.SkyrimSe);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task PluginLoadingService_ShouldLoadPluginsFromFile()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"AutoQAC_Test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var loadOrderPath = Path.Combine(tempDir, "plugins.txt");
            await File.WriteAllLinesAsync(loadOrderPath, [
                "# Comment line",
                "*Skyrim.esm",
                "*Update.esm",
                "TestMod.esp"
            ]);

            var services = new ServiceCollection();
            services.AddInfrastructure();
            services.AddConfiguration();
            services.AddBusinessLogic();
            var provider = services.BuildServiceProvider();

            var pluginLoadingService = provider.GetRequiredService<IPluginLoadingService>();

            // Act
            var plugins = await pluginLoadingService.GetPluginsFromFileAsync(loadOrderPath);

            // Assert
            plugins.Should().HaveCount(3);
            plugins[0].FileName.Should().Be("Skyrim.esm");
            plugins[1].FileName.Should().Be("Update.esm");
            plugins[2].FileName.Should().Be("TestMod.esp");
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
