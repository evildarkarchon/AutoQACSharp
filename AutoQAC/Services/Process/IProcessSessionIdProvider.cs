namespace AutoQAC.Services.Process;

/// <summary>
/// Provides a stable identifier for the current AutoQAC application run.
/// </summary>
public interface IProcessSessionIdProvider
{
    /// <summary>Gets the session ID used for PID entries written during this app run.</summary>
    string CurrentSessionId { get; }
}
