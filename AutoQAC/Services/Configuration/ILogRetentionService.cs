using System.Threading;
using System.Threading.Tasks;

namespace AutoQAC.Services.Configuration;

/// <summary>
/// Cleans up old log files on app startup according to configured retention policy.
/// </summary>
public interface ILogRetentionService
{
    /// <summary>
    /// Reads retention settings from config, enumerates log files, and deletes
    /// those exceeding the configured age or count limit.
    /// The most recent (active Serilog) log file is never deleted.
    /// </summary>
    Task CleanupAsync(CancellationToken ct = default);
}
