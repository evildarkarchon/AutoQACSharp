using AutoQAC.Models;
using AutoQAC.Services.MO2;
using FluentAssertions;

namespace AutoQAC.Tests.Services;

public sealed class Mo2InstanceServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AutoQAC-MO2-" + Guid.NewGuid());

    public Mo2InstanceServiceTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task ResolveInstanceAsync_ShouldParseIniAndExpandBaseDirectoryPaths()
    {
        var localAppData = Path.Combine(_root, "LocalAppData");
        var instanceDir = Path.Combine(localAppData, "ModOrganizer", "SSE");
        Directory.CreateDirectory(instanceDir);
        await File.WriteAllTextAsync(Path.Combine(instanceDir, "ModOrganizer.ini"), """
            [General]
            gameName=Skyrim Special Edition
            selected_profile=@ByteArray(Profile One)
            base_directory=%BASE_DIR%
            mod_directory=%BASE_DIR%\custom-mods
            profiles_directory=%BASE_DIR%\custom-profiles
            """);

        var sut = new Mo2InstanceService(localAppDataResolver: () => localAppData);

        var result = await sut.ResolveInstanceAsync(GameType.SkyrimSe, null, null);

        result.Should().NotBeNull();
        result!.BaseDirectory.Should().Be(instanceDir);
        result.ModsDirectory.Should().Be(Path.Combine(instanceDir, "custom-mods"));
        result.ProfilesDirectory.Should().Be(Path.Combine(instanceDir, "custom-profiles"));
        result.IniSelectedProfile.Should().Be("Profile One");
        result.GameName.Should().Be("Skyrim Special Edition");
        result.IsAutoDetected.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveInstanceAsync_ShouldPreferPortableIniOverGlobalMatch()
    {
        var localAppData = Path.Combine(_root, "LocalAppData");
        var globalDir = Path.Combine(localAppData, "ModOrganizer", "SSE");
        var portableDir = Path.Combine(_root, "PortableMO2");
        Directory.CreateDirectory(globalDir);
        Directory.CreateDirectory(portableDir);
        var globalIni = Path.Combine(globalDir, "ModOrganizer.ini");
        var portableIni = Path.Combine(portableDir, "ModOrganizer.ini");
        await File.WriteAllTextAsync(globalIni, "gameName=Skyrim Special Edition");
        await File.WriteAllTextAsync(portableIni, "gameName=Skyrim Special Edition");
        File.SetLastWriteTimeUtc(globalIni, DateTime.UtcNow.AddMinutes(5));

        var sut = new Mo2InstanceService(localAppDataResolver: () => localAppData);

        var result = await sut.ResolveInstanceAsync(
            GameType.SkyrimSe,
            Path.Combine(portableDir, "ModOrganizer.exe"),
            null);

        result.Should().NotBeNull();
        result!.IniPath.Should().Be(portableIni);
    }

    [Fact]
    public void ChooseProfile_ShouldPreferPersistedThenDefaultThenIniThenAlphabetical()
    {
        var sut = new Mo2InstanceService();
        var instance = new Mo2InstanceInfo(
            _root,
            Path.Combine(_root, "mods"),
            Path.Combine(_root, "profiles"),
            Path.Combine(_root, "overwrite"),
            "IniProfile",
            "Skyrim Special Edition",
            true,
            null);

        sut.ChooseProfile(instance, ["Default", "Persisted"], "Persisted").Should().Be("Persisted");
        sut.ChooseProfile(instance, ["Default", "IniProfile"], "Missing").Should().Be("Default");
        sut.ChooseProfile(instance, ["Alpha", "IniProfile"], null).Should().Be("IniProfile");
        sut.ChooseProfile(instance, ["Alpha", "Beta"], null).Should().Be("Alpha");
    }

    [Fact]
    public void BuildPluginPathMap_ShouldResolveOverwriteBeforeTopModBeforeBaseDataAndSkipDisabledMods()
    {
        var instance = CreateInstance();
        var profileDir = Path.Combine(instance.ProfilesDirectory, "Default");
        Directory.CreateDirectory(profileDir);
        Directory.CreateDirectory(instance.OverwriteDirectory);
        Directory.CreateDirectory(Path.Combine(instance.ModsDirectory, "HighPriority"));
        Directory.CreateDirectory(Path.Combine(instance.ModsDirectory, "LowPriority"));
        Directory.CreateDirectory(Path.Combine(instance.ModsDirectory, "Disabled"));
        var dataDir = Path.Combine(_root, "Data");
        Directory.CreateDirectory(dataDir);

        File.WriteAllText(Path.Combine(profileDir, "modlist.txt"), """
            +HighPriority
            +LowPriority
            -Disabled
            """);
        File.WriteAllText(Path.Combine(instance.OverwriteDirectory, "OverwriteWins.esp"), string.Empty);
        File.WriteAllText(Path.Combine(instance.ModsDirectory, "HighPriority", "Conflict.esp"), string.Empty);
        File.WriteAllText(Path.Combine(instance.ModsDirectory, "LowPriority", "Conflict.esp"), string.Empty);
        File.WriteAllText(Path.Combine(instance.ModsDirectory, "Disabled", "Disabled.esp"), string.Empty);
        File.WriteAllText(Path.Combine(dataDir, "Conflict.esp"), string.Empty);
        File.WriteAllText(Path.Combine(dataDir, "BaseOnly.esm"), string.Empty);

        var sut = new Mo2InstanceService();

        var map = sut.BuildPluginPathMap(instance, "Default", dataDir);

        map["OverwriteWins.esp"].Should().Be(Path.Combine(instance.OverwriteDirectory, "OverwriteWins.esp"));
        map["Conflict.esp"].Should().Be(Path.Combine(instance.ModsDirectory, "HighPriority", "Conflict.esp"));
        map["BaseOnly.esm"].Should().Be(Path.Combine(dataDir, "BaseOnly.esm"));
        map.Should().NotContainKey("Disabled.esp");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private Mo2InstanceInfo CreateInstance()
    {
        var instance = new Mo2InstanceInfo(
            _root,
            Path.Combine(_root, "mods"),
            Path.Combine(_root, "profiles"),
            Path.Combine(_root, "overwrite"),
            null,
            "Skyrim Special Edition",
            false,
            null);
        Directory.CreateDirectory(instance.ModsDirectory);
        Directory.CreateDirectory(instance.ProfilesDirectory);
        Directory.CreateDirectory(instance.OverwriteDirectory);
        return instance;
    }
}
