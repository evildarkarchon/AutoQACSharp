using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Backup;
using AutoQAC.Services.UI;
using ReactiveUI;

namespace AutoQAC.ViewModels;

public sealed class RestoreViewModel : ViewModelBase, IDisposable
{
    private readonly IBackupService _backupService;
    private readonly IMessageDialogService _messageDialog;
    private readonly ILoggingService _logger;
    private readonly CompositeDisposable _disposables = new();

    private string? _backupRoot;

    private ObservableCollection<BackupSession> _sessions = new();
    public ObservableCollection<BackupSession> Sessions
    {
        get => _sessions;
        set => this.RaiseAndSetIfChanged(ref _sessions, value);
    }

    private BackupSession? _selectedSession;
    public BackupSession? SelectedSession
    {
        get => _selectedSession;
        set => this.RaiseAndSetIfChanged(ref _selectedSession, value);
    }

    private ObservableCollection<BackupPluginEntry> _selectedSessionPlugins = new();
    public ObservableCollection<BackupPluginEntry> SelectedSessionPlugins
    {
        get => _selectedSessionPlugins;
        set => this.RaiseAndSetIfChanged(ref _selectedSessionPlugins, value);
    }

    private BackupPluginEntry? _selectedPlugin;
    public BackupPluginEntry? SelectedPlugin
    {
        get => _selectedPlugin;
        set => this.RaiseAndSetIfChanged(ref _selectedPlugin, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    private string _statusText = "Select a backup session to view plugins";
    public string StatusText
    {
        get => _statusText;
        set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    private readonly ObservableAsPropertyHelper<bool> _hasSessions;
    public bool HasSessions => _hasSessions.Value;

    public ReactiveCommand<Unit, Unit> LoadSessionsCommand { get; }
    public ReactiveCommand<Unit, Unit> RestorePluginCommand { get; }
    public ReactiveCommand<Unit, Unit> RestoreAllCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteSessionCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    public event EventHandler? CloseRequested;

    /// <summary>
    /// Design-time constructor.
    /// </summary>
    public RestoreViewModel() : this(null!, null!, null!) { }

    public RestoreViewModel(
        IBackupService backupService,
        IMessageDialogService messageDialog,
        ILoggingService logger)
    {
        _backupService = backupService;
        _messageDialog = messageDialog;
        _logger = logger;

        // HasSessions computed property
        _hasSessions = this.WhenAnyValue(x => x.Sessions)
            .Select(s => s.Count > 0)
            .ToProperty(this, x => x.HasSessions);
        _disposables.Add(_hasSessions);

        // When SelectedSession changes, populate the plugins list
        var selectedSessionSubscription = this.WhenAnyValue(x => x.SelectedSession)
            .Subscribe(session =>
            {
                SelectedSessionPlugins.Clear();
                SelectedPlugin = null;

                if (session != null)
                {
                    foreach (var plugin in session.Plugins)
                    {
                        SelectedSessionPlugins.Add(plugin);
                    }
                    StatusText = $"Session: {session.Timestamp:MMM d, yyyy h:mm tt} - {session.Plugins.Count} plugin(s)";
                }
                else
                {
                    StatusText = "Select a backup session to view plugins";
                }
            });
        _disposables.Add(selectedSessionSubscription);

        // Commands
        LoadSessionsCommand = ReactiveCommand.CreateFromTask(LoadSessionsInternalAsync);

        var canRestorePlugin = this.WhenAnyValue(x => x.SelectedPlugin)
            .Select(p => p != null);
        RestorePluginCommand = ReactiveCommand.CreateFromTask(RestorePluginAsync, canRestorePlugin);

        var canRestoreAll = this.WhenAnyValue(x => x.SelectedSession)
            .Select(s => s != null);
        RestoreAllCommand = ReactiveCommand.CreateFromTask(RestoreAllAsync, canRestoreAll);

        DeleteSessionCommand = ReactiveCommand.CreateFromTask(DeleteSessionAsync, canRestoreAll);

        CloseCommand = ReactiveCommand.Create(() => CloseRequested?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>
    /// Sets the backup root path and triggers loading sessions.
    /// Called from the view before display.
    /// </summary>
    public async Task LoadSessionsAsync(string? dataFolderPath)
    {
        if (string.IsNullOrEmpty(dataFolderPath))
        {
            StatusText = "No game data folder configured -- cannot locate backups";
            return;
        }

        _backupRoot = _backupService.GetBackupRoot(dataFolderPath);
        await LoadSessionsInternalAsync();
    }

    private async Task LoadSessionsInternalAsync()
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

            // Notify HasSessions by re-raising
            this.RaisePropertyChanged(nameof(Sessions));

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

    private async Task RestoreAllAsync()
    {
        if (SelectedSession == null)
            return;

        var pluginCount = SelectedSession.Plugins.Count;
        var timestamp = SelectedSession.Timestamp.ToString("MMM d, yyyy h:mm tt");

        // Confirmation dialog required for Restore All
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
            this.RaisePropertyChanged(nameof(Sessions));
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

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
