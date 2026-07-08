using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using AutoQAC.Models;

namespace AutoQAC.Services.Plugin;

/// <summary>
/// Pure row transformer for Plugin refresh publications.
/// </summary>
internal static class PluginRefreshPublicationRows
{
    /// <summary>
    /// Creates the authoritative row set for an accepted Plugin refresh publication.
    /// </summary>
    /// <param name="decisions">Skip list decisions produced for the discovered plugins.</param>
    /// <param name="initialApproximation">Initial approximation state to stamp onto every row.</param>
    /// <param name="excludedPaths">Current compatibility deselection paths from AppState.</param>
    /// <returns>Rows, visible projection, and AppState mirror facts for the publication.</returns>
    internal static PluginRefreshPublicationRowsCommit Accept(
        IReadOnlyList<SkipListPluginDecision> decisions,
        PluginIssueApproximation initialApproximation,
        IReadOnlySet<string> excludedPaths)
    {
        var rows = decisions.Select(decision =>
            {
                var plugin = decision.Plugin with { Approximation = initialApproximation };
                return new PluginRefreshPublishedRow(
                    plugin,
                    IsVisible: !decision.ShouldSkipByPolicy,
                    IsSelected: decision.ShouldSkipByPolicy || !excludedPaths.Contains(plugin.FullPath),
                    IsSkippedByPolicy: decision.ShouldSkipByPolicy,
                    new PluginRefreshRowKey(plugin.FileName, plugin.FullPath));
            })
            .ToList();

        return Commit(rows);
    }

    /// <summary>
    /// Builds a commit result for an already authoritative full publication row list.
    /// </summary>
    /// <param name="rows">Full publication rows, including hidden Skip list rows.</param>
    /// <returns>Rows, visible projection, and AppState mirror facts.</returns>
    internal static PluginRefreshPublicationRowsCommit Commit(IReadOnlyList<PluginRefreshPublishedRow> rows) =>
        new(rows, ProjectVisibleRows(rows), CreateMirror(rows));

    /// <summary>
    /// Projects full publication rows into the visible row surface consumed by ViewModels.
    /// </summary>
    /// <param name="rows">Full publication rows, including hidden rows.</param>
    /// <returns>Visible rows with selection and approximation facts copied from the full rows.</returns>
    internal static IReadOnlyList<PluginRefreshRow> ProjectVisibleRows(
        IReadOnlyList<PluginRefreshPublishedRow> rows) =>
        rows.Where(row => row.IsVisible)
            .Select(row => new PluginRefreshRow(
                row.Plugin.FileName,
                row.Plugin.FullPath,
                row.Plugin.DetectedGameType,
                row.IsSelected,
                row.Plugin.IsInSkipList,
                row.Plugin.Approximation))
            .ToList();

    /// <summary>
    /// Projects legacy AppState rows into visible Plugin refresh rows when no publication is available yet.
    /// </summary>
    /// <param name="plugins">Compatibility plugin rows from AppState.</param>
    /// <param name="excludedPaths">Compatibility deselection paths from AppState.</param>
    /// <returns>Visible rows with hidden Skip list rows removed.</returns>
    internal static IReadOnlyList<PluginRefreshRow> ProjectStateVisibleRows(
        IReadOnlyList<PluginInfo> plugins,
        IReadOnlySet<string> excludedPaths) =>
        plugins.Where(plugin => !plugin.IsInSkipList)
            .Select(plugin => new PluginRefreshRow(
                plugin.FileName,
                plugin.FullPath,
                plugin.DetectedGameType,
                IsSelected: !excludedPaths.Contains(plugin.FullPath),
                plugin.IsInSkipList,
                plugin.Approximation))
            .ToList();

    /// <summary>
    /// Applies a user selection change to visible publication rows only.
    /// </summary>
    /// <param name="rows">Full publication rows.</param>
    /// <param name="change">Selection mutation requested by the UI.</param>
    /// <returns>A selection result containing the updated rows when the change had a visible target.</returns>
    internal static PluginRefreshPublicationRowsSelectionResult ApplySelectionChange(
        IReadOnlyList<PluginRefreshPublishedRow> rows,
        PluginSelectionChange change)
    {
        switch (change)
        {
            case PluginSelectionChange.SelectAllVisible:
                return new PluginRefreshPublicationRowsSelectionResult(
                    true,
                    Commit(rows.Select(row => row.IsVisible ? row with { IsSelected = true } : row).ToList()));

            case PluginSelectionChange.DeselectAllVisible:
                return new PluginRefreshPublicationRowsSelectionResult(
                    true,
                    Commit(rows.Select(row => row.IsVisible ? row with { IsSelected = false } : row).ToList()));

            case PluginSelectionChange.SetOne setOne:
            {
                var found = rows.Any(row => row.IsVisible && IsMatch(row.Key, setOne.Row));
                if (!found)
                {
                    return new PluginRefreshPublicationRowsSelectionResult(false, Commit(rows));
                }

                var updatedRows = rows
                    .Select(row => row.IsVisible && IsMatch(row.Key, setOne.Row)
                        ? row with { IsSelected = setOne.IsSelected }
                        : row)
                    .ToList();
                return new PluginRefreshPublicationRowsSelectionResult(true, Commit(updatedRows));
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(change), change, "Unknown Plugin selection change.");
        }
    }

    /// <summary>
    /// Applies a selection change to legacy AppState exclusion facts when no publication is available yet.
    /// </summary>
    /// <param name="visibleRows">Current visible snapshot rows.</param>
    /// <param name="excludedPaths">Current compatibility exclusion paths.</param>
    /// <param name="change">Selection mutation requested by the UI.</param>
    /// <returns>Updated exclusion paths when the request addressed visible rows.</returns>
    internal static PluginRefreshPublicationRowsStateSelectionResult ApplyStateSelectionChange(
        IReadOnlyList<PluginRefreshRow> visibleRows,
        IReadOnlySet<string> excludedPaths,
        PluginSelectionChange change)
    {
        if (visibleRows.Count == 0)
        {
            return new PluginRefreshPublicationRowsStateSelectionResult(false, excludedPaths);
        }

        switch (change)
        {
            case PluginSelectionChange.SelectAllVisible:
            {
                var next = new HashSet<string>(excludedPaths, StringComparer.OrdinalIgnoreCase);
                foreach (var row in visibleRows)
                {
                    next.Remove(row.FullPath);
                }

                return new PluginRefreshPublicationRowsStateSelectionResult(
                    true,
                    next.ToFrozenSet(StringComparer.OrdinalIgnoreCase));
            }

            case PluginSelectionChange.DeselectAllVisible:
            {
                var next = new HashSet<string>(excludedPaths, StringComparer.OrdinalIgnoreCase);
                foreach (var row in visibleRows)
                {
                    next.Add(row.FullPath);
                }

                return new PluginRefreshPublicationRowsStateSelectionResult(
                    true,
                    next.ToFrozenSet(StringComparer.OrdinalIgnoreCase));
            }

            case PluginSelectionChange.SetOne setOne:
            {
                var row = visibleRows.FirstOrDefault(visible => IsMatch(visible, setOne.Row));
                if (row is null)
                {
                    return new PluginRefreshPublicationRowsStateSelectionResult(false, excludedPaths);
                }

                var alreadyExcluded = excludedPaths.Contains(row.FullPath);
                if (setOne.IsSelected ? !alreadyExcluded : alreadyExcluded)
                {
                    return new PluginRefreshPublicationRowsStateSelectionResult(true, excludedPaths);
                }

                var next = new HashSet<string>(excludedPaths, StringComparer.OrdinalIgnoreCase);
                if (setOne.IsSelected)
                {
                    next.Remove(row.FullPath);
                }
                else
                {
                    next.Add(row.FullPath);
                }

                return new PluginRefreshPublicationRowsStateSelectionResult(
                    true,
                    next.ToFrozenSet(StringComparer.OrdinalIgnoreCase));
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(change), change, "Unknown Plugin selection change.");
        }
    }

    /// <summary>
    /// Marks all rows contained in a target lookup with the supplied approximation state.
    /// </summary>
    /// <param name="rows">Full publication rows.</param>
    /// <param name="targetLookup">Target set used to identify rows.</param>
    /// <param name="approximation">Approximation state to apply to target rows.</param>
    /// <returns>An update result that reports whether any row matched.</returns>
    internal static PluginRefreshPublicationRowsUpdate ApplyApproximationToTargets(
        IReadOnlyList<PluginRefreshPublishedRow> rows,
        TargetLookup targetLookup,
        PluginIssueApproximation approximation) =>
        UpdateRows(
            rows,
            row => targetLookup.Contains(row.Plugin),
            row => row with { Plugin = row.Plugin with { Approximation = approximation } });

    /// <summary>
    /// Marks target rows that are still pending unavailable while preserving completed target results.
    /// </summary>
    /// <param name="rows">Full publication rows.</param>
    /// <param name="targetLookup">Target set used to identify rows.</param>
    /// <returns>An update result that reports whether any target row matched.</returns>
    internal static PluginRefreshPublicationRowsUpdate ApplyUnavailableToPendingTargets(
        IReadOnlyList<PluginRefreshPublishedRow> rows,
        TargetLookup targetLookup) =>
        UpdateRows(
            rows,
            row => targetLookup.Contains(row.Plugin),
            row => row.Plugin.Approximation.Status == PluginIssueApproximationStatus.Pending
                ? row with { Plugin = row.Plugin with { Approximation = PluginIssueApproximation.Unavailable } }
                : row);

    /// <summary>
    /// Applies one issue approximation result to its matching publication row.
    /// </summary>
    /// <param name="rows">Full publication rows.</param>
    /// <param name="result">Approximation result to merge.</param>
    /// <returns>An update result that reports whether any row matched.</returns>
    internal static PluginRefreshPublicationRowsUpdate ApplyApproximationResult(
        IReadOnlyList<PluginRefreshPublishedRow> rows,
        PluginIssueApproximationResult result) =>
        UpdateRows(
            rows,
            row => IsMatch(row.Plugin, result),
            row => row with { Plugin = row.Plugin with { Approximation = result.Approximation } });

    /// <summary>
    /// Creates a lookup optimized for matching approximation targets and results.
    /// </summary>
    /// <param name="targets">Target row identities.</param>
    /// <returns>A lookup that preserves the publication row identity fallback rules.</returns>
    internal static TargetLookup CreateTargetLookup(IReadOnlyList<PluginRefreshRowKey> targets) =>
        TargetLookup.Create(targets);

    /// <summary>
    /// Creates minimal plugin rows from approximation targets for fallback state updates.
    /// </summary>
    /// <param name="gameType">Game type assigned to generated rows.</param>
    /// <param name="targets">Target row identities.</param>
    /// <returns>Pending plugin rows for the supplied targets.</returns>
    internal static IReadOnlyList<PluginInfo> CreateRowsFromTargets(
        GameType gameType,
        IReadOnlyList<PluginRefreshRowKey> targets) =>
        targets.Select(target => new PluginInfo
        {
            FileName = target.FileName,
            FullPath = target.FullPath,
            DetectedGameType = gameType,
            Approximation = PluginIssueApproximation.Pending
        }).ToList();

    /// <summary>
    /// Determines whether a visible row identity matches a requested row key.
    /// </summary>
    /// <param name="row">Visible row identity.</param>
    /// <param name="target">Requested row identity.</param>
    /// <returns>True when the full path matches, or when either side lacks a path and names match.</returns>
    internal static bool IsMatch(PluginRefreshRow row, PluginRefreshRowKey target)
    {
        if (HasUsablePath(row.FullPath) && HasUsablePath(target.FullPath))
        {
            return string.Equals(row.FullPath, target.FullPath, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(row.FileName, target.FileName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines whether two row keys identify the same publication row.
    /// </summary>
    /// <param name="row">Existing row identity.</param>
    /// <param name="target">Requested row identity.</param>
    /// <returns>True when the full path matches, or when either side lacks a path and names match.</returns>
    internal static bool IsMatch(PluginRefreshRowKey row, PluginRefreshRowKey target)
    {
        if (HasUsablePath(row.FullPath) && HasUsablePath(target.FullPath))
        {
            return string.Equals(row.FullPath, target.FullPath, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(row.FileName, target.FileName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines whether an approximation result identifies a plugin row.
    /// </summary>
    /// <param name="plugin">Publication plugin row.</param>
    /// <param name="result">Approximation result.</param>
    /// <returns>True when the full path matches, or when either side lacks a path and names match.</returns>
    internal static bool IsMatch(PluginInfo plugin, PluginIssueApproximationResult result)
    {
        if (HasUsablePath(plugin.FullPath) && HasUsablePath(result.FullPath))
        {
            return string.Equals(plugin.FullPath, result.FullPath, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(plugin.FileName, result.FileName, StringComparison.OrdinalIgnoreCase);
    }

    private static PluginRefreshPublicationRowsMirror CreateMirror(
        IReadOnlyList<PluginRefreshPublishedRow> rows)
    {
        // AppState remains a compatibility adapter, so exclusions represent only user-visible
        // deselections. Hidden Skip list rows are mirrored as full facts but not as exclusions.
        var plugins = rows.Select(row => row.Plugin).ToList();
        var excludedPaths = rows
            .Where(row => row.IsVisible && !row.IsSelected)
            .Select(row => row.Plugin.FullPath)
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        return new PluginRefreshPublicationRowsMirror(plugins, excludedPaths);
    }

    private static PluginRefreshPublicationRowsUpdate UpdateRows(
        IReadOnlyList<PluginRefreshPublishedRow> rows,
        Func<PluginRefreshPublishedRow, bool> shouldUpdate,
        Func<PluginRefreshPublishedRow, PluginRefreshPublishedRow> update)
    {
        var matched = false;
        var updatedRows = rows.Select(row =>
        {
            if (!shouldUpdate(row))
            {
                return row;
            }

            matched = true;
            return update(row);
        }).ToList();

        return new PluginRefreshPublicationRowsUpdate(matched, Commit(matched ? updatedRows : rows));
    }

    private static bool HasUsablePath(string? path) => !string.IsNullOrWhiteSpace(path);

    /// <summary>
    /// Lookup for publication row targets that preserves path-first, name-fallback identity.
    /// </summary>
    internal sealed class TargetLookup
    {
        private readonly IReadOnlySet<string> _fullPaths;
        private readonly IReadOnlySet<string> _fileNames;
        private readonly IReadOnlySet<string> _pathlessFileNames;

        private TargetLookup(
            IReadOnlySet<string> fullPaths,
            IReadOnlySet<string> fileNames,
            IReadOnlySet<string> pathlessFileNames,
            int count)
        {
            _fullPaths = fullPaths;
            _fileNames = fileNames;
            _pathlessFileNames = pathlessFileNames;
            Count = count;
        }

        /// <summary>
        /// Gets the number of original targets in the lookup.
        /// </summary>
        internal int Count { get; }

        /// <summary>
        /// Creates a target lookup from row identities.
        /// </summary>
        /// <param name="targets">Target row identities.</param>
        /// <returns>A lookup keyed by full path and file name fallback.</returns>
        internal static TargetLookup Create(IReadOnlyList<PluginRefreshRowKey> targets) =>
            new(
                targets.Where(target => HasUsablePath(target.FullPath))
                    .Select(target => target.FullPath)
                    .ToFrozenSet(StringComparer.OrdinalIgnoreCase),
                targets.Select(target => target.FileName)
                    .ToFrozenSet(StringComparer.OrdinalIgnoreCase),
                targets.Where(target => !HasUsablePath(target.FullPath))
                    .Select(target => target.FileName)
                    .ToFrozenSet(StringComparer.OrdinalIgnoreCase),
                targets.Count);

        /// <summary>
        /// Determines whether a plugin row belongs to this target set.
        /// </summary>
        /// <param name="plugin">Plugin row to test.</param>
        /// <returns>True when the plugin matches by full path or pathless file-name fallback.</returns>
        internal bool Contains(PluginInfo plugin) => Contains(plugin.FileName, plugin.FullPath);

        /// <summary>
        /// Determines whether an approximation result belongs to this target set.
        /// </summary>
        /// <param name="result">Approximation result to test.</param>
        /// <returns>True when the result matches by full path or pathless file-name fallback.</returns>
        internal bool Contains(PluginIssueApproximationResult result) => Contains(result.FileName, result.FullPath);

        private bool Contains(string fileName, string? fullPath) =>
            HasUsablePath(fullPath)
                ? _fullPaths.Contains(fullPath!) || _pathlessFileNames.Contains(fileName)
                : _fileNames.Contains(fileName);
    }
}

/// <summary>
/// Row commit produced by the Plugin refresh publication row transformer.
/// </summary>
/// <param name="Rows">Full publication rows, including hidden Skip list rows.</param>
/// <param name="VisibleRows">Visible row projection derived from the full rows.</param>
/// <param name="Mirror">Compatibility AppState facts derived from the full rows.</param>
internal sealed record PluginRefreshPublicationRowsCommit(
    IReadOnlyList<PluginRefreshPublishedRow> Rows,
    IReadOnlyList<PluginRefreshRow> VisibleRows,
    PluginRefreshPublicationRowsMirror Mirror);

/// <summary>
/// Result of applying a selection change to publication rows.
/// </summary>
/// <param name="WasTargetFound">True when the selection request addressed one or more visible rows.</param>
/// <param name="Commit">Rows, projection, and mirror facts after applying the request.</param>
internal sealed record PluginRefreshPublicationRowsSelectionResult(
    bool WasTargetFound,
    PluginRefreshPublicationRowsCommit Commit);

/// <summary>
/// Result of applying a selection change to legacy AppState exclusion facts.
/// </summary>
/// <param name="WasTargetFound">True when the selection request addressed visible rows.</param>
/// <param name="ExcludedPluginPaths">Updated compatibility exclusion paths.</param>
internal sealed record PluginRefreshPublicationRowsStateSelectionResult(
    bool WasTargetFound,
    IReadOnlySet<string> ExcludedPluginPaths);

/// <summary>
/// Result of applying an approximation update to publication rows.
/// </summary>
/// <param name="Matched">True when one or more rows matched the update target.</param>
/// <param name="Commit">Rows, projection, and mirror facts after applying the update.</param>
internal sealed record PluginRefreshPublicationRowsUpdate(
    bool Matched,
    PluginRefreshPublicationRowsCommit Commit);

/// <summary>
/// Compatibility facts to mirror from a Plugin refresh publication into AppState.
/// </summary>
/// <param name="PluginsToClean">Full plugin rows exposed for legacy Cleaning session compatibility.</param>
/// <param name="ExcludedPluginPaths">Visible deselections exposed for legacy selection compatibility.</param>
internal sealed record PluginRefreshPublicationRowsMirror(
    IReadOnlyList<PluginInfo> PluginsToClean,
    IReadOnlySet<string> ExcludedPluginPaths);
