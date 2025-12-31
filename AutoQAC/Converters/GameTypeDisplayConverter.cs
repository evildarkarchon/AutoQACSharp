using System;
using System.Globalization;
using AutoQAC.Models;
using Avalonia.Data.Converters;

namespace AutoQAC.Converters;

/// <summary>
/// Converts GameType enum values to user-friendly display names.
/// </summary>
public sealed class GameTypeDisplayConverter : IValueConverter
{
    public static GameTypeDisplayConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is GameType gameType)
        {
            return GetDisplayName(gameType);
        }
        return value?.ToString() ?? string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Not typically needed for display-only scenarios
        if (value is string displayName)
        {
            return ParseFromDisplayName(displayName);
        }
        return GameType.Unknown;
    }

    /// <summary>
    /// Gets a user-friendly display name for a GameType.
    /// </summary>
    public static string GetDisplayName(GameType gameType) => gameType switch
    {
        GameType.SkyrimLe => "Skyrim (Legendary Edition)",
        GameType.SkyrimSe => "Skyrim Special Edition",
        GameType.SkyrimVr => "Skyrim VR",
        GameType.Fallout3 => "Fallout 3",
        GameType.FalloutNewVegas => "Fallout: New Vegas",
        GameType.Fallout4 => "Fallout 4",
        GameType.Fallout4Vr => "Fallout 4 VR",
        GameType.Oblivion => "The Elder Scrolls IV: Oblivion",
        GameType.Unknown => "Select a Game...",
        _ => gameType.ToString()
    };

    /// <summary>
    /// Parses a display name back to a GameType.
    /// </summary>
    private static GameType ParseFromDisplayName(string displayName) => displayName switch
    {
        "Skyrim (Legendary Edition)" => GameType.SkyrimLe,
        "Skyrim Special Edition" => GameType.SkyrimSe,
        "Skyrim VR" => GameType.SkyrimVr,
        "Fallout 3" => GameType.Fallout3,
        "Fallout: New Vegas" => GameType.FalloutNewVegas,
        "Fallout 4" => GameType.Fallout4,
        "Fallout 4 VR" => GameType.Fallout4Vr,
        "The Elder Scrolls IV: Oblivion" => GameType.Oblivion,
        _ => GameType.Unknown
    };
}
