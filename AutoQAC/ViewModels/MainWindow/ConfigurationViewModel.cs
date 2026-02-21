using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using ReactiveUI;

namespace AutoQAC.ViewModels.MainWindow;

/// <summary>
/// Manages configuration paths, file dialogs, path validation, game selection,
/// and auto-save subscriptions for the main window.
/// </summary>
public sealed class ConfigurationViewModel : ViewModelBase, IDisposable
{
    private readonly IConfigurationService _configService;
    private readonly IStateService _stateService;
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

    private bool _disableSkipListsEnabled;

    public bool DisableSkipListsEnabled
    {
        get => _disableSkipListsEnabled;
        set => this.RaiseAndSetIfChanged(ref _disableSkipListsEnabled, value);
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

    private readonly ObservableAsPropertyHelper<bool> _isGameSelected;
    public bool IsGameSelected => _isGameSelected.Value;

    private readonly ObservableAsPropertyHelper<bool> _requiresLoadOrderFile;
    public bool RequiresLoadOrderFile => _requiresLoadOrderFile.Value;

    private string? _gameDataFolder;

    public string? GameDataFolder
    {
        get => _gameDataFolder;
        set => this.RaiseAndSetIfChanged(ref _gameDataFolder, value);
    }

    private bool _hasGameDataFolderOverride;

    public bool HasGameDataFolderOverride
    {
        get => _hasGameDataFolderOverride;
        set => this.RaiseAndSetIfChanged(ref _hasGameDataFolderOverride, value);
    }

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

    private bool? _isGameDataFolderValid;
    public bool? IsGameDataFolderValid
    {
        get => _isGameDataFolderValid;
        set => this.RaiseAndSetIfChanged(ref _isGameDataFolderValid, value);
    }

    #endregion

    private bool _hasMigrationWarning;

    public bool HasMigrationWarning
    {
        get => _hasMigrationWarning;
        set => this.RaiseAndSetIfChanged(ref _hasMigrationWarning, value);
    }

    private string? _migrationWarningMessage;

    public string? MigrationWarningMessage
    {
        get => _migrationWarningMessage;
        set => this.RaiseAndSetIfChanged(ref _migrationWarningMessage, value);
    }

    private string _statusText = "Ready";

    /// <summary>
    /// Status text managed by configuration operations. The parent orchestrator
    /// reads this to display in the status bar.
    /// </summary>
    public string StatusText
    {
        get => _statusText;
        set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    // Commands
    public ReactiveCommand<Unit, Unit> ConfigureLoadOrderCommand { get; }
    public ReactiveCommand<Unit, Unit> ConfigureXEditCommand { get; }
    public ReactiveCommand<Unit, Unit> ConfigureMo2Command { get; }
    public ReactiveCommand<Unit, Unit> ConfigureGameDataFolderCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearGameDataFolderOverrideCommand { get; }
    public ReactiveCommand<Unit, Unit> TogglePartialFormsCommand { get; }
    public ReactiveCommand<Unit, Unit> DismissMigrationWarningCommand { get; }
    public ReactiveCommand<Unit, Unit> ResetSettingsCommand { get; }

    public ConfigurationViewModel(
        IConfigurationService configService,
        IStateService stateService,
        ILoggingService logger,
        IFileDialogService fileDialog,
        IMessageDialogService messageDialog,
        IPluginValidationService pluginService,
        IPluginLoadingService pluginLoadingService)
    {
        _configService = configService;
        _stateService = stateService;
        _logger = logger;
        _fileDialog = fileDialog;
        _messageDialog = messageDialog;
        _pluginService = pluginService;
        _pluginLoadingService = pluginLoadingService;

        // Initialize available games list
        AvailableGames = _pluginLoadingService.GetAvailableGames();

        // IsMutagenSupported computed from SelectedGame
        _isMutagenSupported = this.WhenAnyValue(x => x.SelectedGame)
            .Select(g => _pluginLoadingService.IsGameSupportedByMutagen(g))
            .ToProperty(this, x => x.IsMutagenSupported);
        _disposables.Add(_isMutagenSupported);

        _isGameSelected = this.WhenAnyValue(x => x.SelectedGame)
            .Select(g => g != GameType.Unknown)
            .ToProperty(this, x => x.IsGameSelected);
        _disposables.Add(_isGameSelected);

        // RequiresLoadOrderFile computed from SelectedGame (inverse of IsMutagenSupported)
        _requiresLoadOrderFile = this.WhenAnyValue(x => x.SelectedGame)
            .Select(g => g != GameType.Unknown && !_pluginLoadingService.IsGameSupportedByMutagen(g))
            .ToProperty(this, x => x.RequiresLoadOrderFile);
        _disposables.Add(_requiresLoadOrderFile);

        // Initialize commands
        ConfigureLoadOrderCommand = ReactiveCommand.CreateFromTask(ConfigureLoadOrderAsync);
        ConfigureXEditCommand = ReactiveCommand.CreateFromTask(ConfigureXEditAsync);
        ConfigureMo2Command = ReactiveCommand.CreateFromTask(ConfigureMo2Async);

        // Game data folder commands - enabled whenever a game is selected
        var canConfigureDataFolder = this.WhenAnyValue(x => x.IsGameSelected);
        ConfigureGameDataFolderCommand =
            ReactiveCommand.CreateFromTask(ConfigureGameDataFolderAsync, canConfigureDataFolder);

        var canClearOverride = this.WhenAnyValue(x => x.HasGameDataFolderOverride);
        ClearGameDataFolderOverrideCommand =
            ReactiveCommand.CreateFromTask(ClearGameDataFolderOverrideAsync, canClearOverride);

        TogglePartialFormsCommand = ReactiveCommand.Create(TogglePartialForms);

        DismissMigrationWarningCommand = ReactiveCommand.Create(() =>
        {
            HasMigrationWarning = false;
            MigrationWarningMessage = null;
        });

        ResetSettingsCommand = ReactiveCommand.CreateFromTask(ResetSettingsAsync);

        // Path validation subscriptions for main window indicators
        // xEdit is required: null when empty, true/false when populated
        var xEditValidation = this.WhenAnyValue(x => x.XEditPath)
            .Select(path => string.IsNullOrWhiteSpace(path)
                ? (bool?)null
                : System.IO.File.Exists(path) && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .Subscribe(v => IsXEditPathValid = v);
        _disposables.Add(xEditValidation);

        // MO2 is optional: null when empty, true/false when populated
        var mo2Validation = this.WhenAnyValue(x => x.Mo2Path)
            .Select(path => string.IsNullOrWhiteSpace(path)
                ? (bool?)null
                : (bool?)(System.IO.File.Exists(path) && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
            .Subscribe(v => IsMo2PathValid = v);
        _disposables.Add(mo2Validation);

        // Load order is required for non-Mutagen games, optional otherwise.
        var loadOrderValidation = this.WhenAnyValue(x => x.LoadOrderPath, x => x.RequiresLoadOrderFile)
            .Select(t =>
            {
                var path = t.Item1;
                var required = t.Item2;

                if (required)
                {
                    return !string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path);
                }

                return string.IsNullOrWhiteSpace(path)
                    ? (bool?)null
                    : (bool?)System.IO.File.Exists(path);
            })
            .Subscribe(v => IsLoadOrderPathValid = v);
        _disposables.Add(loadOrderValidation);

        // Game data folder is optional: null when empty, true/false when populated
        var dataFolderValidation = this.WhenAnyValue(x => x.GameDataFolder)
            .Select(path => string.IsNullOrWhiteSpace(path)
                ? (bool?)null
                : (bool?)System.IO.Directory.Exists(path))
            .Subscribe(v => IsGameDataFolderValid = v);
        _disposables.Add(dataFolderValidation);

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

        // Subscribe to skip list changes to refresh plugin display
        var skipListChangedSubscription = _configService.SkipListChanged
            .Where(changedGame => changedGame == SelectedGame)
            .SelectMany(_ => Observable.FromAsync(() => RefreshPluginsForGameAsync(SelectedGame)))
            .Subscribe(
                _ => { },
                ex => _logger.Error(ex, "Failed to refresh plugins after skip list change"));
        _disposables.Add(skipListChangedSubscription);

        // Auto-save DisableSkipLists when changed and refresh plugin list
        var disableSkipListsSubscription = this.WhenAnyValue(x => x.DisableSkipListsEnabled)
            .Skip(1) // Skip initial load
            .SelectMany(_ => Observable.FromAsync(async () =>
            {
                await SaveConfigurationAsync();
                await RefreshPluginsForGameAsync(SelectedGame);
            }))
            .Subscribe(
                _ => { },
                ex => _logger.Error(ex, "Failed to handle DisableSkipLists change"));
        _disposables.Add(disableSkipListsSubscription);
    }

    /// <summary>
    /// Called by parent after construction to load config and initialize state.
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            var config = await _configService.LoadUserConfigAsync();

            _stateService.UpdateConfigurationPaths(
                null,
                config.ModOrganizer.Binary,
                config.XEdit.Binary);

            _stateService.UpdateState(s => s with
            {
                Mo2ModeEnabled = config.Settings.Mo2Mode,
                CleaningTimeout = config.Settings.CleaningTimeout
            });

            // Load DisableSkipLists setting - use backing field first to avoid triggering
            // the subscription before SelectedGame is set (which would cause a race condition)
            _disableSkipListsEnabled = config.Settings.DisableSkipLists;

            // Load saved game selection - this triggers gameSelectionSubscription which loads plugins
            var savedGame = await _configService.GetSelectedGameAsync();
            SelectedGame = savedGame;

            // Now notify UI that DisableSkipListsEnabled changed - the subscription will fire
            // but SelectedGame is already set correctly, so no race condition occurs
            this.RaisePropertyChanged(nameof(DisableSkipListsEnabled));

            // If no game was saved but we have a load order file, try to detect game and load plugins
            if (savedGame == GameType.Unknown &&
                !string.IsNullOrEmpty(config.LoadOrder.File) &&
                System.IO.File.Exists(config.LoadOrder.File))
            {
                try
                {
                    var plugins = await _pluginService.GetPluginsFromLoadOrderAsync(config.LoadOrder.File);
                    // No game selected, so we can't apply skip list - just pass plugins as-is
                    _stateService.SetPluginsToClean(plugins);
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

    /// <summary>
    /// Updates local properties from application state. Called by parent on state changes.
    /// </summary>
    public void OnStateChanged(AppState state)
    {
        LoadOrderPath = state.LoadOrderPath;
        XEditPath = state.XEditExecutablePath;
        Mo2Path = state.Mo2ExecutablePath;
        Mo2ModeEnabled = state.Mo2ModeEnabled;
        PartialFormsEnabled = state.PartialFormsEnabled;
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
                var plugins = await _pluginService.GetPluginsFromLoadOrderAsync(path, GameDataFolder);

                if (plugins.Count == 0)
                {
                    await _messageDialog.ShowWarningAsync(
                        "No Plugins Found",
                        "The load order file was parsed successfully but no plugins were found.",
                        $"File: {path}\n\nEnsure the file contains a valid list of plugin names (one per line).");
                }

                // Apply skip list status if a game is selected
                var skipList = await _configService.GetSkipListAsync(SelectedGame, ct: CancellationToken.None);
                var pluginsWithSkipStatus =
                    ApplySkipListStatus(plugins, skipList, SelectedGame, DisableSkipListsEnabled);
                _stateService.SetPluginsToClean(pluginsWithSkipStatus);
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

            await _configService.SetGameLoadOrderOverrideAsync(SelectedGame, path);
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

    private async Task ConfigureGameDataFolderAsync()
    {
        var path = await _fileDialog.OpenFolderDialogAsync(
            "Select Game Data Folder");

        if (!string.IsNullOrEmpty(path))
        {
            // Verify the folder exists
            if (!System.IO.Directory.Exists(path))
            {
                await _messageDialog.ShowErrorAsync(
                    "Folder Not Found",
                    "The selected folder does not exist.",
                    $"Path: {path}");
                return;
            }

            // Save the override
            await _configService.SetGameDataFolderOverrideAsync(SelectedGame, path);

            // Update display
            GameDataFolder = path;
            HasGameDataFolderOverride = true;

            // Refresh plugins with the new path
            await RefreshPluginsForGameAsync(SelectedGame);

            StatusText = $"Data folder override set for {SelectedGame}";
        }
    }

    private async Task ClearGameDataFolderOverrideAsync()
    {
        // Clear the override
        await _configService.SetGameDataFolderOverrideAsync(SelectedGame, null);
        HasGameDataFolderOverride = false;

        // Refresh to show auto-detected path
        await RefreshPluginsForGameAsync(SelectedGame);

        // Update display with auto-detected path
        GameDataFolder = _pluginLoadingService.GetGameDataFolder(SelectedGame);

        StatusText = $"Data folder reset to auto-detect for {SelectedGame}";
    }

    private async Task SaveConfigurationAsync()
    {
        // Update UserConfiguration from ViewModel/State
        var config = await _configService.LoadUserConfigAsync();

        // Safely update configuration properties (they may be null in some configurations)
        config.XEdit.Binary = XEditPath;
        config.ModOrganizer.Binary = Mo2Path;
        config.Settings.Mo2Mode = Mo2ModeEnabled;
        config.Settings.DisableSkipLists = DisableSkipListsEnabled;

        await _configService.SaveUserConfigAsync(config);
    }

    private async Task RefreshPluginsForGameAsync(GameType gameType)
    {
        if (gameType == GameType.Unknown)
        {
            _stateService.SetPluginsToClean(new List<PluginInfo>());
            GameDataFolder = null;
            HasGameDataFolderOverride = false;
            StatusText = "No game selected";
            return;
        }

        _stateService.UpdateState(s => s with { CurrentGameType = gameType });

        // Resolve data folder (auto-detect + per-game override)
        var customDataFolder = await _configService.GetGameDataFolderOverrideAsync(gameType);
        HasGameDataFolderOverride = !string.IsNullOrWhiteSpace(customDataFolder);
        GameDataFolder = _pluginLoadingService.GetGameDataFolder(gameType, customDataFolder);

        if (_pluginLoadingService.IsGameSupportedByMutagen(gameType))
        {
            LoadOrderPath = null;
            _stateService.UpdateConfigurationPaths(null, Mo2Path, XEditPath);
        }
        else
        {
            var configuredPath = await _configService.GetGameLoadOrderOverrideAsync(gameType);
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                configuredPath = _pluginLoadingService.GetDefaultLoadOrderPath(gameType);
                if (!string.IsNullOrEmpty(configuredPath))
                {
                    await _configService.SetGameLoadOrderOverrideAsync(gameType, configuredPath);
                    _logger.Information(
                        "Auto-detected load order path for {GameType}: {ConfiguredPath}",
                        gameType,
                        configuredPath);
                }
            }

            LoadOrderPath = configuredPath;
            _stateService.UpdateConfigurationPaths(configuredPath, Mo2Path, XEditPath);
        }

        // Load skip list for the current game
        var skipList = await _configService.GetSkipListAsync(gameType, ct: CancellationToken.None);

        // Try Mutagen first if supported
        if (_pluginLoadingService.IsGameSupportedByMutagen(gameType))
        {
            try
            {
                StatusText = $"Loading plugins via Mutagen for {gameType}...";
                var plugins = await _pluginLoadingService.GetPluginsAsync(gameType, customDataFolder);

                if (plugins.Count > 0)
                {
                    // Apply skip list filtering and mark IsInSkipList
                    var pluginsWithSkipStatus =
                        ApplySkipListStatus(plugins, skipList, gameType, DisableSkipListsEnabled);
                    _stateService.SetPluginsToClean(pluginsWithSkipStatus);
                    StatusText = $"Loaded {plugins.Count} plugins for {gameType}";
                    return;
                }

                // Mutagen returned empty, might need file-based fallback
                _logger.Information("Mutagen returned no plugins for {GameType}, fallback to file-based", gameType);
            }
            catch (Exception ex)
            {
                _logger.Warning("Mutagen failed for {GameType}: {Message}", gameType, ex.Message);
            }
        }

        // Fall back to file-based loading if we have a load order path
        if (!string.IsNullOrEmpty(LoadOrderPath) && System.IO.File.Exists(LoadOrderPath))
        {
            try
            {
                StatusText = $"Loading plugins from file for {gameType}...";
                var plugins = await _pluginLoadingService.GetPluginsFromFileAsync(LoadOrderPath, GameDataFolder);
                // Apply skip list filtering and mark IsInSkipList
                var pluginsWithSkipStatus = ApplySkipListStatus(plugins, skipList, gameType, DisableSkipListsEnabled);
                _stateService.SetPluginsToClean(pluginsWithSkipStatus);
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
            _stateService.SetPluginsToClean(new List<PluginInfo>());
            StatusText = _pluginLoadingService.IsGameSupportedByMutagen(gameType)
                ? $"Could not detect {gameType} installation"
                : $"{gameType} requires a load order file";
        }
    }

    /// <summary>
    /// Applies skip list status to plugins, marking IsInSkipList for each plugin.
    /// If disableSkipLists is true, all plugins will have IsInSkipList = false.
    /// </summary>
    internal static List<PluginInfo> ApplySkipListStatus(List<PluginInfo> plugins, List<string> skipList,
        GameType gameType, bool disableSkipLists)
    {
        var skipSet = new HashSet<string>(skipList, StringComparer.OrdinalIgnoreCase);
        return plugins.Select(p => p with
        {
            IsInSkipList = !disableSkipLists && skipSet.Contains(p.FileName),
            DetectedGameType = gameType
        }).ToList();
    }

    private void TogglePartialForms()
    {
        // Logic to toggle partial forms
        // Show warning dialog if enabling? (Phase 6)
        // For now just toggle
        // PartialFormsEnabled is bound to CheckBox, so it updates automatically.
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
                Mo2ModeEnabled = config.Settings.Mo2Mode,
                CleaningTimeout = config.Settings.CleaningTimeout,
                PartialFormsEnabled = false
            });

            SelectedGame = GameType.Unknown;
            _stateService.SetPluginsToClean(new List<PluginInfo>());

            StatusText = "Settings reset to defaults";
            _logger.Information("Settings reset to defaults by user");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to reset settings");
            StatusText = "Error resetting settings";
        }
    }

    /// <summary>
    /// Shows a non-modal migration warning banner in the main window.
    /// Called from App.axaml.cs after legacy migration runs on startup.
    /// </summary>
    public void ShowMigrationWarning(string message)
    {
        MigrationWarningMessage = message;
        HasMigrationWarning = true;
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
