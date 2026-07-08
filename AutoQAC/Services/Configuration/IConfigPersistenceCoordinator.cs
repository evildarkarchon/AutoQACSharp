using System;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models.Configuration;

namespace AutoQAC.Services.Configuration;

public interface IConfigPersistenceCoordinator
{
    Task StartAsync(CancellationToken ct = default);

    Task StopAsync(CancellationToken ct = default);

    Task<UserConfiguration> LoadCurrentAsync(CancellationToken ct = default);

    Task SaveUserConfigAsync(UserConfiguration config, CancellationToken ct = default);

    Task<ConfigPersistenceResult> FlushPendingSavesAsync(CancellationToken ct = default);

    Task<ConfigPersistenceResult> ReloadFromDiskAsync(CancellationToken ct = default);

    void NotifySettingsFileChanged(ConfigFileSignalKind kind);

    IObservable<ConfigPersistenceFailure> Failures { get; }

    IObservable<ConfigPersistenceResult> PersistenceResults { get; }

    IObservable<UserConfiguration> ConfigurationAccepted { get; }

    ConfigPersistenceFailure? LastFailure { get; }
}
