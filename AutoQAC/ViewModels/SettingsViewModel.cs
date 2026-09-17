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
    private const string SaveFailureBanner =
        "Could not save settings. Active settings were restored to last saved values; your edits are still shown.";

    private readonly IDisposable? _configChangedClearSubscription;

    private readonly IConfigurationService? _configService;
    private readonly IDiscoverySettingsModule? _discoverySettingsModule;
    private readonly DiscoverySettingsAdmission? _admission;
    private readonly IUiDispatcher? _dispatcher;
    private readonly CancellationTokenSource _lifetime = new();
    private UserConfiguration? _baseline;
    private bool _disposed;
    private readonly IDisposable? _failuresSubscription;
    private readonly IFileDialogService? _fileDialog;
    private readonly DebouncedAction _loadOrderValidate;
    private readonly ILoggingService? _logger;
    private readonly DebouncedAction _mo2Validate;
    private readonly IDisposable? _resultsSubscription;

    // Debounced path validators
    private readonly DebouncedAction _xEditValidate;

    // Loading flag to suppress validation during initial property population
    private bool _isLoading;
    private bool _loadSucceeded;
    private BackupSettings _originalBackupSettings = new();
    private string? _originalLoadOrderPath;
    private string? _originalMo2Path;
    private RetentionSettings _originalRetention = new();

    // Original values for tracking unsaved changes
    private AutoQacSettings _originalSettings = new();
    private string? _originalXEditPath;

    /// <summary>Design-time constructor (parameterless for XAML previewer).</summary>
    public SettingsViewModel() : this(new SynchronousFallbackDispatcher())
    {
    }

    private SettingsViewModel(IUiDispatcher uiDispatcher)
    {
        ArgumentNullException.ThrowIfNull(uiDispatcher);

        _configService = null;
        _logger = null;
        _fileDialog = null;

        _xEditValidate = new DebouncedAction(uiDispatcher, TimeSpan.FromMilliseconds(400));
        _mo2Validate = new DebouncedAction(uiDispatcher, TimeSpan.FromMilliseconds(400));
        _loadOrderValidate = new DebouncedAction(uiDispatcher, TimeSpan.FromMilliseconds(400));

        ResetToDefaults();
    }

    /// <summary>Creates an editor whose saves cross the shared settings seam; notifications are marshaled to its UI dispatcher.</summary>
    public SettingsViewModel(
        IConfigurationService configService,
        ILoggingService logger,
        IUiDispatcher uiDispatcher,
        IFileDialogService? fileDialog = null,
        IDiscoverySettingsModule? discoverySettingsModule = null,
        DiscoverySettingsAdmission? admission = null)
    {
        ArgumentNullException.ThrowIfNull(configService);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(uiDispatcher);

        _configService = configService;
        _logger = logger;
        _fileDialog = fileDialog;
        _discoverySettingsModule = discoverySettingsModule;
        _admission = admission;
        _dispatcher = uiDispatcher;
        if (admission is not null)
        {
            admission.CleaningChanged += OnCleaningAdmissionChanged;
            IsCleaning = admission.IsCleaning;
        }

        _xEditValidate = new DebouncedAction(uiDispatcher, TimeSpan.FromMilliseconds(400));
        _mo2Validate = new DebouncedAction(uiDispatcher, TimeSpan.FromMilliseconds(400));
        _loadOrderValidate = new DebouncedAction(uiDispatcher, TimeSpan.FromMilliseconds(400));

        // Phase 10 D-24/D-27: typed failure stream feeds a concise banner; clears on next success.
        // Per AGENTS.md, ViewModels must not import System.Reactive — use CallbackObserver + IUiDispatcher.
        _failuresSubscription = _configService.Failures.Subscribe(
            new CallbackObserver<ConfigPersistenceFailure>(failure =>
                uiDispatcher.Post(() => OnPersistenceFailureReceived(failure))));
        _resultsSubscription = _configService.PersistenceResults.Subscribe(
            new CallbackObserver<ConfigPersistenceResult>(result =>
                uiDispatcher.Post(() => OnPersistenceResultReceived(result))));
        _configChangedClearSubscription = _configService.UserConfigurationChanged.Subscribe(
            new CallbackObserver<UserConfiguration>(_ =>
                uiDispatcher.Post(ClearPersistenceBanner)));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    [NotifyPropertyChangedFor(nameof(HasValidationErrors))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial int JournalExpiration { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    [NotifyPropertyChangedFor(nameof(HasValidationErrors))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial int CleaningTimeout { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    [NotifyPropertyChangedFor(nameof(HasValidationErrors))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial int CpuThreshold { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    public partial bool Mo2Mode { get; set; }

    [ObservableProperty] public partial string? CleaningTimeoutError { get; set; }

    [ObservableProperty] public partial string? JournalExpirationError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    public partial string? XEditPath { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    public partial string? Mo2Path { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    public partial string? LoadOrderPath { get; set; }

    [ObservableProperty] public partial bool? IsXEditPathValid { get; set; }

    [ObservableProperty] public partial bool? IsMo2PathValid { get; set; }

    [ObservableProperty] public partial bool? IsLoadOrderPathValid { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    public partial int RetentionMode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    [NotifyPropertyChangedFor(nameof(HasValidationErrors))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial int MaxAgeDays { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    [NotifyPropertyChangedFor(nameof(HasValidationErrors))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial int MaxFileCount { get; set; }

    [ObservableProperty] public partial bool IsAgeBasedMode { get; set; } = true;

    [ObservableProperty] public partial bool IsCountBasedMode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    public partial bool BackupEnabled { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    [NotifyPropertyChangedFor(nameof(HasValidationErrors))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial int BackupMaxSessions { get; set; } = 10;

    [ObservableProperty] public partial string? BackupMaxSessionsError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPersistenceBanner))]
    public partial string? PersistenceBannerText { get; set; }

    public bool HasPersistenceBanner => !string.IsNullOrWhiteSpace(PersistenceBannerText);

    public bool HasValidationErrors =>
        !ValidateCleaningTimeout(CleaningTimeout)
        || !ValidateJournalExpiration(JournalExpiration)
        || CpuThreshold is < 1 or > 100
        || !(MaxAgeDays >= 1 && MaxFileCount >= 1)
        || BackupMaxSessions is < 1 or > 100;

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

    /// <summary>Withdraws pending acceptance and releases subscriptions without rolling back any persisted edit.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        if (_admission is not null) _admission.CleaningChanged -= OnCleaningAdmissionChanged;
        _failuresSubscription?.Dispose();
        _resultsSubscription?.Dispose();
        _configChangedClearSubscription?.Dispose();
        _xEditValidate.Dispose();
        _mo2Validate.Dispose();
        _loadOrderValidate.Dispose();
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyPropertyChangedFor(nameof(CanEditSettings))]
    public partial bool IsCleaning { get; set; }

    /// <summary>Whether the editor may change settings while the Cleaning session owns admission.</summary>
    public bool CanEditSettings => !IsCleaning;

    /// <summary>Marshals startup and finalization transitions to the editor's UI thread.</summary>
    private void OnCleaningAdmissionChanged(object? sender, EventArgs args)
    {
        _dispatcher?.Post(() => { if (!_disposed) IsCleaning = _admission?.IsCleaning == true; });
    }

    /// <summary>Raised when the user picks Save or Cancel. The view closes the dialog with this value.</summary>
    public event Action<bool>? CloseRequested;

    /// <summary>
    ///     Maps safe configuration persistence failures to a text-only Settings banner.
    ///     Watcher echo skips are filtered before the failure stream, so every value here is actionable.
    /// </summary>
    /// <param name="failure">The typed, safe failure payload from the configuration service.</param>
    private void OnPersistenceFailureReceived(ConfigPersistenceFailure failure)
    {
        // D-25: Watcher silent skips never reach Failures; only actionable failures arrive here.
        // D-29: text-only banner, no modal dialog.
        PersistenceBannerText = MapFailureToBanner(failure);
    }

    /// <summary>
    ///     Clears stale persistence failure text when the persistence coordinator reports a successful barrier.
    /// </summary>
    /// <param name="result">The typed persistence result emitted by the configuration service.</param>
    private void OnPersistenceResultReceived(ConfigPersistenceResult result)
    {
        if (result.Status is ConfigPersistenceStatusKind.Success or ConfigPersistenceStatusKind.NoOp)
            ClearPersistenceBanner();
    }

    /// <summary>
    ///     Converts safe failure categories into concise user-facing Settings banner text.
    /// </summary>
    /// <param name="failure">The typed safe failure payload to present.</param>
    /// <returns>A text-only banner message shorter than the Settings dialog copy budget.</returns>
    private static string MapFailureToBanner(ConfigPersistenceFailure failure)
    {
        return failure switch
        {
            // D-30: failed optimistic save explicitly tells user settings were restored.
            { Operation: ConfigPersistenceOperationKind.Save, Kind: ConfigPersistenceFailureKind.WriteFailed }
                => failure.SafeSummary,
            { Operation: ConfigPersistenceOperationKind.Flush, Kind: ConfigPersistenceFailureKind.WriteFailed }
                => "Could not save settings before cleaning. Cleaning was blocked. Settings were restored to last saved values.",
            { Operation: ConfigPersistenceOperationKind.Flush }
                => "Could not save settings (flush). Cleaning was blocked.",
            { Kind: ConfigPersistenceFailureKind.InvalidExternalYaml }
                => "External settings file has invalid YAML. Active settings were not changed.",
            { Kind: ConfigPersistenceFailureKind.MissingFile }
                => "Settings file is missing or unreadable. Active settings were not changed.",
            { Kind: ConfigPersistenceFailureKind.ReadFailed }
                => failure.SafeSummary,
            _ => $"Could not persist settings: {failure.SafeSummary}"
        };
    }

    /// <summary>
    ///     Clears the visible persistence banner if one is currently displayed.
    /// </summary>
    private void ClearPersistenceBanner()
    {
        if (PersistenceBannerText != null) PersistenceBannerText = null;
    }

    partial void OnCleaningTimeoutChanged(int value)
    {
        CleaningTimeoutError = ValidateCleaningTimeout(value)
            ? null
            : "Timeout must be between 30 and 3600 seconds";
    }

    partial void OnJournalExpirationChanged(int value)
    {
        JournalExpirationError = ValidateJournalExpiration(value)
            ? null
            : "Expiration must be between 1 and 365 days";
    }

    partial void OnBackupMaxSessionsChanged(int value)
    {
        BackupMaxSessionsError = value is >= 1 and <= 100
            ? null
            : "Sessions to keep must be between 1 and 100";
    }

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
        _mo2Validate.Trigger(() =>
            IsMo2PathValid = string.IsNullOrWhiteSpace(value) ? null : ValidateExecutablePath(value));
    }

    partial void OnLoadOrderPathChanged(string? value)
    {
        if (_isLoading) return;
        _loadOrderValidate.Trigger(() =>
            IsLoadOrderPathValid = string.IsNullOrWhiteSpace(value) ? null : ValidateFilePath(value));
    }

    public async Task LoadSettingsAsync()
    {
        var configService = _configService;

        try
        {
            _isLoading = true;
            _loadSucceeded = false;

            if (configService is null)
            {
                ResetToDefaults();
                return;
            }

            var config = await configService.LoadUserConfigAsync();
            _baseline = config.Copy();
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

            _loadSucceeded = true;
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to load settings");
            _originalSettings = new AutoQacSettings();
            _originalRetention = new RetentionSettings();
            _originalBackupSettings = new BackupSettings();
            _originalXEditPath = null;
            _originalMo2Path = null;
            _originalLoadOrderPath = null;
            ResetToDefaults();
            XEditPath = null;
            Mo2Path = null;
            LoadOrderPath = null;
            IsXEditPathValid = null;
            IsMo2PathValid = null;
            IsLoadOrderPathValid = null;
            PersistenceBannerText = "Could not load settings. Save is disabled until settings are loaded successfully.";
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

    private bool CanSave()
    {
        return _discoverySettingsModule is not null && _loadSucceeded && !HasValidationErrors && !IsCleaning;
    }

    /// <summary>Submits editor changes against their loaded baseline and closes only after durable acceptance.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (_discoverySettingsModule is null || _baseline is null || !CanSave() || _disposed) return;

        try
        {
            var config = _baseline.Copy();

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

            var result = await _discoverySettingsModule.ExecuteAsync(
                new DiscoverySettingsIntent.ApplySettings(_baseline, config), _lifetime.Token);
            if (_disposed || _lifetime.IsCancellationRequested) return;
            if (result.Status != DiscoverySettingsChangeStatus.Accepted)
            {
                if (result.Status == DiscoverySettingsChangeStatus.SaveFailed)
                    PersistenceBannerText = SaveFailureBanner;
                else if (result.Failure is not null)
                    PersistenceBannerText = result.Failure.SafeMessage;
                return;
            }

            _logger?.Information("Settings saved successfully");

            // D-27: explicit Settings Save only closes after the module proves disk persistence and
            // matching publication. Otherwise a later failure could surface after the editor closed.
            ClearPersistenceBanner();

            CloseRequested?.Invoke(true);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to save settings");
            // Synchronous save failures may occur before the coordinator publishes Failures.
            if (string.IsNullOrEmpty(PersistenceBannerText)) PersistenceBannerText = SaveFailureBanner;
        }
    }

    /// <summary>Withdraws any pending acceptance and closes the editor without rolling back saved choices.</summary>
    [RelayCommand]
    private void Cancel()
    {
        _lifetime.Cancel();
        CloseRequested?.Invoke(false);
    }

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
    {
        return !string.IsNullOrWhiteSpace(path) &&
               path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
               File.Exists(path);
    }

    internal static bool ValidateFilePath(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    }

    private static bool ValidateCleaningTimeout(int value)
    {
        return value is >= 30 and <= 3600;
    }

    private static bool ValidateJournalExpiration(int value)
    {
        return value is >= 1 and <= 365;
    }

    /// <summary>
    ///     Cancellable, debounced action. Replaces ReactiveUI's
    ///     <c>WhenAnyValue(...).Throttle(...).ObserveOn(MainThreadScheduler)</c> for
    ///     path-validation pipelines without pulling in System.Reactive.
    /// </summary>
    private sealed class DebouncedAction(IUiDispatcher dispatcher, TimeSpan delay) : IDisposable
    {
        private CancellationTokenSource? _cts;

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
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
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                    dispatcher.Post(action);
                }
                catch (OperationCanceledException)
                {
                    // Latest trigger superseded this one — drop silently.
                }
            }, ct);
        }
    }

    /// <summary>
    ///     Used only by the design-time constructor when no real <see cref="IUiDispatcher" /> is
    ///     available; runs callbacks inline. Production code resolves <see cref="IUiDispatcher" />
    ///     from DI.
    /// </summary>
    private sealed class SynchronousFallbackDispatcher : IUiDispatcher
    {
        public void Post(Action action)
        {
            action();
        }

        public Task InvokeAsync(Func<Task> action)
        {
            return action();
        }
    }
}
