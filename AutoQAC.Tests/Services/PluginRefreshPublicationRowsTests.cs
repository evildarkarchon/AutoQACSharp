using AutoQAC.Models;
using AutoQAC.Services.Plugin;
using FluentAssertions;

namespace AutoQAC.Tests.Services;

public sealed class PluginRefreshPublicationRowsTests
{
    [Fact]
    public void Accept_ShouldCreateFullRowsAndVisibleProjection()
    {
        var accepted = PluginRefreshPublicationRows.Accept(
            [
                Decision("Visible.esp"),
                Decision("Hidden.esp", shouldSkip: true)
            ],
            PluginIssueApproximation.Pending,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\Data\Hidden.esp" });

        accepted.Rows.Should().HaveCount(2);
        accepted.Rows.Should().Contain(row =>
            row.Plugin.FileName == "Hidden.esp" &&
            !row.IsVisible &&
            row.IsSelected &&
            row.IsSkippedByPolicy &&
            row.Plugin.Approximation.Status == PluginIssueApproximationStatus.Pending);
        accepted.VisibleRows.Should().ContainSingle(row => row.FileName == "Visible.esp");
        accepted.VisibleRows.Should().NotContain(row => row.FileName == "Hidden.esp");
    }

    [Fact]
    public void ProjectStateVisibleRows_ShouldHideSkipListRowsAndApplyVisibleExclusions()
    {
        var visible = Plugin("Visible.esp");
        var hidden = Plugin("Hidden.esp", isInSkipList: true);

        var rows = PluginRefreshPublicationRows.ProjectStateVisibleRows(
            [visible, hidden],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { visible.FullPath });

        rows.Should().ContainSingle(row =>
            row.FileName == "Visible.esp" &&
            !row.IsSelected &&
            row.Approximation.Status == PluginIssueApproximationStatus.Unavailable);
        rows.Should().NotContain(row => row.FileName == "Hidden.esp");
    }

    [Fact]
    public void ApplySelectionChange_ShouldSelectAndDeselectOnlyVisibleRows()
    {
        var rows = new[]
        {
            Published("Visible.esp", isVisible: true, isSelected: true),
            Published("Hidden.esp", isVisible: false, isSelected: true, isSkippedByPolicy: true)
        };

        var deselected = PluginRefreshPublicationRows.ApplySelectionChange(
            rows,
            new PluginSelectionChange.DeselectAllVisible());
        var reselected = PluginRefreshPublicationRows.ApplySelectionChange(
            deselected.Commit.Rows,
            new PluginSelectionChange.SelectAllVisible());

        deselected.WasTargetFound.Should().BeTrue();
        deselected.Commit.Rows.Should().Contain(row => row.Plugin.FileName == "Visible.esp" && !row.IsSelected);
        deselected.Commit.Rows.Should().Contain(row => row.Plugin.FileName == "Hidden.esp" && row.IsSelected);
        deselected.Commit.Mirror.ExcludedPluginPaths.Should().ContainSingle()
            .Which.Should().Be(@"C:\Data\Visible.esp");

        reselected.Commit.Rows.Should().OnlyContain(row => row.IsSelected);
        reselected.Commit.Mirror.ExcludedPluginPaths.Should().BeEmpty();
    }

    [Fact]
    public void ApplySelectionChange_SetOne_ShouldMutateSingleVisibleRowByFullPath()
    {
        var rows = new[]
        {
            Published("Duplicate.esp", @"C:\A\Duplicate.esp"),
            Published("Duplicate.esp", @"C:\B\Duplicate.esp")
        };

        var changed = PluginRefreshPublicationRows.ApplySelectionChange(
            rows,
            new PluginSelectionChange.SetOne(new PluginRefreshRowKey("Duplicate.esp", @"C:\B\Duplicate.esp"), false));

        changed.WasTargetFound.Should().BeTrue();
        changed.Commit.Rows.Should().Contain(row => row.Plugin.FullPath == @"C:\A\Duplicate.esp" && row.IsSelected);
        changed.Commit.Rows.Should().Contain(row => row.Plugin.FullPath == @"C:\B\Duplicate.esp" && !row.IsSelected);
    }

    [Fact]
    public void ApplySelectionChange_SetOne_ShouldFallbackToFileNameWhenAPathIsMissing()
    {
        var rows = new[]
        {
            Published("Fallback.esp", fullPath: string.Empty)
        };

        var changed = PluginRefreshPublicationRows.ApplySelectionChange(
            rows,
            new PluginSelectionChange.SetOne(new PluginRefreshRowKey("Fallback.esp", @"C:\Data\Fallback.esp"), false));

        changed.WasTargetFound.Should().BeTrue();
        changed.Commit.Rows.Should().ContainSingle(row =>
            row.Plugin.FileName == "Fallback.esp" && !row.IsSelected);
    }

    [Fact]
    public void ApplySelectionChange_SetOne_ShouldIgnoreHiddenRows()
    {
        var hidden = Published("Hidden.esp", isVisible: false, isSelected: true, isSkippedByPolicy: true);

        var changed = PluginRefreshPublicationRows.ApplySelectionChange(
            [hidden],
            new PluginSelectionChange.SetOne(hidden.Key, false));

        changed.WasTargetFound.Should().BeFalse();
        changed.Commit.Rows.Should().ContainSingle(row =>
            row.Plugin.FileName == "Hidden.esp" && row.IsSelected);
    }

    [Fact]
    public void ApplyStateSelectionChange_ShouldUpdateCompatibilityExclusionsForVisibleRows()
    {
        var rows = new[]
        {
            new PluginRefreshRow(
                "Visible.esp",
                @"C:\Data\Visible.esp",
                GameType.SkyrimSe,
                IsSelected: true,
                IsInSkipList: false,
                PluginIssueApproximation.Unavailable)
        };

        var deselected = PluginRefreshPublicationRows.ApplyStateSelectionChange(
            rows,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new PluginSelectionChange.DeselectAllVisible());
        var reselected = PluginRefreshPublicationRows.ApplyStateSelectionChange(
            rows,
            deselected.ExcludedPluginPaths,
            new PluginSelectionChange.SetOne(rows[0].Key, true));

        deselected.WasTargetFound.Should().BeTrue();
        deselected.ExcludedPluginPaths.Should().ContainSingle().Which.Should().Be(@"C:\Data\Visible.esp");
        reselected.WasTargetFound.Should().BeTrue();
        reselected.ExcludedPluginPaths.Should().BeEmpty();
    }

    [Fact]
    public void ApplyApproximationUpdates_ShouldApplyPendingUnavailableAndAvailableToMatchingRows()
    {
        var rows = new[]
        {
            Published("Target.esp", approximation: PluginIssueApproximation.Unavailable),
            Published("Other.esp", approximation: PluginIssueApproximation.Unavailable)
        };
        var lookup = PluginRefreshPublicationRows.CreateTargetLookup(
            [new PluginRefreshRowKey("Target.esp", @"C:\Data\Target.esp")]);

        var pending = PluginRefreshPublicationRows.ApplyApproximationToTargets(
            rows,
            lookup,
            PluginIssueApproximation.Pending);
        var available = PluginRefreshPublicationRows.ApplyApproximationResult(
            pending.Commit.Rows,
            Result("Target.esp", @"C:\Data\Target.esp", PluginIssueApproximation.Available(4, 2, 1)));
        var unavailable = PluginRefreshPublicationRows.ApplyApproximationToTargets(
            available.Commit.Rows,
            lookup,
            PluginIssueApproximation.Unavailable);

        pending.Matched.Should().BeTrue();
        pending.Commit.Rows.Should().Contain(row =>
            row.Plugin.FileName == "Target.esp" &&
            row.Plugin.Approximation.Status == PluginIssueApproximationStatus.Pending);
        available.Commit.Rows.Should().Contain(row =>
            row.Plugin.FileName == "Target.esp" &&
            row.Plugin.Approximation.Status == PluginIssueApproximationStatus.Available &&
            row.Plugin.Approximation.ItmCount == 4);
        unavailable.Commit.Rows.Should().Contain(row =>
            row.Plugin.FileName == "Target.esp" &&
            row.Plugin.Approximation.Status == PluginIssueApproximationStatus.Unavailable);
        unavailable.Commit.Rows.Should().Contain(row =>
            row.Plugin.FileName == "Other.esp" &&
            row.Plugin.Approximation.Status == PluginIssueApproximationStatus.Unavailable);
    }

    [Fact]
    public void ApplyApproximationResult_ShouldPreferFullPathAndUsePathlessFallback()
    {
        var rows = new[]
        {
            Published("Duplicate.esp", @"C:\A\Duplicate.esp", approximation: PluginIssueApproximation.Pending),
            Published("Duplicate.esp", @"C:\B\Duplicate.esp", approximation: PluginIssueApproximation.Pending),
            Published("Fallback.esp", string.Empty, approximation: PluginIssueApproximation.Pending)
        };

        var fullPathMatch = PluginRefreshPublicationRows.ApplyApproximationResult(
            rows,
            Result("Duplicate.esp", @"C:\B\Duplicate.esp", PluginIssueApproximation.Available(1, 0, 0)));
        var fallbackMatch = PluginRefreshPublicationRows.ApplyApproximationResult(
            fullPathMatch.Commit.Rows,
            Result("Fallback.esp", @"C:\Data\Fallback.esp", PluginIssueApproximation.Available(2, 0, 0)));

        fallbackMatch.Commit.Rows.Should().Contain(row =>
            row.Plugin.FullPath == @"C:\A\Duplicate.esp" &&
            row.Plugin.Approximation.Status == PluginIssueApproximationStatus.Pending);
        fallbackMatch.Commit.Rows.Should().Contain(row =>
            row.Plugin.FullPath == @"C:\B\Duplicate.esp" &&
            row.Plugin.Approximation.ItmCount == 1);
        fallbackMatch.Commit.Rows.Should().Contain(row =>
            row.Plugin.FileName == "Fallback.esp" &&
            row.Plugin.Approximation.ItmCount == 2);
    }

    [Fact]
    public void Commit_ShouldMirrorFullRowsAndVisibleDeselectionsOnly()
    {
        var hidden = Published("Hidden.esp", isVisible: false, isSelected: false, isSkippedByPolicy: true);

        var committed = PluginRefreshPublicationRows.Commit(
            [
                Published("VisibleSelected.esp", isSelected: true),
                Published("VisibleDeselected.esp", isSelected: false),
                hidden
            ]);

        committed.Mirror.PluginsToClean.Select(plugin => plugin.FileName)
            .Should().Equal("VisibleSelected.esp", "VisibleDeselected.esp", "Hidden.esp");
        committed.Mirror.ExcludedPluginPaths.Should().ContainSingle()
            .Which.Should().Be(@"C:\Data\VisibleDeselected.esp");
        committed.Mirror.ExcludedPluginPaths.Should().NotContain(hidden.Plugin.FullPath);
    }

    private static SkipListPluginDecision Decision(string fileName, bool shouldSkip = false) =>
        new(
            Plugin(fileName, isInSkipList: shouldSkip),
            IsInEffectiveSkipList: shouldSkip,
            ShouldSkipByPolicy: shouldSkip);

    private static PluginRefreshPublishedRow Published(
        string fileName,
        string? fullPath = null,
        bool isVisible = true,
        bool isSelected = true,
        bool isSkippedByPolicy = false,
        PluginIssueApproximation? approximation = null)
    {
        var plugin = Plugin(
            fileName,
            fullPath,
            isInSkipList: isSkippedByPolicy,
            approximation: approximation);
        return new PluginRefreshPublishedRow(
            plugin,
            isVisible,
            isSelected,
            isSkippedByPolicy,
            new PluginRefreshRowKey(plugin.FileName, plugin.FullPath));
    }

    private static PluginInfo Plugin(
        string fileName,
        string? fullPath = null,
        bool isInSkipList = false,
        PluginIssueApproximation? approximation = null) =>
        new()
        {
            FileName = fileName,
            FullPath = fullPath ?? $@"C:\Data\{fileName}",
            IsInSkipList = isInSkipList,
            DetectedGameType = GameType.SkyrimSe,
            Approximation = approximation ?? PluginIssueApproximation.Unavailable
        };

    private static PluginIssueApproximationResult Result(
        string fileName,
        string fullPath,
        PluginIssueApproximation approximation) =>
        new()
        {
            FileName = fileName,
            FullPath = fullPath,
            Approximation = approximation
        };
}
