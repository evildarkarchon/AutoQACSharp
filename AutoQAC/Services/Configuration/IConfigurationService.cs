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
    Task<List<string>> GetSkipListAsync(GameType gameType);
    Task<List<string>> GetXEditExecutableNamesAsync(GameType gameType);

    // Reactive configuration changes
    IObservable<UserConfiguration> UserConfigurationChanged { get; }
}
