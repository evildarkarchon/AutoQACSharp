using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using QueryPlugins.Detectors.Games;
using QueryPlugins.Models;

namespace QueryPlugins.Tests.Detectors.Games;

/// <summary>
/// Unit tests for <see cref="SkyrimDetector"/> using in-memory Mutagen mod construction.
/// </summary>
public sealed class SkyrimDetectorTests
{
    private static readonly ModKey PluginKey = ModKey.FromNameAndExtension("Plugin.esp");

    private readonly SkyrimDetector _sut = new();

    // ── SupportedReleases ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(GameRelease.SkyrimLE)]
    [InlineData(GameRelease.SkyrimSE)]
    [InlineData(GameRelease.SkyrimSEGog)]
    [InlineData(GameRelease.SkyrimVR)]
    [InlineData(GameRelease.EnderalLE)]
    [InlineData(GameRelease.EnderalSE)]
    public void SupportedReleases_ContainsExpectedGame(GameRelease release)
    {
        _sut.SupportedReleases.Should().Contain(release);
    }

    // ── Wrong mod type ────────────────────────────────────────────────────────

    [Fact]
    public void FindDeletedReferences_WrongModType_ThrowsArgumentException()
    {
        var fo4Mod = new Mutagen.Bethesda.Fallout4.Fallout4Mod(PluginKey, Mutagen.Bethesda.Fallout4.Fallout4Release.Fallout4);

        var act = () => _sut.FindDeletedReferences(fo4Mod).ToList();

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FindDeletedNavmeshes_WrongModType_ThrowsArgumentException()
    {
        var fo4Mod = new Mutagen.Bethesda.Fallout4.Fallout4Mod(PluginKey, Mutagen.Bethesda.Fallout4.Fallout4Release.Fallout4);

        var act = () => _sut.FindDeletedNavmeshes(fo4Mod).ToList();

        act.Should().Throw<ArgumentException>();
    }

    // ── Deleted references ────────────────────────────────────────────────────

    [Fact]
    public void FindDeletedReferences_EmptyMod_ReturnsNoIssues()
    {
        var mod = new SkyrimMod(PluginKey, SkyrimRelease.SkyrimSE);

        var issues = _sut.FindDeletedReferences(mod).ToList();

        issues.Should().BeEmpty();
    }

    [Fact]
    public void FindDeletedReferences_DeletedPlacedObject_IsFound()
    {
        var mod = new SkyrimMod(PluginKey, SkyrimRelease.SkyrimSE);
        var cell = AddInteriorCell(mod);
        var placedObj = new PlacedObject(mod) { IsDeleted = true };
        cell.Persistent.Add(placedObj);

        var issues = _sut.FindDeletedReferences(mod).ToList();

        issues.Should().HaveCount(1);
        issues[0].Type.Should().Be(IssueType.DeletedReference);
        issues[0].FormKey.Should().Be(placedObj.FormKey);
    }

    [Fact]
    public void FindDeletedReferences_DeletedPlacedNpc_IsFound()
    {
        var mod = new SkyrimMod(PluginKey, SkyrimRelease.SkyrimSE);
        var cell = AddInteriorCell(mod);
        var placedNpc = new PlacedNpc(mod) { IsDeleted = true };
        cell.Persistent.Add(placedNpc);

        var issues = _sut.FindDeletedReferences(mod).ToList();

        issues.Should().HaveCount(1);
        issues[0].Type.Should().Be(IssueType.DeletedReference);
    }

    [Fact]
    public void FindDeletedReferences_NonDeletedPlacedObject_IsNotFlagged()
    {
        var mod = new SkyrimMod(PluginKey, SkyrimRelease.SkyrimSE);
        var cell = AddInteriorCell(mod);
        var placedObj = new PlacedObject(mod) { IsDeleted = false };
        cell.Persistent.Add(placedObj);

        var issues = _sut.FindDeletedReferences(mod).ToList();

        issues.Should().BeEmpty();
    }

    [Fact]
    public void FindDeletedReferences_DeletedInTemporary_IsFound()
    {
        var mod = new SkyrimMod(PluginKey, SkyrimRelease.SkyrimSE);
        var cell = AddInteriorCell(mod);
        var placedObj = new PlacedObject(mod) { IsDeleted = true };
        cell.Temporary.Add(placedObj);

        var issues = _sut.FindDeletedReferences(mod).ToList();

        issues.Should().HaveCount(1);
        issues[0].Type.Should().Be(IssueType.DeletedReference);
    }

    [Fact]
    public void FindDeletedReferences_MixedReferences_OnlyDeletedFlagged()
    {
        var mod = new SkyrimMod(PluginKey, SkyrimRelease.SkyrimSE);
        var cell = AddInteriorCell(mod);
        var deletedObj = new PlacedObject(mod) { IsDeleted = true };
        var aliveObj = new PlacedObject(mod) { IsDeleted = false };
        cell.Persistent.Add(deletedObj);
        cell.Persistent.Add(aliveObj);

        var issues = _sut.FindDeletedReferences(mod).ToList();

        issues.Should().HaveCount(1);
        issues[0].FormKey.Should().Be(deletedObj.FormKey);
    }

    // ── Deleted navmeshes ─────────────────────────────────────────────────────

    [Fact]
    public void FindDeletedNavmeshes_EmptyMod_ReturnsNoIssues()
    {
        var mod = new SkyrimMod(PluginKey, SkyrimRelease.SkyrimSE);

        var issues = _sut.FindDeletedNavmeshes(mod).ToList();

        issues.Should().BeEmpty();
    }

    [Fact]
    public void FindDeletedNavmeshes_DeletedNavmesh_IsFound()
    {
        var mod = new SkyrimMod(PluginKey, SkyrimRelease.SkyrimSE);
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
        var mod = new SkyrimMod(PluginKey, SkyrimRelease.SkyrimSE);
        var cell = AddInteriorCell(mod);
        cell.NavigationMeshes.Add(new NavigationMesh(mod) { IsDeleted = false });

        var issues = _sut.FindDeletedNavmeshes(mod).ToList();

        issues.Should().BeEmpty();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a minimal interior cell structure and adds it to <paramref name="mod"/>'s
    /// <c>Cells</c> list group. Returns the cell for further record insertion.
    /// </summary>
    private static Cell AddInteriorCell(SkyrimMod mod)
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
