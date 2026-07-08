using AutoQAC.Models;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;
using AutoQAC.Tests.TestInfrastructure;
using FluentAssertions;
using NSubstitute;

namespace AutoQAC.Tests.Services.Configuration;

public sealed class DiscoverySettingsModuleTests
{
    [Fact]
    public async Task ExecuteAsync_SelectGame_ShouldPersistSelectedGameAndRefreshPlugins()
    {
        var config = Substitute.For<IConfigurationService>();
        using var state = new StateService();
        using var refresh = new RecordingPluginRefreshModule();
        var expectedSnapshot = RecordingPluginRefreshModule.CreateSnapshot(
            GameType.SkyrimSe,
            statusText: "Loaded SkyrimSe");
        refresh.ExecuteHandler = (_, _) => Task.FromResult(expectedSnapshot);
        var sut = new DiscoverySettingsModule(config, state, refresh);

        var result = await sut.ExecuteAsync(new DiscoverySettingsIntent.SelectGame(GameType.SkyrimSe));

        await config.Received(1).SetSelectedGameAsync(GameType.SkyrimSe, Arg.Any<CancellationToken>());
        refresh.Intents.Should().ContainSingle()
            .Which.Should().Be(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));
        result.Status.Should().Be(DiscoverySettingsChangeStatus.Accepted);
        result.Snapshot.Should().BeSameAs(expectedSnapshot);
    }

    [Fact]
    public async Task ExecuteAsync_SetMo2Mode_ShouldPersistSettingAndRefreshCurrentGame()
    {
        var config = Substitute.For<IConfigurationService>();
        using var state = new StateService();
        state.UpdateState(current => current with { CurrentGameType = GameType.Fallout4 });
        using var refresh = new RecordingPluginRefreshModule();
        var userConfig = new UserConfiguration();
        UserConfiguration? savedConfig = null;
        config.LoadUserConfigAsync(Arg.Any<CancellationToken>()).Returns(userConfig);
        config.SaveUserConfigAsync(
                Arg.Do<UserConfiguration>(value => savedConfig = value.Copy()),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var sut = new DiscoverySettingsModule(config, state, refresh);

        var result = await sut.ExecuteAsync(new DiscoverySettingsIntent.SetMo2Mode(true));

        savedConfig.Should().NotBeNull();
        savedConfig!.Settings.Mo2Mode.Should().BeTrue();
        refresh.Intents.Should().ContainSingle()
            .Which.Should().Be(new PluginRefreshIntent.RefreshGame(GameType.Fallout4));
        result.Status.Should().Be(DiscoverySettingsChangeStatus.Accepted);
    }

    [Fact]
    public async Task ExecuteAsync_SetLoadOrderPath_ShouldRefreshWithSelectedPathBeforePersistingOverride()
    {
        var loadOrderPath = Path.Combine(Path.GetTempPath(), $"AutoQAC-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(loadOrderPath, string.Empty);
        var config = Substitute.For<IConfigurationService>();
        using var state = new StateService();
        using var refresh = new RecordingPluginRefreshModule();
        var order = new List<string>();
        refresh.ExecuteHandler = (intent, _) =>
        {
            intent.Should().Be(new PluginRefreshIntent.RefreshGame(GameType.FalloutNewVegas, loadOrderPath));
            order.Add("refresh");
            return Task.FromResult(refresh.CurrentSnapshot);
        };
        config.SetGameLoadOrderOverrideAsync(GameType.FalloutNewVegas, loadOrderPath, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                order.Add("persist");
                return Task.CompletedTask;
            });
        var sut = new DiscoverySettingsModule(config, state, refresh);

        try
        {
            var result = await sut.ExecuteAsync(
                new DiscoverySettingsIntent.SetLoadOrderPath(GameType.FalloutNewVegas, loadOrderPath));

            result.Status.Should().Be(DiscoverySettingsChangeStatus.Accepted);
            order.Should().Equal("refresh", "persist");
        }
        finally
        {
            File.Delete(loadOrderPath);
        }
    }

    [Fact]
    public async Task ExecuteAsync_SetLoadOrderPath_WhenFileMissing_ShouldRejectWithoutSavingOrRefreshing()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"AutoQAC-missing-{Guid.NewGuid():N}.txt");
        var config = Substitute.For<IConfigurationService>();
        using var state = new StateService();
        using var refresh = new RecordingPluginRefreshModule();
        var sut = new DiscoverySettingsModule(config, state, refresh);

        var result = await sut.ExecuteAsync(
            new DiscoverySettingsIntent.SetLoadOrderPath(GameType.FalloutNewVegas, missingPath));

        result.Status.Should().Be(DiscoverySettingsChangeStatus.Rejected);
        result.Failure!.Kind.Should().Be(DiscoverySettingsChangeFailureKind.InvalidLoadOrderPath);
        refresh.Intents.Should().BeEmpty();
        await config.DidNotReceive().SetGameLoadOrderOverrideAsync(
            Arg.Any<GameType>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_Reset_ShouldCancelActiveRefreshResetConfigClearStateAndPublishNoGame()
    {
        var config = Substitute.For<IConfigurationService>();
        var defaultConfig = new UserConfiguration
        {
            ModOrganizer = new ModOrganizerConfig { Binary = "default-mo2.exe" },
            XEdit = new XEditConfig { Binary = "default-xedit.exe" },
            Settings = new AutoQacSettings { Mo2Mode = false, CleaningTimeout = 300 }
        };
        config.LoadUserConfigAsync(Arg.Any<CancellationToken>()).Returns(defaultConfig);
        using var state = new StateService();
        state.UpdateState(current => current with
        {
            CurrentGameType = GameType.SkyrimSe,
            PluginsToClean =
            [
                new PluginInfo
                {
                    FileName = "Example.esp",
                    FullPath = @"C:\Game\Data\Example.esp",
                    DetectedGameType = GameType.SkyrimSe
                }
            ]
        });
        using var refresh = new RecordingPluginRefreshModule();
        var sut = new DiscoverySettingsModule(config, state, refresh);

        var result = await sut.ExecuteAsync(new DiscoverySettingsIntent.Reset());

        await config.Received(1).ResetToDefaultsAsync(Arg.Any<CancellationToken>());
        refresh.Intents.Should().Equal(
            new PluginRefreshIntent.Cancel(PluginRefreshCancelReason.Reset),
            new PluginRefreshIntent.RefreshGame(GameType.Unknown));
        state.CurrentState.PluginsToClean.Should().BeEmpty();
        state.CurrentState.Mo2ExecutablePath.Should().Be(defaultConfig.ModOrganizer.Binary);
        state.CurrentState.XEditExecutablePath.Should().Be(defaultConfig.XEdit.Binary);
        result.Status.Should().Be(DiscoverySettingsChangeStatus.Accepted);
    }
}
