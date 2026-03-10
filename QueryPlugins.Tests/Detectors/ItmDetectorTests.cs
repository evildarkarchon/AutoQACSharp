using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using QueryPlugins.Detectors;
using QueryPlugins.Models;

namespace QueryPlugins.Tests.Detectors;

/// <summary>
/// Unit tests for <see cref="ItmDetector"/> using in-memory Mutagen mod construction.
/// All tests use Skyrim SE as the game since ITM detection is game-agnostic and
/// Skyrim is the simplest supported game to work with in-memory.
/// </summary>
public sealed class ItmDetectorTests
{
    private static readonly ModKey MasterKey = ModKey.FromNameAndExtension("Master.esm");
    private static readonly ModKey PluginKey = ModKey.FromNameAndExtension("Plugin.esp");

    private readonly ItmDetector _sut = new();

    // ── New records ───────────────────────────────────────────────────────────

    [Fact]
    public void NewRecord_IsNotFlagged()
    {
        var (_, plugin, cache) = BuildLoadOrder(overrideAction: null, addNewToPlugin: true);

        var issues = _sut.FindItmRecords(plugin, cache).ToList();

        issues.Should().BeEmpty();
    }

    // ── Identical overrides ───────────────────────────────────────────────────

    [Fact]
    public void IdenticalOverride_IsFlagged_AsItmRecord()
    {
        var (_, plugin, cache) = BuildLoadOrder(overrideAction: null);

        var issues = _sut.FindItmRecords(plugin, cache).ToList();

        issues.Should().HaveCount(1);
        issues[0].Type.Should().Be(IssueType.ItmRecord);
    }

    [Fact]
    public void IdenticalOverride_ReportsCorrectFormKey()
    {
        var (master, plugin, cache) = BuildLoadOrder(overrideAction: null);
        var expectedFormKey = master.Npcs.First().FormKey;

        var issue = _sut.FindItmRecords(plugin, cache).Single();

        issue.FormKey.Should().Be(expectedFormKey);
    }

    // ── Modified overrides ────────────────────────────────────────────────────

    [Fact]
    public void ModifiedOverride_IsNotFlagged()
    {
        var (_, plugin, cache) = BuildLoadOrder(overrideAction: npc =>
        {
            npc.ShortName = "Modified Name";
        });

        var issues = _sut.FindItmRecords(plugin, cache).ToList();

        issues.Should().BeEmpty();
    }

    [Fact]
    public void MiddleOverride_IdenticalToPreviousVersion_IsFlagged()
    {
        var (master, plugin, _, cache) = BuildThreeModLoadOrder(
            pluginOverrideAction: null,
            laterOverrideAction: npc =>
            {
                npc.ShortName = "Later Change";
            });
        var expectedFormKey = master.Npcs.First().FormKey;

        var issues = _sut.FindItmRecords(plugin, cache).ToList();

        issues.Should().HaveCount(1);
        issues[0].Type.Should().Be(IssueType.ItmRecord);
        issues[0].FormKey.Should().Be(expectedFormKey);
    }

    [Fact]
    public void MiddleOverride_ModifiedRelativeToPreviousVersion_IsNotFlagged()
    {
        var (_, plugin, _, cache) = BuildThreeModLoadOrder(
            pluginOverrideAction: npc =>
            {
                npc.ShortName = "Plugin Change";
            },
            laterOverrideAction: npc =>
            {
                npc.ShortName = "Later Change";
            });

        var issues = _sut.FindItmRecords(plugin, cache).ToList();

        issues.Should().BeEmpty();
    }

    [Fact]
    public void WinningOverride_IdenticalToImmediatePreviousVersion_IsFlagged()
    {
        var (master, _, later, cache) = BuildThreeModLoadOrder(
            pluginOverrideAction: npc =>
            {
                npc.ShortName = "Plugin Change";
            },
            laterOverrideAction: _ =>
            {
            });
        var expectedFormKey = master.Npcs.First().FormKey;

        var issues = _sut.FindItmRecords(later, cache).ToList();

        issues.Should().HaveCount(1);
        issues[0].Type.Should().Be(IssueType.ItmRecord);
        issues[0].FormKey.Should().Be(expectedFormKey);
    }

    // ── Deleted overrides ─────────────────────────────────────────────────────

    [Fact]
    public void DeletedOverride_IsNotFlagged_ByItmDetector()
    {
        var (_, plugin, cache) = BuildLoadOrder(overrideAction: npc =>
        {
            npc.IsDeleted = true;
        });

        var issues = _sut.FindItmRecords(plugin, cache).ToList();

        issues.Should().BeEmpty("deleted records are a separate issue type and must not be double-counted as ITMs");
    }

    // ── Empty plugin ──────────────────────────────────────────────────────────

    [Fact]
    public void EmptyPlugin_ReturnsNoIssues()
    {
        var masterMod = new SkyrimMod(MasterKey, SkyrimRelease.SkyrimSE);
        masterMod.Npcs.AddNew("OriginalNpc");

        var pluginMod = new SkyrimMod(PluginKey, SkyrimRelease.SkyrimSE);

        var cache = new ISkyrimModGetter[] { masterMod, pluginMod }.ToImmutableLinkCache();

        var issues = _sut.FindItmRecords(pluginMod, cache).ToList();

        issues.Should().BeEmpty();
    }

    // ── Multiple records ──────────────────────────────────────────────────────

    [Fact]
    public void MultipleRecords_OnlyIdenticalOnesAreFlagged()
    {
        var masterMod = new SkyrimMod(MasterKey, SkyrimRelease.SkyrimSE);
        var npc1 = masterMod.Npcs.AddNew("Npc1");
        var npc2 = masterMod.Npcs.AddNew("Npc2");

        var pluginMod = new SkyrimMod(PluginKey, SkyrimRelease.SkyrimSE);

        // Override npc1 identically (ITM) and npc2 with a change (not ITM).
        pluginMod.Npcs.Set(npc1.DeepCopy());
        var npc2Override = npc2.DeepCopy();
        npc2Override.ShortName = "Modified";
        pluginMod.Npcs.Set(npc2Override);

        var cache = new ISkyrimModGetter[] { masterMod, pluginMod }.ToImmutableLinkCache();

        var issues = _sut.FindItmRecords(pluginMod, cache).ToList();

        issues.Should().HaveCount(1);
        issues[0].FormKey.Should().Be(npc1.FormKey);
    }

    [Fact]
    public void FindItmRecords_PluginMissingFromCache_ThrowsArgumentException()
    {
        var masterMod = new SkyrimMod(MasterKey, SkyrimRelease.SkyrimSE);
        var originalNpc = masterMod.Npcs.AddNew("OriginalNpc");

        var pluginMod = new SkyrimMod(PluginKey, SkyrimRelease.SkyrimSE);
        pluginMod.Npcs.Set(originalNpc.DeepCopy());

        var laterMod = new SkyrimMod(ModKey.FromNameAndExtension("Later.esp"), SkyrimRelease.SkyrimSE);
        var laterOverride = originalNpc.DeepCopy();
        laterOverride.ShortName = "Later Change";
        laterMod.Npcs.Set(laterOverride);

        var cache = new ISkyrimModGetter[] { masterMod, laterMod }.ToImmutableLinkCache();

        var act = () => _sut.FindItmRecords(pluginMod, cache);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("linkCache")
            .WithMessage("*does not contain analyzed plugin*");
    }

    [Fact]
    public void FindItmRecords_PluginWithOnlyNewRecordsMissingFromCache_ThrowsArgumentException()
    {
        var masterMod = new SkyrimMod(MasterKey, SkyrimRelease.SkyrimSE);
        masterMod.Npcs.AddNew("OriginalNpc");

        var pluginMod = new SkyrimMod(PluginKey, SkyrimRelease.SkyrimSE);
        pluginMod.Npcs.AddNew("NewNpc");

        var cache = new ISkyrimModGetter[] { masterMod }.ToImmutableLinkCache();

        var act = () => _sut.FindItmRecords(pluginMod, cache);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("linkCache")
            .WithMessage("*does not contain analyzed plugin*");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a two-mod load order: a master with one NPC and a plugin that overrides it.
    /// <paramref name="overrideAction"/> is applied to the override after deep-copying from the master.
    /// If <paramref name="addNewToPlugin"/> is true, a new NPC (defined in the plugin) is added instead.
    /// </summary>
    private static (SkyrimMod master, SkyrimMod plugin, ILinkCache cache)
        BuildLoadOrder(Action<Npc>? overrideAction, bool addNewToPlugin = false)
    {
        var masterMod = new SkyrimMod(MasterKey, SkyrimRelease.SkyrimSE);
        var originalNpc = masterMod.Npcs.AddNew("OriginalNpc");

        var pluginMod = new SkyrimMod(PluginKey, SkyrimRelease.SkyrimSE);

        if (addNewToPlugin)
        {
            pluginMod.Npcs.AddNew("NewNpc");
        }
        else
        {
            var overrideNpc = originalNpc.DeepCopy();
            overrideAction?.Invoke(overrideNpc);
            pluginMod.Npcs.Set(overrideNpc);
        }

        var cache = new ISkyrimModGetter[] { masterMod, pluginMod }.ToImmutableLinkCache();
        return (masterMod, pluginMod, cache);
    }

    private static (SkyrimMod master, SkyrimMod plugin, SkyrimMod later, ILinkCache cache)
        BuildThreeModLoadOrder(Action<Npc>? pluginOverrideAction, Action<Npc> laterOverrideAction)
    {
        var masterMod = new SkyrimMod(MasterKey, SkyrimRelease.SkyrimSE);
        var originalNpc = masterMod.Npcs.AddNew("OriginalNpc");

        var pluginMod = new SkyrimMod(PluginKey, SkyrimRelease.SkyrimSE);
        var pluginOverride = originalNpc.DeepCopy();
        pluginOverrideAction?.Invoke(pluginOverride);
        pluginMod.Npcs.Set(pluginOverride);

        var laterMod = new SkyrimMod(ModKey.FromNameAndExtension("Later.esp"), SkyrimRelease.SkyrimSE);
        var laterOverride = pluginOverride.DeepCopy();
        laterOverrideAction(laterOverride);
        laterMod.Npcs.Set(laterOverride);

        var cache = new ISkyrimModGetter[] { masterMod, pluginMod, laterMod }.ToImmutableLinkCache();
        return (masterMod, pluginMod, laterMod, cache);
    }
}
