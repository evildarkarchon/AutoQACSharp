using AutoQAC.Models;
using AutoQAC.Services.Plugin;
using FluentAssertions;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using NSubstitute;
using QueryPlugins;
using QueryPlugins.Models;

namespace AutoQAC.Tests.Services;

public sealed class PluginIssueApproximationModuleTests
{
    private readonly IPluginQueryService _queryService = Substitute.For<IPluginQueryService>();

    [Fact]
    public async Task AnalyzeAsync_WithDirectSource_UsesCompleteContextButQueriesOnlyOrderedTarget()
    {
        using var fixture = new SkyrimSePluginFixture();
        var contextKey = fixture.WritePlugin("Skyrim.esm");
        var targetKey = fixture.WritePlugin("Update.esm");
        var reported = new List<PluginIssueApproximationModuleResult>();
        var expectedContext = new[]
        {
            ModKey.FromFileName(contextKey.FileName),
            ModKey.FromFileName(targetKey.FileName)
        };

        _queryService
            .Analyse(
                Arg.Is<IModGetter>(plugin => plugin.ModKey == ModKey.FromFileName(targetKey.FileName)),
                Arg.Is<ILinkCache>(cache =>
                    cache.ListedOrder.Select(plugin => plugin.ModKey)
                        .SequenceEqual(expectedContext)),
                GameRelease.SkyrimSE,
                Arg.Any<CancellationToken>())
            .Returns(new PluginAnalysisResult(
            [
                new PluginIssue(FormKey.Null, null, IssueType.ItmRecord)
            ]));

        IPluginIssueApproximationModule sut = new PluginIssueApproximationModule(_queryService);
        var request = new PluginIssueApproximationModuleRequest(
            GameType.SkyrimSe,
            new PluginIssueApproximationModuleSource.DirectDataFolder(fixture.DataFolder),
            [targetKey]);

        await sut.AnalyzeAsync(request, reported.Add, CancellationToken.None);

        reported.Should().ContainSingle();
        reported[0].Target.Should().BeSameAs(targetKey);
        reported[0].Approximation.Should().Be(
            PluginIssueApproximation.Available(
                itmCount: 1,
                deletedReferenceCount: 0,
                deletedNavmeshCount: 0));
        _queryService.Received(1).Analyse(
            Arg.Any<IModGetter>(),
            Arg.Any<ILinkCache>(),
            GameRelease.SkyrimSE,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnalyzeAsync_WithStructurallyInvalidIdentity_RejectsEntireRequestBeforeAnalysis()
    {
        var directSource = new PluginIssueApproximationModuleSource.DirectDataFolder(@"C:\Game\Data");
        var invalidRequests = new[]
        {
            new PluginIssueApproximationModuleRequest(
                GameType.SkyrimSe,
                directSource,
                [new PluginRefreshRowKey("Pathless.esp", string.Empty)]),
            new PluginIssueApproximationModuleRequest(
                GameType.SkyrimSe,
                directSource,
                [new PluginRefreshRowKey("Relative.esp", @"Data\Relative.esp")]),
            new PluginIssueApproximationModuleRequest(
                GameType.SkyrimSe,
                directSource,
                [new PluginRefreshRowKey("Named.esp", @"C:\Game\Data\Different.esp")]),
            new PluginIssueApproximationModuleRequest(
                GameType.SkyrimSe,
                directSource,
                [
                    new PluginRefreshRowKey("Duplicate.esp", @"C:\Game\Data\Duplicate.esp"),
                    new PluginRefreshRowKey("DUPLICATE.ESP", @"c:\game\data\folder\..\DUPLICATE.ESP")
                ]),
            new PluginIssueApproximationModuleRequest(
                GameType.SkyrimSe,
                new PluginIssueApproximationModuleSource.ResolvedLoadOrder(
                    @"C:\Game\Data",
                    [
                        new PluginRefreshRowKey("Duplicate.esp", @"C:\Winner\Duplicate.esp"),
                        new PluginRefreshRowKey("DUPLICATE.ESP", @"C:\Other\DUPLICATE.ESP")
                    ]),
                []),
            new PluginIssueApproximationModuleRequest(
                GameType.SkyrimSe,
                new PluginIssueApproximationModuleSource.ResolvedLoadOrder(
                    @"C:\Game\Data",
                    [new PluginRefreshRowKey("Context.esp", @"C:\Winner\Context.esp")]),
                [new PluginRefreshRowKey("Absent.esp", @"C:\Winner\Absent.esp")])
        };

        IPluginIssueApproximationModule sut = new PluginIssueApproximationModule(_queryService);

        foreach (var request in invalidRequests)
        {
            var reported = new List<PluginIssueApproximationModuleResult>();

            var act = () => sut.AnalyzeAsync(request, reported.Add, CancellationToken.None);

            await act.Should().ThrowAsync<ArgumentException>();
            reported.Should().BeEmpty();
        }

        _queryService.DidNotReceiveWithAnyArgs().Analyse(default!, default!, default, default);
    }

    [Fact]
    public async Task AnalyzeAsync_WithResolvedSource_StreamsExactTargetsInOrderWithCompleteContext()
    {
        using var fixture = new SkyrimSePluginFixture();
        var contextKey = fixture.WritePlugin("Context.esm");
        var firstSourceKey = fixture.WritePlugin("First.esp");
        var secondKey = fixture.WritePlugin("Second.esp");
        var firstTarget = new PluginRefreshRowKey(
            firstSourceKey.FileName.ToUpperInvariant(),
            firstSourceKey.FullPath.ToUpperInvariant());
        var expectedContext = new[]
        {
            ModKey.FromFileName(contextKey.FileName),
            ModKey.FromFileName(firstSourceKey.FileName),
            ModKey.FromFileName(secondKey.FileName)
        };
        var analyzed = new List<ModKey>();
        var reported = new List<PluginIssueApproximationModuleResult>();

        _queryService
            .Analyse(
                Arg.Any<IModGetter>(),
                Arg.Any<ILinkCache>(),
                GameRelease.SkyrimSE,
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var plugin = call.ArgAt<IModGetter>(0);
                var cache = call.ArgAt<ILinkCache>(1);
                cache.ListedOrder.Select(item => item.ModKey).Should().Equal(expectedContext);
                analyzed.Add(plugin.ModKey);
                return new PluginAnalysisResult([]);
            });

        IPluginIssueApproximationModule sut = new PluginIssueApproximationModule(_queryService);
        var request = new PluginIssueApproximationModuleRequest(
            GameType.SkyrimSe,
            new PluginIssueApproximationModuleSource.ResolvedLoadOrder(
                fixture.DataFolder,
                [contextKey, firstSourceKey, secondKey]),
            [firstTarget, secondKey]);

        await sut.AnalyzeAsync(request, reported.Add, CancellationToken.None);

        analyzed.Should().Equal(
            ModKey.FromFileName(firstSourceKey.FileName),
            ModKey.FromFileName(secondKey.FileName));
        reported.Select(result => result.Target).Should().Equal(firstTarget, secondKey);
        reported[0].Target.Should().BeSameAs(firstTarget);
        reported[1].Target.Should().BeSameAs(secondKey);
        reported.Should().OnlyContain(
            result => result.Approximation.Status == PluginIssueApproximationStatus.Available);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenTargetsDisappearOrFail_ReportsUnavailableAndContinuesInOrder()
    {
        using var fixture = new SkyrimSePluginFixture();
        var missingKey = fixture.WritePlugin("Missing.esp");
        var unreadableKey = fixture.WritePlugin("Unreadable.esp");
        var brokenKey = fixture.WritePlugin("Broken.esp");
        var laterKey = fixture.WritePlugin("Later.esp");
        File.Delete(missingKey.FullPath);
        File.WriteAllText(unreadableKey.FullPath, "not a plugin");
        var analyzed = new List<ModKey>();
        var reported = new List<PluginIssueApproximationModuleResult>();

        _queryService
            .Analyse(
                Arg.Any<IModGetter>(),
                Arg.Any<ILinkCache>(),
                GameRelease.SkyrimSE,
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var plugin = call.ArgAt<IModGetter>(0);
                analyzed.Add(plugin.ModKey);
                if (plugin.ModKey == ModKey.FromFileName(brokenKey.FileName))
                {
                    throw new InvalidOperationException("Target-specific query failure.");
                }

                return new PluginAnalysisResult(
                [
                    new PluginIssue(FormKey.Null, null, IssueType.DeletedReference)
                ]);
            });

        IPluginIssueApproximationModule sut = new PluginIssueApproximationModule(_queryService);
        var request = new PluginIssueApproximationModuleRequest(
            GameType.SkyrimSe,
            new PluginIssueApproximationModuleSource.ResolvedLoadOrder(
                fixture.DataFolder,
                [missingKey, unreadableKey, brokenKey, laterKey]),
            [missingKey, unreadableKey, brokenKey, laterKey]);

        await sut.AnalyzeAsync(request, reported.Add, CancellationToken.None);

        analyzed.Should().Equal(
            ModKey.FromFileName(brokenKey.FileName),
            ModKey.FromFileName(laterKey.FileName));
        reported.Select(result => result.Target).Should().Equal(missingKey, unreadableKey, brokenKey, laterKey);
        reported.Select(result => result.Approximation.Status).Should().Equal(
            PluginIssueApproximationStatus.Unavailable,
            PluginIssueApproximationStatus.Unavailable,
            PluginIssueApproximationStatus.Unavailable,
            PluginIssueApproximationStatus.Available);
        reported[3].Approximation.DeletedReferenceCount.Should().Be(1);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenDependencyContextCannotBeBuilt_FailsBeforeTargetAnalysis()
    {
        using var fixture = new SkyrimSePluginFixture();
        var missingContextKey = fixture.WritePlugin("MissingContext.esm");
        var targetKey = fixture.WritePlugin("Target.esp");
        File.Delete(missingContextKey.FullPath);
        var reported = new List<PluginIssueApproximationModuleResult>();
        IPluginIssueApproximationModule sut = new PluginIssueApproximationModule(_queryService);
        var request = new PluginIssueApproximationModuleRequest(
            GameType.SkyrimSe,
            new PluginIssueApproximationModuleSource.ResolvedLoadOrder(
                fixture.DataFolder,
                [missingContextKey, targetKey]),
            [targetKey]);

        var act = () => sut.AnalyzeAsync(request, reported.Add, CancellationToken.None);

        await act.Should().ThrowAsync<FileNotFoundException>();
        reported.Should().BeEmpty();
        _queryService.DidNotReceiveWithAnyArgs().Analyse(default!, default!, default, default);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenCancellationIsObservedAfterQuery_DoesNotPublishActiveOrLaterTarget()
    {
        using var fixture = new SkyrimSePluginFixture();
        var activeKey = fixture.WritePlugin("Active.esp");
        var laterKey = fixture.WritePlugin("Later.esp");
        using var cts = new CancellationTokenSource();
        var analyzed = new List<ModKey>();
        var reported = new List<PluginIssueApproximationModuleResult>();

        _queryService
            .Analyse(
                Arg.Any<IModGetter>(),
                Arg.Any<ILinkCache>(),
                GameRelease.SkyrimSE,
                Arg.Is<CancellationToken>(token => token == cts.Token))
            .Returns(call =>
            {
                analyzed.Add(call.ArgAt<IModGetter>(0).ModKey);
                cts.Cancel();
                return new PluginAnalysisResult([]);
            });

        IPluginIssueApproximationModule sut = new PluginIssueApproximationModule(_queryService);
        var request = new PluginIssueApproximationModuleRequest(
            GameType.SkyrimSe,
            new PluginIssueApproximationModuleSource.ResolvedLoadOrder(
                fixture.DataFolder,
                [activeKey, laterKey]),
            [activeKey, laterKey]);

        var act = () => sut.AnalyzeAsync(request, reported.Add, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        analyzed.Should().Equal(ModKey.FromFileName(activeKey.FileName));
        reported.Should().BeEmpty();
    }

    [Fact]
    public async Task AnalyzeAsync_WithUnsupportedGame_CompletesWithoutAnalysisOrCallbacks()
    {
        var target = new PluginRefreshRowKey("Unsupported.esp", @"C:\Game\Data\Unsupported.esp");
        var reported = new List<PluginIssueApproximationModuleResult>();
        IPluginIssueApproximationModule sut = new PluginIssueApproximationModule(_queryService);
        var request = new PluginIssueApproximationModuleRequest(
            GameType.Fallout3,
            new PluginIssueApproximationModuleSource.DirectDataFolder(@"C:\Game\Data"),
            [target]);

        await sut.AnalyzeAsync(request, reported.Add, CancellationToken.None);

        reported.Should().BeEmpty();
        _queryService.DidNotReceiveWithAnyArgs().Analyse(default!, default!, default, default);
    }

    [Fact]
    public async Task AnalyzeAsync_WithFallout4Source_PreservesExistingSupportedGameAvailability()
    {
        using var fixture = new Fallout4PluginFixture();
        var target = fixture.WritePlugin("Fallout4.esm");
        var reported = new List<PluginIssueApproximationModuleResult>();

        _queryService
            .Analyse(
                Arg.Is<IModGetter>(plugin => plugin.ModKey == ModKey.FromFileName(target.FileName)),
                Arg.Any<ILinkCache>(),
                GameRelease.Fallout4,
                Arg.Any<CancellationToken>())
            .Returns(new PluginAnalysisResult([]));

        IPluginIssueApproximationModule sut = new PluginIssueApproximationModule(_queryService);
        var request = new PluginIssueApproximationModuleRequest(
            GameType.Fallout4,
            new PluginIssueApproximationModuleSource.ResolvedLoadOrder(
                fixture.DataFolder,
                [target]),
            [target]);

        await sut.AnalyzeAsync(request, reported.Add, CancellationToken.None);

        reported.Should().ContainSingle(result =>
            ReferenceEquals(result.Target, target) &&
            result.Approximation.Status == PluginIssueApproximationStatus.Available);
        _queryService.Received(1).Analyse(
            Arg.Any<IModGetter>(),
            Arg.Any<ILinkCache>(),
            GameRelease.Fallout4,
            Arg.Any<CancellationToken>());
    }

    private sealed class SkyrimSePluginFixture : IDisposable
    {
        public SkyrimSePluginFixture()
        {
            DataFolder = Path.Combine(
                Path.GetTempPath(),
                "AutoQAC_PluginIssueApproximation_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataFolder);
        }

        public string DataFolder { get; }

        public PluginRefreshRowKey WritePlugin(string fileName)
        {
            var path = Path.Combine(DataFolder, fileName);
            var plugin = new SkyrimMod(ModKey.FromNameAndExtension(fileName), SkyrimRelease.SkyrimSE);
            plugin.BeginWrite
                .ToPath(path)
                .WithNoLoadOrder()
                .Write();
            return new PluginRefreshRowKey(fileName, path);
        }

        public void Dispose()
        {
            Directory.Delete(DataFolder, recursive: true);
        }
    }

    private sealed class Fallout4PluginFixture : IDisposable
    {
        public Fallout4PluginFixture()
        {
            DataFolder = Path.Combine(
                Path.GetTempPath(),
                "AutoQAC_PluginIssueApproximation_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataFolder);
        }

        public string DataFolder { get; }

        public PluginRefreshRowKey WritePlugin(string fileName)
        {
            var path = Path.Combine(DataFolder, fileName);
            var plugin = new Fallout4Mod(ModKey.FromNameAndExtension(fileName), Fallout4Release.Fallout4);
            plugin.BeginWrite
                .ToPath(path)
                .WithNoLoadOrder()
                .Write();
            return new PluginRefreshRowKey(fileName, path);
        }

        public void Dispose()
        {
            Directory.Delete(DataFolder, recursive: true);
        }
    }
}
