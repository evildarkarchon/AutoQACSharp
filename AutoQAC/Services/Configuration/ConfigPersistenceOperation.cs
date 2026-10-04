using System.Threading.Tasks;
using AutoQAC.Models.Configuration;

namespace AutoQAC.Services.Configuration;

internal abstract record ConfigPersistenceOperation;

internal sealed record SaveIntent(UserConfiguration Config, long Generation, TaskCompletionSource Completion)
    : ConfigPersistenceOperation;

internal sealed record FlushBarrier(TaskCompletionSource<ConfigPersistenceResult> Completion)
    : ConfigPersistenceOperation;

internal sealed record WatcherObserved(string? CurrentHash, long ObservedAtGeneration, ConfigFileSignalKind Kind)
    : ConfigPersistenceOperation;

/// <summary>Cleaning admission reopened; retry the latest deferred external candidate.</summary>
internal sealed record AdmissionAvailable : ConfigPersistenceOperation;

internal sealed record ReloadRequest(TaskCompletionSource<ConfigPersistenceResult> Completion)
    : ConfigPersistenceOperation;

internal sealed record ShutdownOperation(TaskCompletionSource Completion) : ConfigPersistenceOperation;

public enum ConfigFileSignalKind
{
    Changed,
    Created,
    Renamed,
    Deleted,
    Error
}