namespace AutoQAC.Models;

/// <summary>
/// Supported game types for plugin cleaning operations.
/// Aligned with Mutagen's GameRelease enum where applicable.
/// </summary>
public enum GameType
{
    Unknown,

    // Mutagen-supported games
    SkyrimLE,           // Skyrim Legendary Edition (original)
    SkyrimSE,           // Skyrim Special Edition
    SkyrimVR,           // Skyrim VR
    Fallout3,           // Fallout 3
    FalloutNewVegas,    // Fallout: New Vegas
    Fallout4,           // Fallout 4
    Fallout4VR,         // Fallout 4 VR

    // File-based only (not supported by Mutagen for load order)
    Oblivion,           // The Elder Scrolls IV: Oblivion (limited xEdit QAC support)
}
