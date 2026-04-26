using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoQAC.ViewModels;

public sealed partial class SettingsViewModel : ViewModelBase, IDisposable
{
    private readonly IConfigurationService _configService;
    private readonly ILoggingService _logger;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IFileDialogService? _fileDialog;

    // Loading flag to suppress validation during initial property population
    private bool _isLoading;

    // Original values for tracking unsaved changes
    private AutoQacSettings _originalSettings = new();
    private RetentionSettings _originalRetention = new();
    private BackupSettings _originalBackupSettings = new();
    private string? _originalXEditPath;
    private string? _originalMo2Path;
    private string? _originalLoadOrderPath;

    // Debounced path validators
    private readonly DebouncedAction _xEditValidate;
    private readonly DebouncedAction _mo2Validate;
    private readonly DebouncedAction _loadOrderValidate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    [NotifyPropertyChangedFor(nameof(HasValidationErrors))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private int _journalExpiration;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    [NotifyPropertyChangedFor(nameof(HasValidationErrors))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private int _cleaningTimeout;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    [NotifyPropertyChangedFor(nameof(HasValidationErrors))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private int _cpuThreshold;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private bool _mo2Mode;

    [ObservableProperty]
    private string? _cleaningTimeoutError;

    [ObservableProperty]
    private string? _journalExpirationError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private string? _xEditPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private string? _mo2Path;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private string? _loadOrderPath;

    [ObservableProperty]
    private bool? _isXEditPathValid;

    [ObservableProperty]
    private bool? _isMo2PathValid;

    [ObservableProperty]
    private bool? _isLoadOrderPathValid;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private int _retentionMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    [NotifyPropertyChangedFor(nameof(HasValidationErrors))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private int _maxAgeDays;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    [NotifyPropertyChangedFor(nameof(HasValidationErrors))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private int _maxFileCount;

    [ObservableProperty]
    private bool _isAgeBasedMode = true;

    [ObservableProperty]
    private bool _isCountBasedMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private bool _backupEnabled = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    [NotifyPropertyChangedFor(nameof(HasValidationErrors))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private int _backupMaxSessions = 10;

    [ObservableProperty]
    private string? _backupMaxSessionsError;

    public bool HasValidationErrors =>
        !ValidateCleaningTimeout(CleaningTimeout)
        || !ValidateJournalExpiration(JournalExpiration)
        || !(CpuThreshold is >= 1 and <= 100)
        || !(MaxAgeDays >= 1 && MaxFileCount >= 1)
        || !(BackupMaxSessions is >= 1 and <= 100);

    public bool HasUnsavedChanges =>
        JournalExpiration != _originalSettings.JournalExpiration ||
        CleaningTimeout != _originalSettings.CleaningTimeout ||
        CpuThreshold != _originalSettings.CpuThreshold ||
        Mo2Mode != _originalSettings.Mo2Mode ||
        XEditPath != _originalXEditPath ||
        Mo2Path != _originalMo2Path ||
        LoadOrderPath != _originalLoadOrderPath ||
        RetentionMode != (int)_originalRetention.Mode ||
        MaxAgeDays != _originalRetention.MaxAgeDays ||
        MaxFileCount != _originalRetention.MaxFileCount ||
        BackupEnabled != _originalBackupSettings.Enabled ||
        BackupMaxSessions != _originalBackupSettings.MaxSessions;

    /// <summary>Raised when the user picks Save or Cancel. The view closes the dialog with this value.</summary>
    public event Action<bool>? CloseRequested;

    /// <summary>Design-time constructor (parameterless for XAML previewer).</summary>
    public SettingsViewModel() : this(null!, null!, new SynchronousFallbackDispatcher(), null) { }

    public SettingsViewModel(
        IConfigurationService configService,
        ILoggingService logger,
        IUiDispatcher uiDispatcher,
        IFileDialogService? fileDialog = null)
    {
        _configService = configService;
        _logger = logger;
        _uiDispatcher = uiDispatcher;
        _fileDialog = fileDialog;

        _xEditValidate = new DebouncedAction(uiDispatcher, TimeSpan.FromMilliseconds(400));
        _mo2Validate = new DebouncedAction(uiDispatcher, TimeSpan.FromMilliseconds(400));
        _loadOrderValidate = new DebouncedAction(uiDispatcher, TimeSpan.FromMilliseconds(400));
    }

    partial void OnCleaningTimeoutChanged(int value) =>
        CleaningTimeoutError = ValidateCleaningTimeout(value)
            ? null
            : "Timeout must be between 30 and 3600 seconds";

    partial void OnJournalExpirationChanged(int value) =>
        JournalExpirationError = ValidateJournalExpiration(value)
            ? null
            : "Expiration must be between 1 and 365 days";

    partial void OnBackupMaxSessionsChanged(int value) =>
        BackupMaxSessionsError = value is >= 1 and <= 100
            ? null
            : "Sessions to keep must be between 1 and 100";

    partial void OnRetentionModeChanged(int value)
    {
        IsAgeBasedMode = value == 0;
        IsCountBasedMode = value == 1;
    }

    partial void OnXEditPathChanged(string? value)
    {
        if (_isLoading) return;
        _xEditValidate.Trigger(() => IsXEditPathValid = ValidateExecutablePath(value));
    }

    partial void OnMo2PathChanged(string? value)
    {
        if (_isLoading) return;
        _mo2Validate.Trigger(() => IsMo2PathValid = string.IsNullOrWhiteSpace(value) ? null : ValidateExecutablePath(value));
    }

    partial void OnLoadOrderPathChanged(string? value)
    {
        if (_isLoading) return;
        _loadOrderValidate.Trigger(() => IsLoadOrderPathValid = string.IsNullOrWhiteSpace(value) ? null : ValidateFilePath(value));
    }

    public async Task LoadSettingsAsync()
    {
        try
        {
            _isLoading = true;

            var config = await _configService.LoadUserConfigAsync();
            _originalSettings = config.Settings;
            _originalRetention = config.LogRetention;

            JournalExpiration = config.Settings.JournalExpiration;
            CleaningTimeout = config.Settings.CleaningTimeout;
            CpuThreshold = config.Settings.CpuThreshold;
            Mo2Mode = config.Settings.Mo2Mode;

            XEditPath = config.XEdit.Binary;
            _originalXEditPath = config.XEdit.Binary;
            Mo2Path = config.ModOrganizer.Binary;
            _originalMo2Path = config.ModOrganizer.Binary;
            LoadOrderPath = config.LoadOrder.File;
            _originalLoadOrderPath = config.LoadOrder.File;

            RetentionMode = (int)config.LogRetention.Mode;
            MaxAgeDays = config.LogRetention.MaxAgeDays;
            MaxFileCount = config.LogRetention.MaxFileCount;

            _originalBackupSettings = config.Backup;
            BackupEnabled = config.Backup.Enabled;
            BackupMaxSessions = config.Backup.MaxSessions;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load settings");
            ResetToDefaults();
        }
        finally
        {
            _isLoading = false;
            ValidateLoadedPaths();
            OnPropertyChanged(nameof(HasUnsavedChanges));
            OnPropertyChanged(nameof(HasValidationErrors));
            SaveCommand.NotifyCanExecuteChanged();
        }
    }

    private void ValidateLoadedPaths()
    {
        if (!string.IsNullOrWhiteSpace(XEditPath))
            IsXEditPathValid = ValidateExecutablePath(XEditPath);

        if (!string.IsNullOrWhiteSpace(Mo2Path))
            IsMo2PathValid = ValidateExecutablePath(Mo2Path);

        if (!string.IsNullOrWhiteSpace(LoadOrderPath))
            IsLoadOrderPathValid = ValidateFilePath(LoadOrderPath);
    }

    private bool CanSave() => !HasValidationErrors;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        try
        {
            var config = await _configService.LoadUserConfigAsync();

            config.Settings.JournalExpiration = JournalExpiration;
            config.Settings.CleaningTimeout = CleaningTimeout;
            config.Settings.CpuThreshold = CpuThreshold;
            config.Settings.Mo2Mode = Mo2Mode;

            config.XEdit.Binary = XEditPath;
            config.ModOrganizer.Binary = Mo2Path;
            config.LoadOrder.File = LoadOrderPath;

            config.LogRetention.Mode = (RetentionMode)RetentionMode;
            config.LogRetention.MaxAgeDays = MaxAgeDays;
            config.LogRetention.MaxFileCount = MaxFileCount;

            config.Backup.Enabled = BackupEnabled;
            config.Backup.MaxSessions = BackupMaxSessions;

            await _configService.SaveUserConfigAsync(config);
            _logger.Information("Settings saved successfully");

            CloseRequested?.Invoke(true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save settings");
            CloseRequested?.Invoke(false);
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

    [RelayCommand]
    private void ResetToDefaults()
    {
        var defaults = new AutoQacSettings();
        JournalExpiration = defaults.JournalExpiration;
        CleaningTimeout = defaults.CleaningTimeout;
        CpuThreshold = defaults.CpuThreshold;
        Mo2Mode = defaults.Mo2Mode;

        var retentionDefaults = new RetentionSettings();
        RetentionMode = (int)retentionDefaults.Mode;
        MaxAgeDays = retentionDefaults.MaxAgeDays;
        MaxFileCount = retentionDefaults.MaxFileCount;

        var backupDefaults = new BackupSettings();
        BackupEnabled = backupDefaults.Enabled;
        BackupMaxSessions = backupDefaults.MaxSessions;
    }

    [RelayCommand]
    private async Task BrowseXEditAsync()
    {
        if (_fileDialog == null) return;

        var path = await _fileDialog.OpenFileDialogAsync(
            "Select xEdit Executable",
            "Executables (*.exe)|*.exe|All Files (*.*)|*.*");

        if (!string.IsNullOrEmpty(path))
        {
            XEditPath = path;
            IsXEditPathValid = ValidateExecutablePath(path);
        }
    }

    [RelayCommand]
    private async Task BrowseMo2Async()
    {
        if (_fileDialog == null) return;

        var path = await _fileDialog.OpenFileDialogAsync(
            "Select Mod Organizer 2 Executable",
            "Executables (*.exe)|*.exe|All Files (*.*)|*.*");

        if (!string.IsNullOrEmpty(path))
        {
            Mo2Path = path;
            IsMo2PathValid = ValidateExecutablePath(path);
        }
    }

    [RelayCommand]
    private async Task BrowseLoadOrderAsync()
    {
        if (_fileDialog == null) return;

        var path = await _fileDialog.OpenFileDialogAsync(
            "Select Load Order File",
            "Text Files (*.txt)|*.txt|All Files (*.*)|*.*");

        if (!string.IsNullOrEmpty(path))
        {
            LoadOrderPath = path;
            IsLoadOrderPathValid = ValidateFilePath(path);
        }
    }

    internal static bool ValidateExecutablePath(string? path)
        => !string.IsNullOrWhiteSpace(path) &&
           path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
           File.Exists(path);

    internal static bool ValidateFilePath(string? path)
        => !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    internal static bool ValidateDirectoryPath(string? path)
        => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);

    internal static bool ValidateOptionalPath(string? path, Func<string?, bool> validator)
        => string.IsNullOrWhiteSpace(path) || validator(path);

    private static bool ValidateCleaningTimeout(int value) => value is >= 30 and <= 3600;
    private static bool ValidateJournalExpiration(int value) => value is >= 1 and <= 365;

    public void Dispose()
    {
        _xEditValidate.Dispose();
        _mo2Validate.Dispose();
        _loadOrderValidate.Dispose();
    }

    /// <summary>
    /// Cancellable, debounced action. Replaces ReactiveUI's
    /// <c>WhenAnyValue(...).Throttle(...).ObserveOn(MainThreadScheduler)</c> for
    /// path-validation pipelines without pulling in System.Reactive.
    /// </summary>
    private sealed class DebouncedAction : IDisposable
    {
        private readonly IUiDispatcher _dispatcher;
        private readonly TimeSpan _delay;
        private CancellationTokenSource? _cts;

        public DebouncedAction(IUiDispatcher dispatcher, TimeSpan delay)
        {
            _dispatcher = dispatcher;
            _delay = delay;
        }

        public void Trigger(Action action)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(_delay, ct).ConfigureAwait(false);
                    _dispatcher.Post(action);
                }
                catch (OperationCanceledException)
                {
                    // Latest trigger superseded this one — drop silently.
                }
            }, ct);
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// Used only by the design-time constructor when no real <see cref="IUiDispatcher"/> is
    /// available; runs callbacks inline. Production code resolves <see cref="IUiDispatcher"/>
    /// from DI.
    /// </summary>
    private sealed class SynchronousFallbackDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();
        public Task InvokeAsync(Func<Task> action) => action();
    }
}
