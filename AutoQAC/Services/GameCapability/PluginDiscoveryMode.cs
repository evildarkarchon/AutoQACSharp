namespace AutoQAC.Services.GameCapability;

/// <summary>
/// Describes how AutoQAC can discover plugin rows for a game.
/// </summary>
public enum PluginDiscoveryMode
{
    /// <summary>
    /// AutoQAC cannot load plugin rows for this game.
    /// </summary>
    None,

    /// <summary>
    /// AutoQAC can discover the load order automatically from the game data folder.
    /// </summary>
    Automatic,

    /// <summary>
    /// AutoQAC requires an explicit load-order file such as plugins.txt or loadorder.txt.
    /// </summary>
    LoadOrderFile
}
