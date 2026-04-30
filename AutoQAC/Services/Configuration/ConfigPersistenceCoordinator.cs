using System;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.State;

namespace AutoQAC.Services.Configuration;

internal sealed class ConfigPersistenceCoordinator
{
    private readonly IUserConfigFileStore _fileStore;
    private readonly IStateService _stateService;
    private readonly ILoggingService _logger;
    private readonly Subject<ConfigPersistenceFailure> _failures = new();
    private readonly Subject<ConfigPersistenceResult> _persistenceResults = new();
    private readonly Subject<UserConfiguration> _configurationAccepted = new();

    private readonly Channel<ConfigPersistenceOperation> _operations =
        Channel.CreateUnbounded<ConfigPersistenceOperation>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    public ConfigPersistenceCoordinator(
        IUserConfigFileStore fileStore,
        IStateService stateService,
        ILoggingService logger,
        TimeSpan? autoFlushDelay = null)
    {
        _fileStore = fileStore;
        _stateService = stateService;
        _logger = logger;
    }

    public IObservable<ConfigPersistenceFailure> Failures => _failures;

    public IObservable<ConfigPersistenceResult> PersistenceResults => _persistenceResults;

    public IObservable<UserConfiguration> ConfigurationAccepted => _configurationAccepted;

    public ConfigPersistenceFailure? LastFailure => throw new NotImplementedException();

    public Task StartAsync(CancellationToken ct = default) => throw new NotImplementedException();

    public Task StopAsync(CancellationToken ct = default) => throw new NotImplementedException();

    public Task<UserConfiguration> LoadCurrentAsync(CancellationToken ct = default) => throw new NotImplementedException();

    public Task SaveUserConfigAsync(UserConfiguration config, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<ConfigPersistenceResult> FlushPendingSavesAsync(CancellationToken ct = default) => throw new NotImplementedException();

    public Task<ConfigPersistenceResult> ReloadFromDiskAsync(CancellationToken ct = default) => throw new NotImplementedException();

    public void NotifySettingsFileChanged(ConfigFileSignalKind kind) => throw new NotImplementedException();

    public string? GetLastWrittenHash() => throw new NotImplementedException();
}
