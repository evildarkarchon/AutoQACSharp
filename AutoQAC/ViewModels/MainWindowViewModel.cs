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
    private readonly IMessageDialogService _messageDialog;
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
    public string? Mo2Path
    {
        get => _mo2Path;
        set => this.RaiseAndSetIfChanged(ref _mo2Path, value);
    }

    private bool _mo2ModeEnabled;
    public bool Mo2ModeEnabled
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
    public ReactiveCommand<Unit, Unit> ConfigureMo2Command { get; }
    public ReactiveCommand<Unit, Unit> TogglePartialFormsCommand { get; }
    public ReactiveCommand<Unit, Unit> StartCleaningCommand { get; }
    public ReactiveCommand<Unit, Unit> StopCleaningCommand { get; }
    public ReactiveCommand<Unit, Unit> ExitCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowAboutCommand { get; }
    public ReactiveCommand<Unit, Unit> ResetSettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowSettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowSkipListCommand { get; }

    /// <summary>
    /// Interaction for showing the progress window during cleaning.
    /// The window is shown non-modal and remains open until user closes it.
    /// </summary>
    public Interaction<Unit, Unit> ShowProgressInteraction { get; } = new();

    /// <summary>
    /// Interaction for showing the cleaning results window.
    /// </summary>
    public Interaction<CleaningSessionResult, Unit> ShowCleaningResultsInteraction { get; } = new();

    /// <summary>
    /// Interaction for showing the settings window.
    /// Returns true if settings were saved, false if cancelled.
    /// </summary>
    public Interaction<Unit, bool> ShowSettingsInteraction { get; } = new();

    /// <summary>
    /// Interaction for showing the skip list management window.
    /// Returns true if skip list was saved, false if cancelled.
    /// </summary>
    public Interaction<Unit, bool> ShowSkipListInteraction { get; } = new();

    public MainWindowViewModel(
        IConfigurationService configService,
        IStateService stateService,
        ICleaningOrchestrator orchestrator,
        ILoggingService logger,
        IFileDialogService fileDialog,
        IMessageDialogService messageDialog,
        IPluginValidationService pluginService,
        IPluginLoadingService pluginLoadingService)
    {
        _configService = configService;
        _stateService = stateService;
        _orchestrator = orchestrator;
        _logger = logger;
        _fileDialog = fileDialog;
        _messageDialog = messageDialog;
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
        ConfigureMo2Command = ReactiveCommand.CreateFromTask(ConfigureMo2Async);

        TogglePartialFormsCommand = ReactiveCommand.Create(TogglePartialForms);

        // Define canStart observable - requires xEdit and plugins (from game detection OR load order file)
        var hasPlugins = _stateService.StateChanged
            .Select(s => s.PluginsToClean.Count > 0);

        var canStart = hasPlugins.CombineLatest(this.WhenAnyValue(x => x.XEditPath),
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
        ShowSettingsCommand = ReactiveCommand.CreateFromTask(ShowSettingsAsync);
        
        // Skip list command - disabled during cleaning
        var canShowSkipList = this.WhenAnyValue(x => x.IsCleaning)
            .Select(cleaning => !cleaning);
        ShowSkipListCommand = ReactiveCommand.CreateFromTask(ShowSkipListAsync, canShowSkipList);

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
        var mo2ModeSubscription = this.WhenAnyValue(x => x.Mo2ModeEnabled)
            .Skip(1) // Skip initial load
            .SelectMany(_ => Observable.FromAsync(SaveConfigurationAsync))
            .Subscribe(
                _ => { },
                ex => _logger.Error(ex, "Failed to auto-save MO2Mode configuration"));
        _disposables.Add(mo2ModeSubscription);

        // Auto-save and refresh plugins when SelectedGame changes
        var gameSelectionSubscription = this.WhenAnyValue(x => x.SelectedGame)
            .Skip(1) // Skip initial load
            .SelectMany(gameType => Observable.FromAsync(async () =>
            {
                await _configService.SetSelectedGameAsync(gameType);
                await RefreshPluginsForGameAsync(gameType);
            }))
            .Subscribe(
                _ => { },
                ex =>
                {
                    _logger.Error(ex, "Failed to handle game selection change");
                    StatusText = "Error changing game selection";
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
                Mo2ModeEnabled = config.Settings.MO2Mode,
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
            // Validate file exists
            if (!System.IO.File.Exists(path))
            {
                await _messageDialog.ShowErrorAsync(
                    "File Not Found",
                    "The selected load order file does not exist.",
                    $"Path: {path}");
                return;
            }

            _stateService.UpdateConfigurationPaths(path, Mo2Path, XEditPath);

            // Parse plugins from load order and update state
            try
            {
                var plugins = await _pluginService.GetPluginsFromLoadOrderAsync(path);

                if (plugins.Count == 0)
                {
                    await _messageDialog.ShowWarningAsync(
                        "No Plugins Found",
                        "The load order file was parsed successfully but no plugins were found.",
                        $"File: {path}\n\nEnsure the file contains a valid list of plugin names (one per line).");
                }

                var pluginNames = plugins.Select(p => p.FileName).ToList();
                _stateService.SetPluginsToClean(pluginNames);
                StatusText = $"Loaded {plugins.Count} plugins from load order";
            }
            catch (System.IO.FileNotFoundException ex)
            {
                _logger.Error(ex, "Load order file not found");
                await _messageDialog.ShowErrorAsync(
                    "File Not Found",
                    "The load order file could not be found.",
                    $"Path: {path}\n\nError: {ex.Message}");
                StatusText = "Load order file not found";
                return;
            }
            catch (System.IO.IOException ex)
            {
                _logger.Error(ex, "Failed to read load order file");
                await _messageDialog.ShowErrorAsync(
                    "Read Error",
                    "Failed to read the load order file. The file may be in use by another application.",
                    $"Path: {path}\n\nError: {ex.Message}");
                StatusText = "Error reading load order file";
                return;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to parse selected load order");
                await _messageDialog.ShowErrorAsync(
                    "Invalid Load Order",
                    "Failed to parse the load order file. The file format may be invalid.",
                    $"Path: {path}\n\nError: {ex.Message}\n\nExpected format: One plugin filename per line (e.g., 'MyMod.esp')");
                StatusText = "Error parsing load order file";
                return;
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
            _stateService.UpdateConfigurationPaths(LoadOrderPath, Mo2Path, path);
            await SaveConfigurationAsync();
        }
    }

    private async Task ConfigureMo2Async()
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

        // Safely update configuration properties (they may be null in some configurations)
        config.LoadOrder.File = LoadOrderPath;
        config.XEdit.Binary = XEditPath;
        config.ModOrganizer.Binary = Mo2Path;
        config.Settings.MO2Mode = Mo2ModeEnabled;

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
        // Validate xEdit path before starting
        if (string.IsNullOrEmpty(XEditPath))
        {
            await _messageDialog.ShowErrorAsync(
                "xEdit Not Configured",
                "xEdit executable path is not configured. Please select your xEdit executable (SSEEdit, FO4Edit, etc.) before starting the cleaning process.",
                "Go to Edit > Configure xEdit Path to select your xEdit executable.");
            return;
        }

        if (!System.IO.File.Exists(XEditPath))
        {
            await _messageDialog.ShowErrorAsync(
                "xEdit Not Found",
                "The configured xEdit executable was not found at the specified path.",
                $"Path: {XEditPath}\n\nPlease verify the path is correct or select a new xEdit executable.");
            return;
        }

        try
        {
            // Show progress window (non-modal)
            _ = ShowProgressInteraction.Handle(Unit.Default);

            StatusText = "Cleaning started...";
            await _orchestrator.StartCleaningAsync(HandleTimeoutRetryAsync);
            StatusText = "Cleaning completed.";
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Configuration is invalid"))
        {
            _logger.Error(ex, "Configuration validation failed before cleaning");
            await _messageDialog.ShowErrorAsync(
                "Configuration Invalid",
                "The current configuration is invalid and cleaning cannot start.",
                $"Please ensure:\n- xEdit path is set correctly\n- Load order file is selected\n- Game type is detected\n\nDetails: {ex.Message}");
            StatusText = "Configuration error";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            _logger.Error(ex, "StartCleaningAsync failed");
            await _messageDialog.ShowErrorAsync(
                "Cleaning Failed",
                "An error occurred during the cleaning process.",
                $"Error: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Handles timeout retry prompts for plugin cleaning.
    /// </summary>
    private async Task<bool> HandleTimeoutRetryAsync(string pluginName, int timeoutSeconds, int attemptNumber)
    {
        var message = $"Cleaning of '{pluginName}' timed out after {timeoutSeconds} seconds.\n\n" +
                      $"Attempt {attemptNumber} of 3 failed.\n\n" +
                      "Would you like to retry cleaning this plugin?";

        var details = "Possible causes:\n" +
                      "- The plugin is very large\n" +
                      "- xEdit is processing slowly\n" +
                      "- The system is under heavy load\n\n" +
                      "You can increase the timeout in Edit > Settings if plugins regularly time out.";

        return await _messageDialog.ShowRetryAsync("Plugin Timeout", message, details);
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
                Mo2ModeEnabled = config.Settings.MO2Mode,
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

    private async Task ShowSettingsAsync()
    {
        try
        {
            var result = await ShowSettingsInteraction.Handle(Unit.Default);

            if (result)
            {
                // Settings were saved - reload configuration into state
                var config = await _configService.LoadUserConfigAsync();

                _stateService.UpdateState(s => s with
                {
                    Mo2ModeEnabled = config.Settings.MO2Mode,
                    CleaningTimeout = config.Settings.CleaningTimeout,
                    MaxConcurrentSubprocesses = config.Settings.MaxConcurrentSubprocesses
                });

                // Update local property
                Mo2ModeEnabled = config.Settings.MO2Mode;

                StatusText = "Settings saved";
                _logger.Information("Settings updated from settings dialog");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to show or process settings dialog");
            StatusText = "Error opening settings";
        }
    }

    private async Task ShowSkipListAsync()
    {
        try
        {
            var result = await ShowSkipListInteraction.Handle(Unit.Default);

            if (result)
            {
                StatusText = "Skip list saved";
                _logger.Information("Skip list updated from skip list dialog");

                // Refresh plugins to update IsInSkipList flags
                await RefreshPluginsForGameAsync(SelectedGame);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to show or process skip list dialog");
            StatusText = "Error opening skip list";
        }
    }

    private void OnStateChanged(AppState state)
    {
        LoadOrderPath = state.LoadOrderPath;
        XEditPath = state.XEditExecutablePath;
        Mo2Path = state.MO2ExecutablePath;
        Mo2ModeEnabled = state.Mo2ModeEnabled;
        PartialFormsEnabled = state.PartialFormsEnabled;
        
        // Plugins list update - optimized to avoid recreation if possible
        var statePlugins = state.PluginsToClean;
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
