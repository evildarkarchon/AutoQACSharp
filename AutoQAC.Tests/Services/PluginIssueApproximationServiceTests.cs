using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Plugin;
using FluentAssertions;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using NSubstitute;
using QueryPlugins;
using QueryPlugins.Models;

namespace AutoQAC.Tests.Services;

public sealed class PluginIssueApproximationServiceTests
{
    private readonly ILoggingService _logger = Substitute.For<ILoggingService>();
    private readonly IPluginQueryService _queryService = Substitute.For<IPluginQueryService>();

    [Fact]
    public async Task GetApproximationsAsync_WithSupportedGame_ShouldMapAnalysisCounts()
    {
        var plugin = new SkyrimMod(ModKey.FromNameAndExtension("Plugin.esp"), SkyrimRelease.SkyrimSE);
        var cache = new ISkyrimModGetter[] { plugin }.ToImmutableLinkCache();

        _queryService.Analyse(Arg.Any<IModGetter>(), Arg.Any<ILinkCache>(), GameRelease.SkyrimSE)
            .Returns(new PluginAnalysisResult(
            [
                new PluginIssue(FormKey.Null, null, IssueType.ItmRecord),
                new PluginIssue(FormKey.Null, null, IssueType.DeletedReference),
                new PluginIssue(FormKey.Null, null, IssueType.DeletedNavmesh)
            ]));

        var sut = new PluginIssueApproximationService(
            _logger,
            _queryService,
            (_, _, _) => new PluginIssueApproximationService.AnalysisContext(
                GameRelease.SkyrimSE,
                cache,
                [new PluginIssueApproximationService.AnalysisTarget("Plugin.esp", @"C:\Game\Data\Plugin.esp", plugin)]));

        var results = await sut.GetApproximationsAsync(GameType.SkyrimSe, @"C:\Game\Data");

        results.Should().ContainSingle();
        results[0].Approximation.Status.Should().Be(PluginIssueApproximationStatus.Available);
        results[0].Approximation.ItmCount.Should().Be(1);
        results[0].Approximation.DeletedReferenceCount.Should().Be(1);
        results[0].Approximation.DeletedNavmeshCount.Should().Be(1);
    }

    [Fact]
    public async Task GetApproximationsAsync_WithUnsupportedGame_ShouldReturnEmptyWithoutAnalyzing()
    {
        var sut = new PluginIssueApproximationService(
            _logger,
            _queryService,
            (_, _, _) => throw new InvalidOperationException("Should not be called"));

        var results = await sut.GetApproximationsAsync(GameType.Fallout3, @"C:\Game\Data");

        results.Should().BeEmpty();
        _queryService.DidNotReceiveWithAnyArgs().Analyse(default!, default!, default);
    }

    [Fact]
    public async Task GetApproximationsAsync_WithCanceledToken_ShouldThrowOperationCanceledException()
    {
        var sut = new PluginIssueApproximationService(
            _logger,
            _queryService,
            (_, _, _) => throw new InvalidOperationException("Should not be called"));

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => sut.GetApproximationsAsync(GameType.SkyrimSe, @"C:\Game\Data", ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _queryService.DidNotReceiveWithAnyArgs().Analyse(default!, default!, default);
    }

    [Fact]
    public async Task GetApproximationsAsync_WhenContextFactoryCancelsBeforeReturningEmptyContext_ShouldThrowOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();

        var sut = new PluginIssueApproximationService(
            _logger,
            _queryService,
            (_, _, _) =>
            {
                cts.Cancel();
                return new PluginIssueApproximationService.AnalysisContext(
                    GameRelease.SkyrimSE,
                    Array.Empty<ISkyrimModGetter>().ToImmutableLinkCache(),
                    []);
            });

        var act = () => sut.GetApproximationsAsync(GameType.SkyrimSe, @"C:\Game\Data", ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _queryService.DidNotReceiveWithAnyArgs().Analyse(default!, default!, default);
    }

    [Fact]
    public async Task GetApproximationsAsync_WhenSinglePluginAnalysisFails_ShouldReturnUnavailableForThatPlugin()
    {
        var plugin1 = new SkyrimMod(ModKey.FromNameAndExtension("One.esp"), SkyrimRelease.SkyrimSE);
        var plugin2 = new SkyrimMod(ModKey.FromNameAndExtension("Two.esp"), SkyrimRelease.SkyrimSE);
        var cache = new ISkyrimModGetter[] { plugin1, plugin2 }.ToImmutableLinkCache();

        _queryService.Analyse(Arg.Is<IModGetter>(p => ReferenceEquals(p, plugin1)), Arg.Any<ILinkCache>(), GameRelease.SkyrimSE)
            .Returns(new PluginAnalysisResult([new PluginIssue(FormKey.Null, null, IssueType.ItmRecord)]));
        _queryService.Analyse(Arg.Is<IModGetter>(p => ReferenceEquals(p, plugin2)), Arg.Any<ILinkCache>(), GameRelease.SkyrimSE)
            .Returns(_ => throw new InvalidOperationException("boom"));

        var sut = new PluginIssueApproximationService(
            _logger,
            _queryService,
            (_, _, _) => new PluginIssueApproximationService.AnalysisContext(
                GameRelease.SkyrimSE,
                cache,
                [
                    new PluginIssueApproximationService.AnalysisTarget("One.esp", @"C:\Game\Data\One.esp", plugin1),
                    new PluginIssueApproximationService.AnalysisTarget("Two.esp", @"C:\Game\Data\Two.esp", plugin2)
                ]));

        var results = await sut.GetApproximationsAsync(GameType.SkyrimSe, @"C:\Game\Data");

        results.Should().HaveCount(2);
        results.Single(r => r.FileName == "One.esp").Approximation.Status.Should().Be(PluginIssueApproximationStatus.Available);
        results.Single(r => r.FileName == "Two.esp").Approximation.Status.Should().Be(PluginIssueApproximationStatus.Unavailable);
    }

    [Fact]
    public async Task GetApproximationsAsync_ShouldReportEachPluginThroughCallbackInOrder()
    {
        var plugin1 = new SkyrimMod(ModKey.FromNameAndExtension("One.esp"), SkyrimRelease.SkyrimSE);
        var plugin2 = new SkyrimMod(ModKey.FromNameAndExtension("Two.esp"), SkyrimRelease.SkyrimSE);
        var cache = new ISkyrimModGetter[] { plugin1, plugin2 }.ToImmutableLinkCache();
        var reported = new List<PluginIssueApproximationResult>();

        _queryService.Analyse(Arg.Is<IModGetter>(p => ReferenceEquals(p, plugin1)), Arg.Any<ILinkCache>(), GameRelease.SkyrimSE)
            .Returns(new PluginAnalysisResult([new PluginIssue(FormKey.Null, null, IssueType.ItmRecord)]));
        _queryService.Analyse(Arg.Is<IModGetter>(p => ReferenceEquals(p, plugin2)), Arg.Any<ILinkCache>(), GameRelease.SkyrimSE)
            .Returns(new PluginAnalysisResult([new PluginIssue(FormKey.Null, null, IssueType.DeletedReference)]));

        var sut = new PluginIssueApproximationService(
            _logger,
            _queryService,
            (_, _, _) => new PluginIssueApproximationService.AnalysisContext(
                GameRelease.SkyrimSE,
                cache,
                [
                    new PluginIssueApproximationService.AnalysisTarget("One.esp", @"C:\Game\Data\One.esp", plugin1),
                    new PluginIssueApproximationService.AnalysisTarget("Two.esp", @"C:\Game\Data\Two.esp", plugin2)
                ]));

        var results = await sut.GetApproximationsAsync(
            GameType.SkyrimSe,
            @"C:\Game\Data",
            reported.Add,
            CancellationToken.None);

        results.Select(r => r.FileName).Should().Equal("One.esp", "Two.esp");
        reported.Select(r => r.FileName).Should().Equal("One.esp", "Two.esp");
        reported[0].Approximation.Status.Should().Be(PluginIssueApproximationStatus.Available);
        reported[1].Approximation.Status.Should().Be(PluginIssueApproximationStatus.Available);
    }

    [Fact]
    public async Task GetApproximationsAsync_ShouldReportUnavailableThroughCallback_WhenPluginAnalysisFails()
    {
        var plugin = new SkyrimMod(ModKey.FromNameAndExtension("Broken.esp"), SkyrimRelease.SkyrimSE);
        var cache = new ISkyrimModGetter[] { plugin }.ToImmutableLinkCache();
        var reported = new List<PluginIssueApproximationResult>();

        _queryService.Analyse(Arg.Any<IModGetter>(), Arg.Any<ILinkCache>(), GameRelease.SkyrimSE)
            .Returns(_ => throw new InvalidOperationException("boom"));

        var sut = new PluginIssueApproximationService(
            _logger,
            _queryService,
            (_, _, _) => new PluginIssueApproximationService.AnalysisContext(
                GameRelease.SkyrimSE,
                cache,
                [new PluginIssueApproximationService.AnalysisTarget("Broken.esp", @"C:\Game\Data\Broken.esp", plugin)]));

        var results = await sut.GetApproximationsAsync(
            GameType.SkyrimSe,
            @"C:\Game\Data",
            reported.Add,
            CancellationToken.None);

        results.Should().ContainSingle();
        reported.Should().ContainSingle();
        reported[0].Approximation.Status.Should().Be(PluginIssueApproximationStatus.Unavailable);
    }
}
