using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Models.Diagnostics;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameDetection;
using AutoQAC.Services.MO2;
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
    private readonly IPluginLoadingService _pluginLoadingService;
    private readonly IPluginRefreshCoordinator _pluginRefreshCoordinator;
    private readonly IPluginValidationService _pluginService;
    private readonly IMo2InstanceService _mo2InstanceService;
    private readonly IStateService _stateService;
    private readonly IDisposable _skipListChangedSubscription;
    private readonly IDisposable _pluginRefreshStatusSubscription;

    private bool _initialized;
    private bool _suppressSelectedProfileChanged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoadOrderConfigured))]
    public partial string? LoadOrderPath { get; set; }

    [ObservableProperty] public partial string? XEditPath { get; set; }

    [ObservableProperty] public partial string? Mo2Path { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMo2Config))]
    [NotifyPropertyChangedFor(nameof(ShowProfileSelector))]
    [NotifyPropertyChangedFor(nameof(RequiresLoadOrderFile))]
    public partial bool Mo2ModeEnabled { get; set; }

    [ObservableProperty] public partial bool PartialFormsEnabled { get; set; }

    [ObservableProperty] public partial bool DisableSkipListsEnabled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMutagenSupported))]
    [NotifyPropertyChangedFor(nameof(IsGameSelected))]
    [NotifyPropertyChangedFor(nameof(RequiresLoadOrderFile))]
    [NotifyPropertyChangedFor(nameof(ShowMo2Config))]
    [NotifyPropertyChangedFor(nameof(ShowProfileSelector))]
    [NotifyCanExecuteChangedFor(nameof(ConfigureGameDataFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfigureMo2InstanceCommand))]
    public partial GameType SelectedGame { get; set; } = GameType.Unknown;

    [ObservableProperty] public partial string? GameDataFolder { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearGameDataFolderOverrideCommand))]
    public partial bool HasGameDataFolderOverride { get; set; }

    [ObservableProperty] public partial bool HasMigrationWarning { get; set; }

    [ObservableProperty] public partial string? MigrationWarningMessage { get; set; }

    [ObservableProperty] public partial string StatusText { get; set; } = "Ready";

    [ObservableProperty] public partial bool? IsXEditPathValid { get; set; }

    [ObservableProperty] public partial bool? IsMo2PathValid { get; set; }

    [ObservableProperty] public partial bool? IsLoadOrderPathValid { get; set; }

    [ObservableProperty] public partial bool? IsGameDataFolderValid { get; set; }

    [ObservableProperty] public partial string? Mo2InstancePath { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetMo2InstanceCommand))]
    public partial bool IsMo2InstanceOverride { get; set; }

    [ObservableProperty] public partial bool? IsMo2InstanceValid { get; set; }

    [ObservableProperty] public partial string? SelectedProfile { get; set; }

    public ObservableCollection<string> AvailableProfiles { get; } = [];

    public IReadOnlyList<GameType> AvailableGames { get; }

    public bool IsMutagenSupported => _pluginLoadingService.IsGameSupportedByMutagen(SelectedGame);
    public bool IsGameSelected => SelectedGame != GameType.Unknown;

    public bool RequiresLoadOrderFile =>
        SelectedGame != GameType.Unknown && !Mo2ModeEnabled &&
        !_pluginLoadingService.IsGameSupportedByMutagen(SelectedGame);

    public bool IsLoadOrderConfigured => !string.IsNullOrWhiteSpace(LoadOrderPath);
    public bool ShowMo2Config => Mo2ModeEnabled && IsGameSelected;
    public bool ShowProfileSelector => ShowMo2Config && AvailableProfiles.Count > 1;

    public ConfigurationViewModel(
        IConfigurationService configService,
        IStateService stateService,
        ILoggingService logger,
        IFileDialogService fileDialog,
        IMessageDialogService messageDialog,
        IPluginValidationService pluginService,
        IPluginLoadingService pluginLoadingService,
        IPluginIssueApproximationService? pluginIssueApproximationService = null,
        IPluginRefreshCoordinator? pluginRefreshCoordinator = null,
        IGameDetectionService? gameDetectionService = null,
        IUiDispatcher? uiDispatcher = null,
        IMo2InstanceService? mo2InstanceService = null)
    {
        _configService = configService;
        _stateService = stateService;
        _logger = logger;
        _fileDialog = fileDialog;
        _messageDialog = messageDialog;
        _pluginService = pluginService;
        _pluginLoadingService = pluginLoadingService;
        _mo2InstanceService = mo2InstanceService ?? new Mo2InstanceService(logger);
        _pluginRefreshCoordinator = pluginRefreshCoordinator ?? new PluginRefreshCoordinator(
            pluginLoadingService,
            pluginIssueApproximationService ?? NoOpPluginIssueApproximationService.Instance,
            stateService,
            new PluginRefreshCapabilityPolicy(pluginLoadingService),
            gameDetectionService ?? new GameDetectionService(logger),
            configService,
            logger);
        var dispatcher = uiDispatcher ?? new SynchronousFallbackDispatcher();

        AvailableGames = _pluginLoadingService.GetAvailableGames();

        _skipListChangedSubscription = _configService.SkipListChanged.Subscribe(
            new CallbackObserver<GameType>(OnSkipListChanged));
        _pluginRefreshStatusSubscription = _pluginRefreshCoordinator.StatusChanged.Subscribe(
            new CallbackObserver<PluginRefreshStatus>(status =>
                dispatcher.Post(() => OnPluginRefreshStatusChanged(status))));
    }

    private void OnPluginRefreshStatusChanged(PluginRefreshStatus status)
    {
        StatusText = status.Kind switch
        {
            PluginRefreshStatusKind.SelectPlugins => "Select plugins to refresh.",
            PluginRefreshStatusKind.Canceled => "Approximation refresh canceled.",
            PluginRefreshStatusKind.AnalyzingSelected =>
                $"Analyzing {status.Current} of {status.Total} selected plugins.",
            PluginRefreshStatusKind.SelectedRefreshCompleted =>
                $"Updated {status.UpdatedCount} selected plugin approximations.",
            PluginRefreshStatusKind.ApproximationUnavailable => "Approximation refresh is not available for this game.",
            PluginRefreshStatusKind.LoadingPlugins => $"Loading plugins for {SelectedGame}...",
            _ => status.Message ?? StatusText
        };
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

    partial void OnMo2InstancePathChanged(string? value) =>
        IsMo2InstanceValid = string.IsNullOrWhiteSpace(value)
            ? null
            : Directory.Exists(value);

    // ReSharper disable once UnusedParameter.Global
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
        _ = HandleMo2ModeChangedAsync(value);
    }

    partial void OnPartialFormsEnabledChanged(bool value)
    {
        if (!_initialized) return;
        _stateService.UpdateState(s => s with { PartialFormsEnabled = value });
    }

    partial void OnSelectedProfileChanged(string? value)
    {
        if (_suppressSelectedProfileChanged || !_initialized || !Mo2ModeEnabled ||
            SelectedGame == GameType.Unknown) return;
        _ = HandleSelectedProfileChangedAsync(value);
    }

    private async Task HandleMo2ModeChangedAsync(bool value)
    {
        try
        {
            await SaveConfigurationAsync();
            _stateService.UpdateState(s => s with { Mo2ModeEnabled = value });
            await RefreshPluginsForGameAsync(SelectedGame);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to handle MO2 mode change");
        }
    }

    private async Task HandleSelectedProfileChangedAsync(string? value)
    {
        try
        {
            await _configService.SetMo2ProfileAsync(SelectedGame, value);
            await _configService.FlushPendingSavesAsync();
            await RefreshPluginsForGameAsync(SelectedGame);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to handle MO2 profile change");
            StatusText = "Error changing MO2 profile";
        }
    }

    // ReSharper disable once UnusedParameter.Global
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
            var loadOrderIdentifier =
                DiagnosticTextFormatter.SafeFileIdentifier("Load Order File", path, "load order file");
            await _messageDialog.ShowErrorAsync(
                "File Not Found",
                $"{loadOrderIdentifier} is missing. Choose the current plugins.txt or loadorder.txt file.");
            return;
        }

        _stateService.UpdateConfigurationPaths(path, Mo2Path, XEditPath);

        try
        {
            LoadOrderPath = path;
            await _pluginRefreshCoordinator.RefreshForGameAsync(
                new PluginRefreshRequest(SelectedGame, GameDataFolder, path));
        }
        catch (FileNotFoundException ex)
        {
            _logger.Error(ex, "Load order file not found");
            var loadOrderIdentifier =
                DiagnosticTextFormatter.SafeFileIdentifier("Load Order File", path, "load order file");
            await _messageDialog.ShowErrorAsync(
                "File Not Found",
                $"{loadOrderIdentifier} is missing. Choose the current plugins.txt or loadorder.txt file.");
            StatusText = $"{loadOrderIdentifier} is missing.";
            return;
        }
        catch (IOException ex)
        {
            _logger.Error(ex, "Failed to read load order file");
            var loadOrderIdentifier =
                DiagnosticTextFormatter.SafeFileIdentifier("Load Order File", path, "load order file");
            await _messageDialog.ShowErrorAsync(
                "Read Error",
                $"{loadOrderIdentifier} could not be read. See the latest AutoQAC log for technical details.",
                DiagnosticTextFormatter.LatestLogDetails);
            StatusText = $"{loadOrderIdentifier} could not be read. See the latest AutoQAC log for technical details.";
            return;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to parse selected load order");
            var loadOrderIdentifier =
                DiagnosticTextFormatter.SafeFileIdentifier("Load Order File", path, "load order file");
            var failureMessage = DiagnosticTextFormatter.OperationFailed("Load order selection");
            await _messageDialog.ShowErrorAsync(
                "Invalid Load Order",
                $"{loadOrderIdentifier} could not be parsed. {failureMessage}",
                DiagnosticTextFormatter.LatestLogDetails);
            StatusText = failureMessage;
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
        await RefreshPluginsForGameAsync(SelectedGame);
    }

    private bool CanConfigureMo2Instance() => IsGameSelected;

    [RelayCommand(CanExecute = nameof(CanConfigureMo2Instance))]
    private async Task ConfigureMo2InstanceAsync()
    {
        var path = await _fileDialog.OpenFolderDialogAsync(
            "Select MO2 Instance Folder",
            Mo2InstancePath);

        if (string.IsNullOrEmpty(path))
            return;

        if (!Directory.Exists(path))
        {
            await _messageDialog.ShowErrorAsync(
                "Folder Not Found",
                "The selected MO2 instance folder does not exist.");
            return;
        }

        Mo2InstancePath = path;
        await _configService.SetMo2InstanceOverrideAsync(SelectedGame, path);
        await _configService.FlushPendingSavesAsync();
        IsMo2InstanceOverride = true;

        await RefreshPluginsForGameAsync(SelectedGame);
        StatusText = $"MO2 instance override set for {SelectedGame}";
    }

    private bool CanResetMo2Instance() => IsMo2InstanceOverride;

    [RelayCommand(CanExecute = nameof(CanResetMo2Instance))]
    private async Task ResetMo2InstanceAsync()
    {
        await _configService.SetMo2InstanceOverrideAsync(SelectedGame, null);
        await _configService.FlushPendingSavesAsync();
        IsMo2InstanceOverride = false;
        await RefreshPluginsForGameAsync(SelectedGame);
        StatusText = $"MO2 instance reset to auto-detect for {SelectedGame}";
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
            var folderIssue = DiagnosticTextFormatter.SafeFolderIssue(GetSelectedGameFolderDisplayName());
            await _messageDialog.ShowErrorAsync(
                "Folder Not Found",
                folderIssue);
            return;
        }

        await _configService.SetGameDataFolderOverrideAsync(SelectedGame, path);

        GameDataFolder = path;
        HasGameDataFolderOverride = true;

        await RefreshPluginsForGameAsync(SelectedGame);

        StatusText = $"Data folder override set for {SelectedGame}";
    }

    private bool CanClearGameDataFolderOverride() => HasGameDataFolderOverride;

    /// <summary>
    /// Converts the selected game into the folder label used by diagnostics without exposing a selected folder path.
    /// </summary>
    /// <returns>A safe game label, or the stable fallback phrase when no game is selected.</returns>
    private string GetSelectedGameFolderDisplayName() => SelectedGame switch
    {
        GameType.SkyrimLe => "Skyrim Legendary Edition",
        GameType.SkyrimSe => "Skyrim Special Edition",
        GameType.SkyrimVr => "Skyrim VR",
        GameType.Fallout4 => "Fallout 4",
        GameType.Fallout4Vr => "Fallout 4 VR",
        GameType.Fallout3 => "Fallout 3",
        GameType.FalloutNewVegas => "Fallout New Vegas",
        GameType.Oblivion => "Oblivion",
        _ => "selected game"
    };

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
            _pluginRefreshCoordinator.CancelActiveRefresh(PluginRefreshCancelReason.Reset);
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
                PartialFormsEnabled = false,
                Mo2Profile = null
            });

            SelectedGame = GameType.Unknown;
            Mo2InstancePath = null;
            IsMo2InstanceOverride = false;
            SetSelectedProfileWithoutPersistence(null);
            AvailableProfiles.Clear();
            _stateService.SetPluginsToClean([]);

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
        SetSelectedProfileWithoutPersistence(state.Mo2Profile);
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
        var customDataFolder = gameType == GameType.Unknown
            ? null
            : await _configService.GetGameDataFolderOverrideAsync(gameType);
        HasGameDataFolderOverride = !string.IsNullOrWhiteSpace(customDataFolder);
        GameDataFolder = gameType == GameType.Unknown
            ? null
            : _pluginLoadingService.GetGameDataFolder(gameType, customDataFolder);

        if (Mo2ModeEnabled && gameType != GameType.Unknown)
        {
            await RefreshMo2PluginsForGameAsync(gameType);
            return;
        }

        ClearMo2ProfileState();

        LoadOrderPath = _pluginLoadingService.IsGameSupportedByMutagen(gameType)
            ? null
            : await ResolveLoadOrderPathAsync(gameType);

        _stateService.UpdateConfigurationPaths(LoadOrderPath, Mo2Path, XEditPath, null);
        await _pluginRefreshCoordinator.RefreshForGameAsync(
            new PluginRefreshRequest(
                gameType,
                GameDataFolder,
                LoadOrderPath,
                DisableSkipLists: DisableSkipListsEnabled));
    }

    private async Task RefreshMo2PluginsForGameAsync(GameType gameType)
    {
        LoadOrderPath = null;
        var instanceOverride = await _configService.GetMo2InstanceOverrideAsync(gameType);
        IsMo2InstanceOverride = !string.IsNullOrWhiteSpace(instanceOverride);

        var instance = await _mo2InstanceService.ResolveInstanceAsync(gameType, Mo2Path, instanceOverride);
        if (instance is null)
        {
            Mo2InstancePath = instanceOverride;
            IsMo2InstanceValid = string.IsNullOrWhiteSpace(Mo2InstancePath) ? null : Directory.Exists(Mo2InstancePath);
            ClearAvailableProfiles();
            SetSelectedProfileWithoutPersistence(null);
            _stateService.UpdateConfigurationPaths(null, Mo2Path, XEditPath, null);
            _stateService.SetPluginsToClean([]);
            StatusText = $"No MO2 instance found for {gameType}. Browse to the instance folder.";
            return;
        }

        Mo2InstancePath = instance.BaseDirectory;
        IsMo2InstanceValid = Directory.Exists(instance.BaseDirectory);

        var profiles = await Task.Run(() => _mo2InstanceService.GetProfiles(instance));
        SetAvailableProfiles(profiles);

        var persistedProfile = await _configService.GetMo2ProfileAsync(gameType);
        var profile = _mo2InstanceService.ChooseProfile(instance, profiles, persistedProfile);
        SetSelectedProfileWithoutPersistence(profile);
        _stateService.UpdateConfigurationPaths(null, Mo2Path, XEditPath, profile);

        if (string.IsNullOrWhiteSpace(profile))
        {
            _stateService.SetPluginsToClean([]);
            StatusText = $"No MO2 profiles with loadorder.txt were found for {gameType}.";
            return;
        }

        var mo2LoadOrderPath = _mo2InstanceService.GetLoadOrderPath(instance, profile);
        if (string.IsNullOrWhiteSpace(mo2LoadOrderPath) || !File.Exists(mo2LoadOrderPath))
        {
            _stateService.SetPluginsToClean([]);
            StatusText = $"MO2 profile '{profile}' does not contain a loadorder.txt.";
            return;
        }

        var pathMap = await Task.Run(() => _mo2InstanceService.BuildPluginPathMap(instance, profile, GameDataFolder));
        await _pluginRefreshCoordinator.RefreshForGameAsync(
            new PluginRefreshRequest(
                gameType,
                GameDataFolder,
                DisableSkipLists: DisableSkipListsEnabled,
                Mo2Mode: true,
                Mo2LoadOrderPath: mo2LoadOrderPath,
                Mo2PathMap: pathMap,
                Mo2BaseDataFolder: GameDataFolder));
    }

    private void SetAvailableProfiles(IReadOnlyList<string> profiles)
    {
        AvailableProfiles.Clear();
        foreach (var profile in profiles)
        {
            AvailableProfiles.Add(profile);
        }

        OnPropertyChanged(nameof(ShowProfileSelector));
    }

    private void ClearAvailableProfiles() => SetAvailableProfiles([]);

    private void ClearMo2ProfileState()
    {
        Mo2InstancePath = null;
        IsMo2InstanceOverride = false;
        IsMo2InstanceValid = null;
        SetSelectedProfileWithoutPersistence(null);
        ClearAvailableProfiles();
    }

    private void SetSelectedProfileWithoutPersistence(string? profile)
    {
        _suppressSelectedProfileChanged = true;
        try
        {
            SelectedProfile = profile;
        }
        finally
        {
            _suppressSelectedProfileChanged = false;
        }
    }

    private async Task<string?> ResolveLoadOrderPathAsync(GameType gameType)
    {
        if (gameType == GameType.Unknown)
        {
            return null;
        }

        var configuredPath = await _configService.GetGameLoadOrderOverrideAsync(gameType);
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath;
        }

        configuredPath = _pluginLoadingService.GetDefaultLoadOrderPath(gameType);
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            await _configService.SetGameLoadOrderOverrideAsync(gameType, configuredPath);
            _logger.Information(
                "Auto-detected load order path for {GameType}: {ConfiguredPath}",
                gameType,
                configuredPath);
        }

        return configuredPath;
    }

    /// <summary>
    /// Shows a non-modal migration warning banner in the main window.
    /// Called from App.xaml.cs after legacy migration runs on startup.
    /// </summary>
    public void ShowMigrationWarning(string message)
    {
        MigrationWarningMessage = message;
        HasMigrationWarning = true;
    }

    public void Dispose()
    {
        _pluginRefreshCoordinator.CancelActiveRefresh(PluginRefreshCancelReason.Disposed);
        _pluginRefreshStatusSubscription.Dispose();
        _skipListChangedSubscription.Dispose();
    }

    private sealed class SynchronousFallbackDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();

        public Task InvokeAsync(Func<Task> action) => action();
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
                []);

        public Task<IReadOnlyList<PluginIssueApproximationResult>> GetApproximationsAsync(
            GameType gameType,
            string baseDataFolder,
            IReadOnlyList<string> orderedPluginNames,
            Func<Mutagen.Bethesda.Plugins.ModKey, string?> pathResolver,
            Action<PluginIssueApproximationResult>? onApproximationReady = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PluginIssueApproximationResult>>(
                []);
    }
}
