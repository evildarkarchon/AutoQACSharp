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
    Task<List<string>> GetSkipListAsync(GameType gameType, GameVariant variant = GameVariant.None);
    Task<List<string>> GetDefaultSkipListAsync(GameType gameType);
    Task<List<string>> GetXEditExecutableNamesAsync(GameType gameType);

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

    // Settings management
    Task ResetToDefaultsAsync(CancellationToken ct = default);

    /// <summary>
    /// Force-flush any pending debounced config saves to disk immediately.
    /// Call before starting xEdit and during app shutdown.
    /// </summary>
    Task FlushPendingSavesAsync(CancellationToken ct = default);

    // Reactive configuration changes
    IObservable<UserConfiguration> UserConfigurationChanged { get; }
}
