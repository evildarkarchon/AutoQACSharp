using AutoQAC.Models;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameCapability;
using AutoQAC.Services.MO2;
using AutoQAC.Services.Plugin;
using FluentAssertions;
using NSubstitute;

namespace AutoQAC.Tests.Services;

public sealed class PluginRefreshDiscoveryPlannerFreshnessTests
{
    [Fact]
    public async Task CheckFreshnessAsync_ReturnsFresh_WhenCurrentContextAndConfigurationMatchAcceptedToken()
    {
        var userConfig = CreateUserConfig();
        var gameDataFolderOverride = @"C:\Overrides\Skyrim\Data";
        var sut = CreateSut(userConfig, () => gameDataFolderOverride);
        var plan = CreateDirectPlan();
        var accepted = await sut.CreateFreshnessTokenAsync(plan);

        var freshness = await sut.CheckFreshnessAsync(accepted, CreateMatchingContext(plan));

        freshness.Should().Be(PluginRefreshFreshness.Fresh);
    }

    [Fact]
    public async Task CheckFreshnessAsync_ReturnsSelectedGameChanged_WhenCurrentGameChanges()
    {
        var userConfig = CreateUserConfig();
        var gameDataFolderOverride = @"C:\Overrides\Skyrim\Data";
        var sut = CreateSut(userConfig, () => gameDataFolderOverride);
        var plan = CreateDirectPlan();
        var accepted = await sut.CreateFreshnessTokenAsync(plan);

        var freshness = await sut.CheckFreshnessAsync(
            accepted,
            CreateMatchingContext(plan) with { CurrentGameType = GameType.Fallout4 });

        freshness.Should().Be(new PluginRefreshFreshness(false, PluginRefreshStalenessReason.SelectedGameChanged));
    }

    [Fact]
    public async Task CheckFreshnessAsync_ReturnsMo2ModeChanged_WhenMo2ModeChanges()
    {
        var userConfig = CreateUserConfig();
        var gameDataFolderOverride = @"C:\Overrides\Skyrim\Data";
        var sut = CreateSut(userConfig, () => gameDataFolderOverride);
        var plan = CreateDirectPlan();
        var accepted = await sut.CreateFreshnessTokenAsync(plan);

        var freshness = await sut.CheckFreshnessAsync(
            accepted,
            CreateMatchingContext(plan) with { Mo2ModeEnabled = true });

        freshness.Should().Be(new PluginRefreshFreshness(false, PluginRefreshStalenessReason.Mo2ModeChanged));
    }

    [Fact]
    public async Task CheckFreshnessAsync_ReturnsMo2ExecutablePathChanged_WhenMo2BinaryChanges()
    {
        var userConfig = CreateUserConfig(mo2Binary: @"C:\MO2\ModOrganizer.exe");
        var gameDataFolderOverride = @"C:\Overrides\Skyrim\Data";
        var sut = CreateSut(userConfig, () => gameDataFolderOverride);
        var plan = CreateMo2Plan(userConfig.ModOrganizer.Binary);
        var accepted = await sut.CreateFreshnessTokenAsync(plan);
        userConfig.ModOrganizer.Binary = @"D:\MO2\ModOrganizer.exe";

        var freshness = await sut.CheckFreshnessAsync(accepted, CreateMatchingContext(plan));

        freshness.Should().Be(new PluginRefreshFreshness(false, PluginRefreshStalenessReason.Mo2ExecutablePathChanged));
    }

    [Fact]
    public async Task CheckFreshnessAsync_ReturnsLoadOrderPathChanged_WhenDirectLoadOrderPathChanges()
    {
        var userConfig = CreateUserConfig();
        var gameDataFolderOverride = @"C:\Overrides\Skyrim\Data";
        var sut = CreateSut(userConfig, () => gameDataFolderOverride);
        var plan = CreateDirectPlan();
        var accepted = await sut.CreateFreshnessTokenAsync(plan);

        var freshness = await sut.CheckFreshnessAsync(
            accepted,
            CreateMatchingContext(plan) with { LoadOrderPath = @"C:\Skyrim\Profiles\Other\plugins.txt" });

        freshness.Should().Be(new PluginRefreshFreshness(false, PluginRefreshStalenessReason.LoadOrderPathChanged));
    }

    [Fact]
    public async Task CheckFreshnessAsync_ReturnsGameDataFolderOverrideChanged_WhenOverrideChanges()
    {
        var userConfig = CreateUserConfig();
        var gameDataFolderOverride = @"C:\Overrides\Skyrim\Data";
        var sut = CreateSut(userConfig, () => gameDataFolderOverride);
        var plan = CreateDirectPlan();
        var accepted = await sut.CreateFreshnessTokenAsync(plan);
        gameDataFolderOverride = @"C:\Overrides\Skyrim\OtherData";

        var freshness = await sut.CheckFreshnessAsync(accepted, CreateMatchingContext(plan));

        freshness.Should().Be(new PluginRefreshFreshness(false, PluginRefreshStalenessReason.GameDataFolderOverrideChanged));
    }

    [Fact]
    public async Task CheckFreshnessAsync_ReturnsMo2InstanceChanged_WhenMo2InstanceOverrideChanges()
    {
        var userConfig = CreateUserConfig(mo2InstanceOverride: @"C:\MO2\Skyrim");
        var gameDataFolderOverride = @"C:\Overrides\Skyrim\Data";
        var sut = CreateSut(userConfig, () => gameDataFolderOverride);
        var plan = CreateMo2Plan();
        var accepted = await sut.CreateFreshnessTokenAsync(plan);
        userConfig.Mo2InstanceOverrides[GameType.SkyrimSe.ToString()] = @"C:\MO2\SkyrimOther";

        var freshness = await sut.CheckFreshnessAsync(accepted, CreateMatchingContext(plan));

        freshness.Should().Be(new PluginRefreshFreshness(false, PluginRefreshStalenessReason.Mo2InstanceChanged));
    }

    [Fact]
    public async Task CheckFreshnessAsync_ReturnsMo2ProfileChanged_WhenMo2ProfileChanges()
    {
        var userConfig = CreateUserConfig(mo2InstanceOverride: @"C:\MO2\Skyrim");
        var gameDataFolderOverride = @"C:\Overrides\Skyrim\Data";
        var sut = CreateSut(userConfig, () => gameDataFolderOverride);
        var plan = CreateMo2Plan();
        var accepted = await sut.CreateFreshnessTokenAsync(plan);

        var freshness = await sut.CheckFreshnessAsync(
            accepted,
            CreateMatchingContext(plan) with { Mo2Profile = "Survival" });

        freshness.Should().Be(new PluginRefreshFreshness(false, PluginRefreshStalenessReason.Mo2ProfileChanged));
    }

    [Fact]
    public async Task CheckFreshnessAsync_ReturnsSkipListSettingsChanged_WhenDisableSkipListsChanges()
    {
        var userConfig = CreateUserConfig(disableSkipLists: false);
        var gameDataFolderOverride = @"C:\Overrides\Skyrim\Data";
        var sut = CreateSut(userConfig, () => gameDataFolderOverride);
        var plan = CreateDirectPlan();
        var accepted = await sut.CreateFreshnessTokenAsync(plan);
        userConfig.Settings.DisableSkipLists = true;

        var freshness = await sut.CheckFreshnessAsync(accepted, CreateMatchingContext(plan));

        freshness.Should().Be(new PluginRefreshFreshness(false, PluginRefreshStalenessReason.SkipListSettingsChanged));
    }

    [Fact]
    public async Task CheckFreshnessAsync_ReturnsSkipListSettingsChanged_WhenSkipListEntriesChange()
    {
        var userConfig = CreateUserConfig();
        var gameDataFolderOverride = @"C:\Overrides\Skyrim\Data";
        var sut = CreateSut(userConfig, () => gameDataFolderOverride);
        var plan = CreateDirectPlan();
        var accepted = await sut.CreateFreshnessTokenAsync(plan);
        userConfig.SkipLists["SkyrimSe"].Add("NewlySkipped.esp");

        var freshness = await sut.CheckFreshnessAsync(accepted, CreateMatchingContext(plan));

        freshness.Should().Be(new PluginRefreshFreshness(false, PluginRefreshStalenessReason.SkipListSettingsChanged));
    }

    private static PluginRefreshDiscoveryPlanner CreateSut(
        UserConfiguration userConfig,
        Func<string?> getGameDataFolderOverride)
    {
        var configurationService = Substitute.For<IConfigurationService>();
        configurationService.LoadUserConfigAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(userConfig));
        configurationService.GetGameDataFolderOverrideAsync(GameType.SkyrimSe, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(getGameDataFolderOverride()));

        return new PluginRefreshDiscoveryPlanner(
            configurationService,
            Substitute.For<IPluginLoadingService>(),
            Substitute.For<IMo2InstanceService>(),
            new GameCapabilityProvider());
    }

    private static UserConfiguration CreateUserConfig(
        bool disableSkipLists = false,
        string? mo2InstanceOverride = null,
        string? mo2Binary = @"C:\MO2\ModOrganizer.exe")
    {
        var userConfig = new UserConfiguration
        {
            ModOrganizer = new ModOrganizerConfig { Binary = mo2Binary },
            Settings = new AutoQacSettings { DisableSkipLists = disableSkipLists },
            SkipLists = new Dictionary<string, List<string>>
            {
                ["SkyrimSe"] = ["Completed.esp"],
                ["Universal"] = ["Universal.esp"]
            }
        };

        if (!string.IsNullOrWhiteSpace(mo2InstanceOverride))
        {
            userConfig.Mo2InstanceOverrides[GameType.SkyrimSe.ToString()] = mo2InstanceOverride;
        }

        return userConfig;
    }

    private static PluginRefreshDiscoveryPlan CreateDirectPlan() =>
        CreatePlan(
            PluginRefreshDiscoveryMode.DirectLoadOrderFile,
            mo2ModeEnabled: false,
            loadOrderPath: @"C:\Skyrim\Profiles\Default\plugins.txt",
            selectedProfile: null);

    private static PluginRefreshDiscoveryPlan CreateMo2Plan(string? mo2Path = @"C:\MO2\ModOrganizer.exe") =>
        CreatePlan(
            PluginRefreshDiscoveryMode.Mo2LoadOrderFile,
            mo2ModeEnabled: true,
            loadOrderPath: null,
            selectedProfile: "Default",
            mo2Path);

    private static PluginRefreshDiscoveryPlan CreatePlan(
        PluginRefreshDiscoveryMode mode,
        bool mo2ModeEnabled,
        string? loadOrderPath,
        string? selectedProfile,
        string? mo2Path = null)
    {
        var configuration = new PluginRefreshConfigurationProjection(
            LoadOrderPath: loadOrderPath,
            GameDataFolder: @"C:\Skyrim\Data",
            HasGameDataFolderOverride: true,
            XEditPath: null,
            Mo2Path: mo2ModeEnabled ? mo2Path : null,
            Mo2ModeEnabled: mo2ModeEnabled,
            Mo2InstancePath: mo2ModeEnabled ? @"C:\MO2\Skyrim" : null,
            IsMo2InstanceOverride: mo2ModeEnabled,
            IsMo2InstanceValid: mo2ModeEnabled,
            AvailableProfiles: mo2ModeEnabled ? ["Default", "Survival"] : [],
            SelectedProfile: selectedProfile,
            CleaningTimeout: 300);

        return new PluginRefreshDiscoveryPlan(
            GameType.SkyrimSe,
            mode,
            configuration,
            DisableSkipLists: false,
            CanAttemptIssueApproximation: true,
            DataFolderPath: @"C:\Skyrim\Data",
            LoadOrderPath: loadOrderPath,
            Mo2LoadOrderPath: mo2ModeEnabled ? @"C:\MO2\profiles\Default\loadorder.txt" : null,
            Mo2PathMap: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Mo2BaseDataFolder: mo2ModeEnabled ? @"C:\Skyrim\Data" : null);
    }

    private static PluginRefreshDiscoveryFreshnessContext CreateMatchingContext(PluginRefreshDiscoveryPlan plan) =>
        new(
            plan.GameType,
            plan.Configuration.Mo2ModeEnabled,
            plan.Configuration.LoadOrderPath,
            plan.Configuration.SelectedProfile);
}
