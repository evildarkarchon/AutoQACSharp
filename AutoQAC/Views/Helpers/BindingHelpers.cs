using AutoQAC.Models;
using Microsoft.UI.Xaml;

namespace AutoQAC.Views.Helpers;

public static class BindingHelpers
{
    public static Visibility BoolToVisibility(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility InverseBoolToVisibility(bool value) =>
        value ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility NullableTrueToVisibility(bool? value) =>
        value == true ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility NullableFalseToVisibility(bool? value) =>
        value == false ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility StringToVisibility(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility NullToVisibility(object? value) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public static string GameTypeDisplayName(GameType gameType) => gameType switch
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
