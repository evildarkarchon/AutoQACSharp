using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
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

public sealed class PluginIssueApproximationService(
    ILoggingService logger,
    IPluginQueryService? pluginQueryService = null,
    Func<GameType, string, CancellationToken, PluginIssueApproximationService.AnalysisContext>? contextFactory = null)
    : IPluginIssueApproximationService
{
    public sealed record AnalysisTarget(string FileName, string FullPath, IModGetter? Plugin);

    public sealed record AnalysisContext(
        GameRelease GameRelease,
        ILinkCache LinkCache,
        IReadOnlyList<AnalysisTarget> Targets);

    private readonly IPluginQueryService _pluginQueryService = pluginQueryService ?? PluginQueryService.Default;

    private readonly Func<GameType, string, CancellationToken, AnalysisContext> _contextFactory =
        contextFactory ?? CreateAnalysisContext;

    public async Task<IReadOnlyList<PluginIssueApproximationResult>> GetApproximationsAsync(
        GameType gameType,
        string dataFolder,
        Action<PluginIssueApproximationResult>? onApproximationReady = null,
        CancellationToken ct = default)
    {
        if (!IsSupportedGame(gameType) || string.IsNullOrWhiteSpace(dataFolder))
        {
            return [];
        }

        return await Task.Run(() => AnalyzePlugins(gameType, dataFolder, onApproximationReady, ct), ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PluginIssueApproximationResult>> GetApproximationsAsync(
        GameType gameType,
        string baseDataFolder,
        IReadOnlyList<string> orderedPluginNames,
        Func<ModKey, string?> pathResolver,
        Action<PluginIssueApproximationResult>? onApproximationReady = null,
        CancellationToken ct = default)
    {
        if (!IsSupportedGame(gameType) || string.IsNullOrWhiteSpace(baseDataFolder) || orderedPluginNames.Count == 0)
        {
            return [];
        }

        return await Task.Run(
            () => AnalyzePluginsFromResolvedPaths(gameType, baseDataFolder, orderedPluginNames, pathResolver,
                onApproximationReady, ct),
            ct).ConfigureAwait(false);
    }

    private IReadOnlyList<PluginIssueApproximationResult> AnalyzePlugins(
        GameType gameType,
        string dataFolder,
        Action<PluginIssueApproximationResult>? onApproximationReady,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var context = _contextFactory(gameType, dataFolder, ct);
        ct.ThrowIfCancellationRequested();
        var results = new List<PluginIssueApproximationResult>(context.Targets.Count);

        foreach (var target in context.Targets)
        {
            ct.ThrowIfCancellationRequested();

            if (target.Plugin is null)
            {
                var unavailableResult = CreateUnavailableResult(target);
                results.Add(unavailableResult);
                onApproximationReady?.Invoke(unavailableResult);
                continue;
            }

            try
            {
                var analysis = _pluginQueryService.Analyse(target.Plugin, context.LinkCache, context.GameRelease, ct);
                var result = new PluginIssueApproximationResult
                {
                    FileName = target.FileName,
                    FullPath = target.FullPath,
                    Approximation = PluginIssueApproximation.Available(
                        analysis.ItmCount,
                        analysis.DeletedReferenceCount,
                        analysis.DeletedNavmeshCount)
                };
                results.Add(result);
                onApproximationReady?.Invoke(result);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.Warning("Approximation analysis failed for plugin {PluginName}: {Message}", target.FileName,
                    ex.Message);
                var unavailableResult = CreateUnavailableResult(target);
                results.Add(unavailableResult);
                onApproximationReady?.Invoke(unavailableResult);
            }
        }

        return results;
    }

    private static bool IsSupportedGame(GameType gameType)
    {
        return gameType is GameType.SkyrimLe or GameType.SkyrimSe or GameType.SkyrimVr or GameType.Fallout4
            or GameType.Fallout4Vr;
    }

    private static PluginIssueApproximationResult CreateUnavailableResult(AnalysisTarget target)
    {
        return new PluginIssueApproximationResult
        {
            FileName = target.FileName,
            FullPath = target.FullPath,
            Approximation = PluginIssueApproximation.Unavailable
        };
    }

    private static AnalysisContext CreateAnalysisContext(GameType gameType, string dataFolder, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var release = MapToGameRelease(gameType);
        var listings = LoadOrder.GetLoadOrderListings(release, new DirectoryPath(dataFolder), throwOnMissingMods: false)
            .ToList();
        ct.ThrowIfCancellationRequested();

        return gameType switch
        {
            GameType.SkyrimLe or GameType.SkyrimSe or GameType.SkyrimVr =>
                CreateSkyrimContext(release, dataFolder, listings, ct),
            GameType.Fallout4 or GameType.Fallout4Vr =>
                CreateFallout4Context(release, dataFolder, listings, ct),
            _ => throw new ArgumentException($"Game {gameType} is not supported by plugin issue approximations")
        };
    }

    private IReadOnlyList<PluginIssueApproximationResult> AnalyzePluginsFromResolvedPaths(
        GameType gameType,
        string baseDataFolder,
        IReadOnlyList<string> orderedPluginNames,
        Func<ModKey, string?> pathResolver,
        Action<PluginIssueApproximationResult>? onApproximationReady,
        CancellationToken ct)
    {
        var release = MapToGameRelease(gameType);
        var resolved = new Dictionary<ModKey, string>();
        var unresolved = new List<AnalysisTarget>();
        var listings = new List<ILoadOrderListingGetter>();

        foreach (var pluginName in orderedPluginNames)
        {
            ct.ThrowIfCancellationRequested();
            var modKey = ModKey.FromFileName(pluginName);
            var fullPath = pathResolver(modKey);
            if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
            {
                unresolved.Add(new AnalysisTarget(pluginName, fullPath ?? pluginName, null));
                continue;
            }

            resolved[modKey] = fullPath;
            listings.Add(LoadOrderListing.CreateEnabled(modKey));
        }

        var context = gameType switch
        {
            GameType.SkyrimLe or GameType.SkyrimSe or GameType.SkyrimVr =>
                CreateSkyrimContext(release, baseDataFolder, listings, resolved, ct),
            GameType.Fallout4 or GameType.Fallout4Vr =>
                CreateFallout4Context(release, baseDataFolder, listings, resolved, ct),
            _ => throw new ArgumentException($"Game {gameType} is not supported by plugin issue approximations")
        };

        var results = AnalyzeContext(context, onApproximationReady, ct).ToList();
        foreach (var target in unresolved)
        {
            ct.ThrowIfCancellationRequested();
            var unavailable = CreateUnavailableResult(target);
            results.Add(unavailable);
            onApproximationReady?.Invoke(unavailable);
        }

        return results;
    }

    private static AnalysisContext CreateSkyrimContext(
        GameRelease release,
        string dataFolder,
        IReadOnlyList<ILoadOrderListingGetter> listings,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var loadOrder = LoadOrder.Import<ISkyrimModGetter>(new DirectoryPath(dataFolder), listings, release);
        ct.ThrowIfCancellationRequested();
        var linkCache = loadOrder.ToImmutableLinkCache();
        var targets = new List<AnalysisTarget>(listings.Count);

        foreach (var listing in loadOrder)
        {
            ct.ThrowIfCancellationRequested();
            var modListing = listing.Value;
            targets.Add(new AnalysisTarget(
                modListing.ModKey.FileName.String,
                Path.Combine(dataFolder, modListing.ModKey.FileName.String),
                modListing.Mod));
        }

        return new AnalysisContext(release, linkCache, targets);
    }

    private static AnalysisContext CreateSkyrimContext(
        GameRelease release,
        string dataFolder,
        IReadOnlyList<ILoadOrderListingGetter> listings,
        IReadOnlyDictionary<ModKey, string> resolvedPaths,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var skyrimRelease = release.ToSkyrimRelease();
        var loadOrder = LoadOrder.Import<ISkyrimModGetter>(
            new DirectoryPath(dataFolder),
            listings,
            release,
            modPath => SkyrimMod.CreateFromBinary(
                new ModPath(modPath.ModKey, resolvedPaths[modPath.ModKey]),
                skyrimRelease));
        ct.ThrowIfCancellationRequested();
        var linkCache = loadOrder.ToImmutableLinkCache();
        var targets = loadOrder.Select(listing =>
        {
            var modListing = listing.Value;
            return new AnalysisTarget(
                modListing.ModKey.FileName.String,
                resolvedPaths[modListing.ModKey],
                modListing.Mod);
        }).ToList();

        return new AnalysisContext(release, linkCache, targets);
    }

    private static AnalysisContext CreateFallout4Context(
        GameRelease release,
        string dataFolder,
        IReadOnlyList<ILoadOrderListingGetter> listings,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var loadOrder = LoadOrder.Import<IFallout4ModGetter>(new DirectoryPath(dataFolder), listings, release);
        ct.ThrowIfCancellationRequested();
        var linkCache = loadOrder.ToImmutableLinkCache();
        var targets = new List<AnalysisTarget>(listings.Count);

        foreach (var listing in loadOrder)
        {
            ct.ThrowIfCancellationRequested();
            var modListing = listing.Value;
            targets.Add(new AnalysisTarget(
                modListing.ModKey.FileName.String,
                Path.Combine(dataFolder, modListing.ModKey.FileName.String),
                modListing.Mod));
        }

        return new AnalysisContext(release, linkCache, targets);
    }

    private static AnalysisContext CreateFallout4Context(
        GameRelease release,
        string dataFolder,
        IReadOnlyList<ILoadOrderListingGetter> listings,
        IReadOnlyDictionary<ModKey, string> resolvedPaths,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fallout4Release = release.ToFallout4Release();
        var loadOrder = LoadOrder.Import<IFallout4ModGetter>(
            new DirectoryPath(dataFolder),
            listings,
            release,
            modPath => Fallout4Mod.CreateFromBinary(
                new ModPath(modPath.ModKey, resolvedPaths[modPath.ModKey]),
                fallout4Release));
        ct.ThrowIfCancellationRequested();
        var linkCache = loadOrder.ToImmutableLinkCache();
        var targets = loadOrder.Select(listing =>
        {
            var modListing = listing.Value;
            return new AnalysisTarget(
                modListing.ModKey.FileName.String,
                resolvedPaths[modListing.ModKey],
                modListing.Mod);
        }).ToList();

        return new AnalysisContext(release, linkCache, targets);
    }

    private IReadOnlyList<PluginIssueApproximationResult> AnalyzeContext(
        AnalysisContext context,
        Action<PluginIssueApproximationResult>? onApproximationReady,
        CancellationToken ct)
    {
        var results = new List<PluginIssueApproximationResult>(context.Targets.Count);
        foreach (var target in context.Targets)
        {
            ct.ThrowIfCancellationRequested();

            if (target.Plugin is null)
            {
                var unavailableResult = CreateUnavailableResult(target);
                results.Add(unavailableResult);
                onApproximationReady?.Invoke(unavailableResult);
                continue;
            }

            try
            {
                var analysis = _pluginQueryService.Analyse(target.Plugin, context.LinkCache, context.GameRelease, ct);
                var result = new PluginIssueApproximationResult
                {
                    FileName = target.FileName,
                    FullPath = target.FullPath,
                    Approximation = PluginIssueApproximation.Available(
                        analysis.ItmCount,
                        analysis.DeletedReferenceCount,
                        analysis.DeletedNavmeshCount)
                };
                results.Add(result);
                onApproximationReady?.Invoke(result);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.Warning("Approximation analysis failed for plugin {PluginName}: {Message}", target.FileName,
                    ex.Message);
                var unavailableResult = CreateUnavailableResult(target);
                results.Add(unavailableResult);
                onApproximationReady?.Invoke(unavailableResult);
            }
        }

        return results;
    }

    private static GameRelease MapToGameRelease(GameType gameType) => gameType switch
    {
        GameType.SkyrimLe => GameRelease.SkyrimLE,
        GameType.SkyrimSe => GameRelease.SkyrimSE,
        GameType.SkyrimVr => GameRelease.SkyrimVR,
        GameType.Fallout4 => GameRelease.Fallout4,
        GameType.Fallout4Vr => GameRelease.Fallout4VR,
        _ => throw new ArgumentException($"Game {gameType} is not supported by plugin issue approximations")
    };
}
