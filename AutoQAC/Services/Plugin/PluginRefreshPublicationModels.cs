namespace AutoQAC.Services.Plugin;

/// <summary>
/// Configuration values captured for publishing at the Plugin refresh visibility seam.
/// </summary>
/// <param name="LoadOrderPath">Effective direct-mode load-order path to publish, if any.</param>
/// <param name="Mo2ExecutablePath">Configured Mod Organizer executable path.</param>
/// <param name="XEditExecutablePath">Configured xEdit executable path.</param>
/// <param name="Mo2Profile">Resolved MO2 profile to publish, if any.</param>
/// <param name="Mo2ModeEnabled">Whether MO2 mode is enabled in configuration.</param>
/// <param name="CleaningTimeout">Configured cleaning timeout in seconds.</param>
public sealed record PluginRefreshPublicationSnapshot(
    string? LoadOrderPath,
    string? Mo2ExecutablePath,
    string? XEditExecutablePath,
    string? Mo2Profile,
    bool Mo2ModeEnabled,
    int CleaningTimeout);
