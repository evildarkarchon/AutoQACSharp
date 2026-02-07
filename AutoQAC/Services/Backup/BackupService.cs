using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;

namespace AutoQAC.Services.Backup;

/// <summary>
/// Manages plugin file backup, restore, and session retention.
/// </summary>
public sealed class BackupService : IBackupService
{
    private readonly ILoggingService _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public BackupService(ILoggingService logger)
    {
        _logger = logger;
    }

    public string CreateSessionDirectory(string backupRoot)
    {
        var sessionName = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var sessionDir = Path.Combine(backupRoot, sessionName);
        Directory.CreateDirectory(sessionDir);
        _logger.Information("Created backup session directory: {SessionDir}", sessionDir);
        return sessionDir;
    }

    public BackupResult BackupPlugin(PluginInfo plugin, string sessionDir)
    {
        try
        {
            if (string.IsNullOrEmpty(plugin.FullPath) || !Path.IsPathRooted(plugin.FullPath))
            {
                return BackupResult.Failure($"Plugin path is not a valid rooted path: '{plugin.FullPath}'");
            }

            if (!File.Exists(plugin.FullPath))
            {
                return BackupResult.Failure($"Source file does not exist: '{plugin.FullPath}'");
            }

            // Ensure session directory exists (idempotent)
            Directory.CreateDirectory(sessionDir);

            var destPath = Path.Combine(sessionDir, plugin.FileName);
            File.Copy(plugin.FullPath, destPath, overwrite: false);

            var fileSize = new FileInfo(destPath).Length;
            _logger.Debug("Backed up {Plugin} ({Size} bytes) to {Dest}", plugin.FileName, fileSize, destPath);

            return BackupResult.Ok(fileSize);
        }
        catch (IOException ex)
        {
            _logger.Warning("Backup failed for {Plugin}: {Error}", plugin.FileName, ex.Message);
            return BackupResult.Failure($"I/O error backing up '{plugin.FileName}': {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Warning("Backup failed for {Plugin}: {Error}", plugin.FileName, ex.Message);
            return BackupResult.Failure($"Access denied backing up '{plugin.FileName}': {ex.Message}");
        }
    }

    public async Task WriteSessionMetadataAsync(string sessionDir, BackupSession session, CancellationToken ct = default)
    {
        var metadataPath = Path.Combine(sessionDir, "session.json");

        await using var stream = new FileStream(metadataPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, session, JsonOptions, ct).ConfigureAwait(false);

        _logger.Debug("Wrote session metadata to {Path}", metadataPath);
    }

    public async Task<List<BackupSession>> GetBackupSessionsAsync(string backupRoot, CancellationToken ct = default)
    {
        var sessions = new List<BackupSession>();

        if (!Directory.Exists(backupRoot))
        {
            return sessions;
        }

        var directories = Directory.GetDirectories(backupRoot)
            .OrderByDescending(d => Path.GetFileName(d))
            .ToArray();

        foreach (var dir in directories)
        {
            ct.ThrowIfCancellationRequested();

            var metadataPath = Path.Combine(dir, "session.json");
            if (!File.Exists(metadataPath))
            {
                _logger.Debug("Skipping directory without session.json: {Dir}", dir);
                continue;
            }

            try
            {
                await using var stream = new FileStream(metadataPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var session = await JsonSerializer.DeserializeAsync<BackupSession>(stream, cancellationToken: ct)
                    .ConfigureAwait(false);

                if (session != null)
                {
                    // Populate SessionDirectory from filesystem path (not stored in JSON)
                    sessions.Add(session with { SessionDirectory = dir });
                }
            }
            catch (JsonException ex)
            {
                _logger.Warning("Corrupt session.json in {Dir}: {Error}", dir, ex.Message);
            }
            catch (IOException ex)
            {
                _logger.Warning("Failed to read session.json in {Dir}: {Error}", dir, ex.Message);
            }
        }

        return sessions;
    }

    public void RestorePlugin(BackupPluginEntry entry, string sessionDir)
    {
        var backupPath = Path.Combine(sessionDir, entry.FileName);
        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException($"Backup file not found: '{backupPath}'");
        }

        // Ensure the target directory exists
        var targetDir = Path.GetDirectoryName(entry.OriginalPath);
        if (!string.IsNullOrEmpty(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        File.Copy(backupPath, entry.OriginalPath, overwrite: true);
        _logger.Information("Restored {Plugin} to {Path}", entry.FileName, entry.OriginalPath);
    }

    public void RestoreSession(BackupSession session)
    {
        foreach (var entry in session.Plugins)
        {
            RestorePlugin(entry, session.SessionDirectory);
        }
    }

    public void CleanupOldSessions(string backupRoot, int maxSessionCount, string? currentSessionDir = null)
    {
        if (!Directory.Exists(backupRoot))
        {
            return;
        }

        var directories = Directory.GetDirectories(backupRoot)
            .OrderByDescending(d => Path.GetFileName(d))
            .ToList();

        // Keep the newest maxSessionCount entries and always keep currentSessionDir
        var toDelete = new List<string>();
        var keepCount = 0;

        foreach (var dir in directories)
        {
            // Never delete the current session directory
            if (currentSessionDir != null &&
                string.Equals(Path.GetFullPath(dir), Path.GetFullPath(currentSessionDir), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            keepCount++;
            if (keepCount > maxSessionCount)
            {
                toDelete.Add(dir);
            }
        }

        foreach (var dir in toDelete)
        {
            try
            {
                Directory.Delete(dir, recursive: true);
                _logger.Information("Deleted old backup session: {Dir}", dir);
            }
            catch (Exception ex)
            {
                _logger.Warning("Failed to delete old backup session {Dir}: {Error}", dir, ex.Message);
            }
        }
    }

    public string GetBackupRoot(string dataFolderPath)
    {
        var parentDir = Path.GetDirectoryName(dataFolderPath);
        if (string.IsNullOrEmpty(parentDir))
        {
            // Fallback: use the data folder itself as parent
            parentDir = dataFolderPath;
        }

        return Path.Combine(parentDir, "AutoQAC Backups");
    }
}
