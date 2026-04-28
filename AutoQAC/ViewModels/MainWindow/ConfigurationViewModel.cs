using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoQAC.ViewModels.MainWindow;

/// <summary>
/// Manages configuration paths, file dialogs, path validation, game selection,
/// and auto-save reactions for the main window.
/// </summary>
public sealed partial class ConfigurationViewModel : ViewModelBase, IDisposable
{
    private readonly IConfigurationService _configService;
    private readonly IFileDialogService _fileDialog;
    private readonly ILoggingService _logger;
    private readonly IMessageDialogService _messageDialog;
    private readonly IPluginIssueApproximationService _pluginIssueApproximationService;
    private readonly IPluginLoadingService _pluginLoadingService;
    private readonly IPluginValidationService _pluginService;
    private readonly IStateService _stateService;
    private readonly IDisposable _skipListChangedSubscription;

    private bool _initialized;
    private CancellationTokenSource? _pluginApproximationCts;
    private CancellationTokenSource? _pluginRefreshCts;
    private int _pluginRefreshGeneration;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoadOrderConfigured))]
    private string? _loadOrderPath;

    [ObservableProperty]
    private string? _xEditPath;

    [ObservableProperty]
    private string? _mo2Path;

    [ObservableProperty]
    private bool _mo2ModeEnabled;

    [ObservableProperty]
    private bool _partialFormsEnabled;

    [ObservableProperty]
    private bool _disableSkipListsEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMutagenSupported))]
    [NotifyPropertyChangedFor(nameof(IsGameSelected))]
    [NotifyPropertyChangedFor(nameof(RequiresLoadOrderFile))]
    [NotifyCanExecuteChangedFor(nameof(ConfigureGameDataFolderCommand))]
    private GameType _selectedGame = GameType.Unknown;

    [ObservableProperty]
    private string? _gameDataFolder;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearGameDataFolderOverrideCommand))]
    private bool _hasGameDataFolderOverride;

    [ObservableProperty]
    private bool _hasMigrationWarning;

    [ObservableProperty]
    private string? _migrationWarningMessage;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private bool? _isXEditPathValid;

    [ObservableProperty]
    private bool? _isMo2PathValid;

    [ObservableProperty]
    private bool? _isLoadOrderPathValid;

    [ObservableProperty]
    private bool? _isGameDataFolderValid;

    public IReadOnlyList<GameType> AvailableGames { get; }

    public bool IsMutagenSupported => _pluginLoadingService.IsGameSupportedByMutagen(SelectedGame);
    public bool IsGameSelected => SelectedGame != GameType.Unknown;
    public bool RequiresLoadOrderFile =>
        SelectedGame != GameType.Unknown && !_pluginLoadingService.IsGameSupportedByMutagen(SelectedGame);
    public bool IsLoadOrderConfigured => !string.IsNullOrWhiteSpace(LoadOrderPath);

    public ConfigurationViewModel(
        IConfigurationService configService,
        IStateService stateService,
        ILoggingService logger,
        IFileDialogService fileDialog,
        IMessageDialogService messageDialog,
        IPluginValidationService pluginService,
        IPluginLoadingService pluginLoadingService,
        IPluginIssueApproximationService? pluginIssueApproximationService = null)
    {
        _configService = configService;
        _stateService = stateService;
        _logger = logger;
        _fileDialog = fileDialog;
        _messageDialog = messageDialog;
        _pluginService = pluginService;
        _pluginLoadingService = pluginLoadingService;
        _pluginIssueApproximationService =
            pluginIssueApproximationService ?? NoOpPluginIssueApproximationService.Instance;

        AvailableGames = _pluginLoadingService.GetAvailableGames();

        _skipListChangedSubscription = _configService.SkipListChanged.Subscribe(
            new CallbackObserver<GameType>(OnSkipListChanged));
    }

    private void OnSkipListChanged(GameType changedGame)
    {
        if (changedGame != SelectedGame) return;
        _ = RefreshPluginsForGameAsync(SelectedGame).ContinueWith(t =>
        {
            if (t.Exception is { } ex)
            {
                _logger.Error(ex, "Failed to refresh plugins after skip list change");
            }
        }, TaskScheduler.Default);
    }

    partial void OnXEditPathChanged(string? value) =>
        IsXEditPathValid = string.IsNullOrWhiteSpace(value)
            ? null
            : File.Exists(value) && value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    partial void OnMo2PathChanged(string? value) =>
        IsMo2PathValid = string.IsNullOrWhiteSpace(value)
            ? null
            : File.Exists(value) && value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    partial void OnLoadOrderPathChanged(string? value) => RecomputeLoadOrderValidity();

    partial void OnSelectedGameChanged(GameType value)
    {
        RecomputeLoadOrderValidity();
        if (!_initialized) return;
        _ = HandleSelectedGameChangedAsync(value);
    }

    private async Task HandleSelectedGameChangedAsync(GameType gameType)
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
    }

    private void RecomputeLoadOrderValidity()
    {
        var path = LoadOrderPath;
        if (RequiresLoadOrderFile)
        {
            IsLoadOrderPathValid = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        }
        else
        {
            IsLoadOrderPathValid = string.IsNullOrWhiteSpace(path)
                ? null
                : File.Exists(path);
        }
    }

    partial void OnGameDataFolderChanged(string? value) =>
        IsGameDataFolderValid = string.IsNullOrWhiteSpace(value)
            ? null
            : Directory.Exists(value);

    partial void OnMo2ModeEnabledChanged(bool value)
    {
        if (!_initialized) return;
        _ = AutoSaveConfigurationAsync("MO2 mode");
    }

    partial void OnDisableSkipListsEnabledChanged(bool value)
    {
        if (!_initialized) return;
        _ = HandleDisableSkipListsChangedAsync();
    }

    private async Task HandleDisableSkipListsChangedAsync()
    {
        try
        {
            await SaveConfigurationAsync();
            await RefreshPluginsForGameAsync(SelectedGame);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to handle DisableSkipLists change");
        }
    }

    private async Task AutoSaveConfigurationAsync(string description)
    {
        try
        {
            await SaveConfigurationAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to auto-save {Description} configuration", description);
        }
    }

    [RelayCommand]
    private async Task ConfigureLoadOrderAsync()
    {
        var path = await _fileDialog.OpenFileDialogAsync(
            "Select Load Order File",
            "Text Files (*.txt)|*.txt|All Files (*.*)|*.*");

        if (string.IsNullOrEmpty(path))
            return;

        if (!File.Exists(path))
        {
            await _messageDialog.ShowErrorAsync(
                "File Not Found",
                "The selected load order file does not exist.",
                $"Path: {path}");
            return;
        }

        _stateService.UpdateConfigurationPaths(path, Mo2Path, XEditPath);

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

            var skipList = await _configService.GetSkipListAsync(SelectedGame, ct: CancellationToken.None);
            var pluginsWithSkipStatus =
                ApplySkipListStatus(
                    plugins,
                    skipList,
                    SelectedGame,
                    DisableSkipListsEnabled,
                    PluginIssueApproximation.Unavailable);
            _stateService.SetPluginsToClean(pluginsWithSkipStatus);
            StatusText = $"Loaded {plugins.Count} plugins from load order";
        }
        catch (FileNotFoundException ex)
        {
            _logger.Error(ex, "Load order file not found");
            await _messageDialog.ShowErrorAsync(
                "File Not Found",
                "The load order file could not be found.",
                $"Path: {path}\n\nError: {ex.Message}");
            StatusText = "Load order file not found";
            return;
        }
        catch (IOException ex)
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

    [RelayCommand]
    private async Task ConfigureXEditAsync()
    {
        var path = await _fileDialog.OpenFileDialogAsync(
            "Select xEdit Executable",
            "Executables (*.exe)|*.exe|All Files (*.*)|*.*");

        if (string.IsNullOrEmpty(path)) return;

        // Update the VM property synchronously *before* saving, because
        // SaveConfigurationAsync reads XEditPath directly. The state-to-VM sync that
        // runs through IUiDispatcher is asynchronous, so without this assignment the
        // serialized config can capture the previous XEditPath value and overwrite the
        // user's selection on disk.
        XEditPath = path;
        _stateService.UpdateConfigurationPaths(LoadOrderPath, Mo2Path, path);
        await SaveConfigurationAsync(flushToDisk: true);
    }

    [RelayCommand]
    private async Task ConfigureMo2Async()
    {
        var path = await _fileDialog.OpenFileDialogAsync(
            "Select Mod Organizer 2 Executable",
            "Executables (*.exe)|*.exe|All Files (*.*)|*.*");

        if (string.IsNullOrEmpty(path)) return;

        // See ConfigureXEditAsync — VM property must be set synchronously before save.
        Mo2Path = path;
        _stateService.UpdateConfigurationPaths(LoadOrderPath, path, XEditPath);
        await SaveConfigurationAsync(flushToDisk: true);
    }

    private bool CanConfigureGameDataFolder() => IsGameSelected;

    [RelayCommand(CanExecute = nameof(CanConfigureGameDataFolder))]
    private async Task ConfigureGameDataFolderAsync()
    {
        var path = await _fileDialog.OpenFolderDialogAsync(
            "Select Game Data Folder");

        if (string.IsNullOrEmpty(path))
            return;

        if (!Directory.Exists(path))
        {
            await _messageDialog.ShowErrorAsync(
                "Folder Not Found",
                "The selected folder does not exist.",
                $"Path: {path}");
            return;
        }

        await _configService.SetGameDataFolderOverrideAsync(SelectedGame, path);

        GameDataFolder = path;
        HasGameDataFolderOverride = true;

        await RefreshPluginsForGameAsync(SelectedGame);

        StatusText = $"Data folder override set for {SelectedGame}";
    }

    private bool CanClearGameDataFolderOverride() => HasGameDataFolderOverride;

    [RelayCommand(CanExecute = nameof(CanClearGameDataFolderOverride))]
    private async Task ClearGameDataFolderOverrideAsync()
    {
        await _configService.SetGameDataFolderOverrideAsync(SelectedGame, null);
        HasGameDataFolderOverride = false;

        await RefreshPluginsForGameAsync(SelectedGame);

        GameDataFolder = _pluginLoadingService.GetGameDataFolder(SelectedGame);

        StatusText = $"Data folder reset to auto-detect for {SelectedGame}";
    }

    [RelayCommand]
    private void TogglePartialForms()
    {
        // PartialFormsEnabled is bound to a CheckBox, so it updates automatically.
    }

    [RelayCommand]
    private void DismissMigrationWarning()
    {
        HasMigrationWarning = false;
        MigrationWarningMessage = null;
    }

    [RelayCommand]
    private async Task ResetSettingsAsync()
    {
        try
        {
            StatusText = "Resetting settings to defaults...";
            await _configService.ResetToDefaultsAsync();

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
    /// Called by the parent VM after construction to load config and initialize state.
    /// Sets a flag that causes subsequent property-change reactions (auto-save, refresh) to fire.
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

            // _initialized is still false → OnDisableSkipListsEnabledChanged bails out.
            DisableSkipListsEnabled = config.Settings.DisableSkipLists;

            // Mark initialized BEFORE applying SelectedGame so the side-effecting partial method runs.
            _initialized = true;

            var savedGame = await _configService.GetSelectedGameAsync();
            SelectedGame = savedGame;

            if (savedGame == GameType.Unknown &&
                !string.IsNullOrEmpty(config.LoadOrder.File) &&
                File.Exists(config.LoadOrder.File))
            {
                try
                {
                    var plugins = await _pluginService.GetPluginsFromLoadOrderAsync(config.LoadOrder.File);
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

    /// <summary>
    /// Saves the current main-window configuration values, optionally forcing the
    /// debounced configuration write to disk before returning for explicit Browse saves.
    /// </summary>
    private async Task SaveConfigurationAsync(bool flushToDisk = false)
    {
        var config = await _configService.LoadUserConfigAsync();

        config.XEdit.Binary = XEditPath;
        config.ModOrganizer.Binary = Mo2Path;
        config.Settings.Mo2Mode = Mo2ModeEnabled;
        config.Settings.DisableSkipLists = DisableSkipListsEnabled;

        await _configService.SaveUserConfigAsync(config);
        if (flushToDisk)
        {
            await _configService.FlushPendingSavesAsync();
        }
    }

    private async Task RefreshPluginsForGameAsync(GameType gameType)
    {
        var refreshGeneration = Interlocked.Increment(ref _pluginRefreshGeneration);
        CancelPendingApproximation();

        var cts = new CancellationTokenSource();
        var previousCts = Interlocked.Exchange(ref _pluginRefreshCts, cts);
        CancelAndDispose(previousCts);
        var ct = cts.Token;

        bool IsCurrent() =>
            !ct.IsCancellationRequested
            && refreshGeneration == Volatile.Read(ref _pluginRefreshGeneration);

        try
        {
            if (gameType == GameType.Unknown)
            {
                if (!IsCurrent()) return;
                _stateService.SetPluginsToClean(new List<PluginInfo>());
                GameDataFolder = null;
                HasGameDataFolderOverride = false;
                StatusText = "No game selected";
                return;
            }

            if (!IsCurrent()) return;
            _stateService.UpdateState(s => s with { CurrentGameType = gameType });

            var customDataFolder = await _configService.GetGameDataFolderOverrideAsync(gameType, ct);
            if (!IsCurrent()) return;
            HasGameDataFolderOverride = !string.IsNullOrWhiteSpace(customDataFolder);
            GameDataFolder = _pluginLoadingService.GetGameDataFolder(gameType, customDataFolder);

            if (_pluginLoadingService.IsGameSupportedByMutagen(gameType))
            {
                if (!IsCurrent()) return;
                LoadOrderPath = null;
                _stateService.UpdateConfigurationPaths(null, Mo2Path, XEditPath);
            }
            else
            {
                var configuredPath = await _configService.GetGameLoadOrderOverrideAsync(gameType, ct);
                if (!IsCurrent()) return;
                if (string.IsNullOrWhiteSpace(configuredPath))
                {
                    configuredPath = _pluginLoadingService.GetDefaultLoadOrderPath(gameType);
                    if (!string.IsNullOrEmpty(configuredPath))
                    {
                        await _configService.SetGameLoadOrderOverrideAsync(gameType, configuredPath, ct);
                        if (!IsCurrent()) return;
                        _logger.Information(
                            "Auto-detected load order path for {GameType}: {ConfiguredPath}",
                            gameType,
                            configuredPath);
                    }
                }

                LoadOrderPath = configuredPath;
                _stateService.UpdateConfigurationPaths(configuredPath, Mo2Path, XEditPath);
            }

            var skipList = await _configService.GetSkipListAsync(gameType, ct: ct);
            if (!IsCurrent()) return;
            var hasMutagenStatusMessage = false;

            if (_pluginLoadingService.IsGameSupportedByMutagen(gameType))
            {
                StatusText = $"Loading plugins via Mutagen for {gameType}...";
                var loadResult = await _pluginLoadingService.TryGetPluginsAsync(gameType, customDataFolder, ct);

                if (!IsCurrent()) return;

                if (!string.IsNullOrWhiteSpace(loadResult.DataFolder))
                {
                    GameDataFolder = loadResult.DataFolder;
                }

                switch (loadResult.Status)
                {
                    case PluginLoadingStatus.Success:
                    {
                        var pluginsWithSkipStatus =
                            ApplySkipListStatus(
                                loadResult.Plugins,
                                skipList,
                                gameType,
                                DisableSkipListsEnabled,
                                PluginIssueApproximation.Pending);
                        _stateService.SetPluginsToClean(pluginsWithSkipStatus);
                        StatusText = $"Loaded {loadResult.Plugins.Count} plugins for {gameType}";
                        StartApproximationRefresh(
                            gameType,
                            loadResult.DataFolder ?? GameDataFolder,
                            pluginsWithSkipStatus,
                            refreshGeneration);
                        return;
                    }
                    case PluginLoadingStatus.NoPluginsDiscovered:
                        _logger.Information(
                            "Mutagen returned no plugins for {GameType}; file-based loading can be used if configured",
                            gameType);
                        StatusText =
                            $"No plugins discovered via Mutagen for {gameType}. Verify the game has a valid load order and/or set a Data Folder override.";
                        hasMutagenStatusMessage = true;
                        break;
                    case PluginLoadingStatus.DataFolderNotFound:
                        _logger.Information(
                            "Mutagen could not resolve data folder for {GameType}; file-based loading can be used if configured",
                            gameType);
                        StatusText =
                            $"Could not resolve {gameType} data folder via Mutagen. Set a game data folder override.";
                        hasMutagenStatusMessage = true;
                        break;
                    case PluginLoadingStatus.Failed:
                        _logger.Warning(
                            "Mutagen failed for {GameType}: {Message}",
                            gameType,
                            loadResult.FailureReason ?? "Unknown error");
                        StatusText =
                            $"Failed to load plugins via Mutagen for {gameType}. Verify Data Folder settings and check logs for details.";
                        hasMutagenStatusMessage = true;
                        break;
                    case PluginLoadingStatus.UnsupportedGame:
                        StatusText =
                            $"{gameType} is not supported by Mutagen in this flow. Use file-based loading if configured.";
                        hasMutagenStatusMessage = true;
                        break;
                    default:
                        _logger.Warning("Unexpected plugin loading status from Mutagen path: {Status}", loadResult.Status);
                        StatusText =
                            $"Unexpected plugin loading result for {gameType}. Verify Data Folder settings and check logs for details.";
                        hasMutagenStatusMessage = true;
                        break;
                }
            }

            if (!string.IsNullOrEmpty(LoadOrderPath) && File.Exists(LoadOrderPath))
            {
                try
                {
                    StatusText = $"Loading plugins from file for {gameType}...";
                    var plugins = await _pluginLoadingService.GetPluginsFromFileAsync(LoadOrderPath, GameDataFolder, ct);
                    if (!IsCurrent()) return;
                    var pluginsWithSkipStatus = ApplySkipListStatus(
                        plugins,
                        skipList,
                        gameType,
                        DisableSkipListsEnabled,
                        PluginIssueApproximation.Unavailable);
                    _stateService.SetPluginsToClean(pluginsWithSkipStatus);
                    StatusText = $"Loaded {plugins.Count} plugins from file";
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // Refresh superseded by a newer game selection; safe to ignore.
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to load plugins from file");
                    if (IsCurrent()) StatusText = "Error loading plugins";
                }
            }
            else
            {
                if (!IsCurrent()) return;
                _stateService.SetPluginsToClean(new List<PluginInfo>());
                if (!hasMutagenStatusMessage)
                {
                    StatusText = _pluginLoadingService.IsGameSupportedByMutagen(gameType)
                        ? $"Could not detect {gameType} installation"
                        : $"{gameType} requires a load order file";
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Refresh superseded by a newer game selection; safe to ignore.
        }
        finally
        {
            // A newer refresh may have already swapped this CTS out and disposed it
            // (see Interlocked.Exchange + CancelAndDispose at the top of this method);
            // only dispose here if we're still the active refresh.
            if (Interlocked.CompareExchange(ref _pluginRefreshCts, null, cts) == cts)
            {
                cts.Dispose();
            }
        }
    }

    /// <summary>
    /// Applies skip list status to plugins, marking IsInSkipList for each plugin.
    /// If <paramref name="disableSkipLists"/> is true, all plugins will have IsInSkipList = false.
    /// </summary>
    internal static List<PluginInfo> ApplySkipListStatus(IReadOnlyList<PluginInfo> plugins, List<string> skipList,
        GameType gameType, bool disableSkipLists, PluginIssueApproximation? approximation = null)
    {
        var skipSet = new HashSet<string>(skipList, StringComparer.OrdinalIgnoreCase);
        return plugins.Select(p => p with
        {
            IsInSkipList = !disableSkipLists && skipSet.Contains(p.FileName),
            DetectedGameType = gameType,
            Approximation = approximation ?? p.Approximation
        }).ToList();
    }

    private void StartApproximationRefresh(
        GameType gameType,
        string? dataFolder,
        IReadOnlyList<PluginInfo> plugins,
        int refreshGeneration)
    {
        if (string.IsNullOrWhiteSpace(dataFolder))
        {
            _stateService.MergePluginApproximations(CreateUnavailableApproximations(plugins));
            return;
        }

        if (refreshGeneration != Volatile.Read(ref _pluginRefreshGeneration))
        {
            return;
        }

        var cts = new CancellationTokenSource();

        var previous = Interlocked.Exchange(ref _pluginApproximationCts, cts);
        if (refreshGeneration != Volatile.Read(ref _pluginRefreshGeneration))
        {
            Interlocked.CompareExchange(ref _pluginApproximationCts, null, cts);
            CancelAndDispose(previous);
            cts.Dispose();
            return;
        }

        CancelAndDispose(previous);
        _ = RunApproximationRefreshAsync(gameType, dataFolder, plugins, refreshGeneration, cts);
    }

    private async Task RunApproximationRefreshAsync(
        GameType gameType,
        string dataFolder,
        IReadOnlyList<PluginInfo> plugins,
        int refreshGeneration,
        CancellationTokenSource cts)
    {
        var cancellationToken = cts.Token;

        try
        {
            var results = await _pluginIssueApproximationService
                .GetApproximationsAsync(
                    gameType,
                    dataFolder,
                    approximation =>
                    {
                        if (cancellationToken.IsCancellationRequested ||
                            refreshGeneration != Volatile.Read(ref _pluginRefreshGeneration))
                        {
                            return;
                        }

                        _stateService.MergePluginApproximation(approximation);
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested ||
                refreshGeneration != Volatile.Read(ref _pluginRefreshGeneration))
            {
                return;
            }

            _stateService.MergePluginApproximations(
                results.Count == 0 ? CreateUnavailableApproximations(plugins) : results);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.Warning("Plugin issue approximation refresh failed for {GameType}: {Message}", gameType,
                ex.Message);

            if (!cancellationToken.IsCancellationRequested &&
                refreshGeneration == Volatile.Read(ref _pluginRefreshGeneration))
            {
                _stateService.MergePluginApproximations(CreateUnavailableApproximations(plugins));
            }
        }
        finally
        {
            Interlocked.CompareExchange(ref _pluginApproximationCts, null, cts);
            cts.Dispose();
        }
    }

    private static List<PluginIssueApproximationResult> CreateUnavailableApproximations(
        IReadOnlyList<PluginInfo> plugins) =>
        plugins.Select(plugin => new PluginIssueApproximationResult
        {
            FileName = plugin.FileName,
            FullPath = plugin.FullPath,
            Approximation = PluginIssueApproximation.Unavailable
        }).ToList();

    private void CancelPendingApproximation()
    {
        var cts = Interlocked.Exchange(ref _pluginApproximationCts, null);
        CancelAndDispose(cts);
    }

    private static void CancelAndDispose(CancellationTokenSource? cts)
    {
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        cts.Dispose();
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
        CancelPendingApproximation();
        CancelAndDispose(Interlocked.Exchange(ref _pluginRefreshCts, null));
        _skipListChangedSubscription.Dispose();
    }

    private sealed class NoOpPluginIssueApproximationService : IPluginIssueApproximationService
    {
        public static NoOpPluginIssueApproximationService Instance { get; } = new();

        public Task<IReadOnlyList<PluginIssueApproximationResult>> GetApproximationsAsync(
            GameType gameType,
            string dataFolder,
            Action<PluginIssueApproximationResult>? onApproximationReady = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PluginIssueApproximationResult>>(
                Array.Empty<PluginIssueApproximationResult>());
    }
}
