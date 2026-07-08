using System;

namespace AutoQAC.Services.Process;

/// <summary>
/// Provides a per-app-run GUID used to identify PID entries created by the current AutoQAC instance.
/// </summary>
public sealed class ProcessSessionIdProvider : IProcessSessionIdProvider
{
    /// <inheritdoc />
    public string CurrentSessionId { get; } = Guid.NewGuid().ToString("N");
}
