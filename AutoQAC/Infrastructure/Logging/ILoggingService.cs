using System;

namespace AutoQAC.Infrastructure.Logging;

public interface ILoggingService
{
    void Debug(string message, params object[] args);
    void Information(string message, params object[] args);
    void Warning(string message, params object[] args);
    void Error(Exception? ex, string message, params object[] args);
    void Fatal(Exception? ex, string message, params object[] args);
}
