using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using Avalonia.Controls.ApplicationLifetimes;
using ReactiveUI;

namespace AutoQAC.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly IConfigurationService _configService;
    private readonly IStateService _stateService;
    private readonly ICleaningOrchestrator _orchestrator;
    private readonly ILoggingService _logger;
    private readonly IFileDialogService _fileDialog;
    private readonly IPluginValidationService _pluginService;
    private readonly IPluginLoadingService _pluginLoadingService;
    private readonly CompositeDisposable _disposables = new();

    // Observable properties
    private string? _loadOrderPath;
    public string? LoadOrderPath
    {
        get => _loadOrderPath;
        set => this.RaiseAndSetIfChanged(ref _loadOrderPath, value);
    }

    private string? _xEditPath;
    public string? XEditPath
    {
        get => _xEditPath;
        set => this.RaiseAndSetIfChanged(ref _xEditPath, value);
    }

    private string? _mo2Path;
    public string? MO2Path
    {
        get => _mo2Path;
        set => this.RaiseAndSetIfChanged(ref _mo2Path, value);
    }

    private bool _mo2ModeEnabled;
    public bool MO2ModeEnabled
    {
        get => _mo2ModeEnabled;
        set => this.RaiseAndSetIfChanged(ref _mo2ModeEnabled, value);
    }

    private bool _partialFormsEnabled;
    public bool PartialFormsEnabled
    {
        get => _partialFormsEnabled;
        set => this.RaiseAndSetIfChanged(ref _partialFormsEnabled, value);
    }

    private GameType _selectedGame = GameType.Unknown;
    public GameType SelectedGame
    {
        get => _selectedGame;
        set => this.RaiseAndSetIfChanged(ref _selectedGame, value);
    }

    public IReadOnlyList<GameType> AvailableGames { get; }

    private readonly ObservableAsPropertyHelper<bool> _isMutagenSupported;
    public bool IsMutagenSupported => _isMutagenSupported.Value;

    private string _statusText = "Ready";
    public string StatusText
    {
        get => _statusText;
        set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    private ObservableCollection<PluginInfo> _pluginsToClean = new();
    public ObservableCollection<PluginInfo> PluginsToClean
    {
        get => _pluginsToClean;
        set => this.RaiseAndSetIfChanged(ref _pluginsToClean, value);
    }

    private PluginInfo? _selectedPlugin;
    public PluginInfo? SelectedPlugin
    {
        get => _selectedPlugin;
        set => this.RaiseAndSetIfChanged(ref _selectedPlugin, value);
    }

    // Computed properties
    private readonly ObservableAsPropertyHelper<bool> _canStartCleaning;
    public bool CanStartCleaning => _canStartCleaning.Value;

    private readonly ObservableAsPropertyHelper<bool> _isCleaning;
    public bool IsCleaning => _isCleaning.Value;

    // Commands
    public ReactiveCommand<Unit, Unit> ConfigureLoadOrderCommand { get; }
    public ReactiveCommand<Unit, Unit> ConfigureXEditCommand { get; }
    public ReactiveCommand<Unit, Unit> ConfigureMO2Command { get; }
    public ReactiveCommand<Unit, Unit> TogglePartialFormsCommand { get; }
    public ReactiveCommand<Unit, Unit> StartCleaningCommand { get; }
    public ReactiveCommand<Unit, Unit> StopCleaningCommand { get; }
    public ReactiveCommand<Unit, Unit> ExitCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowAboutCommand { get; }
    public ReactiveCommand<Unit, Unit> ResetSettingsCommand { get; }

    public MainWindowViewModel(
        IConfigurationService configService,
        IStateService stateService,
        ICleaningOrchestrator orchestrator,
        ILoggingService logger,
        IFileDialogService fileDialog,
        IPluginValidationService pluginService,
        IPluginLoadingService pluginLoadingService)
    {
        _configService = configService;
        _stateService = stateService;
        _orchestrator = orchestrator;
        _logger = logger;
        _fileDialog = fileDialog;
        _pluginService = pluginService;
        _pluginLoadingService = pluginLoadingService;

        // Initialize available games list
        AvailableGames = _pluginLoadingService.GetAvailableGames();

        // Initialize OAPHs first
        _isCleaning = _stateService.StateChanged
            .Select(s => s.IsCleaning)
            .ToProperty(this, x => x.IsCleaning);
        _disposables.Add(_isCleaning);

        // IsMutagenSupported computed from SelectedGame
        _isMutagenSupported = this.WhenAnyValue(x => x.SelectedGame)
            .Select(g => _pluginLoadingService.IsGameSupportedByMutagen(g))
            .ToProperty(this, x => x.IsMutagenSupported);
        _disposables.Add(_isMutagenSupported);

        // Initialize commands
        ConfigureLoadOrderCommand = ReactiveCommand.CreateFromTask(ConfigureLoadOrderAsync);
        ConfigureXEditCommand = ReactiveCommand.CreateFromTask(ConfigureXEditAsync);
        ConfigureMO2Command = ReactiveCommand.CreateFromTask(ConfigureMO2Async);

        TogglePartialFormsCommand = ReactiveCommand.Create(TogglePartialForms);

        // Define canStart observable - requires xEdit and plugins (from game detection OR load order file)
        var hasPlugins = _stateService.StateChanged
            .Select(s => s.PluginsToClean?.Count > 0);

        var canStart = Observable.CombineLatest(
            hasPlugins,
            this.WhenAnyValue(x => x.XEditPath),
            this.WhenAnyValue(x => x.IsCleaning),
            (hasP, xEdit, isCleaning) =>
                hasP &&
                !string.IsNullOrEmpty(xEdit) &&
                !isCleaning);

        _canStartCleaning = canStart
            .ToProperty(this, x => x.CanStartCleaning);
        _disposables.Add(_canStartCleaning);

        StartCleaningCommand = ReactiveCommand.CreateFromTask(
            StartCleaningAsync,
            canStart);

        StopCleaningCommand = ReactiveCommand.Create(
            StopCleaning,
            this.WhenAnyValue(x => x.IsCleaning));

        ExitCommand = ReactiveCommand.Create(Exit);
        ShowAboutCommand = ReactiveCommand.Create(ShowAbout);
        ResetSettingsCommand = ReactiveCommand.CreateFromTask(ResetSettingsAsync);

        // Subscribe to state changes
        var stateSubscription = _stateService.StateChanged
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(OnStateChanged);
        _disposables.Add(stateSubscription);

        // Initialize UI from current state
        OnStateChanged(_stateService.CurrentState);

        // Load saved configuration asynchronously with error handling
        _ = InitializeAsync();

        // Auto-save MO2Mode when changed
        var mo2ModeSubscription = this.WhenAnyValue(x => x.MO2ModeEnabled)
            .Skip(1) // Skip initial load
            .Subscribe(async _ =>
            {
                try
                {
                    await SaveConfigurationAsync();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to auto-save MO2Mode configuration");
                }
            });
        _disposables.Add(mo2ModeSubscription);

        // Auto-save and refresh plugins when SelectedGame changes
        var gameSelectionSubscription = this.WhenAnyValue(x => x.SelectedGame)
            .Skip(1) // Skip initial load
            .Subscribe(async gameType =>
            {
                try
                {
                    await _configService.SetSelectedGameAsync(gameType);
                    await RefreshPluginsForGameAsync(gameType);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to handle game selection change");
                    StatusText = "Error changing game selection";
                }
            });
        _disposables.Add(gameSelectionSubscription);
    }

    private async Task InitializeAsync()
    {
        try
        {
            var config = await _configService.LoadUserConfigAsync();

            _stateService.UpdateConfigurationPaths(
                config.LoadOrder.File,
                config.ModOrganizer.Binary,
                config.XEdit.Binary);

            _stateService.UpdateState(s => s with
            {
                MO2ModeEnabled = config.Settings.MO2Mode,
                CleaningTimeout = config.Settings.CleaningTimeout,
                MaxConcurrentSubprocesses = config.Settings.MaxConcurrentSubprocesses
            });

            // Load saved game selection
            var savedGame = await _configService.GetSelectedGameAsync();
            SelectedGame = savedGame; // This will trigger the subscription which loads plugins

            // If no game was saved but we have a load order file, try to detect game and load plugins
            if (savedGame == GameType.Unknown &&
                !string.IsNullOrEmpty(config.LoadOrder.File) &&
                System.IO.File.Exists(config.LoadOrder.File))
            {
                try
                {
                    var plugins = await _pluginService.GetPluginsFromLoadOrderAsync(config.LoadOrder.File);
                    var pluginNames = plugins.Select(p => p.FileName).ToList();
                    _stateService.SetPluginsToClean(pluginNames);
                    StatusText = "Configuration loaded";
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to parse saved load order on startup");
                    StatusText = "Configuration loaded (Load Order parse error)";
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to initialize configuration");
            StatusText = "Failed to load configuration";
        }
    }

    private async Task ConfigureLoadOrderAsync()
    {
        var path = await _fileDialog.OpenFileDialogAsync(
            "Select Load Order File",
            "Text Files (*.txt)|*.txt|All Files (*.*)|*.*");
            
        if (!string.IsNullOrEmpty(path))
        {
            _stateService.UpdateConfigurationPaths(path, MO2Path, XEditPath);
            
            // Parse plugins from load order and update state
            try 
            {
                var plugins = await _pluginService.GetPluginsFromLoadOrderAsync(path);
                var pluginNames = plugins.Select(p => p.FileName).ToList();
                _stateService.SetPluginsToClean(pluginNames);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to parse selected load order");
                StatusText = "Error parsing load order file";
            }
            
            await SaveConfigurationAsync();
        }
    }

    private async Task ConfigureXEditAsync()
    {
        var path = await _fileDialog.OpenFileDialogAsync(
            "Select xEdit Executable",
            "Executables (*.exe)|*.exe|All Files (*.*)|*.*");

        if (!string.IsNullOrEmpty(path))
        {
            _stateService.UpdateConfigurationPaths(LoadOrderPath, MO2Path, path);
            await SaveConfigurationAsync();
        }
    }

    private async Task ConfigureMO2Async()
    {
        var path = await _fileDialog.OpenFileDialogAsync(
            "Select Mod Organizer 2 Executable",
            "Executables (*.exe)|*.exe|All Files (*.*)|*.*");

        if (!string.IsNullOrEmpty(path))
        {
            _stateService.UpdateConfigurationPaths(LoadOrderPath, path, XEditPath);
            await SaveConfigurationAsync();
        }
    }

    private async Task SaveConfigurationAsync()
    {
        // Update UserConfiguration from ViewModel/State
        var config = await _configService.LoadUserConfigAsync();
        config.LoadOrder.File = LoadOrderPath;
        config.XEdit.Binary = XEditPath;
        config.ModOrganizer.Binary = MO2Path;
        config.Settings.MO2Mode = MO2ModeEnabled;
        
        // Partial forms setting is read-only from Main config usually, 
        // but user config might override if allowed.
        // For now we update state, saving might be complex if models mismatch.
        // We just save what we have.
        await _configService.SaveUserConfigAsync(config);
    }

    private async Task RefreshPluginsForGameAsync(GameType gameType)
    {
        if (gameType == GameType.Unknown)
        {
            _stateService.SetPluginsToClean(new List<string>());
            StatusText = "No game selected";
            return;
        }

        _stateService.UpdateState(s => s with { CurrentGameType = gameType });

        // Try Mutagen first if supported
        if (_pluginLoadingService.IsGameSupportedByMutagen(gameType))
        {
            try
            {
                StatusText = $"Loading plugins via Mutagen for {gameType}...";
                var plugins = await _pluginLoadingService.GetPluginsAsync(gameType);

                if (plugins.Count > 0)
                {
                    var pluginNames = plugins.Select(p => p.FileName).ToList();
                    _stateService.SetPluginsToClean(pluginNames);
                    StatusText = $"Loaded {plugins.Count} plugins for {gameType}";
                    return;
                }

                // Mutagen returned empty, might need file-based fallback
                _logger.Information($"Mutagen returned no plugins for {gameType}, fallback to file-based");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Mutagen failed for {gameType}: {ex.Message}");
            }
        }

        // Fall back to file-based loading if we have a load order path
        if (!string.IsNullOrEmpty(LoadOrderPath) && System.IO.File.Exists(LoadOrderPath))
        {
            try
            {
                StatusText = $"Loading plugins from file for {gameType}...";
                var plugins = await _pluginLoadingService.GetPluginsFromFileAsync(LoadOrderPath);
                var pluginNames = plugins.Select(p => p.FileName).ToList();
                _stateService.SetPluginsToClean(pluginNames);
                StatusText = $"Loaded {plugins.Count} plugins from file";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load plugins from file");
                StatusText = "Error loading plugins";
            }
        }
        else
        {
            // No Mutagen support and no file path - need user to configure
            StatusText = _pluginLoadingService.IsGameSupportedByMutagen(gameType)
                ? $"Could not detect {gameType} installation"
                : $"{gameType} requires a load order file";
        }
    }

    private void TogglePartialForms()
    {
        // Logic to toggle partial forms
        // Show warning dialog if enabling? (Phase 6)
        // For now just toggle
        // PartialFormsEnabled is bound to CheckBox, so it updates automatically.
        // We might want to intercept the change but for now let it be.
    }

    private async Task StartCleaningAsync()
    {
        try
        {
            StatusText = "Cleaning started...";
            await _orchestrator.StartCleaningAsync();
            StatusText = "Cleaning completed.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            _logger.Error(ex, "StartCleaningAsync failed");
        }
    }

    private void StopCleaning()
    {
        _orchestrator.StopCleaning();
        StatusText = "Stopping...";
    }

    private void Exit()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private void ShowAbout()
    {
        // TODO: Show about dialog
        StatusText = "AutoQAC Sharp v1.0";
    }

    private async Task ResetSettingsAsync()
    {
        try
        {
            StatusText = "Resetting settings to defaults...";
            await _configService.ResetToDefaultsAsync();

            // Reload from the freshly saved defaults
            var config = await _configService.LoadUserConfigAsync();

            _stateService.UpdateConfigurationPaths(
                config.LoadOrder.File,
                config.ModOrganizer.Binary,
                config.XEdit.Binary);

            _stateService.UpdateState(s => s with
            {
                MO2ModeEnabled = config.Settings.MO2Mode,
                CleaningTimeout = config.Settings.CleaningTimeout,
                MaxConcurrentSubprocesses = config.Settings.MaxConcurrentSubprocesses,
                PartialFormsEnabled = false
            });

            SelectedGame = GameType.Unknown;
            _stateService.SetPluginsToClean(new List<string>());

            StatusText = "Settings reset to defaults";
            _logger.Information("Settings reset to defaults by user");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to reset settings");
            StatusText = "Error resetting settings";
        }
    }

    private void OnStateChanged(AppState state)
    {
        LoadOrderPath = state.LoadOrderPath;
        XEditPath = state.XEditExecutablePath;
        MO2Path = state.MO2ExecutablePath;
        MO2ModeEnabled = state.MO2ModeEnabled;
        PartialFormsEnabled = state.PartialFormsEnabled;
        
        // Plugins list update - optimized to avoid recreation if possible
        var statePlugins = state.PluginsToClean ?? new List<string>();
        if (PluginsToClean.Count != statePlugins.Count ||
            !PluginsToClean.Select(p => p.FileName).SequenceEqual(statePlugins))
        {
            PluginsToClean.Clear();
            foreach (var p in statePlugins)
            {
                PluginsToClean.Add(new PluginInfo
                {
                    FileName = p,
                    FullPath = p,
                    DetectedGameType = state.CurrentGameType,
                    IsInSkipList = false
                });
            }
        }
        
        if (state.IsCleaning)
        {
            StatusText = $"Cleaning: {state.CurrentPlugin} ({state.Progress}/{state.TotalPlugins})";
        }
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
