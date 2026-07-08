using AutoQAC.Services.Plugin;

namespace AutoQAC.Services.Configuration;

/// <summary>
/// Result of applying a Discovery settings change.
/// </summary>
/// <param name="Status">Whether the setting change was accepted or rejected.</param>
/// <param name="Snapshot">Accepted Plugin refresh snapshot when a refresh was published.</param>
/// <param name="Failure">Typed rejection facts when the change could not be accepted.</param>
public sealed record DiscoverySettingsChangeResult(
    DiscoverySettingsChangeStatus Status,
    PluginRefreshSnapshot? Snapshot,
    DiscoverySettingsChangeFailure? Failure)
{
    /// <summary>
    /// Creates an accepted result from a published Plugin refresh snapshot.
    /// </summary>
    /// <param name="snapshot">Published snapshot accepted for the setting change.</param>
    /// <returns>An accepted Discovery settings change result.</returns>
    public static DiscoverySettingsChangeResult Accepted(PluginRefreshSnapshot snapshot) =>
        new(DiscoverySettingsChangeStatus.Accepted, snapshot, null);

    /// <summary>
    /// Creates a rejected result with user-safe failure facts.
    /// </summary>
    /// <param name="failure">Typed rejection facts.</param>
    /// <returns>A rejected Discovery settings change result.</returns>
    public static DiscoverySettingsChangeResult Rejected(DiscoverySettingsChangeFailure failure) =>
        new(DiscoverySettingsChangeStatus.Rejected, null, failure);
}

/// <summary>
/// Acceptance status for a Discovery settings change.
/// </summary>
public enum DiscoverySettingsChangeStatus
{
    /// <summary>The change was persisted and any required Plugin refresh publication was accepted.</summary>
    Accepted,

    /// <summary>The change was rejected before persistence or refresh because the intent was invalid.</summary>
    Rejected
}

/// <summary>
/// User-safe rejection facts for a Discovery settings change.
/// </summary>
/// <param name="Kind">Stable rejection kind.</param>
/// <param name="SafeMessage">User-safe message that does not expose raw exception details.</param>
/// <param name="ActionHint">Optional user action hint.</param>
public sealed record DiscoverySettingsChangeFailure(
    DiscoverySettingsChangeFailureKind Kind,
    string SafeMessage,
    string? ActionHint = null);

/// <summary>
/// Stable rejection vocabulary for Discovery settings changes.
/// </summary>
public enum DiscoverySettingsChangeFailureKind
{
    /// <summary>The selected load-order file path was missing or not a file.</summary>
    InvalidLoadOrderPath,

    /// <summary>The selected game data folder path was missing or not a folder.</summary>
    InvalidGameDataFolder,

    /// <summary>The selected MO2 instance path was missing or not a folder.</summary>
    InvalidMo2InstanceFolder,

    /// <summary>The selected Mod Organizer executable path was missing or not an executable file.</summary>
    InvalidMo2ExecutablePath
}
