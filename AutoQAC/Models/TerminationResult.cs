namespace AutoQAC.Models;

/// <summary>
/// Result of a process termination attempt, distinguishing between
/// graceful exit, force killed, grace period expiry, and already exited.
/// </summary>
public enum TerminationResult
{
    /// <summary>Process had already exited before termination was attempted.</summary>
    AlreadyExited,

    /// <summary>Process exited gracefully within the grace period after CloseMainWindow.</summary>
    GracefulExit,

    /// <summary>Grace period expired and the process is still running. Caller should escalate.</summary>
    GracePeriodExpired,

    /// <summary>Process tree was force-killed via Process.Kill(entireProcessTree: true).</summary>
    ForceKilled
}
