using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.Configuration;
using ReactiveUI;

namespace AutoQAC.ViewModels;

public sealed class SettingsViewModel : ViewModelBase, IDisposable
{
    private readonly IConfigurationService _configService;
    private readonly ILoggingService _logger;
    private readonly CompositeDisposable _disposables = new();

    // Original values for tracking unsaved changes
    private AutoQacSettings _originalSettings = new();

    #region Properties

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
    public bool MO2Mode
    {
        get => _mo2Mode;
        set => this.RaiseAndSetIfChanged(ref _mo2Mode, value);
    }

    private int _maxConcurrentSubprocesses;
    public int MaxConcurrentSubprocesses
    {
        get => _maxConcurrentSubprocesses;
        set => this.RaiseAndSetIfChanged(ref _maxConcurrentSubprocesses, value);
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

    // Computed properties
    private readonly ObservableAsPropertyHelper<bool> _hasValidationErrors;
    public bool HasValidationErrors => _hasValidationErrors.Value;

    private readonly ObservableAsPropertyHelper<bool> _hasUnsavedChanges;
    public bool HasUnsavedChanges => _hasUnsavedChanges.Value;

    #endregion

    #region Commands

    public ReactiveCommand<Unit, bool> SaveCommand { get; }
    public ReactiveCommand<Unit, bool> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> ResetToDefaultsCommand { get; }

    #endregion

    public SettingsViewModel(IConfigurationService configService, ILoggingService logger)
    {
        _configService = configService;
        _logger = logger;

        // Validation observables
        var cleaningTimeoutValid = this.WhenAnyValue(x => x.CleaningTimeout)
            .Select(ValidateCleaningTimeout);

        var journalExpirationValid = this.WhenAnyValue(x => x.JournalExpiration)
            .Select(ValidateJournalExpiration);

        var cpuThresholdValid = this.WhenAnyValue(x => x.CpuThreshold)
            .Select(v => v >= 1 && v <= 100);

        var maxSubprocessesValid = this.WhenAnyValue(x => x.MaxConcurrentSubprocesses)
            .Select(v => v >= 1 && v <= 10);

        // Aggregate validation
        var allValid = Observable.CombineLatest(
            cleaningTimeoutValid,
            journalExpirationValid,
            cpuThresholdValid,
            maxSubprocessesValid,
            (ct, je, cpu, max) => ct && je && cpu && max);

        _hasValidationErrors = allValid
            .Select(valid => !valid)
            .ToProperty(this, x => x.HasValidationErrors);
        _disposables.Add(_hasValidationErrors);

        // Track unsaved changes
        var currentValues = Observable.CombineLatest(
            this.WhenAnyValue(x => x.JournalExpiration),
            this.WhenAnyValue(x => x.CleaningTimeout),
            this.WhenAnyValue(x => x.CpuThreshold),
            this.WhenAnyValue(x => x.MO2Mode),
            this.WhenAnyValue(x => x.MaxConcurrentSubprocesses),
            (je, ct, cpu, mo2, max) => new { je, ct, cpu, mo2, max });

        _hasUnsavedChanges = currentValues
            .Select(v =>
                v.je != _originalSettings.JournalExpiration ||
                v.ct != _originalSettings.CleaningTimeout ||
                v.cpu != _originalSettings.CpuThreshold ||
                v.mo2 != _originalSettings.MO2Mode ||
                v.max != _originalSettings.MaxConcurrentSubprocesses)
            .ToProperty(this, x => x.HasUnsavedChanges);
        _disposables.Add(_hasUnsavedChanges);

        // Commands
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync, allValid);
        CancelCommand = ReactiveCommand.Create(() => false);
        ResetToDefaultsCommand = ReactiveCommand.Create(ResetToDefaults);

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
    }

    /// <summary>
    /// Loads settings from configuration service.
    /// Called before showing the window.
    /// </summary>
    public async Task LoadSettingsAsync()
    {
        try
        {
            var config = await _configService.LoadUserConfigAsync();
            _originalSettings = config.Settings;

            // Copy to working properties
            JournalExpiration = config.Settings.JournalExpiration;
            CleaningTimeout = config.Settings.CleaningTimeout;
            CpuThreshold = config.Settings.CpuThreshold;
            MO2Mode = config.Settings.MO2Mode;
            MaxConcurrentSubprocesses = config.Settings.MaxConcurrentSubprocesses;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load settings");
            // Use defaults on error
            ResetToDefaults();
        }
    }

    private async Task<bool> SaveAsync()
    {
        try
        {
            var config = await _configService.LoadUserConfigAsync();

            config.Settings.JournalExpiration = JournalExpiration;
            config.Settings.CleaningTimeout = CleaningTimeout;
            config.Settings.CpuThreshold = CpuThreshold;
            config.Settings.MO2Mode = MO2Mode;
            config.Settings.MaxConcurrentSubprocesses = MaxConcurrentSubprocesses;

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
        MO2Mode = defaults.MO2Mode;
        MaxConcurrentSubprocesses = defaults.MaxConcurrentSubprocesses;
    }

    private static bool ValidateCleaningTimeout(int value) => value >= 30 && value <= 3600;
    private static bool ValidateJournalExpiration(int value) => value >= 1 && value <= 365;

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
