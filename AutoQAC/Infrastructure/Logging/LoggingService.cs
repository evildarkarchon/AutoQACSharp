using System;
using System.IO;
using Serilog;
using Serilog.Events;

namespace AutoQAC.Infrastructure.Logging;

public sealed class LoggingService : ILoggingService, IDisposable
{
    private readonly ILogger _logger;

    public LoggingService()
        : this(LogFilePaths.GetLogDirectory())
    {
    }

    public LoggingService(string logDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        Directory.CreateDirectory(logDirectory);

        _logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(LogEventLevel.Warning)
            .WriteTo.File(
                Path.Combine(logDirectory, LogFilePaths.RollingLogFileName),
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 5 * 1024 * 1024,
                retainedFileCountLimit: 5,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    public void Dispose()
    {
        if (_logger is IDisposable disposable) disposable.Dispose();
    }

    public void Debug(string message, params object[] args)
    {
        _logger.Debug(message, args);
    }

    public void Information(string message, params object[] args)
    {
        _logger.Information(message, args);
    }

    public void Warning(string message, params object[] args)
    {
        _logger.Warning(message, args);
    }

    public void Error(Exception? ex, string message, params object[] args)
    {
        _logger.Error(ex, message, args);
    }
}