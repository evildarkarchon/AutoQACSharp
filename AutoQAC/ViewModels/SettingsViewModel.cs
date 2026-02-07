using System;
using System.IO;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.UI;
using ReactiveUI;

namespace AutoQAC.ViewModels;

public sealed class SettingsViewModel : ViewModelBase, IDisposable
{
    private readonly IConfigurationService _configService;
    private readonly ILoggingService _logger;
    private readonly IFileDialogService? _fileDialog;
    private readonly CompositeDisposable _disposables = new();

    // Loading flag to suppress validation during initial property population
    private bool _isLoading;

    // Original values for tracking unsaved changes
    private AutoQacSettings _originalSettings = new();
    private RetentionSettings _originalRetention = new();
    private BackupSettings _originalBackupSettings = new();
    private string? _originalXEditPath;
    private string? _originalMo2Path;
    private string? _originalLoadOrderPath;
    private string? _originalDataFolderPath;

    #region Existing Settings Properties

    private int _journalExpiration;
    public int JournalExpiration
    {
        get => _journalExpiration;
        set => this.RaiseAndSetIfChanged(ref _journalExpiration, value);
    }

    private int _cleaningTimeout;
    public int CleaningTimeout
    {
        get => _cleaningTimeout;
        set => this.RaiseAndSetIfChanged(ref _cleaningTimeout, value);
    }

    private int _cpuThreshold;
    public int CpuThreshold
    {
        get => _cpuThreshold;
        set => this.RaiseAndSetIfChanged(ref _cpuThreshold, value);
    }

    private bool _mo2Mode;
    public bool Mo2Mode
    {
        get => _mo2Mode;
        set => this.RaiseAndSetIfChanged(ref _mo2Mode, value);
    }

    // Validation error messages
    private string? _cleaningTimeoutError;
    public string? CleaningTimeoutError
    {
        get => _cleaningTimeoutError;
        set => this.RaiseAndSetIfChanged(ref _cleaningTimeoutError, value);
    }

    private string? _journalExpirationError;
    public string? JournalExpirationError
    {
        get => _journalExpirationError;
        set => this.RaiseAndSetIfChanged(ref _journalExpirationError, value);
    }

    #endregion

    #region Path Properties

    private string? _xEditPath;
    public string? XEditPath
    {
        get => _xEditPath;
        set => this.RaiseAndSetIfChanged(ref _xEditPath, value);
    }

    private string? _mo2Path;
    public string? Mo2Path
    {
        get => _mo2Path;
        set => this.RaiseAndSetIfChanged(ref _mo2Path, value);
    }

    private string? _loadOrderPath;
    public string? LoadOrderPath
    {
        get => _loadOrderPath;
        set => this.RaiseAndSetIfChanged(ref _loadOrderPath, value);
    }

    private string? _dataFolderPath;
    public string? DataFolderPath
    {
        get => _dataFolderPath;
        set => this.RaiseAndSetIfChanged(ref _dataFolderPath, value);
    }

    #endregion

    #region Path Validation State (null = untouched, true = valid, false = invalid)

    private bool? _isXEditPathValid;
    public bool? IsXEditPathValid
    {
        get => _isXEditPathValid;
        set => this.RaiseAndSetIfChanged(ref _isXEditPathValid, value);
    }

    private bool? _isMo2PathValid;
    public bool? IsMo2PathValid
    {
        get => _isMo2PathValid;
        set => this.RaiseAndSetIfChanged(ref _isMo2PathValid, value);
    }

    private bool? _isLoadOrderPathValid;
    public bool? IsLoadOrderPathValid
    {
        get => _isLoadOrderPathValid;
        set => this.RaiseAndSetIfChanged(ref _isLoadOrderPathValid, value);
    }

    private bool? _isDataFolderPathValid;
    public bool? IsDataFolderPathValid
    {
        get => _isDataFolderPathValid;
        set => this.RaiseAndSetIfChanged(ref _isDataFolderPathValid, value);
    }

    #endregion

    #region Log Retention Properties

    private int _retentionMode;
    public int RetentionMode
    {
        get => _retentionMode;
        set => this.RaiseAndSetIfChanged(ref _retentionMode, value);
    }

    private int _maxAgeDays;
    public int MaxAgeDays
    {
        get => _maxAgeDays;
        set => this.RaiseAndSetIfChanged(ref _maxAgeDays, value);
    }

    private int _maxFileCount;
    public int MaxFileCount
    {
        get => _maxFileCount;
        set => this.RaiseAndSetIfChanged(ref _maxFileCount, value);
    }

    /// <summary>
    /// True when RetentionMode == 0 (age-based). Used for direct XAML IsVisible binding
    /// instead of IntEqualsConverter which may not resolve ConverterParameter correctly.
    /// </summary>
    private bool _isAgeBasedMode = true;
    public bool IsAgeBasedMode
    {
        get => _isAgeBasedMode;
        set => this.RaiseAndSetIfChanged(ref _isAgeBasedMode, value);
    }

    /// <summary>
    /// True when RetentionMode == 1 (count-based). Used for direct XAML IsVisible binding.
    /// </summary>
    private bool _isCountBasedMode;
    public bool IsCountBasedMode
    {
        get => _isCountBasedMode;
        set => this.RaiseAndSetIfChanged(ref _isCountBasedMode, value);
    }

    #endregion

    #region Backup Properties

    private bool _backupEnabled = true;
    public bool BackupEnabled
    {
        get => _backupEnabled;
        set => this.RaiseAndSetIfChanged(ref _backupEnabled, value);
    }

    private int _backupMaxSessions = 10;
    public int BackupMaxSessions
    {
        get => _backupMaxSessions;
        set => this.RaiseAndSetIfChanged(ref _backupMaxSessions, value);
    }

    private string? _backupMaxSessionsError;
    public string? BackupMaxSessionsError
    {
        get => _backupMaxSessionsError;
        set => this.RaiseAndSetIfChanged(ref _backupMaxSessionsError, value);
    }

    #endregion

    #region Computed Properties

    private readonly ObservableAsPropertyHelper<bool> _hasValidationErrors;
    public bool HasValidationErrors => _hasValidationErrors.Value;

    private readonly ObservableAsPropertyHelper<bool> _hasUnsavedChanges;
    public bool HasUnsavedChanges => _hasUnsavedChanges.Value;

    #endregion

    #region Commands

    public ReactiveCommand<Unit, bool> SaveCommand { get; }
    public ReactiveCommand<Unit, bool> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> ResetToDefaultsCommand { get; }

    // Browse commands for path fields
    public ReactiveCommand<Unit, Unit> BrowseXEditCommand { get; }
    public ReactiveCommand<Unit, Unit> BrowseMo2Command { get; }
    public ReactiveCommand<Unit, Unit> BrowseLoadOrderCommand { get; }
    public ReactiveCommand<Unit, Unit> BrowseDataFolderCommand { get; }

    #endregion

    /// <summary>
    /// Design-time constructor (parameterless for XAML previewer).
    /// </summary>
    public SettingsViewModel() : this(null!, null!, null) { }

    public SettingsViewModel(IConfigurationService configService, ILoggingService logger, IFileDialogService? fileDialog = null)
    {
        _configService = configService;
        _logger = logger;
        _fileDialog = fileDialog;

        // Existing numeric validation observables
        var cleaningTimeoutValid = this.WhenAnyValue(x => x.CleaningTimeout)
            .Select(ValidateCleaningTimeout);

        var journalExpirationValid = this.WhenAnyValue(x => x.JournalExpiration)
            .Select(ValidateJournalExpiration);

        var cpuThresholdValid = this.WhenAnyValue(x => x.CpuThreshold)
            .Select(v => v is >= 1 and <= 100);

        var retentionValid = this.WhenAnyValue(x => x.MaxAgeDays, x => x.MaxFileCount)
            .Select(t => t.Item1 >= 1 && t.Item2 >= 1);

        var backupMaxSessionsValid = this.WhenAnyValue(x => x.BackupMaxSessions)
            .Select(v => v is >= 1 and <= 100);

        // Aggregate validation
        var allValid = cleaningTimeoutValid.CombineLatest(journalExpirationValid,
            cpuThresholdValid,
            retentionValid,
            backupMaxSessionsValid,
            (ct, je, cpu, ret, bms) => ct && je && cpu && ret && bms);

        _hasValidationErrors = allValid
            .Select(valid => !valid)
            .ToProperty(this, x => x.HasValidationErrors);
        _disposables.Add(_hasValidationErrors);

        // Track unsaved changes (including paths, retention, and backup settings)
        var currentValues = Observable.CombineLatest(
            this.WhenAnyValue(x => x.JournalExpiration),
            this.WhenAnyValue(x => x.CleaningTimeout),
            this.WhenAnyValue(x => x.CpuThreshold),
            this.WhenAnyValue(x => x.Mo2Mode),
            this.WhenAnyValue(x => x.XEditPath),
            this.WhenAnyValue(x => x.Mo2Path),
            this.WhenAnyValue(x => x.LoadOrderPath),
            this.WhenAnyValue(x => x.DataFolderPath),
            this.WhenAnyValue(x => x.RetentionMode),
            this.WhenAnyValue(x => x.MaxAgeDays),
            this.WhenAnyValue(x => x.MaxFileCount),
            this.WhenAnyValue(x => x.BackupEnabled),
            this.WhenAnyValue(x => x.BackupMaxSessions),
            (je, ct, cpu, mo2, xedit, mo2p, lo, df, rm, mad, mfc, be, bms) =>
                new { je, ct, cpu, mo2, xedit, mo2p, lo, df, rm, mad, mfc, be, bms });

        _hasUnsavedChanges = currentValues
            .Select(v =>
                v.je != _originalSettings.JournalExpiration ||
                v.ct != _originalSettings.CleaningTimeout ||
                v.cpu != _originalSettings.CpuThreshold ||
                v.mo2 != _originalSettings.Mo2Mode ||
                v.xedit != _originalXEditPath ||
                v.mo2p != _originalMo2Path ||
                v.lo != _originalLoadOrderPath ||
                v.df != _originalDataFolderPath ||
                v.rm != (int)_originalRetention.Mode ||
                v.mad != _originalRetention.MaxAgeDays ||
                v.mfc != _originalRetention.MaxFileCount ||
                v.be != _originalBackupSettings.Enabled ||
                v.bms != _originalBackupSettings.MaxSessions)
            .ToProperty(this, x => x.HasUnsavedChanges);
        _disposables.Add(_hasUnsavedChanges);

        // Commands
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync, allValid);
        CancelCommand = ReactiveCommand.Create(() => false);
        ResetToDefaultsCommand = ReactiveCommand.Create(ResetToDefaults);

        // Browse commands
        BrowseXEditCommand = ReactiveCommand.CreateFromTask(BrowseXEditAsync);
        BrowseMo2Command = ReactiveCommand.CreateFromTask(BrowseMo2Async);
        BrowseLoadOrderCommand = ReactiveCommand.CreateFromTask(BrowseLoadOrderAsync);
        BrowseDataFolderCommand = ReactiveCommand.CreateFromTask(BrowseDataFolderAsync);

        // Update validation error messages reactively
        var timeoutErrorSubscription = this.WhenAnyValue(x => x.CleaningTimeout)
            .Subscribe(v => CleaningTimeoutError = ValidateCleaningTimeout(v)
                ? null
                : "Timeout must be between 30 and 3600 seconds");
        _disposables.Add(timeoutErrorSubscription);

        var expirationErrorSubscription = this.WhenAnyValue(x => x.JournalExpiration)
            .Subscribe(v => JournalExpirationError = ValidateJournalExpiration(v)
                ? null
                : "Expiration must be between 1 and 365 days");
        _disposables.Add(expirationErrorSubscription);

        var backupMaxSessionsErrorSubscription = this.WhenAnyValue(x => x.BackupMaxSessions)
            .Subscribe(v => BackupMaxSessionsError = v is >= 1 and <= 100
                ? null
                : "Sessions to keep must be between 1 and 100");
        _disposables.Add(backupMaxSessionsErrorSubscription);

        // Set up debounced path validation pipelines
        // Each pipeline skips the initial WhenAnyValue emission (before LoadSettingsAsync)
        // and also skips changes while _isLoading is true to avoid showing errors on initial load
        SetupPathValidation(
            this.WhenAnyValue(x => x.XEditPath),
            valid => IsXEditPathValid = valid,
            ValidateExecutablePath);

        SetupOptionalPathValidation(
            this.WhenAnyValue(x => x.Mo2Path),
            valid => IsMo2PathValid = valid,
            ValidateExecutablePath);

        SetupOptionalPathValidation(
            this.WhenAnyValue(x => x.LoadOrderPath),
            valid => IsLoadOrderPathValid = valid,
            ValidateFilePath);

        SetupOptionalPathValidation(
            this.WhenAnyValue(x => x.DataFolderPath),
            valid => IsDataFolderPathValid = valid,
            ValidateDirectoryPath);

        // Wire retention mode booleans reactively
        var retentionModeSubscription = this.WhenAnyValue(x => x.RetentionMode)
            .Subscribe(mode =>
            {
                IsAgeBasedMode = mode == 0;
                IsCountBasedMode = mode == 1;
            });
        _disposables.Add(retentionModeSubscription);
    }

    /// <summary>
    /// Sets up a debounced validation pipeline for a required path property.
    /// Skips the initial WhenAnyValue emission (before LoadSettingsAsync populates the field)
    /// and also skips changes while _isLoading is true. Throttles to 400ms, runs on main thread.
    /// </summary>
    private void SetupPathValidation(
        IObservable<string?> pathObservable,
        Action<bool?> setValidation,
        Func<string?, bool> validator)
    {
        var subscription = pathObservable
            .Skip(1) // Skip initial emission from constructor (before LoadSettingsAsync)
            .Where(_ => !_isLoading) // Suppress validation during LoadSettingsAsync
            .Throttle(TimeSpan.FromMilliseconds(400))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Select(path => (bool?)validator(path))
            .Subscribe(valid => setValidation(valid));
        _disposables.Add(subscription);
    }

    /// <summary>
    /// Sets up a debounced validation pipeline for an optional path property.
    /// Returns null (no indicator) when path is empty/whitespace,
    /// true (green checkmark) when non-empty and valid,
    /// false (red X) when non-empty and invalid.
    /// </summary>
    private void SetupOptionalPathValidation(
        IObservable<string?> pathObservable,
        Action<bool?> setValidation,
        Func<string?, bool> validator)
    {
        var subscription = pathObservable
            .Skip(1) // Skip initial emission from constructor (before LoadSettingsAsync)
            .Where(_ => !_isLoading) // Suppress validation during LoadSettingsAsync
            .Throttle(TimeSpan.FromMilliseconds(400))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Select(path => string.IsNullOrWhiteSpace(path) ? (bool?)null : validator(path))
            .Subscribe(valid => setValidation(valid));
        _disposables.Add(subscription);
    }

    /// <summary>
    /// Loads settings from configuration service.
    /// Called before showing the window.
    /// </summary>
    public async Task LoadSettingsAsync()
    {
        try
        {
            _isLoading = true;

            var config = await _configService.LoadUserConfigAsync();
            _originalSettings = config.Settings;
            _originalRetention = config.LogRetention;

            // Copy to working properties
            JournalExpiration = config.Settings.JournalExpiration;
            CleaningTimeout = config.Settings.CleaningTimeout;
            CpuThreshold = config.Settings.CpuThreshold;
            Mo2Mode = config.Settings.Mo2Mode;

            // Load path values
            XEditPath = config.XEdit.Binary;
            _originalXEditPath = config.XEdit.Binary;
            Mo2Path = config.ModOrganizer.Binary;
            _originalMo2Path = config.ModOrganizer.Binary;
            LoadOrderPath = config.LoadOrder.File;
            _originalLoadOrderPath = config.LoadOrder.File;
            // DataFolderPath is game-specific; load if a game is selected
            // For now, leave it as-is (loaded from game-specific override if needed)
            _originalDataFolderPath = DataFolderPath;

            // Load retention settings
            RetentionMode = (int)config.LogRetention.Mode;
            MaxAgeDays = config.LogRetention.MaxAgeDays;
            MaxFileCount = config.LogRetention.MaxFileCount;

            // Load backup settings
            _originalBackupSettings = config.Backup;
            BackupEnabled = config.Backup.Enabled;
            BackupMaxSessions = config.Backup.MaxSessions;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load settings");
            // Use defaults on error
            ResetToDefaults();
        }
        finally
        {
            _isLoading = false;
            ValidateLoadedPaths();
        }
    }

    /// <summary>
    /// Validates all loaded path values immediately so indicators show
    /// when the Settings window first opens (without waiting for user interaction).
    /// </summary>
    private void ValidateLoadedPaths()
    {
        // Required path — always show indicator if populated
        if (!string.IsNullOrWhiteSpace(XEditPath))
            IsXEditPathValid = ValidateExecutablePath(XEditPath);

        // Optional paths — only show indicator if non-empty
        if (!string.IsNullOrWhiteSpace(Mo2Path))
            IsMo2PathValid = ValidateExecutablePath(Mo2Path);

        if (!string.IsNullOrWhiteSpace(LoadOrderPath))
            IsLoadOrderPathValid = ValidateFilePath(LoadOrderPath);

        if (!string.IsNullOrWhiteSpace(DataFolderPath))
            IsDataFolderPathValid = ValidateDirectoryPath(DataFolderPath);
    }

    private async Task<bool> SaveAsync()
    {
        try
        {
            var config = await _configService.LoadUserConfigAsync();

            // Save existing settings
            config.Settings.JournalExpiration = JournalExpiration;
            config.Settings.CleaningTimeout = CleaningTimeout;
            config.Settings.CpuThreshold = CpuThreshold;
            config.Settings.Mo2Mode = Mo2Mode;

            // Save path values
            config.XEdit.Binary = XEditPath;
            config.ModOrganizer.Binary = Mo2Path;
            config.LoadOrder.File = LoadOrderPath;

            // Save retention settings
            config.LogRetention.Mode = (RetentionMode)RetentionMode;
            config.LogRetention.MaxAgeDays = MaxAgeDays;
            config.LogRetention.MaxFileCount = MaxFileCount;

            // Save backup settings
            config.Backup.Enabled = BackupEnabled;
            config.Backup.MaxSessions = BackupMaxSessions;

            await _configService.SaveUserConfigAsync(config);
            _logger.Information("Settings saved successfully");

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save settings");
            return false;
        }
    }

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

    #region Browse Commands

    private async Task BrowseXEditAsync()
    {
        if (_fileDialog == null) return;

        var path = await _fileDialog.OpenFileDialogAsync(
            "Select xEdit Executable",
            "Executables (*.exe)|*.exe|All Files (*.*)|*.*");

        if (!string.IsNullOrEmpty(path))
        {
            XEditPath = path;
            // Immediate validation (bypass debounce for deliberate user action)
            IsXEditPathValid = ValidateExecutablePath(path);
        }
    }

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

    private async Task BrowseDataFolderAsync()
    {
        if (_fileDialog == null) return;

        var path = await _fileDialog.OpenFolderDialogAsync(
            "Select Game Data Folder");

        if (!string.IsNullOrEmpty(path))
        {
            DataFolderPath = path;
            IsDataFolderPathValid = ValidateDirectoryPath(path);
        }
    }

    #endregion

    #region Path Validation Helpers

    /// <summary>
    /// Validates that the path points to an existing .exe file.
    /// Returns false if empty, null, or not an existing executable.
    /// </summary>
    internal static bool ValidateExecutablePath(string? path)
        => !string.IsNullOrWhiteSpace(path) &&
           path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
           File.Exists(path);

    /// <summary>
    /// Validates that the path points to an existing file.
    /// Returns false if empty, null, or file does not exist.
    /// </summary>
    internal static bool ValidateFilePath(string? path)
        => !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    /// <summary>
    /// Validates that the path points to an existing directory.
    /// Returns false if empty, null, or directory does not exist.
    /// </summary>
    internal static bool ValidateDirectoryPath(string? path)
        => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);

    /// <summary>
    /// For optional fields: empty is valid (returns true), non-empty applies the validator.
    /// </summary>
    internal static bool ValidateOptionalPath(string? path, Func<string?, bool> validator)
        => string.IsNullOrWhiteSpace(path) || validator(path);

    #endregion

    private static bool ValidateCleaningTimeout(int value) => value is >= 30 and <= 3600;
    private static bool ValidateJournalExpiration(int value) => value is >= 1 and <= 365;

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
