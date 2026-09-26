using AutoQAC.Models;
using Microsoft.UI.Xaml;

namespace AutoQAC.Views.Helpers;

public static class BindingHelpers
{
    public static Visibility BoolToVisibility(bool value)
    {
        return value ? Visibility.Visible : Visibility.Collapsed;
    }

    public static Visibility InverseBoolToVisibility(bool value)
    {
        return value ? Visibility.Collapsed : Visibility.Visible;
    }

    public static Visibility NullableTrueToVisibility(bool? value)
    {
        return value == true ? Visibility.Visible : Visibility.Collapsed;
    }

    public static Visibility NullableFalseToVisibility(bool? value)
    {
        return value == false ? Visibility.Visible : Visibility.Collapsed;
    }

    public static Visibility StringToVisibility(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
    }

    public static Visibility NullToVisibility(object? value)
    {
        return value is null ? Visibility.Collapsed : Visibility.Visible;
    }

    public static string GameTypeDisplayName(GameType gameType)
    {
        return gameType switch
        {
            GameType.SkyrimLe => "Skyrim Legendary Edition",
            GameType.SkyrimSe => "Skyrim Special Edition",
            GameType.SkyrimVr => "Skyrim VR",
            GameType.Fallout4 => "Fallout 4",
            GameType.Fallout4Vr => "Fallout 4 VR",
            GameType.Oblivion => "Oblivion",
            GameType.Fallout3 => "Fallout 3",
            GameType.FalloutNewVegas => "Fallout: New Vegas",
            _ => "Unknown"
        };
    }
}