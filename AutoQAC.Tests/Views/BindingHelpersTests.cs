using AutoQAC.Models;
using AutoQAC.Views.Helpers;
using FluentAssertions;
using Microsoft.UI.Xaml;

namespace AutoQAC.Tests.Views;

public sealed class BindingHelpersTests
{
    [Fact]
    public void BoolToVisibility_ShouldMapTrueToVisibleAndFalseToCollapsed()
    {
        BindingHelpers.BoolToVisibility(true).Should().Be(Visibility.Visible);
        BindingHelpers.BoolToVisibility(false).Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void NullableBoolVisibilityHelpers_ShouldHideNullAndOppositeValues()
    {
        BindingHelpers.NullableTrueToVisibility(true).Should().Be(Visibility.Visible);
        BindingHelpers.NullableTrueToVisibility(false).Should().Be(Visibility.Collapsed);
        BindingHelpers.NullableTrueToVisibility(null).Should().Be(Visibility.Collapsed);

        BindingHelpers.NullableFalseToVisibility(false).Should().Be(Visibility.Visible);
        BindingHelpers.NullableFalseToVisibility(true).Should().Be(Visibility.Collapsed);
        BindingHelpers.NullableFalseToVisibility(null).Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void GameTypeDisplayName_ShouldReturnFriendlyGameNames()
    {
        BindingHelpers.GameTypeDisplayName(GameType.SkyrimSe).Should().Be("Skyrim Special Edition");
        BindingHelpers.GameTypeDisplayName(GameType.FalloutNewVegas).Should().Be("Fallout: New Vegas");
        BindingHelpers.GameTypeDisplayName(GameType.Unknown).Should().Be("Unknown");
    }
}
