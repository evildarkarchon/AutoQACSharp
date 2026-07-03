using AutoQAC.Models;
using AutoQAC.Services.GameCapability;
using FluentAssertions;

namespace AutoQAC.Tests.Services;

public sealed class GameCapabilityProviderTests
{
    public static TheoryData<GameType, PluginDiscoveryMode, bool> CapabilityMatrix => new()
    {
        { GameType.Unknown, PluginDiscoveryMode.None, false },
        { GameType.SkyrimLe, PluginDiscoveryMode.Automatic, true },
        { GameType.SkyrimSe, PluginDiscoveryMode.Automatic, true },
        { GameType.SkyrimVr, PluginDiscoveryMode.Automatic, true },
        { GameType.Fallout4, PluginDiscoveryMode.Automatic, true },
        { GameType.Fallout4Vr, PluginDiscoveryMode.Automatic, true },
        { GameType.Oblivion, PluginDiscoveryMode.LoadOrderFile, false },
        { GameType.Fallout3, PluginDiscoveryMode.LoadOrderFile, false },
        { GameType.FalloutNewVegas, PluginDiscoveryMode.LoadOrderFile, false }
    };

    [Theory]
    [MemberData(nameof(CapabilityMatrix))]
    public void Get_ShouldReturnGameCapabilityMatrix(
        GameType gameType,
        PluginDiscoveryMode pluginDiscoveryMode,
        bool supportsIssueApproximation)
    {
        var sut = new GameCapabilityProvider();

        var capability = sut.Get(gameType);

        capability.GameType.Should().Be(gameType);
        capability.PluginDiscoveryMode.Should().Be(pluginDiscoveryMode);
        capability.SupportsIssueApproximation.Should().Be(supportsIssueApproximation);
        capability.SupportsPluginLoading.Should().Be(pluginDiscoveryMode != PluginDiscoveryMode.None);
        capability.RequiresLoadOrderFile.Should().Be(pluginDiscoveryMode == PluginDiscoveryMode.LoadOrderFile);
        capability.SupportsAutomaticPluginDiscovery.Should().Be(pluginDiscoveryMode == PluginDiscoveryMode.Automatic);
    }

    [Fact]
    public void GetAvailableGames_ShouldReturnSelectableGamesInStableOrder()
    {
        var sut = new GameCapabilityProvider();

        var games = sut.GetAvailableGames();

        games.Should().NotContain(GameType.Unknown);
        games.Should().BeEquivalentTo(
            Enum.GetValues<GameType>()
                .Where(gameType => gameType != GameType.Unknown)
                .OrderBy(gameType => gameType.ToString()),
            options => options.WithStrictOrdering());
    }
}
