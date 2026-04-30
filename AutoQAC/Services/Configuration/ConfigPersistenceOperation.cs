using System.Threading.Tasks;
using AutoQAC.Models.Configuration;

namespace AutoQAC.Services.Configuration;

internal abstract record ConfigPersistenceOperation;

internal sealed record SaveIntent(UserConfiguration Config, long Generation) : ConfigPersistenceOperation;

internal sealed record FlushBarrier(TaskCompletionSource<ConfigPersistenceResult> Completion) : ConfigPersistenceOperation;

internal sealed record WatcherObserved(string? CurrentHash, long ObservedAtGeneration, ConfigFileSignalKind Kind) : ConfigPersistenceOperation;

internal sealed record CleaningStateChanged(bool IsCleaning) : ConfigPersistenceOperation;

internal sealed record ReloadRequest(TaskCompletionSource<ConfigPersistenceResult> Completion) : ConfigPersistenceOperation;

internal sealed record ShutdownOperation(TaskCompletionSource Completion) : ConfigPersistenceOperation;

public enum ConfigFileSignalKind
{
    Changed,
    Created,
    Renamed,
    Deleted,
    Error
}
