using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AutoQAC.Infrastructure;
using AutoQAC.Models;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.Plugin;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoQAC.Tests.Integration;

/// <summary>
/// Integration tests for the game selection feature with Mutagen support.
/// </summary>
public sealed class GameSelectionIntegrationTests
{
    [Fact]
    public void PluginLoadingService_ShouldReportCorrectMutagenSupport()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddInfrastructure();
        services.AddConfiguration();
        services.AddBusinessLogic();
        var provider = services.BuildServiceProvider();

        var pluginLoadingService = provider.GetRequiredService<IPluginLoadingService>();

        // Act & Assert - Mutagen supported games
        pluginLoadingService.IsGameSupportedByMutagen(GameType.SkyrimSE).Should().BeTrue();
        pluginLoadingService.IsGameSupportedByMutagen(GameType.SkyrimLE).Should().BeTrue();
        pluginLoadingService.IsGameSupportedByMutagen(GameType.SkyrimVR).Should().BeTrue();
        pluginLoadingService.IsGameSupportedByMutagen(GameType.Fallout4).Should().BeTrue();
        pluginLoadingService.IsGameSupportedByMutagen(GameType.Fallout4VR).Should().BeTrue();

        // Not supported by Mutagen
        pluginLoadingService.IsGameSupportedByMutagen(GameType.Fallout3).Should().BeFalse();
        pluginLoadingService.IsGameSupportedByMutagen(GameType.FalloutNewVegas).Should().BeFalse();
        pluginLoadingService.IsGameSupportedByMutagen(GameType.Oblivion).Should().BeFalse();
        pluginLoadingService.IsGameSupportedByMutagen(GameType.Unknown).Should().BeFalse();
    }

    [Fact]
    public void PluginLoadingService_ShouldReturnAllAvailableGames()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddInfrastructure();
        services.AddConfiguration();
        services.AddBusinessLogic();
        var provider = services.BuildServiceProvider();

        var pluginLoadingService = provider.GetRequiredService<IPluginLoadingService>();

        // Act
        var availableGames = pluginLoadingService.GetAvailableGames();

        // Assert
        availableGames.Should().NotBeEmpty();
        availableGames.Should().NotContain(GameType.Unknown);
        availableGames.Should().Contain(GameType.SkyrimSE);
        availableGames.Should().Contain(GameType.Fallout4);
        availableGames.Should().Contain(GameType.Fallout3);
        availableGames.Should().Contain(GameType.FalloutNewVegas);
    }

    [Fact]
    public async Task ConfigurationService_ShouldPersistAndLoadSelectedGame()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"AutoQAC_Test_{System.Guid.NewGuid()}");
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
            await configService.SetSelectedGameAsync(GameType.SkyrimSE);

            // Create new instance to verify persistence
            var configService2 = new ConfigurationService(
                provider.GetRequiredService<Infrastructure.Logging.ILoggingService>(),
                tempDir);

            var loadedGame = await configService2.GetSelectedGameAsync();

            // Assert
            loadedGame.Should().Be(GameType.SkyrimSE);
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
        var tempDir = Path.Combine(Path.GetTempPath(), $"AutoQAC_Test_{System.Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var loadOrderPath = Path.Combine(tempDir, "plugins.txt");
            await File.WriteAllLinesAsync(loadOrderPath, new[]
            {
                "# Comment line",
                "*Skyrim.esm",
                "*Update.esm",
                "TestMod.esp"
            });

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
