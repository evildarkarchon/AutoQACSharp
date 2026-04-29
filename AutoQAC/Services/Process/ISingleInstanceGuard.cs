using System;

namespace AutoQAC.Services.Process;

/// <summary>
/// Represents ownership of the application-wide single-instance lock.
/// </summary>
public interface ISingleInstanceGuard : IDisposable
{
    /// <summary>Gets whether this process acquired the single-instance lock.</summary>
    bool HasInstanceLock { get; }
}
