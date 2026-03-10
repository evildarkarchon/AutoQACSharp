using Mutagen.Bethesda;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using QueryPlugins.Detectors.Games;
using QueryPlugins.Models;

namespace QueryPlugins.Tests.Detectors.Games;

/// <summary>
/// Unit tests for <see cref="OblivionDetector"/> using in-memory Mutagen mod construction.
/// Covers deleted placed references and verifies that navmesh detection is intentionally empty
/// (Oblivion uses PathGrids, not Navigation Meshes).
/// </summary>
public sealed class OblivionDetectorTests
{
    private static readonly ModKey PluginKey = ModKey.FromNameAndExtension("Plugin.esp");

    private readonly OblivionDetector _sut = new();

    // ── SupportedReleases ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(GameRelease.Oblivion)]
    [InlineData(GameRelease.OblivionRE)]
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
        var mod = new OblivionMod(PluginKey, OblivionRelease.Oblivion);

        var issues = _sut.FindDeletedReferences(mod).ToList();

        issues.Should().BeEmpty();
    }

    [Fact]
    public void FindDeletedReferences_DeletedPlacedObject_IsFound()
    {
        var mod = new OblivionMod(PluginKey, OblivionRelease.Oblivion);
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
        var mod = new OblivionMod(PluginKey, OblivionRelease.Oblivion);
        var cell = AddInteriorCell(mod);
        cell.Persistent.Add(new PlacedObject(mod) { IsDeleted = false });

        var issues = _sut.FindDeletedReferences(mod).ToList();

        issues.Should().BeEmpty();
    }

    [Fact]
    public void FindDeletedReferences_DeletedPlacedNpc_IsFound()
    {
        var mod = new OblivionMod(PluginKey, OblivionRelease.Oblivion);
        var cell = AddInteriorCell(mod);
        var placedNpc = new PlacedNpc(mod) { IsDeleted = true };
        cell.Persistent.Add(placedNpc);

        var issues = _sut.FindDeletedReferences(mod).ToList();

        issues.Should().HaveCount(1);
        issues[0].Type.Should().Be(IssueType.DeletedReference);
    }

    [Fact]
    public void FindDeletedReferences_DeletedInTemporary_IsFound()
    {
        var mod = new OblivionMod(PluginKey, OblivionRelease.Oblivion);
        var cell = AddInteriorCell(mod);
        var placedObj = new PlacedObject(mod) { IsDeleted = true };
        cell.Temporary.Add(placedObj);

        var issues = _sut.FindDeletedReferences(mod).ToList();

        issues.Should().HaveCount(1);
    }

    // ── Navmesh detection: always empty ──────────────────────────────────────

    [Fact]
    public void FindDeletedNavmeshes_AlwaysReturnsEmpty_BecauseOblivionHasNoNavmeshes()
    {
        var mod = new OblivionMod(PluginKey, OblivionRelease.Oblivion);

        // The detector should return empty regardless of what is in the mod,
        // since Oblivion uses PathGrids rather than Navigation Meshes.
        var issues = _sut.FindDeletedNavmeshes(mod).ToList();

        issues.Should().BeEmpty("Oblivion does not use Navigation Mesh records");
    }

    [Fact]
    public void FindDeletedNavmeshes_WithWrongModType_StillReturnsEmpty()
    {
        // OblivionDetector.FindDeletedNavmeshes ignores the plugin entirely
        // (returns Enumerable.Empty), so it should not throw for any input.
        var skyrimMod = new Mutagen.Bethesda.Skyrim.SkyrimMod(PluginKey, Mutagen.Bethesda.Skyrim.SkyrimRelease.SkyrimSE);

        var issues = _sut.FindDeletedNavmeshes(skyrimMod).ToList();

        issues.Should().BeEmpty();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Cell AddInteriorCell(OblivionMod mod)
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
