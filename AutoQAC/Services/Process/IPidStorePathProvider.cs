namespace AutoQAC.Services.Process;

/// <summary>
/// Provides the PID tracking file path so production and tests can use different storage locations.
/// </summary>
public interface IPidStorePathProvider
{
    /// <summary>Gets the full path to the PID tracking JSON file.</summary>
    string PidFilePath { get; }
}
