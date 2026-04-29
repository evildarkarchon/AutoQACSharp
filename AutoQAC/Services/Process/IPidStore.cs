using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;

namespace AutoQAC.Services.Process;

/// <summary>
/// Provides an injectable abstraction for loading and atomically updating tracked process entries.
/// Implementations are responsible for serializing concurrent read-modify-write access.
/// </summary>
public interface IPidStore
{
    /// <summary>
    /// Loads all tracked process entries from the backing store.
    /// </summary>
    /// <param name="ct">Cancellation token for I/O work.</param>
    /// <returns>The tracked process entries currently persisted.</returns>
    Task<IReadOnlyList<TrackedProcess>> LoadAsync(CancellationToken ct = default);

    /// <summary>
    /// Applies a read-modify-write update under the store's synchronization boundary.
    /// </summary>
    /// <param name="update">Pure transformation from existing entries to replacement entries.</param>
    /// <param name="ct">Cancellation token for I/O work.</param>
    Task UpdateAsync(Func<IReadOnlyList<TrackedProcess>, IReadOnlyList<TrackedProcess>> update, CancellationToken ct = default);
}
