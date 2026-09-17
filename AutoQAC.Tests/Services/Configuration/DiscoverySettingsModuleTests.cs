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
    /// <summary>A queued in-memory save must not be mistaken for durable acceptance.</summary>
    [Fact]
    public async Task ExecuteAsync_WhenDiskWriteFails_DoesNotAcceptOrRefresh()
    {
        var config = CreateConfiguration();
        config.LoadUserConfigAsync(Arg.Any<CancellationToken>()).Returns(new UserConfiguration());
        config.FlushPendingSavesAsync(Arg.Any<CancellationToken>()).Returns(new ConfigPersistenceResult(
            ConfigPersistenceStatusKind.Failed, ConfigPersistenceOperationKind.Flush, 1,
            new ConfigPersistenceFailure(ConfigPersistenceOperationKind.Flush,
                ConfigPersistenceFailureKind.WriteFailed, "Could not save settings.", null, 1)));
        using var state = new StateService();
        using var refresh = new RecordingPluginRefreshModule();
        using var sut = new DiscoverySettingsModule(config, state, refresh);

        var result = await sut.ExecuteAsync(new DiscoverySettingsIntent.SetMo2Mode(true));

        result.Status.ToString().Should().Be("SaveFailed");
        refresh.Intents.OfType<PluginRefreshIntent.RefreshGame>().Should().BeEmpty();
    }

    /// <summary>Programmatic callers receive the same cleaning exclusion as the UI.</summary>
    [Fact]
    public async Task ExecuteAsync_DuringCleaning_RejectsWithoutSaving()
    {
        var config = CreateConfiguration();
        config.LoadUserConfigAsync(Arg.Any<CancellationToken>()).Returns(new UserConfiguration());
        using var state = new StateService();
        state.UpdateState(s => s with { IsCleaning = true });
        using var refresh = new RecordingPluginRefreshModule();
        using var sut = new DiscoverySettingsModule(config, state, refresh);

        var result = await sut.ExecuteAsync(new DiscoverySettingsIntent.SetMo2Mode(true));

        result.Status.Should().Be(DiscoverySettingsChangeStatus.Rejected);
        await config.DidNotReceive().SaveUserConfigAsync(Arg.Any<UserConfiguration>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_SelectGame_ShouldPersistSelectedGameAndRefreshPlugins()
    {
        var config = CreateConfiguration();
        using var state = new StateService();
        using var refresh = new RecordingPluginRefreshModule();
        var expectedSnapshot = RecordingPluginRefreshModule.CreateSnapshot(
            GameType.SkyrimSe,
            statusText: "Loaded SkyrimSe");
        refresh.ExecuteHandler = (_, _) => Task.FromResult(expectedSnapshot);
        using var sut = new DiscoverySettingsModule(config, state, refresh);

        var result = await sut.ExecuteAsync(new DiscoverySettingsIntent.SelectGame(GameType.SkyrimSe));

        (await config.LoadUserConfigAsync()).SelectedGame.Should().Be(nameof(GameType.SkyrimSe));
        refresh.Intents.Should().ContainSingle()
            .Which.Should().Be(new PluginRefreshIntent.RefreshGame(GameType.SkyrimSe));
        result.Status.Should().Be(DiscoverySettingsChangeStatus.Accepted);
        result.Snapshot.Should().BeSameAs(expectedSnapshot);
    }

    [Fact]
    public async Task ExecuteAsync_SetMo2Mode_ShouldPersistSettingAndRefreshCurrentGame()
    {
        var config = CreateConfiguration();
        using var state = new StateService();
        state.UpdateState(current => current with { CurrentGameType = GameType.Fallout4 });
        using var refresh = new RecordingPluginRefreshModule();
        var userConfig = new UserConfiguration { SelectedGame = nameof(GameType.Fallout4) };
        UserConfiguration? savedConfig = null;
        await config.SaveUserConfigAsync(userConfig);
        config.When(c => c.SaveUserConfigAsync(Arg.Any<UserConfiguration>(), Arg.Any<CancellationToken>()))
            .Do(call => savedConfig = call.Arg<UserConfiguration>().Copy());
        using var sut = new DiscoverySettingsModule(config, state, refresh);

        var result = await sut.ExecuteAsync(new DiscoverySettingsIntent.SetMo2Mode(true));

        savedConfig.Should().NotBeNull();
        savedConfig!.Settings.Mo2Mode.Should().BeTrue();
        refresh.Intents.Should().ContainSingle()
            .Which.Should().Be(new PluginRefreshIntent.RefreshGame(GameType.Fallout4));
        result.Status.Should().Be(DiscoverySettingsChangeStatus.Accepted);
    }

    [Fact]
    public async Task ExecuteAsync_SetLoadOrderPath_ShouldPersistAndFlushBeforeDiscovery()
    {
        var loadOrderPath = Path.Combine(Path.GetTempPath(), $"AutoQAC-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(loadOrderPath, string.Empty);
        var config = CreateConfiguration();
        await config.SaveUserConfigAsync(new UserConfiguration { SelectedGame = nameof(GameType.FalloutNewVegas) });
        using var state = new StateService();
        using var refresh = new RecordingPluginRefreshModule();
        var order = new List<string>();
        refresh.ExecuteHandler = (intent, _) =>
        {
            intent.Should().Be(new PluginRefreshIntent.RefreshGame(GameType.FalloutNewVegas));
            order.Add("refresh");
            return Task.FromResult(refresh.CurrentSnapshot);
        };
        config.FlushPendingSavesAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                order.Add("persist");
                return Task.FromResult(new ConfigPersistenceResult(ConfigPersistenceStatusKind.Success, ConfigPersistenceOperationKind.Flush, 1, null));
            });
        using var sut = new DiscoverySettingsModule(config, state, refresh);

        try
        {
            var result = await sut.ExecuteAsync(
                new DiscoverySettingsIntent.SetLoadOrderPath(GameType.FalloutNewVegas, loadOrderPath));

            result.Status.Should().Be(DiscoverySettingsChangeStatus.Accepted);
            order.Should().Equal("persist", "refresh");
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
        var config = CreateConfiguration();
        using var state = new StateService();
        using var refresh = new RecordingPluginRefreshModule();
        using var sut = new DiscoverySettingsModule(config, state, refresh);

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
        var config = CreateConfiguration();
        var defaultConfig = new UserConfiguration
        {
            ModOrganizer = new ModOrganizerConfig { Binary = "default-mo2.exe" },
            XEdit = new XEditConfig { Binary = "default-xedit.exe" },
            Settings = new AutoQacSettings { Mo2Mode = false, CleaningTimeout = 300 }
        };
        await config.SaveUserConfigAsync(defaultConfig);
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
        using var sut = new DiscoverySettingsModule(config, state, refresh);

        var result = await sut.ExecuteAsync(new DiscoverySettingsIntent.Reset());

        (await config.LoadUserConfigAsync()).SelectedGame.Should().Be("Unknown");
        refresh.Intents.Should().Equal(
            new PluginRefreshIntent.RefreshGame(GameType.Unknown));
        state.CurrentState.PluginsToClean.Should().BeEmpty();
        state.CurrentState.Mo2ExecutablePath.Should().BeNull();
        state.CurrentState.XEditExecutablePath.Should().BeNull();
        result.Status.Should().Be(DiscoverySettingsChangeStatus.Accepted);
    }
    /// <summary>Models the latest active values and a successful disk barrier without mocking mutation behavior.</summary>
    private static IConfigurationService CreateConfiguration()
    {
        var config = Substitute.For<IConfigurationService>();
        var active = new UserConfiguration();
        config.LoadUserConfigAsync(Arg.Any<CancellationToken>()).Returns(_ => active.Copy());
        config.SaveUserConfigAsync(Arg.Any<UserConfiguration>(), Arg.Any<CancellationToken>())
            .Returns(call => { active = call.Arg<UserConfiguration>().Copy(); return Task.CompletedTask; });
        config.FlushPendingSavesAsync(Arg.Any<CancellationToken>()).Returns(new ConfigPersistenceResult(
            ConfigPersistenceStatusKind.Success, ConfigPersistenceOperationKind.Flush, 1, null));
        return config;
    }
}
