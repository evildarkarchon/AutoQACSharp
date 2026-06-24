using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;
using AutoQAC.Models.Configuration;

namespace AutoQAC.Services.Configuration;

public interface IConfigurationService
{
    // Configuration loading
    Task<MainConfiguration> LoadMainConfigAsync(CancellationToken ct = default);
    Task<UserConfiguration> LoadUserConfigAsync(CancellationToken ct = default);

    // Configuration saving
    Task SaveUserConfigAsync(UserConfiguration config, CancellationToken ct = default);

    // Path validation
    Task<bool> ValidatePathsAsync(UserConfiguration config, CancellationToken ct = default);

    // Game-specific queries
    Task<List<string>> GetSkipListAsync(
        GameType gameType,
        GameVariant variant = GameVariant.None,
        CancellationToken ct = default);
    Task<List<string>> GetDefaultSkipListAsync(GameType gameType, CancellationToken ct = default);
    Task<List<string>> GetXEditExecutableNamesAsync(GameType gameType, CancellationToken ct = default);

    // Skip list management (game-specific only, for UI editing)
    Task<List<string>> GetGameSpecificSkipListAsync(GameType gameType, CancellationToken ct = default);
    Task UpdateSkipListAsync(GameType gameType, List<string> skipList, CancellationToken ct = default);
    Task AddToSkipListAsync(GameType gameType, string pluginName, CancellationToken ct = default);
    Task RemoveFromSkipListAsync(GameType gameType, string pluginName, CancellationToken ct = default);
    IObservable<GameType> SkipListChanged { get; }

    // Game selection
    Task<GameType> GetSelectedGameAsync(CancellationToken ct = default);
    Task SetSelectedGameAsync(GameType gameType, CancellationToken ct = default);

    // Game data folder overrides
    Task<string?> GetGameDataFolderOverrideAsync(GameType gameType, CancellationToken ct = default);
    Task SetGameDataFolderOverrideAsync(GameType gameType, string? folderPath, CancellationToken ct = default);

    // MO2 per-game instance/profile overrides
    Task<string?> GetMo2InstanceOverrideAsync(GameType gameType, CancellationToken ct = default);
    Task SetMo2InstanceOverrideAsync(GameType gameType, string? folderPath, CancellationToken ct = default);
    Task<string?> GetMo2ProfileAsync(GameType gameType, CancellationToken ct = default);
    Task SetMo2ProfileAsync(GameType gameType, string? profileName, CancellationToken ct = default);

    // Game-specific load order path overrides
    Task<string?> GetGameLoadOrderOverrideAsync(GameType gameType, CancellationToken ct = default);
    Task SetGameLoadOrderOverrideAsync(GameType gameType, string? loadOrderPath, CancellationToken ct = default);

    // Settings management
    Task ResetToDefaultsAsync(CancellationToken ct = default);

    /// <summary>
    /// Force-flush any pending debounced config saves to disk immediately and return the typed
    /// persistence result so callers can branch on success, no-op, or failure before continuing.
    /// Call before starting xEdit and during app shutdown.
    /// </summary>
    Task<ConfigPersistenceResult> FlushPendingSavesAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns a flat dictionary of all user-facing settings for bulk inspection.
    /// Keys use dot-notation for nested properties (e.g., "LogRetention.Mode").
    /// </summary>
    Task<Dictionary<string, object?>> GetAllSettingsAsync(CancellationToken ct = default);

    /// <summary>
    /// Forces a fresh read from disk bypassing any in-memory cache.
    /// Used by ConfigWatcherService after detecting an external file change.
    /// </summary>
    Task ReloadFromDiskAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the SHA256 hex hash of the config file as written by the last app-initiated save.
    /// The ConfigWatcherService compares this to the on-disk hash to detect external changes.
    /// Returns null if no save has occurred yet.
    /// </summary>
    string? GetLastWrittenHash();

    // Reactive configuration changes
    IObservable<UserConfiguration> UserConfigurationChanged { get; }

    /// <summary>
    /// Recoverable persistence failure stream. ViewModels map this to user-facing status text.
    /// Per D-24/D-28, payloads contain only safe summary data — never raw exceptions.
    /// </summary>
    IObservable<ConfigPersistenceFailure> Failures { get; }

    /// <summary>
    /// Persistence operation result stream. ViewModels use Success/NoOp events to clear stale
    /// failure banners per D-27; failures still flow through <see cref="Failures" />.
    /// </summary>
    IObservable<ConfigPersistenceResult> PersistenceResults { get; }

    /// <summary>
    /// Snapshot of the most recent recoverable persistence failure, or null if cleared by a
    /// subsequent successful operation (D-27).
    /// </summary>
    ConfigPersistenceFailure? LastFailure { get; }
}
