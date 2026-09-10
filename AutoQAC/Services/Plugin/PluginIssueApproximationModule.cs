using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;
using AutoQAC.Services.GameCapability;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using QueryPlugins;

namespace AutoQAC.Services.Plugin;

/// <summary>
/// Production Issue approximation module that owns load-order import, target selection, and keyed publication.
/// </summary>
/// <param name="pluginQueryService">Production QueryPlugins seam used for target-specific analysis.</param>
public sealed class PluginIssueApproximationModule(IPluginQueryService pluginQueryService)
    : IPluginIssueApproximationModule
{
    private static readonly StringComparer WindowsPathComparer = StringComparer.OrdinalIgnoreCase;

    /// <inheritdoc />
    public Task AnalyzeAsync(
        PluginIssueApproximationModuleRequest request,
        Action<PluginIssueApproximationModuleResult> onResult,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(onResult);

        var validated = ValidateRequest(request);
        if (!GameCapabilityCatalog.Get(request.GameType).SupportsIssueApproximation)
        {
            return Task.CompletedTask;
        }

        return Task.Run(
            () => Analyze(validated, onResult, ct),
            ct);
    }

    private void Analyze(
        ValidatedRequest request,
        Action<PluginIssueApproximationModuleResult> onResult,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var release = MapToGameRelease(request.GameType);
        var sourceRows = request.Source switch
        {
            ValidatedSource.DirectDataFolder direct => LoadOrder
                .GetLoadOrderListings(
                    release,
                    new DirectoryPath(direct.DataFolder),
                    throwOnMissingMods: false)
                .Select(listing => new ValidatedRow(
                    listing.ModKey,
                    Path.GetFullPath(Path.Combine(direct.DataFolder, listing.ModKey.FileName.String))))
                .ToArray(),
            ValidatedSource.ResolvedLoadOrder resolved => resolved.Rows,
            _ => throw new ArgumentOutOfRangeException(nameof(request))
        };

        ct.ThrowIfCancellationRequested();
        AnalyzeRows(request, sourceRows, release, onResult, ct);
    }

    private void AnalyzeRows(
        ValidatedRequest request,
        IReadOnlyList<ValidatedRow> sourceRows,
        GameRelease release,
        Action<PluginIssueApproximationModuleResult> onResult,
        CancellationToken ct)
    {
        var importedByPath = new Dictionary<string, IModGetter>(WindowsPathComparer);
        var importedInOrder = new List<IModGetter>(sourceRows.Count);
        var targetPaths = request.Targets
            .Select(target => target.NormalizedFullPath)
            .ToHashSet(WindowsPathComparer);

        foreach (var row in sourceRows)
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(row.FullPath))
            {
                if (!targetPaths.Contains(row.FullPath))
                {
                    throw new FileNotFoundException(
                        $"Issue approximation dependency context row '{row.ModKey.FileName}' disappeared.",
                        row.FullPath);
                }

                continue;
            }

            try
            {
                var plugin = ImportPlugin(release, row.ModKey, row.FullPath);
                importedByPath[row.FullPath] = plugin;
                importedInOrder.Add(plugin);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch when (targetPaths.Contains(row.FullPath))
            {
                // An unreadable target is terminal for that row, while non-target context failures abort setup.
            }
        }

        ct.ThrowIfCancellationRequested();
        ILinkCache linkCache = importedInOrder.ToUntypedImmutableLinkCache(release.ToCategory());

        foreach (var target in request.Targets)
        {
            ct.ThrowIfCancellationRequested();

            PluginIssueApproximation approximation;
            if (!importedByPath.TryGetValue(target.NormalizedFullPath, out var plugin))
            {
                approximation = PluginIssueApproximation.Unavailable;
            }
            else
            {
                try
                {
                    var analysis = pluginQueryService.Analyse(plugin, linkCache, release, ct);
                    // Cancellation can race a successful query return, so no active-target result exists until this check.
                    ct.ThrowIfCancellationRequested();
                    approximation = PluginIssueApproximation.Available(
                        analysis.ItmCount,
                        analysis.DeletedReferenceCount,
                        analysis.DeletedNavmeshCount);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    approximation = PluginIssueApproximation.Unavailable;
                }
            }

            // The target object, rather than reconstructed filename/path fields, is the publication identity.
            // This last cancellation boundary prevents callbacks once cancellation has been observed between targets.
            ct.ThrowIfCancellationRequested();
            onResult(new PluginIssueApproximationModuleResult(target.Original, approximation));
        }
    }

    private static IModGetter ImportPlugin(GameRelease release, ModKey modKey, string fullPath) => release switch
    {
        GameRelease.SkyrimLE or GameRelease.SkyrimSE or GameRelease.SkyrimVR =>
            SkyrimMod.CreateFromBinary(
                new ModPath(modKey, fullPath),
                release.ToSkyrimRelease()),
        GameRelease.Fallout4 or GameRelease.Fallout4VR =>
            Fallout4Mod.CreateFromBinary(
                new ModPath(modKey, fullPath),
                release.ToFallout4Release()),
        _ => throw new ArgumentException($"Game release {release} is not supported by Issue approximation.")
    };

    private static ValidatedRequest ValidateRequest(PluginIssueApproximationModuleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Source);
        ArgumentNullException.ThrowIfNull(request.Targets);

        var targets = new List<ValidatedTarget>(request.Targets.Count);
        var targetPaths = new HashSet<string>(WindowsPathComparer);
        foreach (var target in request.Targets)
        {
            var validatedTarget = ValidateTarget(target, nameof(request.Targets));
            if (!targetPaths.Add(validatedTarget.NormalizedFullPath))
            {
                throw new ArgumentException(
                    $"Duplicate Issue approximation target path '{target.FullPath}'.",
                    nameof(request));
            }

            targets.Add(validatedTarget);
        }

        ValidatedSource source = request.Source switch
        {
            PluginIssueApproximationModuleSource.DirectDataFolder direct =>
                new ValidatedSource.DirectDataFolder(ValidateFolder(direct.DataFolder, nameof(direct.DataFolder))),
            PluginIssueApproximationModuleSource.ResolvedLoadOrder resolved =>
                ValidateResolvedSource(resolved, targets),
            _ => throw new ArgumentException("Unknown Issue approximation source.", nameof(request))
        };

        return new ValidatedRequest(request.GameType, source, targets);
    }

    private static ValidatedSource.ResolvedLoadOrder ValidateResolvedSource(
        PluginIssueApproximationModuleSource.ResolvedLoadOrder source,
        IReadOnlyList<ValidatedTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(source.Rows);
        var baseDataFolder = ValidateFolder(source.BaseDataFolder, nameof(source.BaseDataFolder));
        var rows = new List<ValidatedRow>(source.Rows.Count);
        var pathsByFileName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in source.Rows)
        {
            var validatedRow = ValidateTarget(row, nameof(source.Rows));
            if (!pathsByFileName.TryAdd(row.FileName, validatedRow.NormalizedFullPath))
            {
                throw new ArgumentException(
                    $"Resolved Issue approximation source contains duplicate filename '{row.FileName}'.",
                    nameof(source));
            }

            rows.Add(new ValidatedRow(
                ModKey.FromFileName(row.FileName),
                validatedRow.NormalizedFullPath));
        }

        foreach (var target in targets)
        {
            if (!pathsByFileName.TryGetValue(target.Original.FileName, out var sourcePath) ||
                !WindowsPathComparer.Equals(sourcePath, target.NormalizedFullPath))
            {
                throw new ArgumentException(
                    $"Target '{target.Original.FileName}' is absent from the resolved source.",
                    nameof(source));
            }
        }

        return new ValidatedSource.ResolvedLoadOrder(baseDataFolder, rows);
    }

    private static ValidatedTarget ValidateTarget(PluginRefreshRowKey target, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (string.IsNullOrWhiteSpace(target.FileName) ||
            string.IsNullOrWhiteSpace(target.FullPath) ||
            !Path.IsPathFullyQualified(target.FullPath))
        {
            throw new ArgumentException(
                "Every Issue approximation target requires a filename and rooted full path.",
                parameterName);
        }

        var normalizedPath = Path.GetFullPath(target.FullPath);
        if (!string.Equals(
                target.FileName,
                Path.GetFileName(normalizedPath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Target filename '{target.FileName}' does not match its path '{target.FullPath}'.",
                parameterName);
        }

        return new ValidatedTarget(target, normalizedPath);
    }

    private static string ValidateFolder(string folder, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder))
        {
            throw new ArgumentException("Issue approximation source folders must be rooted.", parameterName);
        }

        return Path.GetFullPath(folder);
    }

    private static GameRelease MapToGameRelease(GameType gameType) => gameType switch
    {
        GameType.SkyrimLe => GameRelease.SkyrimLE,
        GameType.SkyrimSe => GameRelease.SkyrimSE,
        GameType.SkyrimVr => GameRelease.SkyrimVR,
        GameType.Fallout4 => GameRelease.Fallout4,
        GameType.Fallout4Vr => GameRelease.Fallout4VR,
        _ => throw new ArgumentException($"Game {gameType} is not supported by Issue approximation.")
    };

    private sealed record ValidatedRequest(
        GameType GameType,
        ValidatedSource Source,
        IReadOnlyList<ValidatedTarget> Targets);

    private abstract record ValidatedSource
    {
        public sealed record DirectDataFolder(string DataFolder) : ValidatedSource;

        public sealed record ResolvedLoadOrder(
            string BaseDataFolder,
            IReadOnlyList<ValidatedRow> Rows) : ValidatedSource;
    }

    private sealed record ValidatedTarget(
        PluginRefreshRowKey Original,
        string NormalizedFullPath);

    private sealed record ValidatedRow(
        ModKey ModKey,
        string FullPath);
}
