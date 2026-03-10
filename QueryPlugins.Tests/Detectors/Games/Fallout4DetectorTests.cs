using Mutagen.Bethesda;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using QueryPlugins.Detectors.Games;
using QueryPlugins.Models;

namespace QueryPlugins.Tests.Detectors.Games;

/// <summary>
/// Unit tests for <see cref="Fallout4Detector"/> using in-memory Mutagen mod construction.
/// </summary>
public sealed class Fallout4DetectorTests
{
    private static readonly ModKey PluginKey = ModKey.FromNameAndExtension("Plugin.esp");

    private readonly Fallout4Detector _sut = new();

    // ── SupportedReleases ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(GameRelease.Fallout4)]
    [InlineData(GameRelease.Fallout4VR)]
    public void SupportedReleases_ContainsExpectedGame(GameRelease release)
    {
        _sut.SupportedReleases.Should().Contain(release);
    }

    // ── Wrong mod type ────────────────────────────────────────────────────────

    [Fact]
    public void FindDeletedReferences_WrongModType_ThrowsArgumentException()
    {
        var skyrimMod = new Mutagen.Bethesda.Skyrim.SkyrimMod(PluginKey, Mutagen.Bethesda.Skyrim.SkyrimRelease.SkyrimSE);

        var act = () => _sut.FindDeletedReferences(skyrimMod).ToList();

        act.Should().Throw<ArgumentException>();
    }

    // ── Deleted references ────────────────────────────────────────────────────

    [Fact]
    public void FindDeletedReferences_EmptyMod_ReturnsNoIssues()
    {
        var mod = new Fallout4Mod(PluginKey, Fallout4Release.Fallout4);

        var issues = _sut.FindDeletedReferences(mod).ToList();

        issues.Should().BeEmpty();
    }

    [Fact]
    public void FindDeletedReferences_DeletedPlacedObject_IsFound()
    {
        var mod = new Fallout4Mod(PluginKey, Fallout4Release.Fallout4);
        var cell = AddInteriorCell(mod);
        var placedObj = new PlacedObject(mod) { IsDeleted = true };
        cell.Persistent.Add(placedObj);

        var issues = _sut.FindDeletedReferences(mod).ToList();

        issues.Should().HaveCount(1);
        issues[0].Type.Should().Be(IssueType.DeletedReference);
        issues[0].FormKey.Should().Be(placedObj.FormKey);
    }

    [Fact]
    public void FindDeletedReferences_NonDeletedPlacedObject_IsNotFlagged()
    {
        var mod = new Fallout4Mod(PluginKey, Fallout4Release.Fallout4);
        var cell = AddInteriorCell(mod);
        cell.Persistent.Add(new PlacedObject(mod) { IsDeleted = false });

        var issues = _sut.FindDeletedReferences(mod).ToList();

        issues.Should().BeEmpty();
    }

    [Fact]
    public void FindDeletedReferences_DeletedPlacedNpc_IsFound()
    {
        var mod = new Fallout4Mod(PluginKey, Fallout4Release.Fallout4);
        var cell = AddInteriorCell(mod);
        var placedNpc = new PlacedNpc(mod) { IsDeleted = true };
        cell.Persistent.Add(placedNpc);

        var issues = _sut.FindDeletedReferences(mod).ToList();

        issues.Should().HaveCount(1);
        issues[0].Type.Should().Be(IssueType.DeletedReference);
    }

    // ── Deleted navmeshes ─────────────────────────────────────────────────────

    [Fact]
    public void FindDeletedNavmeshes_EmptyMod_ReturnsNoIssues()
    {
        var mod = new Fallout4Mod(PluginKey, Fallout4Release.Fallout4);

        var issues = _sut.FindDeletedNavmeshes(mod).ToList();

        issues.Should().BeEmpty();
    }

    [Fact]
    public void FindDeletedNavmeshes_DeletedNavmesh_IsFound()
    {
        var mod = new Fallout4Mod(PluginKey, Fallout4Release.Fallout4);
        var cell = AddInteriorCell(mod);
        var navm = new NavigationMesh(mod) { IsDeleted = true };
        cell.NavigationMeshes.Add(navm);

        var issues = _sut.FindDeletedNavmeshes(mod).ToList();

        issues.Should().HaveCount(1);
        issues[0].Type.Should().Be(IssueType.DeletedNavmesh);
        issues[0].FormKey.Should().Be(navm.FormKey);
    }

    [Fact]
    public void FindDeletedNavmeshes_NonDeletedNavmesh_IsNotFlagged()
    {
        var mod = new Fallout4Mod(PluginKey, Fallout4Release.Fallout4);
        var cell = AddInteriorCell(mod);
        cell.NavigationMeshes.Add(new NavigationMesh(mod) { IsDeleted = false });

        var issues = _sut.FindDeletedNavmeshes(mod).ToList();

        issues.Should().BeEmpty();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Cell AddInteriorCell(Fallout4Mod mod)
    {
        var cell = new Cell(mod);
        var subBlock = new CellSubBlock();
        subBlock.Cells.Add(cell);
        var block = new CellBlock();
        block.SubBlocks.Add(subBlock);
        mod.Cells.Records.Add(block);
        return cell;
    }
}
