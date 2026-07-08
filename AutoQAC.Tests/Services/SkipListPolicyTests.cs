using AutoQAC.Models;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameDetection;
using AutoQAC.Services.Plugin;
using FluentAssertions;
using NSubstitute;

namespace AutoQAC.Tests.Services;

public sealed class SkipListPolicyTests
{
    [Fact]
    public async Task EvaluateAsync_WhenDisableSkipListsTrue_DoesNotLoadSkipListOrMarkStaleRows()
    {
        var configurationService = Substitute.For<IConfigurationService>();
        var gameDetectionService = CreateGameDetection(GameVariant.None);
        var sut = new SkipListPolicy(configurationService, gameDetectionService);
        var plugins = new[]
        {
            new PluginInfo
            {
                FileName = "AlreadyMarked.esp",
                FullPath = @"C:\Game\Data\AlreadyMarked.esp",
                DetectedGameType = GameType.SkyrimSe,
                IsInSkipList = true
            }
        };

        var result = await sut.EvaluateAsync(GameType.SkyrimSe, plugins, disableSkipLists: true);

        result.Decisions.Should().ContainSingle().Which.Should().Match<SkipListPluginDecision>(decision =>
            !decision.Plugin.IsInSkipList &&
            !decision.IsInEffectiveSkipList &&
            !decision.ShouldSkipByPolicy);
        await configurationService.DidNotReceive().GetSkipListAsync(
            Arg.Any<GameType>(),
            Arg.Any<GameVariant>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EvaluateAsync_WhenEnderalDetected_LoadsEnderalSkipListAndMarksMatchingRows()
    {
        var configurationService = Substitute.For<IConfigurationService>();
        configurationService.GetSkipListAsync(GameType.SkyrimSe, GameVariant.Enderal, Arg.Any<CancellationToken>())
            .Returns(["EnderalPatch.esp"]);
        var gameDetectionService = CreateGameDetection(GameVariant.Enderal);
        var sut = new SkipListPolicy(configurationService, gameDetectionService);
        var plugins = CreatePlugins("EnderalPatch.esp", "Keep.esp");

        var result = await sut.EvaluateAsync(GameType.SkyrimSe, plugins, disableSkipLists: false);

        result.Variant.Should().Be(GameVariant.Enderal);
        result.Decisions.Should().Contain(decision =>
            decision.Plugin.FileName == "EnderalPatch.esp" &&
            decision.Plugin.IsInSkipList &&
            decision.ShouldSkipByPolicy);
        result.Decisions.Should().Contain(decision =>
            decision.Plugin.FileName == "Keep.esp" &&
            !decision.Plugin.IsInSkipList &&
            !decision.ShouldSkipByPolicy);
        await configurationService.Received(1)
            .GetSkipListAsync(GameType.SkyrimSe, GameVariant.Enderal, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EvaluateAsync_WhenTtwDetected_LoadsTtwSkipListAndMarksMatchingRows()
    {
        var configurationService = Substitute.For<IConfigurationService>();
        configurationService.GetSkipListAsync(GameType.FalloutNewVegas, GameVariant.Ttw, Arg.Any<CancellationToken>())
            .Returns(["Fallout3CarriedForward.esm"]);
        var gameDetectionService = CreateGameDetection(GameVariant.Ttw);
        var sut = new SkipListPolicy(configurationService, gameDetectionService);
        var plugins = CreatePlugins("Fallout3CarriedForward.esm", "Keep.esp");

        var result = await sut.EvaluateAsync(GameType.FalloutNewVegas, plugins, disableSkipLists: false);

        result.Variant.Should().Be(GameVariant.Ttw);
        result.Decisions.Should().Contain(decision =>
            decision.Plugin.FileName == "Fallout3CarriedForward.esm" &&
            decision.Plugin.IsInSkipList &&
            decision.ShouldSkipByPolicy);
        await configurationService.Received(1)
            .GetSkipListAsync(GameType.FalloutNewVegas, GameVariant.Ttw, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EvaluateAsync_UsesConfigurationMergedEntriesCaseInsensitively()
    {
        var configurationService = Substitute.For<IConfigurationService>();
        configurationService.GetSkipListAsync(GameType.Fallout4, GameVariant.None, Arg.Any<CancellationToken>())
            .Returns(["universalplugin.esp"]);
        var gameDetectionService = CreateGameDetection(GameVariant.None);
        var sut = new SkipListPolicy(configurationService, gameDetectionService);
        var plugins = CreatePlugins("UniversalPlugin.esp");

        var result = await sut.EvaluateAsync(GameType.Fallout4, plugins, disableSkipLists: false);

        result.Decisions.Should().ContainSingle().Which.Should().Match<SkipListPluginDecision>(decision =>
            decision.Plugin.IsInSkipList && decision.ShouldSkipByPolicy);
    }

    private static IGameDetectionService CreateGameDetection(GameVariant variant)
    {
        var gameDetectionService = Substitute.For<IGameDetectionService>();
        gameDetectionService.DetectVariant(Arg.Any<GameType>(), Arg.Any<IReadOnlyList<string>>())
            .Returns(variant);
        return gameDetectionService;
    }

    private static IReadOnlyList<PluginInfo> CreatePlugins(params string[] fileNames) =>
        fileNames.Select(fileName => new PluginInfo
        {
            FileName = fileName,
            FullPath = $@"C:\Game\Data\{fileName}",
            DetectedGameType = GameType.Unknown
        }).ToList();
}
