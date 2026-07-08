using AutoQAC.Models;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameCapability;
using AutoQAC.Services.MO2;
using AutoQAC.Services.Plugin;
using FluentAssertions;
using NSubstitute;

namespace AutoQAC.Tests.Services;

public sealed class PluginRefreshDiscoveryPlannerCapabilityTests
{
    public static TheoryData<GameType, bool, bool, bool> AffordanceMatrix => new()
    {
        { GameType.Unknown, false, false, false },
        { GameType.SkyrimLe, true, false, true },
        { GameType.SkyrimSe, true, false, true },
        { GameType.SkyrimVr, true, false, true },
        { GameType.Fallout4, true, false, true },
        { GameType.Fallout4Vr, true, false, true },
        { GameType.Oblivion, false, true, false },
        { GameType.Fallout3, false, true, false },
        { GameType.FalloutNewVegas, false, true, false }
    };

    [Theory]
    [MemberData(nameof(AffordanceMatrix))]
    public void GetAffordance_ShouldExposeGameCapabilityMatrixThroughPlannerSeam(
        GameType gameType,
        bool isMutagenSupported,
        bool requiresLoadOrderFileInDirectMode,
        bool canAttemptIssueApproximation)
    {
        var sut = CreateSut();

        var directAffordance = sut.GetAffordance(gameType, mo2ModeEnabled: false);
        var mo2Affordance = sut.GetAffordance(gameType, mo2ModeEnabled: true);

        directAffordance.GameType.Should().Be(gameType);
        directAffordance.IsMutagenSupported.Should().Be(isMutagenSupported);
        directAffordance.RequiresLoadOrderFile.Should().Be(requiresLoadOrderFileInDirectMode);
        directAffordance.CanAttemptIssueApproximation.Should().Be(canAttemptIssueApproximation);
        mo2Affordance.RequiresLoadOrderFile.Should().BeFalse("MO2 mode uses the profile load order instead of the direct-mode file selector");
    }

    [Fact]
    public void GetAvailableGames_ShouldReturnSelectableGamesInStableOrder()
    {
        var sut = CreateSut();

        var games = sut.GetAvailableGames();

        games.Should().NotContain(GameType.Unknown);
        games.Should().BeEquivalentTo(
            Enum.GetValues<GameType>()
                .Where(gameType => gameType != GameType.Unknown)
                .OrderBy(gameType => gameType.ToString()),
            options => options.WithStrictOrdering());
    }

    private static PluginRefreshDiscoveryPlanner CreateSut() =>
        new(
            Substitute.For<IConfigurationService>(),
            Substitute.For<IPluginLoadingService>(),
            Substitute.For<IMo2InstanceService>());
}
