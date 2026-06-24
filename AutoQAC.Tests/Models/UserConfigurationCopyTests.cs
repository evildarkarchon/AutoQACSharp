using AutoQAC.Models.Configuration;
using FluentAssertions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AutoQAC.Tests.Models;

public sealed class UserConfigurationCopyTests
{
    [Fact]
    public void Copy_PreservesScalarFields()
    {
        var source = new UserConfiguration { SelectedGame = "SkyrimSe" };

        var copy = source.Copy();

        copy.Should().NotBeSameAs(source, because: "D-40 requires a separate manual copy instance");
        copy.SelectedGame.Should().Be(source.SelectedGame, because: "D-42 behavior tests pin scalar copy maintenance");
    }

    [Fact]
    public void Copy_DeepCopiesSkipLists_DictionaryAndNestedLists()
    {
        var source = new UserConfiguration
        {
            SkipLists = new Dictionary<string, List<string>>
            {
                ["SSE"] = ["a.esp", "b.esp", "c.esp"]
            }
        };

        var copy = source.Copy();
        copy.SkipLists["SSE"].Add("d.esp");

        copy.SkipLists.Should().NotBeSameAs(source.SkipLists, because: "D-45 deep-copies mutable dictionaries");
        copy.SkipLists["SSE"].Should().NotBeSameAs(source.SkipLists["SSE"], because: "D-45 deep-copies nested mutable list values, including string lists inside SkipLists");
        source.SkipLists["SSE"].Should().Equal(["a.esp", "b.esp", "c.esp"], because: "D-45 copy mutation must not alter source SkipLists entries");
    }

    [Fact]
    public void Copy_DeepCopiesLoadOrderFileOverrides_Dictionary()
    {
        var source = new UserConfiguration
        {
            LoadOrderFileOverrides = new Dictionary<string, string> { ["SSE"] = @"C:\foo" }
        };

        var copy = source.Copy();
        copy.LoadOrderFileOverrides["SSE"] = @"C:\bar";

        copy.LoadOrderFileOverrides.Should().NotBeSameAs(source.LoadOrderFileOverrides, because: "D-45 dictionary containers must be independent");
        source.LoadOrderFileOverrides["SSE"].Should().Be(@"C:\foo", because: "D-45 copy mutation must not mutate source dictionary values");
    }

    [Fact]
    public void Copy_DeepCopiesGameDataFolderOverrides_Dictionary()
    {
        var source = new UserConfiguration
        {
            GameDataFolderOverrides = new Dictionary<string, string> { ["FO4"] = @"C:\Data" }
        };

        var copy = source.Copy();
        copy.GameDataFolderOverrides["FO4"] = @"D:\Data";

        copy.GameDataFolderOverrides.Should().NotBeSameAs(source.GameDataFolderOverrides, because: "D-45 dictionary containers must be independent");
        source.GameDataFolderOverrides["FO4"].Should().Be(@"C:\Data", because: "D-45 copy mutation must not mutate source dictionary values");
    }

    [Fact]
    public void Copy_DeepCopiesLoadOrderConfig()
    {
        var source = new UserConfiguration { LoadOrder = new LoadOrderConfig { File = @"C:\plugins.txt" } };

        var copy = source.Copy();
        copy.LoadOrder.File = "x";

        copy.LoadOrder.Should().NotBeSameAs(source.LoadOrder, because: "D-41 nested config models own copy behavior");
        source.LoadOrder.File.Should().Be(@"C:\plugins.txt", because: "D-44 nested objects are independent defaults or copies");
    }

    [Fact]
    public void Copy_DeepCopiesModOrganizerConfig()
    {
        var source = new UserConfiguration { ModOrganizer = new ModOrganizerConfig { Binary = @"C:\MO2.exe" } };

        var copy = source.Copy();
        copy.ModOrganizer.Binary = "x";

        copy.ModOrganizer.Should().NotBeSameAs(source.ModOrganizer, because: "D-41 nested config models own copy behavior");
        source.ModOrganizer.Binary.Should().Be(@"C:\MO2.exe", because: "D-44 nested objects are independent defaults or copies");
    }

    [Fact]
    public void Copy_DeepCopiesXEditConfig()
    {
        var source = new UserConfiguration { XEdit = new XEditConfig { Binary = @"C:\xEdit.exe" } };

        var copy = source.Copy();
        copy.XEdit.Binary = "x";

        copy.XEdit.Should().NotBeSameAs(source.XEdit, because: "D-41 nested config models own copy behavior");
        source.XEdit.Binary.Should().Be(@"C:\xEdit.exe", because: "D-44 nested objects are independent defaults or copies");
    }

    [Fact]
    public void Copy_DeepCopiesAutoQacSettings_AllFields()
    {
        var source = new UserConfiguration
        {
            Settings = new AutoQacSettings
            {
                JournalExpiration = 12,
                CleaningTimeout = 600,
                CpuThreshold = 10,
                Mo2Mode = true,
                DisableSkipLists = true
            }
        };

        var copy = source.Copy();
        copy.Settings.CleaningTimeout = 999;

        copy.Settings.Should().NotBeSameAs(source.Settings, because: "D-41 nested config models own copy behavior");
        copy.Settings.JournalExpiration.Should().Be(12, because: "D-42 behavior tests pin every scalar field");
        copy.Settings.CpuThreshold.Should().Be(10, because: "D-42 behavior tests pin every scalar field");
        copy.Settings.Mo2Mode.Should().BeTrue(because: "D-42 behavior tests pin every scalar field");
        copy.Settings.DisableSkipLists.Should().BeTrue(because: "D-42 behavior tests pin every scalar field");
        source.Settings.CleaningTimeout.Should().Be(600, because: "D-44 nested objects are independent defaults or copies");
    }

    [Fact]
    public void Copy_DeepCopiesRetentionSettings()
    {
        var source = new UserConfiguration
        {
            LogRetention = new RetentionSettings { Mode = RetentionMode.CountBased, MaxAgeDays = 10, MaxFileCount = 25 }
        };

        var copy = source.Copy();
        copy.LogRetention.MaxFileCount = 99;

        copy.LogRetention.Should().NotBeSameAs(source.LogRetention, because: "D-41 nested config models own copy behavior");
        copy.LogRetention.Mode.Should().Be(RetentionMode.CountBased, because: "D-42 behavior tests pin every scalar field");
        copy.LogRetention.MaxAgeDays.Should().Be(10, because: "D-42 behavior tests pin every scalar field");
        source.LogRetention.MaxFileCount.Should().Be(25, because: "D-44 nested objects are independent defaults or copies");
    }

    [Fact]
    public void Copy_DeepCopiesBackupSettings()
    {
        var source = new UserConfiguration
        {
            Backup = new BackupSettings { Enabled = false, MaxSessions = 5 }
        };

        var copy = source.Copy();
        copy.Backup.MaxSessions = 99;

        copy.Backup.Should().NotBeSameAs(source.Backup, because: "D-41 nested config models own copy behavior");
        copy.Backup.Enabled.Should().BeFalse(because: "D-42 behavior tests pin every scalar field");
        source.Backup.MaxSessions.Should().Be(5, because: "D-44 nested objects are independent defaults or copies");
    }

    [Fact]
    public void Copy_NormalizesNullNestedObjectsAndCollectionsToDefaults()
    {
        var source = new UserConfiguration
        {
            LoadOrder = null!,
            LoadOrderFileOverrides = null!,
            ModOrganizer = null!,
            XEdit = null!,
            Settings = null!,
            SkipLists = null!,
            GameDataFolderOverrides = null!,
            Mo2InstanceOverrides = null!,
            Mo2ProfileSelections = null!,
            LogRetention = null!,
            Backup = null!
        };

        var copy = source.Copy();

        copy.LoadOrder.Should().NotBeNull(because: "D-44 normalizes null nested config objects to defaults");
        copy.ModOrganizer.Should().NotBeNull(because: "D-44 normalizes null nested config objects to defaults");
        copy.XEdit.Should().NotBeNull(because: "D-44 normalizes null nested config objects to defaults");
        copy.Settings.Should().NotBeNull(because: "D-44 normalizes null nested config objects to defaults");
        copy.LogRetention.Should().NotBeNull(because: "D-44 normalizes null nested config objects to defaults");
        copy.Backup.Should().NotBeNull(because: "D-44 normalizes null nested config objects to defaults");
        copy.LoadOrderFileOverrides.Should().BeEmpty(because: "D-44 normalizes null collections to empty collections");
        copy.SkipLists.Should().BeEmpty(because: "D-44 normalizes null collections to empty collections");
        copy.GameDataFolderOverrides.Should().BeEmpty(because: "D-44 normalizes null collections to empty collections");
        copy.Mo2InstanceOverrides.Should().BeEmpty(because: "D-44 normalizes null collections to empty collections");
        copy.Mo2ProfileSelections.Should().BeEmpty(because: "D-44 normalizes null collections to empty collections");
    }

    [Fact]
    public void Copy_BehaviorMatchesYamlRoundTrip_FullyPopulatedGraph()
    {
        var source = CreateFullyPopulatedConfiguration();
        var serializer = new SerializerBuilder().WithNamingConvention(NullNamingConvention.Instance).Build();
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        var yaml = serializer.Serialize(source);
        var roundTrip = deserializer.Deserialize<UserConfiguration>(yaml);

        var copy = source.Copy();

        AssertConfigurationsEquivalent(copy, roundTrip, "D-42 behavior parity replaces brittle source-regex clone guards");
        AssertDeepCopyReferences(source, copy);
    }

    [Fact]
    public void Copy_PreservesAllExistingYamlMemberAliases()
    {
        var solutionRoot = ResolveSolutionRoot();
        if (solutionRoot is null)
        {
            return;
        }

        var userConfigurationSource = File.ReadAllText(Path.Combine(solutionRoot, "AutoQAC", "Models", "Configuration", "UserConfiguration.cs"));
        var backupSettingsSource = File.ReadAllText(Path.Combine(solutionRoot, "AutoQAC", "Models", "Configuration", "BackupSettings.cs"));
        var retentionSettingsSource = File.ReadAllText(Path.Combine(solutionRoot, "AutoQAC", "Models", "Configuration", "RetentionSettings.cs"));

        userConfigurationSource.Should().ContainAll(
            ["Selected_Game", "Load_Order", "Load_Order_Files", "Mod_Organizer", "xEdit", "AutoQAC_Settings", "Skip_Lists", "Game_Data_Folders", "Log_Retention", "Backup"],
            because: "D-42 protects observable YAML compatibility without reflection-based clone guards");
        backupSettingsSource.Should().ContainAll(["enabled", "max_sessions"], because: "D-42 protects existing BackupSettings YAML aliases");
        retentionSettingsSource.Should().ContainAll(["mode", "max_age_days", "max_file_count"], because: "D-42 protects existing RetentionSettings YAML aliases");
    }

    [Fact]
    public void Copy_IsIdempotent_SameSource_TwoCalls_ProduceEqualResults()
    {
        var source = CreateFullyPopulatedConfiguration();
        var mutatedCopy = source.Copy();
        mutatedCopy.SkipLists["SSE"].Add("mutated.esp");
        mutatedCopy.LoadOrderFileOverrides["SSE"] = @"D:\mutated\plugins.txt";
        var serializer = new SerializerBuilder().WithNamingConvention(NullNamingConvention.Instance).Build();
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        var yaml = serializer.Serialize(source);
        var roundTrip = deserializer.Deserialize<UserConfiguration>(yaml);

        var laterCopy = source.Copy();

        AssertConfigurationsEquivalent(laterCopy, roundTrip, "D-45 copy mutation must not poison future copies from a stable source");
    }

    private static UserConfiguration CreateFullyPopulatedConfiguration() => new()
    {
        SelectedGame = "SkyrimSe",
        LoadOrder = new LoadOrderConfig { File = @"C:\Games\Skyrim Special Edition\plugins.txt" },
        LoadOrderFileOverrides = new Dictionary<string, string>
        {
            ["SSE"] = @"C:\Users\Test\SSE\plugins.txt",
            ["FO4"] = @"C:\Users\Test\FO4\plugins.txt"
        },
        ModOrganizer = new ModOrganizerConfig { Binary = @"C:\Tools\MO2\ModOrganizer.exe" },
        XEdit = new XEditConfig { Binary = @"C:\Tools\xEdit\SSEEdit.exe" },
        Settings = new AutoQacSettings
        {
            JournalExpiration = 14,
            CleaningTimeout = 600,
            CpuThreshold = 9,
            Mo2Mode = true,
            DisableSkipLists = true
        },
        SkipLists = new Dictionary<string, List<string>>
        {
            ["SSE"] = ["Skyrim.esm", "Update.esm"],
            ["Universal"] = ["Universal.esm", "UniversalPatch.esp"]
        },
        GameDataFolderOverrides = new Dictionary<string, string>
        {
            ["SSE"] = @"C:\Games\Skyrim Special Edition\Data",
            ["FO4"] = @"C:\Games\Fallout 4\Data"
        },
        LogRetention = new RetentionSettings
        {
            Mode = RetentionMode.CountBased,
            MaxAgeDays = 21,
            MaxFileCount = 25
        },
        Backup = new BackupSettings
        {
            Enabled = false,
            MaxSessions = 5
        }
    };

    private static void AssertConfigurationsEquivalent(UserConfiguration actual, UserConfiguration expected, string because)
    {
        actual.SelectedGame.Should().Be(expected.SelectedGame, because: because);
        actual.LoadOrder.File.Should().Be(expected.LoadOrder.File, because: because);
        actual.LoadOrderFileOverrides.Should().Equal(expected.LoadOrderFileOverrides, because: because);
        actual.ModOrganizer.Binary.Should().Be(expected.ModOrganizer.Binary, because: because);
        actual.XEdit.Binary.Should().Be(expected.XEdit.Binary, because: because);
        actual.Settings.JournalExpiration.Should().Be(expected.Settings.JournalExpiration, because: because);
        actual.Settings.CleaningTimeout.Should().Be(expected.Settings.CleaningTimeout, because: because);
        actual.Settings.CpuThreshold.Should().Be(expected.Settings.CpuThreshold, because: because);
        actual.Settings.Mo2Mode.Should().Be(expected.Settings.Mo2Mode, because: because);
        actual.Settings.DisableSkipLists.Should().Be(expected.Settings.DisableSkipLists, because: because);
        actual.SkipLists.Keys.Should().BeEquivalentTo(expected.SkipLists.Keys, because: because);
        foreach (var key in expected.SkipLists.Keys)
        {
            actual.SkipLists[key].Should().Equal(expected.SkipLists[key], because: because);
        }

        actual.GameDataFolderOverrides.Should().Equal(expected.GameDataFolderOverrides, because: because);
        actual.LogRetention.Mode.Should().Be(expected.LogRetention.Mode, because: because);
        actual.LogRetention.MaxAgeDays.Should().Be(expected.LogRetention.MaxAgeDays, because: because);
        actual.LogRetention.MaxFileCount.Should().Be(expected.LogRetention.MaxFileCount, because: because);
        actual.Backup.Enabled.Should().Be(expected.Backup.Enabled, because: because);
        actual.Backup.MaxSessions.Should().Be(expected.Backup.MaxSessions, because: because);
    }

    private static void AssertDeepCopyReferences(UserConfiguration source, UserConfiguration copy)
    {
        copy.LoadOrder.Should().NotBeSameAs(source.LoadOrder, because: "D-41 nested config models own copy behavior");
        copy.LoadOrderFileOverrides.Should().NotBeSameAs(source.LoadOrderFileOverrides, because: "D-45 mutable dictionaries are copied");
        copy.ModOrganizer.Should().NotBeSameAs(source.ModOrganizer, because: "D-41 nested config models own copy behavior");
        copy.XEdit.Should().NotBeSameAs(source.XEdit, because: "D-41 nested config models own copy behavior");
        copy.Settings.Should().NotBeSameAs(source.Settings, because: "D-41 nested config models own copy behavior");
        copy.SkipLists.Should().NotBeSameAs(source.SkipLists, because: "D-45 mutable dictionaries are copied");
        foreach (var key in source.SkipLists.Keys)
        {
            copy.SkipLists[key].Should().NotBeSameAs(source.SkipLists[key], because: "D-45 nested SkipLists lists are copied");
        }

        copy.GameDataFolderOverrides.Should().NotBeSameAs(source.GameDataFolderOverrides, because: "D-45 mutable dictionaries are copied");
        copy.LogRetention.Should().NotBeSameAs(source.LogRetention, because: "D-41 nested config models own copy behavior");
        copy.Backup.Should().NotBeSameAs(source.Backup, because: "D-41 nested config models own copy behavior");
    }

    private static string? ResolveSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AutoQACSharp.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
