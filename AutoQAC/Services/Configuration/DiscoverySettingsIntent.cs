using AutoQAC.Models;

namespace AutoQAC.Services.Configuration;

/// <summary>
/// Describes a user or app intent to change one Discovery-affecting setting.
/// </summary>
public abstract record DiscoverySettingsIntent
{
    /// <summary>
    /// Changes the selected game and refreshes Plugin refresh publication for that game.
    /// </summary>
    /// <param name="GameType">Selected game.</param>
    public sealed record SelectGame(GameType GameType) : DiscoverySettingsIntent;

    /// <summary>
    /// Enables or disables MO2 mode and refreshes the current game publication.
    /// </summary>
    /// <param name="Enabled">Whether MO2 mode should be enabled.</param>
    public sealed record SetMo2Mode(bool Enabled) : DiscoverySettingsIntent;

    /// <summary>
    /// Changes the selected MO2 profile for a game and refreshes that game publication.
    /// </summary>
    /// <param name="GameType">Game whose MO2 profile changed.</param>
    /// <param name="ProfileName">Selected profile name, or null to clear it.</param>
    public sealed record SetMo2Profile(GameType GameType, string? ProfileName) : DiscoverySettingsIntent;

    /// <summary>
    /// Changes the selected load-order path for a game and refreshes from that explicit path.
    /// </summary>
    /// <param name="GameType">Game whose load-order path changed.</param>
    /// <param name="LoadOrderPath">Selected load-order file path.</param>
    public sealed record SetLoadOrderPath(GameType GameType, string LoadOrderPath) : DiscoverySettingsIntent;

    /// <summary>
    /// Changes the game data folder override and refreshes Plugin refresh publication for that game.
    /// </summary>
    /// <param name="GameType">Game whose data folder override changed.</param>
    /// <param name="FolderPath">Override folder path, or null to clear it.</param>
    public sealed record SetGameDataFolderOverride(GameType GameType, string? FolderPath) : DiscoverySettingsIntent;

    /// <summary>
    /// Changes the MO2 instance override and refreshes Plugin refresh publication for that game.
    /// </summary>
    /// <param name="GameType">Game whose MO2 instance override changed.</param>
    /// <param name="FolderPath">Override folder path, or null to clear it.</param>
    public sealed record SetMo2InstanceOverride(GameType GameType, string? FolderPath) : DiscoverySettingsIntent;

    /// <summary>
    /// Changes the configured Mod Organizer executable and refreshes the current game publication.
    /// </summary>
    /// <param name="ExecutablePath">Selected ModOrganizer.exe path.</param>
    public sealed record SetMo2ExecutablePath(string ExecutablePath) : DiscoverySettingsIntent;

    /// <summary>
    /// Enables or disables Skip list usage and refreshes the current game publication.
    /// </summary>
    /// <param name="Disabled">Whether Skip lists should be disabled.</param>
    public sealed record SetDisableSkipLists(bool Disabled) : DiscoverySettingsIntent;

    /// <summary>
    /// Resets Discovery-affecting settings to defaults and publishes the no-game Plugin refresh state.
    /// </summary>
    public sealed record Reset : DiscoverySettingsIntent;
}
