using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models.Configuration;

namespace AutoQAC.Services.Configuration;

/// <summary>
/// Cleans up old log files on app startup according to configured retention policy.
/// Always skips the most recent log file (the active Serilog file).
/// </summary>
public sealed class LogRetentionService : ILogRetentionService
{
    private readonly IConfigurationService _configService;
    private readonly ILoggingService _logger;

    private const string LogDirectory = "logs";
    private const string LogFilePattern = "autoqac-*.log";

    public LogRetentionService(IConfigurationService configService, ILoggingService logger)
    {
        _configService = configService;
        _logger = logger;
    }

    public async Task CleanupAsync(CancellationToken ct = default)
    {
        try
        {
            if (!Directory.Exists(LogDirectory))
            {
                _logger.Debug("[LogRetention] Log directory does not exist, skipping cleanup");
                return;
            }

            var config = await _configService.LoadUserConfigAsync(ct);
            var settings = config.LogRetention;

            var logFiles = Directory.GetFiles(LogDirectory, LogFilePattern)
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            if (logFiles.Count <= 1)
            {
                _logger.Debug("[LogRetention] No old log files to clean up ({Count} total)", logFiles.Count);
                return;
            }

            // CRITICAL: Always skip the first file (most recent = active Serilog log file)
            var candidates = logFiles.Skip(1).ToList();

            int deletedCount = 0;

            switch (settings.Mode)
            {
                case RetentionMode.AgeBased:
                {
                    var cutoff = DateTime.UtcNow.AddDays(-settings.MaxAgeDays);
                    foreach (var file in candidates)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (file.LastWriteTimeUtc < cutoff)
                        {
                            if (TryDeleteFile(file.FullName))
                                deletedCount++;
                        }
                    }
                    break;
                }
                case RetentionMode.CountBased:
                {
                    // We want to keep MaxFileCount files total. Since we already skipped the
                    // active file (index 0), we keep (MaxFileCount - 1) more from candidates.
                    var maxToKeep = Math.Max(0, settings.MaxFileCount - 1);
                    var toDelete = candidates.Skip(maxToKeep).ToList();
                    foreach (var file in toDelete)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (TryDeleteFile(file.FullName))
                            deletedCount++;
                    }
                    break;
                }
            }

            if (deletedCount > 0)
            {
                _logger.Information("[LogRetention] Cleaned up {Count} old log files", deletedCount);
            }
            else
            {
                _logger.Debug("[LogRetention] No log files exceeded retention policy");
            }
        }
        catch (OperationCanceledException)
        {
            throw; // Let cancellation propagate
        }
        catch (Exception ex)
        {
            _logger.Warning("[LogRetention] Failed to complete log cleanup: {Message}", ex.Message);
        }
    }

    private bool TryDeleteFile(string filePath)
    {
        try
        {
            File.Delete(filePath);
            _logger.Debug("[LogRetention] Deleted old log file: {File}", Path.GetFileName(filePath));
            return true;
        }
        catch (Exception ex)
        {
            _logger.Warning("[LogRetention] Failed to delete {File}: {Message}",
                Path.GetFileName(filePath), ex.Message);
            return false;
        }
    }
}
