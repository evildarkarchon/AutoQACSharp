using System;
using System.Collections.ObjectModel;
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

    private string? _backupRoot;

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

    public bool HasSessions => Sessions.Count > 0;

    public event EventHandler? CloseRequested;

    /// <summary>Design-time constructor.</summary>
    public RestoreViewModel() : this(null!, null!, null!) { }

    public RestoreViewModel(
        IBackupService backupService,
        IMessageDialogService messageDialog,
        ILoggingService logger)
    {
        _backupService = backupService;
        _messageDialog = messageDialog;
        _logger = logger;
    }

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
        if (string.IsNullOrEmpty(dataFolderPath))
        {
            StatusText = "No game data folder configured -- cannot locate backups";
            return;
        }

        _backupRoot = _backupService.GetBackupRoot(dataFolderPath);
        await LoadSessions();
    }

    [RelayCommand]
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

    private bool CanRestorePlugin() => SelectedPlugin != null;

    [RelayCommand(CanExecute = nameof(CanRestorePlugin))]
    private async Task RestorePluginAsync()
    {
        if (SelectedPlugin == null || SelectedSession == null)
            return;

        try
        {
            _backupService.RestorePlugin(SelectedPlugin, SelectedSession.SessionDirectory);
            StatusText = $"Restored: {SelectedPlugin.FileName}";
            _logger.Information("Restored plugin {Plugin} from backup", SelectedPlugin.FileName);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to restore plugin {Plugin}", SelectedPlugin.FileName);
            await _messageDialog.ShowErrorAsync(
                "Restore Failed",
                $"Failed to restore '{SelectedPlugin.FileName}'.",
                ex.Message);
            StatusText = $"Failed to restore: {SelectedPlugin.FileName}";
        }
    }

    private bool CanRestoreAll() => SelectedSession != null;

    [RelayCommand(CanExecute = nameof(CanRestoreAll))]
    private async Task RestoreAllAsync()
    {
        if (SelectedSession == null)
            return;

        var pluginCount = SelectedSession.Plugins.Count;
        var timestamp = SelectedSession.Timestamp.ToString("MMM d, yyyy h:mm tt");

        var confirmed = await _messageDialog.ShowConfirmAsync(
            "Restore All Plugins",
            $"Restore all {pluginCount} plugin(s) from session {timestamp}?\n\n" +
            "This will overwrite current files with the backed-up versions.");

        if (!confirmed)
            return;

        try
        {
            _backupService.RestoreSession(SelectedSession);
            StatusText = $"Restored all {pluginCount} plugin(s) from session";
            _logger.Information("Restored all {Count} plugins from backup session {Timestamp}",
                pluginCount, timestamp);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to restore session");
            await _messageDialog.ShowErrorAsync(
                "Restore Failed",
                "Some plugins may have failed to restore.",
                ex.Message);
            StatusText = "Partial restore -- some plugins may have failed";
        }
    }

    [RelayCommand(CanExecute = nameof(CanRestoreAll))]
    private async Task DeleteSessionAsync()
    {
        if (SelectedSession == null)
            return;

        var timestamp = SelectedSession.Timestamp.ToString("MMM d, yyyy h:mm tt");
        var confirmed = await _messageDialog.ShowConfirmAsync(
            "Delete Backup Session",
            $"Permanently delete backup session from {timestamp}?\n\n" +
            "This action cannot be undone.");

        if (!confirmed)
            return;

        try
        {
            var dirToDelete = SelectedSession.SessionDirectory;
            if (System.IO.Directory.Exists(dirToDelete))
            {
                System.IO.Directory.Delete(dirToDelete, recursive: true);
            }

            Sessions.Remove(SelectedSession);
            SelectedSession = null;
            OnPropertyChanged(nameof(HasSessions));
            StatusText = "Session deleted";
            _logger.Information("Deleted backup session: {Timestamp}", timestamp);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete backup session");
            await _messageDialog.ShowErrorAsync(
                "Delete Failed",
                "Failed to delete the backup session.",
                ex.Message);
        }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
    }
}
