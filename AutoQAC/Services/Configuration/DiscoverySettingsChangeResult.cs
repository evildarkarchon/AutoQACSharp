using AutoQAC.Services.Plugin;

namespace AutoQAC.Services.Configuration;

/// <summary>
///     Result of applying a Discovery settings change.
/// </summary>
/// <param name="Status">Terminal outcome of this operation.</param>
/// <param name="Snapshot">Matching publication at acceptance, when required; historical evidence, not a UI replay.</param>
/// <param name="Failure">Safe rejection or failure facts when the change could not be accepted.</param>
public sealed record DiscoverySettingsChangeResult(
    DiscoverySettingsChangeStatus Status,
    PluginRefreshSnapshot? Snapshot,
    DiscoverySettingsChangeFailure? Failure)
{
    /// <summary>True when this operation's choices reached disk, even if publication failed or was canceled.</summary>
    public bool SettingsSaved { get; init; }
    /// <summary>
    ///     Creates an accepted result from a published Plugin refresh snapshot.
    /// </summary>
    /// <param name="snapshot">Published snapshot accepted for the setting change.</param>
    /// <returns>An accepted Discovery settings change result.</returns>
    public static DiscoverySettingsChangeResult Accepted(PluginRefreshSnapshot snapshot)
    {
        return new DiscoverySettingsChangeResult(DiscoverySettingsChangeStatus.Accepted, snapshot, null)
        { SettingsSaved = true };
    }

    /// <summary>
    ///     Creates a rejected result with user-safe failure facts.
    /// </summary>
    /// <param name="failure">Typed rejection facts.</param>
    /// <returns>A rejected Discovery settings change result.</returns>
    public static DiscoverySettingsChangeResult Rejected(DiscoverySettingsChangeFailure failure)
    {
        return new DiscoverySettingsChangeResult(DiscoverySettingsChangeStatus.Rejected, null, failure);
    }
}

/// <summary>
///     Acceptance status for a Discovery settings change.
/// </summary>
public enum DiscoverySettingsChangeStatus
{
    /// <summary>The change was persisted and any required Plugin refresh publication was accepted.</summary>
    Accepted,

    /// <summary>The change was rejected before persistence because it was invalid or cleaning owns admission.</summary>
    Rejected,
    /// <summary>The setting could not be durably saved.</summary>
    SaveFailed,
    /// <summary>Saved choices remain available, but no matching publication was accepted.</summary>
    RefreshFailed,
    /// <summary>The caller canceled before acceptance; SettingsSaved indicates whether choices reached disk.</summary>
    Canceled,
    /// <summary>A newer valid choice or external change replaced this unfinished operation.</summary>
    Superseded
}

/// <summary>
///     User-safe rejection facts for a Discovery settings change.
/// </summary>
/// <param name="Kind">Stable rejection kind.</param>
/// <param name="SafeMessage">User-safe message that does not expose raw exception details.</param>
/// <param name="ActionHint">Optional user action hint.</param>
public sealed record DiscoverySettingsChangeFailure(
    DiscoverySettingsChangeFailureKind Kind,
    string SafeMessage,
    string? ActionHint = null);

/// <summary>
///     Stable rejection vocabulary for Discovery settings changes.
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
    InvalidMo2ExecutablePath,
    /// <summary>A Cleaning session is starting or active.</summary>
    CleaningActive,
    /// <summary>Disk persistence failed.</summary>
    PersistenceFailed,
    /// <summary>The saved choices could not produce a matching publication.</summary>
    PublicationFailed,
    /// <summary>The selected game is not a defined game choice.</summary>
    InvalidGame
}
