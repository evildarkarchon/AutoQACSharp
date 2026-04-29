using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Backup;
using AutoQAC.Services.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoQAC.ViewModels;

public sealed partial class RestoreViewModel : ViewModelBase, IDisposable
{
    private readonly IBackupService _backupService;
    private readonly IMessageDialogService _messageDialog;
    private readonly ILoggingService _logger;
    private readonly IUiDispatcher _uiDispatcher;

    private string? _backupRoot;
    private string? _trustedRestoreRoot;
    private CancellationTokenSource? _restoreCts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSessions))]
    private ObservableCollection<BackupSession> _sessions = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSessionCommand))]
    private BackupSession? _selectedSession;

    [ObservableProperty]
    private ObservableCollection<BackupPluginEntry> _selectedSessionPlugins = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestorePluginCommand))]
    private BackupPluginEntry? _selectedPlugin;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusText = "Select a backup session to view plugins";

    [ObservableProperty]
    private string _restoreOutcomeTitle = string.Empty;

    [ObservableProperty]
    private string _restoreSummaryText = string.Empty;

    [ObservableProperty]
    private string _restoreProgressText = string.Empty;

    [ObservableProperty]
    private long _restoreBytesCopied;

    [ObservableProperty]
    private long? _restoreTotalBytes;

    [ObservableProperty]
    private ObservableCollection<BackupRestoreRowResult> _restoreResults = new();

    [ObservableProperty]
    private bool _isRestoreResultVisible;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestorePluginCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSessionCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadSessionsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelRestoreCommand))]
    private bool _isRestoreActive;

    public bool HasSessions => Sessions.Count > 0;

    public event EventHandler? CloseRequested;

    /// <summary>Design-time constructor.</summary>
    public RestoreViewModel() : this(null!, null!, null!, new SynchronousFallbackDispatcher()) { }

    public RestoreViewModel(
        IBackupService backupService,
        IMessageDialogService messageDialog,
        ILoggingService logger,
        IUiDispatcher uiDispatcher)
    {
        _backupService = backupService;
        _messageDialog = messageDialog;
        _logger = logger;
        _uiDispatcher = uiDispatcher;
    }

    private bool HasTrustedRestoreRoot => !string.IsNullOrWhiteSpace(_trustedRestoreRoot);

    partial void OnSelectedSessionChanged(BackupSession? value)
    {
        SelectedSessionPlugins.Clear();
        SelectedPlugin = null;

        if (value != null)
        {
            foreach (var plugin in value.Plugins)
            {
                SelectedSessionPlugins.Add(plugin);
            }
            StatusText = $"Session: {value.Timestamp:MMM d, yyyy h:mm tt} - {value.Plugins.Count} plugin(s)";
        }
        else
        {
            StatusText = "Select a backup session to view plugins";
        }
    }

    /// <summary>
    /// Sets the backup root path and triggers loading sessions. Called from the view before display.
    /// </summary>
    public async Task LoadSessionsAsync(string? dataFolderPath)
    {
        _trustedRestoreRoot = string.IsNullOrWhiteSpace(dataFolderPath) ? null : dataFolderPath;
        RestorePluginCommand.NotifyCanExecuteChanged();
        RestoreAllCommand.NotifyCanExecuteChanged();

        if (!HasTrustedRestoreRoot)
        {
            _backupRoot = null;
            // _backupRoot is a private field (not [ObservableProperty]), so the DeleteSession
            // predicate must be re-evaluated manually after assignment.
            DeleteSessionCommand.NotifyCanExecuteChanged();
            StatusText = "No game data folder configured -- cannot locate backups";
            return;
        }

        var trustedRestoreRoot = _trustedRestoreRoot!;
        _backupRoot = _backupService.GetBackupRoot(trustedRestoreRoot);
        // Re-evaluate DeleteSession predicate after _backupRoot transitions to a non-null value.
        DeleteSessionCommand.NotifyCanExecuteChanged();
        await LoadSessions();
    }

    private bool CanLoadSessions() => !IsRestoreActive;

    [RelayCommand(CanExecute = nameof(CanLoadSessions))]
    private async Task LoadSessions()
    {
        if (string.IsNullOrEmpty(_backupRoot))
        {
            StatusText = "No backup root configured";
            return;
        }

        try
        {
            IsLoading = true;
            StatusText = "Loading backup sessions...";

            var sessions = await _backupService.GetBackupSessionsAsync(_backupRoot);

            Sessions.Clear();
            foreach (var session in sessions)
            {
                Sessions.Add(session);
            }

            OnPropertyChanged(nameof(HasSessions));

            StatusText = sessions.Count > 0
                ? $"Found {sessions.Count} backup session(s)"
                : "No backup sessions found";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load backup sessions");
            StatusText = $"Error loading sessions: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanRestorePlugin() => SelectedPlugin != null && HasTrustedRestoreRoot && !IsRestoreActive;

    [RelayCommand(CanExecute = nameof(CanRestorePlugin))]
    private async Task RestorePluginAsync()
    {
        if (SelectedPlugin == null || SelectedSession == null)
            return;

        var plugin = SelectedPlugin;
        var session = SelectedSession;
        var timestamp = FormatSessionTimestamp(session.Timestamp);
        var confirmed = await _messageDialog.ShowConfirmAsync(
            "Restore Selected",
            $"Restore Selected: Restore {plugin.FileName} from {timestamp}? This overwrites the current plugin file with the backup copy.");

        if (!confirmed)
            return;

        try
        {
            IsRestoreActive = true;
            ClearRestoreResult();
            StatusText = $"Restoring: {plugin.FileName}";
            RestoreProgressText = $"Restoring 1 / 1 plugins";

            var cts = CreateRestoreCancellationSource();
            var progress = CreateRestoreProgressReporter([plugin]);

            var result = await _backupService.RestorePluginAsync(plugin, session.SessionDirectory, _trustedRestoreRoot, progress, cts.Token);
            ApplyRestoreResult(result);
            _logger.Information("Restore selected completed with status {Status} for plugin {Plugin}", result.Status, plugin.FileName);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to restore plugin {Plugin}", plugin.FileName);
            await _messageDialog.ShowErrorAsync(
                "Restore Failed",
                $"Failed to restore '{plugin.FileName}'.",
                "Technical details were written to the log.");
            StatusText = $"Failed to restore: {plugin.FileName}";
        }
        finally
        {
            DisposeRestoreCancellationSource(cancel: false);
            IsRestoreActive = false;
        }
    }

    private bool CanRestoreAll() => SelectedSession != null && HasTrustedRestoreRoot && !IsRestoreActive;

    [RelayCommand(CanExecute = nameof(CanRestoreAll))]
    private async Task RestoreAllAsync()
    {
        if (SelectedSession == null)
            return;

        var pluginCount = SelectedSession.Plugins.Count;
        var session = SelectedSession;
        var timestamp = FormatSessionTimestamp(session.Timestamp);

        var confirmed = await _messageDialog.ShowConfirmAsync(
            "Restore All",
            $"Restore All: Restore {pluginCount} plugin(s) from {timestamp}? Current plugin files will be overwritten by backup copies. AutoQAC will continue past individual failures and show a result list.");

        if (!confirmed)
            return;

        try
        {
            IsRestoreActive = true;
            ClearRestoreResult();
            StatusText = $"Restoring {pluginCount} plugin(s) from session";
            RestoreProgressText = $"Restoring 0 / {pluginCount} plugins";

            var cts = CreateRestoreCancellationSource();
            var progress = CreateRestoreProgressReporter(session.Plugins);

            var result = await _backupService.RestoreSessionAsync(session, _trustedRestoreRoot, progress, cts.Token);
            ApplyRestoreResult(result);
            _logger.Information("Restore all completed with status {Status} for backup session {Timestamp}",
                result.Status, timestamp);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to restore session");
            await _messageDialog.ShowErrorAsync(
                "Restore Failed",
                "Some plugins may have failed to restore.",
                "Technical details were written to the log.");
            StatusText = "Partial restore -- some plugins may have failed";
        }
        finally
        {
            DisposeRestoreCancellationSource(cancel: false);
            IsRestoreActive = false;
        }
    }

    private bool CanCancelRestore() => IsRestoreActive;

    [RelayCommand(CanExecute = nameof(CanCancelRestore))]
    private void CancelRestore()
    {
        _restoreCts?.Cancel();
        StatusText = "Cancel restore requested. The partial file will be deleted and completed/failed/canceled rows will remain visible.";
    }

    /// <summary>
    /// Plan 07-13: gates DeleteSessionCommand on a non-null/non-whitespace _backupRoot AND a
    /// loaded trusted restore root, unifying Delete Session safety with Restore Selected/All
    /// gating from Plan 07-11. _backupRoot is a private field rather than an [ObservableProperty],
    /// so callers must invoke <see cref="System.Windows.Input.ICommand"/> NotifyCanExecuteChanged
    /// after mutating it (see <see cref="LoadSessionsAsync"/>).
    /// </summary>
    private bool CanDeleteSession() =>
        SelectedSession != null &&
        !string.IsNullOrWhiteSpace(_backupRoot) &&
        HasTrustedRestoreRoot &&
        !IsRestoreActive;

    [RelayCommand(CanExecute = nameof(CanDeleteSession))]
    private async Task DeleteSessionAsync()
    {
        // Defense-in-depth guard: even if a caller bypasses CanExecute, a missing _backupRoot
        // or null SelectedSession must still short-circuit before invoking the backup service.
        if (SelectedSession == null || string.IsNullOrWhiteSpace(_backupRoot))
            return;

        var session = SelectedSession;
        var timestamp = session.Timestamp.ToString("MMM d, yyyy h:mm tt");
        var confirmed = await _messageDialog.ShowConfirmAsync(
            "Delete Backup Session",
            $"Permanently delete backup session from {timestamp}?\n\n" +
            "This action cannot be undone.");

        if (!confirmed)
            return;

        // Plan 07-13: filesystem work runs in IBackupService.DeleteSessionAsync (per CLAUDE.md
        // "All business logic lives in services, not ViewModels"). The service validates
        // session-directory containment via BackupPathContainment.IsContained (Plan 07-14)
        // and routes the recursive delete through IBackupSessionDeleter (Plan 07-02).
        var result = await _backupService.DeleteSessionAsync(session, _backupRoot!, CancellationToken.None);

        switch (result.Status)
        {
            case BackupSessionDeleteStatus.Deleted:
                Sessions.Remove(session);
                SelectedSession = null;
                OnPropertyChanged(nameof(HasSessions));
                StatusText = "Session deleted";
                _logger.Information("Deleted backup session: {Timestamp}", timestamp);
                break;

            case BackupSessionDeleteStatus.RejectedOutsideBackupRoot:
                // One canonical user-facing sentence is shared between StatusText and dialog
                // details so the safety message stays consistent across the restore-window
                // surface (LOW finding from cross-AI review).
                StatusText = "The selected backup session is outside the configured backup folder.";
                await _messageDialog.ShowErrorAsync(
                    "Delete Failed",
                    "Failed to delete the backup session.",
                    "The selected backup session is outside the configured backup folder.");
                break;

            case BackupSessionDeleteStatus.Failed:
            default:
                // Generic IO failure copy: the technical exception detail lives in the
                // service log, not in the dialog (D-04 concise reason pattern).
                StatusText = "Failed to delete the backup session. Technical details were written to the log.";
                await _messageDialog.ShowErrorAsync(
                    "Delete Failed",
                    "Failed to delete the backup session.",
                    "Technical details were written to the log.");
                break;
        }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Clears the inline restore result state before a new restore operation starts.
    /// </summary>
    private void ClearRestoreResult()
    {
        RestoreOutcomeTitle = string.Empty;
        RestoreSummaryText = string.Empty;
        RestoreProgressText = string.Empty;
        RestoreBytesCopied = 0;
        RestoreTotalBytes = null;
        RestoreResults.Clear();
        IsRestoreResultVisible = false;
    }

    /// <summary>
    /// Creates and owns the cancellation source for exactly one active restore operation.
    /// </summary>
    /// <returns>The newly created cancellation source.</returns>
    private CancellationTokenSource CreateRestoreCancellationSource()
    {
        DisposeRestoreCancellationSource(cancel: false);
        _restoreCts = new CancellationTokenSource();
        return _restoreCts;
    }

    /// <summary>
    /// Disposes the active restore cancellation source and optionally requests cancellation first.
    /// </summary>
    /// <param name="cancel">True when disposal should also cancel active restore work.</param>
    private void DisposeRestoreCancellationSource(bool cancel)
    {
        var cts = _restoreCts;
        _restoreCts = null;

        if (cts == null)
            return;

        if (cancel)
        {
            cts.Cancel();
        }

        cts.Dispose();
    }

    /// <summary>
    /// Builds a progress reporter that translates byte-level service progress into restore-window text.
    /// </summary>
    /// <param name="plugins">Plugins included in the current restore operation.</param>
    /// <returns>A progress reporter safe for the backup service to call during restore copies.</returns>
    private IProgress<BackupCopyProgress> CreateRestoreProgressReporter(IReadOnlyList<BackupPluginEntry> plugins) =>
        new RestoreProgressReporter(progress =>
            _uiDispatcher.Post(() => UpdateRestoreProgress(progress, plugins)));

    /// <summary>
    /// Updates bindable progress fields with plugin position and decimal byte counts when available.
    /// </summary>
    /// <param name="progress">Latest copy progress from the backup service.</param>
    /// <param name="plugins">Plugins included in the active restore operation.</param>
    private void UpdateRestoreProgress(BackupCopyProgress progress, IReadOnlyList<BackupPluginEntry> plugins)
    {
        RestoreBytesCopied = progress.BytesCopied;
        RestoreTotalBytes = progress.TotalBytes;

        var currentIndex = -1;
        for (var i = 0; i < plugins.Count; i++)
        {
            if (string.Equals(plugins[i].FileName, progress.FileName, StringComparison.OrdinalIgnoreCase))
            {
                currentIndex = i;
                break;
            }
        }
        var currentCount = currentIndex >= 0 ? currentIndex + 1 : Math.Min(RestoreResults.Count + 1, plugins.Count);
        var text = $"Restoring {currentCount} / {plugins.Count} plugins";

        if (progress.TotalBytes is { } totalBytes)
        {
            text += $" — {BackupProgressTextFormatter.FormatBytes(progress.BytesCopied)} / {BackupProgressTextFormatter.FormatBytes(totalBytes)}";
        }

        RestoreProgressText = text;
    }

    /// <summary>
    /// Marshals service progress through the UI dispatcher before applying it to bindable state.
    /// </summary>
    private sealed class RestoreProgressReporter(Action<BackupCopyProgress> onProgress) : IProgress<BackupCopyProgress>
    {
        /// <summary>
        /// Applies a progress update to the owning ViewModel.
        /// </summary>
        /// <param name="value">Progress value reported by the backup service.</param>
        public void Report(BackupCopyProgress value) => onProgress(value);
    }

    /// <summary>
    /// Design-time fallback dispatcher used only when the XAML designer invokes the parameterless constructor.
    /// </summary>
    private sealed class SynchronousFallbackDispatcher : IUiDispatcher
    {
        /// <inheritdoc />
        public void Post(Action action) => action();

        /// <inheritdoc />
        public Task InvokeAsync(Func<Task> action) => action();
    }

    /// <summary>
    /// Copies a structured restore service result into bindable inline result properties without exposing raw exception details.
    /// </summary>
    /// <param name="result">Structured restore result returned by the backup service.</param>
    private void ApplyRestoreResult(BackupRestoreResult result)
    {
        RestoreOutcomeTitle = result.Status switch
        {
            BackupOperationStatus.Complete => "Restore Complete",
            BackupOperationStatus.Partial => "Restore Partial",
            BackupOperationStatus.Failed => "Restore Failed",
            BackupOperationStatus.Canceled => "Restore Canceled",
            _ => "Restore Failed"
        };

        RestoreResults.Clear();
        foreach (var row in result.Rows)
        {
            RestoreResults.Add(row);
        }

        RestoreSummaryText = BuildRestoreSummaryText(result);
        StatusText = RestoreSummaryText;
        IsRestoreResultVisible = true;
    }

    /// <summary>
    /// Builds concise restore summary copy for the inline result panel.
    /// </summary>
    /// <param name="result">Structured restore result to summarize.</param>
    /// <returns>User-facing summary text without raw file paths or exception details.</returns>
    private static string BuildRestoreSummaryText(BackupRestoreResult result)
    {
        var counts = $"{result.RestoredCount} restored, {result.FailedCount} failed, {result.CanceledCount} canceled";

        return result.Status switch
        {
            BackupOperationStatus.Complete => $"Restore completed: {result.RestoredCount} plugin(s) restored.",
            BackupOperationStatus.Partial => $"Restore partially completed: {counts}. Review the rows below. Technical details were written to the log.",
            BackupOperationStatus.Failed => $"Restore failed: {counts}. Review the failed rows, fix missing files or permissions, then try again. Technical details were written to the log.",
            BackupOperationStatus.Canceled => $"Restore canceled: {counts}. Partial files were removed and completed/failed/canceled rows remain visible.",
            _ => $"Restore completed with status {result.Status}: {counts}."
        };
    }

    /// <summary>
    /// Formats session timestamps for restore confirmation copy so tests and dialogs use one consistent value.
    /// </summary>
    /// <param name="timestamp">Backup session timestamp.</param>
    /// <returns>Short local timestamp suitable for confirmation dialogs.</returns>
    private static string FormatSessionTimestamp(DateTime timestamp) => timestamp.ToString("MMM d, yyyy h:mm tt");

    public void Dispose()
    {
        DisposeRestoreCancellationSource(cancel: true);
    }
}
