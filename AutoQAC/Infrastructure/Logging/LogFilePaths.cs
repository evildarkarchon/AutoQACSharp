using System;
using System.IO;

namespace AutoQAC.Infrastructure.Logging;

public static class LogFilePaths
{
    public const string LogDirectoryName = "logs";
    public const string LogFilePattern = "autoqac-*.log";
    public const string RollingLogFileName = "autoqac-.log";

    public static string GetLogDirectory()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, LogDirectoryName));
    }
}
