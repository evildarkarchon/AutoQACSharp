namespace AutoQAC.Models;

/// <summary>
/// Supported game types for plugin cleaning operations.
/// Aligned with Mutagen's GameRelease enum where applicable.
/// </summary>
public enum GameType
{
    Unknown,

    // Mutagen-supported games
    SkyrimLe,           // Skyrim Legendary Edition (original)
    SkyrimSe,           // Skyrim Special Edition
    SkyrimVr,           // Skyrim VR
    Fallout4,           // Fallout 4
    Fallout4Vr,         // Fallout 4 VR

    // File-based only (not supported by Mutagen for load order)
    Oblivion,           // The Elder Scrolls IV: Oblivion (limited xEdit QAC support)
    Fallout3,           // Fallout 3
    FalloutNewVegas,    // Fallout: New Vegas
}
