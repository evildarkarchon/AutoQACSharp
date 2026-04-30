using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Backup;
using AutoQAC.Services.State;

namespace AutoQAC.Services.Cleaning;

/// <summary>
/// Coordinates cleaning-session backup directory creation, per-plugin backups, metadata writes,
/// retention cleanup, and active backup-operation cancellation.
/// </summary>
public sealed class BackupSessionCoordinator : IBackupSessionCoordinator
{
    private readonly IBackupService _backupService;
    private readonly IStateService _stateService;
    private readonly ILoggingService _logger;

    private readonly object _backupOperationLock = new();
    private CancellationTokenSource? _backupOperationCts;

    /// <summary>
    /// Creates the coordinator with backup, state, and logging collaborators; the coordinator owns
    /// only its short-lived non-xEdit operation CTS state.
    /// </summary>
    /// <param name="backupService">Backup service used for file copies, metadata, and retention cleanup.</param>
    /// <param name="stateService">State service used to publish and clear visible backup-operation progress.</param>
    /// <param name="logger">Logger for diagnostics that should not be surfaced as raw exceptions.</param>
    public BackupSessionCoordinator(IBackupService backupService, IStateService stateService, ILoggingService logger)
    {
        _backupService = backupService;
        _stateService = stateService;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<string?> BeginSessionAsync(CleaningPreflightPlan plan, CancellationToken sessionToken)
    {
        sessionToken.ThrowIfCancellationRequested();

        if (!plan.BackupEnabled)
        {
            return Task.FromResult<string?>(null);
        }

        if (plan.IsMo2ModeActive)
        {
            _logger.Warning("Backup skipped in MO2 mode -- MO2 manages files through its virtual filesystem");
            return Task.FromResult<string?>(null);
        }

        // Derive data folder from the first plugin with a rooted FullPath
        var firstRootedPlugin = plan.PluginRows
            .Where(r => r.Decision == PreflightDecision.Clean)
            .Select(r => r.Plugin)
            .FirstOrDefault(p => !string.IsNullOrEmpty(p.FullPath) && Path.IsPathRooted(p.FullPath));

        if (firstRootedPlugin is null)
        {
            _logger.Warning("Backup enabled but no plugins have rooted paths -- skipping backup initialization");
            return Task.FromResult<string?>(null);
        }

        var dataFolder = Path.GetDirectoryName(firstRootedPlugin.FullPath)!;
        var backupRoot = _backupService.GetBackupRoot(dataFolder);
        var sessionDir = _backupService.CreateSessionDirectory(backupRoot);
        _logger.Information("Backup session directory created: {SessionDir}", sessionDir);
        return Task.FromResult<string?>(sessionDir);
    }

    /// <inheritdoc />
    public async Task<PluginBackupOutcome> RunPluginBackupAsync(
        PluginInfo plugin,
        string sessionDir,
        BackupFailureCallback? onBackupFailure,
        CancellationToken sessionToken)
    {
        var backupResult = await BackupPluginAsync(plugin, sessionDir, sessionToken).ConfigureAwait(false);
        var reasonText = GetBackupFailureReasonText(backupResult);

        if (backupResult.Status == BackupOperationStatus.Canceled)
        {
            _logger.Information("Backup canceled for {Plugin}; skipping xEdit launch for this plugin", plugin.FileName);
            return new PluginBackupOutcome
            {
                Kind = PluginBackupOutcomeKind.Canceled,
                SkippedResult = new PluginCleaningResult
                {
                    PluginName = plugin.FileName,
                    Status = CleaningStatus.Skipped,
                    Success = false,
                    Message = "Backup canceled"
                },
                FailureReasonText = reasonText
            };
        }

        if (backupResult.Status == BackupOperationStatus.Complete)
        {
            return new PluginBackupOutcome
            {
                Kind = PluginBackupOutcomeKind.Succeeded,
                Entry = new BackupPluginEntry
                {
                    FileName = plugin.FileName,
                    OriginalPath = plugin.FullPath,
                    FileSizeBytes = backupResult.TotalBytes ?? backupResult.BytesCopied
                }
            };
        }

        if (onBackupFailure is null)
        {
            _logger.Warning("Backup failed for {Plugin}: {Reason}. No callback, continuing without backup.",
                plugin.FileName, reasonText);
            return new PluginBackupOutcome
            {
                Kind = PluginBackupOutcomeKind.ContinueWithoutBackup,
                FailureReasonText = reasonText
            };
        }

        var choice = await onBackupFailure(plugin.FileName, reasonText).ConfigureAwait(false);
        switch (choice)
        {
            case BackupFailureChoice.SkipPlugin:
                _logger.Information("User chose to skip plugin after backup failure: {Plugin}", plugin.FileName);
                return new PluginBackupOutcome
                {
                    Kind = PluginBackupOutcomeKind.UserSkipped,
                    SkippedResult = new PluginCleaningResult
                    {
                        PluginName = plugin.FileName,
                        Status = CleaningStatus.Skipped,
                        Success = false,
                        Message = "Backup failed - skipped by user"
                    },
                    FailureReasonText = reasonText
                };

            case BackupFailureChoice.AbortSession:
                _logger.Information("User chose to abort session after backup failure for: {Plugin}", plugin.FileName);
                return new PluginBackupOutcome
                {
                    Kind = PluginBackupOutcomeKind.AbortSession,
                    FailureReasonText = reasonText
                };

            case BackupFailureChoice.ContinueWithoutBackup:
            default:
                _logger.Information("User chose to continue without backup for: {Plugin}", plugin.FileName);
                return new PluginBackupOutcome
                {
                    Kind = PluginBackupOutcomeKind.ContinueWithoutBackup,
                    FailureReasonText = reasonText
                };
        }
    }

    /// <inheritdoc />
    public async Task<BackupRetentionCleanupResult?> FinalizeSessionAsync(
        string sessionDir,
        GameType gameType,
        IReadOnlyList<BackupPluginEntry> entries,
        int maxSessions,
        CancellationToken sessionToken)
    {
        if (entries.Count == 0)
        {
            return null;
        }

        var backupSession = new BackupSession
        {
            Timestamp = DateTime.UtcNow,
            GameType = gameType.ToString(),
            SessionDirectory = sessionDir,
            Plugins = entries.ToList()
        };
        await _backupService.WriteSessionMetadataAsync(sessionDir, backupSession, sessionToken).ConfigureAwait(false);

        var backupRoot = Path.GetDirectoryName(sessionDir)!;
        var retentionResult = await CleanupOldSessionsAsync(
                backupRoot,
                maxSessions,
                sessionDir,
                sessionToken)
            .ConfigureAwait(false);
        _logger.Information("Backup session complete: {Count} plugins backed up", entries.Count);
        return retentionResult;
    }

    /// <inheritdoc />
    public async Task WritePartialMetadataAsync(
        string sessionDir,
        GameType gameType,
        IReadOnlyList<BackupPluginEntry> entries,
        CancellationToken ct)
    {
        if (entries.Count == 0)
        {
            return;
        }

        try
        {
            var partialBackupSession = new BackupSession
            {
                Timestamp = DateTime.UtcNow,
                GameType = gameType.ToString(),
                SessionDirectory = sessionDir,
                Plugins = entries.ToList()
            };
            await _backupService.WriteSessionMetadataAsync(
                sessionDir,
                partialBackupSession,
                ct).ConfigureAwait(false);
        }
        catch (Exception backupEx)
        {
            _logger.Warning("Failed to write partial backup metadata after cancellation: {Error}", backupEx.Message);
        }
    }

    /// <inheritdoc />
    public Task CancelActiveOperationAsync()
    {
        CancellationTokenSource? cts;
        lock (_backupOperationLock)
        {
            cts = _backupOperationCts;
        }

        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The operation completed between reading the CTS and attempting cancellation.
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs a single-plugin backup with a file-operation CTS and publishes progress through state.
    /// </summary>
    /// <param name="plugin">Plugin to back up before xEdit runs.</param>
    /// <param name="sessionDir">Backup session directory that receives the copied file.</param>
    /// <param name="sessionToken">Cleaning-session cancellation token linked into the operation CTS.</param>
    /// <returns>Structured backup creation result from the backup service.</returns>
    private async Task<BackupCreateResult> BackupPluginAsync(
        PluginInfo plugin,
        string sessionDir,
        CancellationToken sessionToken)
    {
        var operationCts = CreateBackupOperationCts(sessionToken);
        var progress = new Progress<BackupCopyProgress>(copyProgress =>
        {
            _stateService.SetBackupOperation(new BackupOperationState
            {
                Kind = BackupOperationKind.Backup,
                Label = $"Backing up: {plugin.FileName}",
                FileName = copyProgress.FileName,
                FilesCompleted = 0,
                TotalFiles = 1,
                BytesCopied = copyProgress.BytesCopied,
                TotalBytes = copyProgress.TotalBytes,
                IsActive = true,
                CanCancel = true
            });
        });

        try
        {
            _stateService.SetBackupOperation(new BackupOperationState
            {
                Kind = BackupOperationKind.Backup,
                Label = $"Backing up: {plugin.FileName}",
                FileName = plugin.FileName,
                FilesCompleted = 0,
                TotalFiles = 1,
                IsActive = true,
                CanCancel = true
            });

            return await _backupService.BackupPluginAsync(plugin, sessionDir, progress, operationCts.Token)
                .ConfigureAwait(false);
        }
        finally
        {
            _stateService.ClearBackupOperation();
            ClearBackupOperationCts(operationCts);
        }
    }

    /// <summary>
    /// Runs retention cleanup before final session completion and publishes cleanup progress through state.
    /// </summary>
    /// <param name="backupRoot">Root directory containing backup session directories.</param>
    /// <param name="maxSessionCount">Maximum retained session count.</param>
    /// <param name="currentSessionDir">Current session directory that cleanup must never delete.</param>
    /// <param name="sessionToken">Cleaning-session cancellation token linked into the operation CTS.</param>
    /// <returns>Structured retention cleanup result from the backup service.</returns>
    private async Task<BackupRetentionCleanupResult> CleanupOldSessionsAsync(
        string backupRoot,
        int maxSessionCount,
        string currentSessionDir,
        CancellationToken sessionToken)
    {
        var operationCts = CreateBackupOperationCts(sessionToken);
        var progress = new Progress<BackupCopyProgress>(copyProgress =>
        {
            _stateService.SetBackupOperation(new BackupOperationState
            {
                Kind = BackupOperationKind.RetentionCleanup,
                Label = "Cleaning up old backups",
                FileName = copyProgress.FileName,
                FilesCompleted = copyProgress.FilesCompleted,
                TotalFiles = copyProgress.TotalFiles,
                BytesCopied = copyProgress.BytesCopied,
                TotalBytes = copyProgress.TotalBytes,
                IsActive = true,
                CanCancel = true
            });
        });

        try
        {
            _stateService.SetBackupOperation(new BackupOperationState
            {
                Kind = BackupOperationKind.RetentionCleanup,
                Label = "Cleaning up old backups",
                IsActive = true,
                CanCancel = true
            });

            return await _backupService.CleanupOldSessionsAsync(
                    backupRoot,
                    maxSessionCount,
                    currentSessionDir,
                    progress,
                    operationCts.Token)
                .ConfigureAwait(false);
        }
        finally
        {
            _stateService.ClearBackupOperation();
            ClearBackupOperationCts(operationCts);
        }
    }

    /// <summary>
    /// Creates the current non-xEdit operation CTS linked to the overall cleaning session.
    /// </summary>
    private CancellationTokenSource CreateBackupOperationCts(CancellationToken sessionToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
        lock (_backupOperationLock)
        {
            _backupOperationCts = cts;
        }

        return cts;
    }

    /// <summary>
    /// Clears and disposes the active non-xEdit operation CTS when the owning operation exits.
    /// </summary>
    private void ClearBackupOperationCts(CancellationTokenSource? expected = null)
    {
        CancellationTokenSource? toDispose = null;
        lock (_backupOperationLock)
        {
            if (expected == null || ReferenceEquals(_backupOperationCts, expected))
            {
                toDispose = _backupOperationCts;
                _backupOperationCts = null;
            }
        }

        toDispose?.Dispose();
    }

    private static string GetBackupFailureReasonText(BackupCreateResult result) =>
        result.DisplayReason ?? result.FailureReason?.ToString() ?? "Backup failed";
}
