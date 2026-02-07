namespace AutoQAC.Models;

/// <summary>
/// Represents a game variant that requires special skip list handling.
/// </summary>
public enum GameVariant
{
    /// <summary>No special variant detected.</summary>
    None,

    /// <summary>Tale of Two Wastelands -- FNV with FO3 content merged in.</summary>
    TTW,

    /// <summary>Enderal -- Total conversion mod for Skyrim SE.</summary>
    Enderal
}
