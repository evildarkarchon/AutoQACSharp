using AutoQAC.Models;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameCapability;
using AutoQAC.Services.MO2;
using AutoQAC.Services.Plugin;
using FluentAssertions;
using NSubstitute;

namespace AutoQAC.Tests.Services;

public sealed class PluginRefreshDiscoveryPlannerTests
{
    [Fact]
    public void GetAvailableGames_ShouldExcludeUnknownAndUseStableStringOrder()
    {
        var sut = CreateSut();

        var games = sut.GetAvailableGames();

        games.Should().NotContain(GameType.Unknown);
        games.Should().BeEquivalentTo(
            Enum.GetValues<GameType>()
                .Where(gameType => gameType != GameType.Unknown)
                .OrderBy(gameType => gameType.ToString()),
            options => options.WithStrictOrdering());
    }

    [Fact]
    public void GetAffordance_ShouldPreserveMutagenAndLoadOrderUiRules()
    {
        var sut = CreateSut();

        sut.GetAffordance(GameType.SkyrimSe, mo2ModeEnabled: false).IsMutagenSupported.Should().BeTrue();
        sut.GetAffordance(GameType.FalloutNewVegas, mo2ModeEnabled: false).RequiresLoadOrderFile.Should().BeTrue();
        sut.GetAffordance(GameType.FalloutNewVegas, mo2ModeEnabled: true).RequiresLoadOrderFile.Should().BeFalse();
        sut.GetAffordance(GameType.Oblivion, mo2ModeEnabled: false).CanAttemptIssueApproximation.Should().BeFalse();
    }

    [Fact]
    public async Task CreatePlanAsync_ForDirectAutomaticGame_ShouldReturnReadyAutomaticPlan()
    {
        var configurationService = CreateConfigurationService(disableSkipLists: true);
        var pluginLoadingService = Substitute.For<IPluginLoadingService>();
        configurationService.GetGameDataFolderOverrideAsync(GameType.SkyrimSe, Arg.Any<CancellationToken>())
            .Returns(@"C:\Overrides\Skyrim\Data");
        pluginLoadingService.GetGameDataFolder(GameType.SkyrimSe, @"C:\Overrides\Skyrim\Data")
            .Returns(@"C:\Resolved\Skyrim\Data");
        var sut = CreateSut(configurationService, pluginLoadingService);

        var result = await sut.CreatePlanAsync(new PluginRefreshDiscoveryPlanRequest(GameType.SkyrimSe, null));

        result.Status.Should().Be(PluginRefreshDiscoveryPlanStatus.Ready);
        result.Plan.Should().NotBeNull();
        result.Plan!.Mode.Should().Be(PluginRefreshDiscoveryMode.DirectAutomatic);
        result.Plan.DataFolderPath.Should().Be(@"C:\Resolved\Skyrim\Data");
        result.Plan.LoadOrderPath.Should().BeNull();
        result.Plan.DisableSkipLists.Should().BeTrue();
        result.Plan.CanAttemptIssueApproximation.Should().BeTrue();
        result.Configuration.GameDataFolder.Should().Be(@"C:\Resolved\Skyrim\Data");
        result.Configuration.HasGameDataFolderOverride.Should().BeTrue();
        await configurationService.DidNotReceive()
            .GetGameLoadOrderOverrideAsync(Arg.Any<GameType>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreatePlanAsync_ForDirectLoadOrderGame_ShouldUseSelectedPathFirst()
    {
        var configurationService = CreateConfigurationService();
        var pluginLoadingService = Substitute.For<IPluginLoadingService>();
        pluginLoadingService.GetGameDataFolder(GameType.FalloutNewVegas, null)
            .Returns(@"C:\FNV\Data");
        var sut = CreateSut(configurationService, pluginLoadingService);

        var result = await sut.CreatePlanAsync(new PluginRefreshDiscoveryPlanRequest(
            GameType.FalloutNewVegas,
            @"C:\Selected\plugins.txt"));

        result.Status.Should().Be(PluginRefreshDiscoveryPlanStatus.Ready);
        result.Plan!.Mode.Should().Be(PluginRefreshDiscoveryMode.DirectLoadOrderFile);
        result.Plan.LoadOrderPath.Should().Be(@"C:\Selected\plugins.txt");
        result.Configuration.LoadOrderPath.Should().Be(@"C:\Selected\plugins.txt");
        await configurationService.DidNotReceive()
            .GetGameLoadOrderOverrideAsync(Arg.Any<GameType>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreatePlanAsync_ForDirectLoadOrderGame_ShouldFallBackToPerGameOverride()
    {
        var configurationService = CreateConfigurationService();
        var pluginLoadingService = Substitute.For<IPluginLoadingService>();
        configurationService.GetGameLoadOrderOverrideAsync(GameType.Fallout3, Arg.Any<CancellationToken>())
            .Returns(@"C:\Overrides\Fallout3\plugins.txt");
        pluginLoadingService.GetGameDataFolder(GameType.Fallout3, null)
            .Returns(@"C:\Fallout3\Data");
        pluginLoadingService.GetDefaultLoadOrderPath(GameType.Fallout3)
            .Returns(@"C:\Default\Fallout3\plugins.txt");
        var sut = CreateSut(configurationService, pluginLoadingService);

        var result = await sut.CreatePlanAsync(new PluginRefreshDiscoveryPlanRequest(GameType.Fallout3, null));

        result.Status.Should().Be(PluginRefreshDiscoveryPlanStatus.Ready);
        result.Plan!.LoadOrderPath.Should().Be(@"C:\Overrides\Fallout3\plugins.txt");
        pluginLoadingService.DidNotReceive().GetDefaultLoadOrderPath(GameType.Fallout3);
    }

    [Fact]
    public async Task CreatePlanAsync_ForDirectLoadOrderGame_ShouldFallBackToDefaultLoadOrderPath()
    {
        var configurationService = CreateConfigurationService();
        var pluginLoadingService = Substitute.For<IPluginLoadingService>();
        configurationService.GetGameLoadOrderOverrideAsync(GameType.Oblivion, Arg.Any<CancellationToken>())
            .Returns((string?)null);
        pluginLoadingService.GetGameDataFolder(GameType.Oblivion, null)
            .Returns(@"C:\Oblivion\Data");
        pluginLoadingService.GetDefaultLoadOrderPath(GameType.Oblivion)
            .Returns(@"C:\Default\Oblivion\plugins.txt");
        var sut = CreateSut(configurationService, pluginLoadingService);

        var result = await sut.CreatePlanAsync(new PluginRefreshDiscoveryPlanRequest(GameType.Oblivion, null));

        result.Status.Should().Be(PluginRefreshDiscoveryPlanStatus.Ready);
        result.Plan!.LoadOrderPath.Should().Be(@"C:\Default\Oblivion\plugins.txt");
    }

    [Fact]
    public async Task CreatePlanAsync_ForDirectLoadOrderGame_ShouldReturnMissingLoadOrderWhenNoPathExists()
    {
        var configurationService = CreateConfigurationService();
        var pluginLoadingService = Substitute.For<IPluginLoadingService>();
        configurationService.GetGameLoadOrderOverrideAsync(GameType.FalloutNewVegas, Arg.Any<CancellationToken>())
            .Returns((string?)null);
        pluginLoadingService.GetGameDataFolder(GameType.FalloutNewVegas, null)
            .Returns(@"C:\FNV\Data");
        pluginLoadingService.GetDefaultLoadOrderPath(GameType.FalloutNewVegas)
            .Returns((string?)null);
        var sut = CreateSut(configurationService, pluginLoadingService);

        var result = await sut.CreatePlanAsync(new PluginRefreshDiscoveryPlanRequest(GameType.FalloutNewVegas, null));

        result.Status.Should().Be(PluginRefreshDiscoveryPlanStatus.MissingLoadOrderFile);
        result.Plan.Should().BeNull();
        result.Configuration.LoadOrderPath.Should().BeNull();
    }

    [Fact]
    public async Task CreatePlanAsync_ForMo2Mode_ShouldResolveInstanceProfileLoadOrderAndPathMap()
    {
        var tempDir = Directory.CreateTempSubdirectory("AutoQAC_MO2_");
        try
        {
            var loadOrderPath = Path.Combine(tempDir.FullName, "loadorder.txt");
            await File.WriteAllTextAsync(loadOrderPath, "Mapped.esp");
            var configurationService = CreateConfigurationService(mo2Mode: true, disableSkipLists: true, mo2Path: @"C:\MO2\ModOrganizer.exe");
            var pluginLoadingService = Substitute.For<IPluginLoadingService>();
            var mo2InstanceService = Substitute.For<IMo2InstanceService>();
            var instance = CreateMo2Instance(tempDir.FullName);
            var profiles = new[] { "Default", "Survival" };
            var pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Mapped.esp"] = @"C:\MO2\mods\Mapped.esp"
            };
            configurationService.GetGameDataFolderOverrideAsync(GameType.SkyrimSe, Arg.Any<CancellationToken>())
                .Returns((string?)null);
            configurationService.GetMo2InstanceOverrideAsync(GameType.SkyrimSe, Arg.Any<CancellationToken>())
                .Returns(tempDir.FullName);
            configurationService.GetMo2ProfileAsync(GameType.SkyrimSe, Arg.Any<CancellationToken>())
                .Returns("Survival");
            pluginLoadingService.GetGameDataFolder(GameType.SkyrimSe, null)
                .Returns(@"C:\Skyrim\Data");
            mo2InstanceService.ResolveInstanceAsync(
                    GameType.SkyrimSe,
                    @"C:\MO2\ModOrganizer.exe",
                    tempDir.FullName,
                    Arg.Any<CancellationToken>())
                .Returns(instance);
            mo2InstanceService.GetProfiles(instance).Returns(profiles);
            mo2InstanceService.ChooseProfile(instance, profiles, "Survival").Returns("Survival");
            mo2InstanceService.GetLoadOrderPath(instance, "Survival").Returns(loadOrderPath);
            mo2InstanceService.BuildPluginPathMap(instance, "Survival", @"C:\Skyrim\Data").Returns(pathMap);
            var sut = CreateSut(configurationService, pluginLoadingService, mo2InstanceService);

            var result = await sut.CreatePlanAsync(new PluginRefreshDiscoveryPlanRequest(GameType.SkyrimSe, null));

            result.Status.Should().Be(PluginRefreshDiscoveryPlanStatus.Ready);
            result.Plan!.Mode.Should().Be(PluginRefreshDiscoveryMode.Mo2LoadOrderFile);
            result.Plan.LoadOrderPath.Should().BeNull();
            result.Plan.Mo2LoadOrderPath.Should().Be(loadOrderPath);
            result.Plan.Mo2PathMap.Should().Contain("Mapped.esp", @"C:\MO2\mods\Mapped.esp");
            result.Plan.Mo2BaseDataFolder.Should().Be(@"C:\Skyrim\Data");
            result.Plan.DisableSkipLists.Should().BeTrue();
            result.Plan.CanAttemptIssueApproximation.Should().BeTrue();
            result.Configuration.LoadOrderPath.Should().BeNull();
            result.Configuration.Mo2InstancePath.Should().Be(tempDir.FullName);
            result.Configuration.IsMo2InstanceOverride.Should().BeTrue();
            result.Configuration.IsMo2InstanceValid.Should().BeTrue();
            result.Configuration.AvailableProfiles.Should().BeEquivalentTo(profiles, options => options.WithStrictOrdering());
            result.Configuration.SelectedProfile.Should().Be("Survival");
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task CreatePlanAsync_ForMo2Mode_ShouldReturnMissingInstanceWhenResolutionFails()
    {
        var configurationService = CreateConfigurationService(mo2Mode: true, mo2Path: @"C:\MO2\ModOrganizer.exe");
        var pluginLoadingService = Substitute.For<IPluginLoadingService>();
        var mo2InstanceService = Substitute.For<IMo2InstanceService>();
        configurationService.GetMo2InstanceOverrideAsync(GameType.Fallout4, Arg.Any<CancellationToken>())
            .Returns(@"C:\Missing\MO2");
        pluginLoadingService.GetGameDataFolder(GameType.Fallout4, null)
            .Returns(@"C:\Fallout4\Data");
        mo2InstanceService.ResolveInstanceAsync(
                GameType.Fallout4,
                @"C:\MO2\ModOrganizer.exe",
                @"C:\Missing\MO2",
                Arg.Any<CancellationToken>())
            .Returns((Mo2InstanceInfo?)null);
        var sut = CreateSut(configurationService, pluginLoadingService, mo2InstanceService);

        var result = await sut.CreatePlanAsync(new PluginRefreshDiscoveryPlanRequest(GameType.Fallout4, null));

        result.Status.Should().Be(PluginRefreshDiscoveryPlanStatus.MissingMo2Instance);
        result.Plan.Should().BeNull();
        result.Configuration.Mo2InstancePath.Should().Be(@"C:\Missing\MO2");
        result.Configuration.AvailableProfiles.Should().BeEmpty();
    }

    [Fact]
    public async Task CreatePlanAsync_ForMo2Mode_ShouldReturnMissingProfileWhenNoneCanBeSelected()
    {
        var tempDir = Directory.CreateTempSubdirectory("AutoQAC_MO2_");
        try
        {
            var configurationService = CreateConfigurationService(mo2Mode: true);
            var pluginLoadingService = Substitute.For<IPluginLoadingService>();
            var mo2InstanceService = Substitute.For<IMo2InstanceService>();
            var instance = CreateMo2Instance(tempDir.FullName);
            pluginLoadingService.GetGameDataFolder(GameType.SkyrimSe, null)
                .Returns(@"C:\Skyrim\Data");
            mo2InstanceService.ResolveInstanceAsync(
                    GameType.SkyrimSe,
                    Arg.Is<string?>(value => value == null),
                    Arg.Is<string?>(value => value == null),
                    Arg.Any<CancellationToken>())
                .Returns(instance);
            mo2InstanceService.GetProfiles(instance).Returns([]);
            mo2InstanceService.ChooseProfile(instance, [], null).Returns((string?)null);
            var sut = CreateSut(configurationService, pluginLoadingService, mo2InstanceService);

            var result = await sut.CreatePlanAsync(new PluginRefreshDiscoveryPlanRequest(GameType.SkyrimSe, null));

            result.Status.Should().Be(PluginRefreshDiscoveryPlanStatus.MissingMo2Profile);
            result.Plan.Should().BeNull();
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task CreatePlanAsync_ForMo2Mode_ShouldReturnMissingProfileLoadOrderWhenFileIsMissing()
    {
        var tempDir = Directory.CreateTempSubdirectory("AutoQAC_MO2_");
        try
        {
            var missingLoadOrderPath = Path.Combine(tempDir.FullName, "missing-loadorder.txt");
            var configurationService = CreateConfigurationService(mo2Mode: true);
            var pluginLoadingService = Substitute.For<IPluginLoadingService>();
            var mo2InstanceService = Substitute.For<IMo2InstanceService>();
            var instance = CreateMo2Instance(tempDir.FullName);
            var profiles = new[] { "Default" };
            pluginLoadingService.GetGameDataFolder(GameType.SkyrimSe, null)
                .Returns(@"C:\Skyrim\Data");
            mo2InstanceService.ResolveInstanceAsync(
                    GameType.SkyrimSe,
                    Arg.Is<string?>(value => value == null),
                    Arg.Is<string?>(value => value == null),
                    Arg.Any<CancellationToken>())
                .Returns(instance);
            mo2InstanceService.GetProfiles(instance).Returns(profiles);
            mo2InstanceService.ChooseProfile(instance, profiles, null).Returns("Default");
            mo2InstanceService.GetLoadOrderPath(instance, "Default").Returns(missingLoadOrderPath);
            var sut = CreateSut(configurationService, pluginLoadingService, mo2InstanceService);

            var result = await sut.CreatePlanAsync(new PluginRefreshDiscoveryPlanRequest(GameType.SkyrimSe, null));

            result.Status.Should().Be(PluginRefreshDiscoveryPlanStatus.MissingMo2ProfileLoadOrder);
            result.Plan.Should().BeNull();
            result.Configuration.SelectedProfile.Should().Be("Default");
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task LoadPluginsAsync_ForDirectAutomaticPlan_ShouldUseTryGetPluginsAsync()
    {
        var pluginLoadingService = Substitute.For<IPluginLoadingService>();
        var plan = CreatePlan(PluginRefreshDiscoveryMode.DirectAutomatic, GameType.Fallout4, dataFolder: @"C:\Fallout4\Data");
        var plugins = new[] { Plugin("Automatic.esp", @"C:\Fallout4\Data\Automatic.esp", GameType.Fallout4) };
        pluginLoadingService.TryGetPluginsAsync(GameType.Fallout4, @"C:\Fallout4\Data", Arg.Any<CancellationToken>())
            .Returns(new PluginLoadingResult
            {
                Status = PluginLoadingStatus.Success,
                Plugins = plugins
            });
        var sut = CreateSut(pluginLoadingService: pluginLoadingService);

        var result = await sut.LoadPluginsAsync(plan);

        result.Plugins.Should().BeEquivalentTo(plugins);
        result.LoadingStatus.Should().Be(PluginLoadingStatus.Success);
        await pluginLoadingService.Received(1)
            .TryGetPluginsAsync(GameType.Fallout4, @"C:\Fallout4\Data", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadPluginsAsync_ForDirectLoadOrderPlan_ShouldUseConfiguredLoadOrderFile()
    {
        var pluginLoadingService = Substitute.For<IPluginLoadingService>();
        var plan = CreatePlan(
            PluginRefreshDiscoveryMode.DirectLoadOrderFile,
            GameType.FalloutNewVegas,
            dataFolder: @"C:\FNV\Data",
            loadOrderPath: @"C:\FNV\plugins.txt");
        var plugins = new[] { Plugin("FNV.esp", @"C:\FNV\Data\FNV.esp", GameType.FalloutNewVegas) };
        pluginLoadingService.GetPluginsFromFileAsync(@"C:\FNV\plugins.txt", @"C:\FNV\Data", Arg.Any<CancellationToken>())
            .Returns(plugins.ToList());
        var sut = CreateSut(pluginLoadingService: pluginLoadingService);

        var result = await sut.LoadPluginsAsync(plan);

        result.Plugins.Should().BeEquivalentTo(plugins);
        await pluginLoadingService.Received(1)
            .GetPluginsFromFileAsync(@"C:\FNV\plugins.txt", @"C:\FNV\Data", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadPluginsAsync_ForMo2Plan_ShouldUseMo2LoadOrderAndApplyPathMap()
    {
        var pluginLoadingService = Substitute.For<IPluginLoadingService>();
        var plan = CreatePlan(
            PluginRefreshDiscoveryMode.Mo2LoadOrderFile,
            GameType.SkyrimSe,
            dataFolder: @"C:\Skyrim\Data",
            mo2LoadOrderPath: @"C:\MO2\profiles\Default\loadorder.txt",
            mo2PathMap: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Mapped.esp"] = @"C:\MO2\mods\Mapped.esp"
            });
        pluginLoadingService.GetPluginsFromFileAsync(
                @"C:\MO2\profiles\Default\loadorder.txt",
                null,
                Arg.Any<CancellationToken>())
            .Returns([
                Plugin("Mapped.esp", @"C:\Skyrim\Data\Mapped.esp", GameType.SkyrimSe),
                Plugin("Unmapped.esp", @"C:\Skyrim\Data\Unmapped.esp", GameType.SkyrimSe)
            ]);
        var sut = CreateSut(pluginLoadingService: pluginLoadingService);

        var result = await sut.LoadPluginsAsync(plan);

        result.Plugins.Should().Contain(plugin =>
            plugin.FileName == "Mapped.esp" && plugin.FullPath == @"C:\MO2\mods\Mapped.esp");
        result.Plugins.Should().Contain(plugin =>
            plugin.FileName == "Unmapped.esp" && plugin.FullPath == @"C:\Skyrim\Data\Unmapped.esp");
        await pluginLoadingService.Received(1)
            .GetPluginsFromFileAsync(
                @"C:\MO2\profiles\Default\loadorder.txt",
                null,
                Arg.Any<CancellationToken>());
    }

    private static PluginRefreshDiscoveryPlanner CreateSut(
        IConfigurationService? configurationService = null,
        IPluginLoadingService? pluginLoadingService = null,
        IMo2InstanceService? mo2InstanceService = null) =>
        new(
            configurationService ?? CreateConfigurationService(),
            pluginLoadingService ?? Substitute.For<IPluginLoadingService>(),
            mo2InstanceService ?? Substitute.For<IMo2InstanceService>());

    private static IConfigurationService CreateConfigurationService(
        bool mo2Mode = false,
        bool disableSkipLists = false,
        string? mo2Path = null,
        string? xEditPath = null)
    {
        var configurationService = Substitute.For<IConfigurationService>();
        configurationService.LoadUserConfigAsync(Arg.Any<CancellationToken>())
            .Returns(new UserConfiguration
            {
                XEdit = new XEditConfig { Binary = xEditPath },
                ModOrganizer = new ModOrganizerConfig { Binary = mo2Path },
                Settings = new AutoQacSettings
                {
                    Mo2Mode = mo2Mode,
                    DisableSkipLists = disableSkipLists,
                    CleaningTimeout = 123
                }
            });
        configurationService.GetGameDataFolderOverrideAsync(Arg.Any<GameType>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        configurationService.GetGameLoadOrderOverrideAsync(Arg.Any<GameType>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        configurationService.GetMo2InstanceOverrideAsync(Arg.Any<GameType>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        configurationService.GetMo2ProfileAsync(Arg.Any<GameType>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        return configurationService;
    }

    private static PluginRefreshDiscoveryPlan CreatePlan(
        PluginRefreshDiscoveryMode mode,
        GameType gameType,
        string? dataFolder,
        string? loadOrderPath = null,
        string? mo2LoadOrderPath = null,
        IReadOnlyDictionary<string, string>? mo2PathMap = null) =>
        new(
            gameType,
            mode,
            new PluginRefreshConfigurationProjection(
                loadOrderPath,
                dataFolder,
                HasGameDataFolderOverride: false,
                XEditPath: null,
                Mo2Path: null,
                Mo2ModeEnabled: mode == PluginRefreshDiscoveryMode.Mo2LoadOrderFile,
                Mo2InstancePath: null,
                IsMo2InstanceOverride: false,
                IsMo2InstanceValid: null,
                AvailableProfiles: [],
                SelectedProfile: null,
                CleaningTimeout: 0),
            DisableSkipLists: false,
            CanAttemptIssueApproximation: true,
            dataFolder,
            loadOrderPath,
            mo2LoadOrderPath,
            mo2PathMap ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Mo2BaseDataFolder: mode == PluginRefreshDiscoveryMode.Mo2LoadOrderFile ? dataFolder : null);

    private static PluginInfo Plugin(string fileName, string fullPath, GameType gameType) =>
        new()
        {
            FileName = fileName,
            FullPath = fullPath,
            DetectedGameType = gameType
        };

    private static Mo2InstanceInfo CreateMo2Instance(string baseDirectory) =>
        new(
            BaseDirectory: baseDirectory,
            ModsDirectory: Path.Combine(baseDirectory, "mods"),
            ProfilesDirectory: Path.Combine(baseDirectory, "profiles"),
            OverwriteDirectory: Path.Combine(baseDirectory, "overwrite"),
            IniSelectedProfile: null,
            GameName: null,
            IsAutoDetected: false,
            IniPath: null);
}
