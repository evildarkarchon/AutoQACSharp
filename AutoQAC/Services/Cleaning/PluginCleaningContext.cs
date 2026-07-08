using AutoQAC.Models;

namespace AutoQAC.Services.Cleaning;

/// <summary>
/// Per-plugin facts needed to clean one plugin within a Cleaning session.
/// </summary>
/// <param name="Plugin">Plugin selected by preflight for cleaning.</param>
/// <param name="GameType">Detected game type used for launch and xEdit log names.</param>
/// <param name="XEditDirectory">Directory containing the xEdit executable and logs.</param>
/// <param name="TimeoutSeconds">Configured timeout shown to retry decisions.</param>
/// <param name="MaxRetryAttempts">Maximum number of xEdit launch attempts for this plugin.</param>
public sealed record PluginCleaningContext(
    PluginInfo Plugin,
    GameType GameType,
    string XEditDirectory,
    int TimeoutSeconds,
    int MaxRetryAttempts);
