using AutoQAC.Models;

namespace AutoQAC.Services.Plugin;

/// <summary>
/// Immutable input for a plugin refresh generation.
/// The coordinator uses this to resolve plugin rows and approximation targets without reading mutable ViewModel state mid-refresh.
/// </summary>
/// <param name="GameType">Game whose plugin rows or approximations should be refreshed.</param>
/// <param name="DataFolderPath">Optional data folder override for Mutagen-backed loading and approximation analysis.</param>
/// <param name="LoadOrderPath">Optional explicit load-order file path for file-based games or manual load-order refresh.</param>
public sealed record PluginRefreshRequest(
    GameType GameType,
    string? DataFolderPath = null,
    string? LoadOrderPath = null);

/// <summary>
/// Immutable snapshot of a plugin row selected for targeted approximation refresh.
/// The full path is the primary identity, while file name remains available for existing merge fallback behavior.
/// </summary>
/// <param name="FileName">Plugin file name shown in the UI.</param>
/// <param name="FullPath">Resolved full path used to match state rows.</param>
public sealed record PluginRefreshTarget(string FileName, string FullPath);
