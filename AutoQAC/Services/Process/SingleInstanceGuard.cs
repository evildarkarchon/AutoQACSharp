using System.Threading;

namespace AutoQAC.Services.Process;

/// <summary>
/// Named-mutex implementation of AutoQAC's per-user single-instance guard.
/// </summary>
public sealed class SingleInstanceGuard : ISingleInstanceGuard
{
    public const string ProductionMutexName = "Local\\AutoQAC";
    private readonly Mutex _mutex;
    private bool _disposed;

    /// <summary>
    /// Creates the guard and attempts to acquire the named mutex immediately.
    /// </summary>
    /// <param name="name">Mutex name; tests pass unique names while production uses <see cref="ProductionMutexName" />.</param>
    public SingleInstanceGuard(string name = ProductionMutexName)
    {
        _mutex = new Mutex(initiallyOwned: true, name, out var ownsMutex);
        HasInstanceLock = ownsMutex;
    }

    /// <inheritdoc />
    public bool HasInstanceLock { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (HasInstanceLock)
        {
            // ownsMutex means this process actually acquired ownership; non-owning guards must not release it.
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
        _disposed = true;
    }
}
