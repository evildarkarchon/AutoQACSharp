using System;
using System.IO;

namespace AutoQAC.Services.Process;

/// <summary>
/// Resolves the production PID tracking file path using the same AutoQAC Data convention as configuration files.
/// </summary>
public sealed class DefaultPidStorePathProvider : IPidStorePathProvider
{
    private const string PidFileName = "autoqac-pids.json";

    /// <inheritdoc />
    public string PidFilePath => GetPidFilePath();

    private static string GetPidFilePath()
    {
        var baseDir = AppContext.BaseDirectory;

#if DEBUG
        var current = new DirectoryInfo(baseDir);
        for (var i = 0; i < 6 && current != null; i++)
        {
            var candidate = Path.Combine(current.FullName, "AutoQAC Data");
            if (Directory.Exists(candidate))
            {
                return Path.Combine(candidate, PidFileName);
            }

            current = current.Parent;
        }
#endif

        var configDir = Path.Combine(baseDir, "AutoQAC Data");
        if (!Directory.Exists(configDir))
        {
            Directory.CreateDirectory(configDir);
        }

        return Path.Combine(configDir, PidFileName);
    }
}
