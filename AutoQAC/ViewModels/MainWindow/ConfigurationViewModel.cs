using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Models.Configuration;
using AutoQAC.Models.Diagnostics;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameCapability;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoQAC.ViewModels.MainWindow;

/// <summary>
///     Manages configuration paths, file dialogs, path validation, game selection,
///     and auto-save reactions for the main window.
/// </summary>
public sealed partial class ConfigurationViewModel : ViewModelBase, IDisposable
{
    private readonly IConfigurationService _configService;
    private readonly IPluginRefreshDiscoveryPlanner _discoveryPlanner;
    private readonly IDiscoverySettingsModule _discoverySettingsModule;
    private readonly IFileDialogService _fileDialog;
    private readonly ILoggingService _logger;
    private readonly IMessageDialogService _messageDialog;
    private readonly IPluginRefreshModule _pluginRefreshModule;
    private readonly IDisposable _skipListChangedSubscription;
    private readonly IStateService _stateService;

    private bool _initialized;
    private bool _suppressMo2ModeChanged;
    private bool _cleaningReserved;
    private bool _isCleaning;
    private bool _suppressDisableSkipListsChanged;
    private bool _suppressSelectedGameChanged;
    private bool _suppressSelectedProfileChanged;

    public ConfigurationViewModel(
        IConfigurationService configService,
        IStateService stateService,
        ILoggingService logger,
        IFileDialogService fileDialog,
        IMessageDialogService messageDialog,
        IPluginValidationService pluginService,
        IPluginLoadingService pluginLoadingService,
        IPluginRefreshModule pluginRefreshModule,
        IPluginRefreshDiscoveryPlanner discoveryPlanner,
        IDiscoverySettingsModule discoverySettingsModule)
    {
        _configService = configService;
        _stateService = stateService;
        _logger = logger;
        _fileDialog = fileDialog;
        _messageDialog = messageDialog;
        _pluginRefreshModule = pluginRefreshModule;
        _discoveryPlanner = discoveryPlanner;
        _discoverySettingsModule = discoverySettingsModule;

        AvailableGames = _discoveryPlanner.GetAvailableGames();

        _skipListChangedSubscription = _configService.SkipListChanged.Subscribe(
            new CallbackObserver<GameType>(OnSkipListChanged));
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfigureLoadOrderCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfigureXEditCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfigureMo2Command))]
    [NotifyCanExecuteChangedFor(nameof(ConfigureGameDataFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearGameDataFolderOverrideCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfigureMo2InstanceCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetMo2InstanceCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetSettingsCommand))]
    public partial bool CanChangeSettings { get; private set; } = true;

    /// <summary>Includes startup reservation in settings command availability.</summary>
    public void OnCleaningAdmissionChanged(bool reserved)
    {
        _cleaningReserved = reserved;
        CanChangeSettings = !_cleaningReserved && !_isCleaning;
    }

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

    public bool IsMutagenSupported => _discoveryPlanner.GetAffordance(SelectedGame, Mo2ModeEnabled).IsMutagenSupported;
    public bool IsGameSelected => SelectedGame != GameType.Unknown;

    public bool RequiresLoadOrderFile =>
        _discoveryPlanner.GetAffordance(SelectedGame, Mo2ModeEnabled).RequiresLoadOrderFile;

    public bool IsLoadOrderConfigured => !string.IsNullOrWhiteSpace(LoadOrderPath);
    public bool ShowMo2Config => Mo2ModeEnabled && IsGameSelected;
    public bool ShowProfileSelector => ShowMo2Config && AvailableProfiles.Count > 1;

    public void Dispose()
    {
        _ = _pluginRefreshModule.ExecuteAsync(new PluginRefreshIntent.Cancel(PluginRefreshCancelReason.Disposed));
        _skipListChangedSubscription.Dispose();
    }

    private void OnSkipListChanged(GameType changedGame)
    {
        if (changedGame != SelectedGame) return;
        _ = RefreshPluginsForGameAsync(SelectedGame).ContinueWith(t =>
        {
            if (t.Exception is { } ex) _logger.Error(ex, "Failed to refresh plugins after skip list change");
        }, TaskScheduler.Default);
    }

    partial void OnXEditPathChanged(string? value)
    {
        IsXEditPathValid = string.IsNullOrWhiteSpace(value)
            ? null
            : File.Exists(value) && value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
    }

    partial void OnMo2PathChanged(string? value)
    {
        IsMo2PathValid = string.IsNullOrWhiteSpace(value)
            ? null
            : File.Exists(value) && value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
    }

    partial void OnMo2InstancePathChanged(string? value)
    {
        IsMo2InstanceValid = string.IsNullOrWhiteSpace(value)
            ? null
            : Directory.Exists(value);
    }

    // ReSharper disable once UnusedParameter.Global
    partial void OnLoadOrderPathChanged(string? value)
    {
        RecomputeLoadOrderValidity();
    }

    partial void OnSelectedGameChanged(GameType value)
    {
        RecomputeLoadOrderValidity();
        if (_suppressSelectedGameChanged || !_initialized) return;
        _ = HandleSelectedGameChangedAsync(value);
    }

    private async Task HandleSelectedGameChangedAsync(GameType gameType)
    {
        try
        {
            var result = await _discoverySettingsModule.ExecuteAsync(
                new DiscoverySettingsIntent.SelectGame(gameType));
            await ApplyDiscoverySettingsResultAsync(result);
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
            IsLoadOrderPathValid = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        else
            IsLoadOrderPathValid = string.IsNullOrWhiteSpace(path)
                ? null
                : File.Exists(path);
    }

    partial void OnGameDataFolderChanged(string? value)
    {
        IsGameDataFolderValid = string.IsNullOrWhiteSpace(value)
            ? null
            : Directory.Exists(value);
    }

    partial void OnMo2ModeEnabledChanged(bool value)
    {
        if (_suppressMo2ModeChanged || !_initialized) return;
        _ = HandleMo2ModeChangedAsync(value);
    }

    partial void OnPartialFormsEnabledChanged(bool value)
    {
        if (!_initialized || !CanChangeSettings) return;
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
            var result = await _discoverySettingsModule.ExecuteAsync(
                new DiscoverySettingsIntent.SetMo2Mode(value));
            await ApplyDiscoverySettingsResultAsync(result);
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
            var result = await _discoverySettingsModule.ExecuteAsync(
                new DiscoverySettingsIntent.SetMo2Profile(SelectedGame, value));
            await ApplyDiscoverySettingsResultAsync(result);
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
        if (_suppressDisableSkipListsChanged || !_initialized) return;
        _ = HandleDisableSkipListsChangedAsync();
    }

    private async Task HandleDisableSkipListsChangedAsync()
    {
        try
        {
            var result = await _discoverySettingsModule.ExecuteAsync(
                new DiscoverySettingsIntent.SetDisableSkipLists(DisableSkipListsEnabled));
            await ApplyDiscoverySettingsResultAsync(result);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to handle DisableSkipLists change");
        }
    }

    [RelayCommand(CanExecute = nameof(CanChangeSettings))]
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

        try
        {
            var result = await _discoverySettingsModule.ExecuteAsync(
                new DiscoverySettingsIntent.SetLoadOrderPath(SelectedGame, path));
            await ApplyDiscoverySettingsResultAsync(result);
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
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to apply selected load order");
            var loadOrderIdentifier =
                DiagnosticTextFormatter.SafeFileIdentifier("Load Order File", path, "load order file");
            var failureMessage = DiagnosticTextFormatter.OperationFailed("Load order selection");
            await _messageDialog.ShowErrorAsync(
                "Load Order Selection Failed",
                $"{loadOrderIdentifier} could not be applied. {failureMessage}",
                DiagnosticTextFormatter.LatestLogDetails);
            StatusText = failureMessage;
        }
    }

    /// <summary>Submits only the chosen xEdit path through settings admission and durable acceptance.</summary>
    [RelayCommand(CanExecute = nameof(CanChangeSettings))]
    private async Task ConfigureXEditAsync()
    {
        var path = await _fileDialog.OpenFileDialogAsync(
            "Select xEdit Executable",
            "Executables (*.exe)|*.exe|All Files (*.*)|*.*");

        if (string.IsNullOrEmpty(path)) return;

        var baseline = await _configService.LoadUserConfigAsync();
        var requested = baseline.Copy();
        requested.XEdit.Binary = path;
        // Patch only the chosen path so settings changed while the picker was open survive.
        var result = await _discoverySettingsModule.ExecuteAsync(
            new DiscoverySettingsIntent.ApplySettings(baseline, requested));
        await ApplyDiscoverySettingsResultAsync(result);
    }

    [RelayCommand(CanExecute = nameof(CanChangeSettings))]
    private async Task ConfigureMo2Async()
    {
        var path = await _fileDialog.OpenFileDialogAsync(
            "Select Mod Organizer 2 Executable",
            "Executables (*.exe)|*.exe|All Files (*.*)|*.*");

        if (string.IsNullOrEmpty(path)) return;

        var result = await _discoverySettingsModule.ExecuteAsync(
            new DiscoverySettingsIntent.SetMo2ExecutablePath(path));
        await ApplyDiscoverySettingsResultAsync(result);
    }

    private bool CanConfigureMo2Instance()
    {
        return CanChangeSettings && IsGameSelected;
    }

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

        var result = await _discoverySettingsModule.ExecuteAsync(
            new DiscoverySettingsIntent.SetMo2InstanceOverride(SelectedGame, path));
        await ApplyDiscoverySettingsResultAsync(result);
    }

    private bool CanResetMo2Instance()
    {
        return CanChangeSettings && IsMo2InstanceOverride;
    }

    [RelayCommand(CanExecute = nameof(CanResetMo2Instance))]
    private async Task ResetMo2InstanceAsync()
    {
        var result = await _discoverySettingsModule.ExecuteAsync(
            new DiscoverySettingsIntent.SetMo2InstanceOverride(SelectedGame, null));
        await ApplyDiscoverySettingsResultAsync(result);
    }

    private bool CanConfigureGameDataFolder()
    {
        return CanChangeSettings && IsGameSelected;
    }

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

        var result = await _discoverySettingsModule.ExecuteAsync(
            new DiscoverySettingsIntent.SetGameDataFolderOverride(SelectedGame, path));
        await ApplyDiscoverySettingsResultAsync(result);
    }

    private bool CanClearGameDataFolderOverride()
    {
        return CanChangeSettings && HasGameDataFolderOverride;
    }

    /// <summary>
    ///     Converts the selected game into the folder label used by diagnostics without exposing a selected folder path.
    /// </summary>
    /// <returns>A safe game label, or the stable fallback phrase when no game is selected.</returns>
    private string GetSelectedGameFolderDisplayName()
    {
        return SelectedGame switch
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
    }

    [RelayCommand(CanExecute = nameof(CanClearGameDataFolderOverride))]
    private async Task ClearGameDataFolderOverrideAsync()
    {
        var result = await _discoverySettingsModule.ExecuteAsync(
            new DiscoverySettingsIntent.SetGameDataFolderOverride(SelectedGame, null));
        await ApplyDiscoverySettingsResultAsync(result);
    }

    [RelayCommand]
    private void DismissMigrationWarning()
    {
        HasMigrationWarning = false;
        MigrationWarningMessage = null;
    }

    [RelayCommand(CanExecute = nameof(CanChangeSettings))]
    private async Task ResetSettingsAsync()
    {
        try
        {
            StatusText = "Resetting settings to defaults...";
            var result = await _discoverySettingsModule.ExecuteAsync(new DiscoverySettingsIntent.Reset());

            await ApplyDiscoverySettingsResultAsync(result);
            if (result.Status == DiscoverySettingsChangeStatus.Accepted)
                _logger.Information("Settings reset to defaults by user");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to reset settings");
            StatusText = "Error resetting settings";
        }
    }

    /// <summary>
    ///     Called by the parent VM after construction to load config and initialize state.
    ///     Sets a flag that causes subsequent property-change reactions (auto-save, refresh) to fire.
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

            var savedGame = await _configService.GetSelectedGameAsync();
            SetSelectedGameWithoutPersistence(savedGame);
            _initialized = true;
            await RefreshPluginsForGameAsync(savedGame);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to initialize configuration");
            StatusText = "Failed to load configuration";
        }
    }

    /// <summary>
    ///     Updates local properties from application state. Called by parent on state changes.
    /// </summary>
    public void OnStateChanged(AppState state)
    {
        LoadOrderPath = state.LoadOrderPath;
        XEditPath = state.XEditExecutablePath;
        Mo2Path = state.Mo2ExecutablePath;
        _isCleaning = state.IsCleaning;
        CanChangeSettings = !_cleaningReserved && !_isCleaning;
        SetMo2ModeWithoutPersistence(state.Mo2ModeEnabled);
        SetSelectedGameWithoutPersistence(state.CurrentGameType);
        PartialFormsEnabled = state.PartialFormsEnabled;
        SetSelectedProfileWithoutPersistence(state.Mo2Profile);
    }

    /// <summary>
    ///     Applies visible configuration and status fields published by the Plugin refresh module.
    /// </summary>
    /// <param name="snapshot">Whole Plugin refresh publication snapshot.</param>
    public void OnPluginRefreshSnapshot(PluginRefreshSnapshot snapshot)
    {
        ApplyRefreshConfiguration(snapshot.Configuration);
        StatusText = snapshot.StatusText;
    }

    /// <summary>Requests refresh; the parent projects only the authoritative snapshot stream.</summary>
    private async Task RefreshPluginsForGameAsync(GameType gameType)
    {
        await _pluginRefreshModule.ExecuteAsync(new PluginRefreshIntent.RefreshGame(gameType));
    }

    /// <summary>Reports failures without replaying an older operation snapshot over the current publication.</summary>
    private async Task<bool> ApplyDiscoverySettingsResultAsync(DiscoverySettingsChangeResult result)
    {
        if (result.Status == DiscoverySettingsChangeStatus.Accepted)
        {
            return true;
        }

        if (result.Failure is not null) await ProjectDiscoverySettingsFailureAsync(result.Failure);

        return false;
    }

    /// <summary>Shows the module's safe failure details while preserving saved choices for retry.</summary>
    private async Task ProjectDiscoverySettingsFailureAsync(DiscoverySettingsChangeFailure failure)
    {
        StatusText = failure.SafeMessage;
        if (failure.Kind is DiscoverySettingsChangeFailureKind.PersistenceFailed or DiscoverySettingsChangeFailureKind.PublicationFailed)
        {
            await _messageDialog.ShowErrorAsync(
                failure.Kind == DiscoverySettingsChangeFailureKind.PersistenceFailed ? "Settings Save Failed" : "Plugin Refresh Failed",
                failure.SafeMessage,
                DiagnosticTextFormatter.LatestLogDetails);
            return;
        }
        if (failure.Kind == DiscoverySettingsChangeFailureKind.InvalidLoadOrderPath)
        {
            await _messageDialog.ShowErrorAsync(
                "File Not Found",
                failure.SafeMessage);
            return;
        }

        if (failure.Kind is DiscoverySettingsChangeFailureKind.InvalidGameDataFolder
            or DiscoverySettingsChangeFailureKind.InvalidMo2InstanceFolder)
        {
            await _messageDialog.ShowErrorAsync(
                "Folder Not Found",
                failure.SafeMessage);
            return;
        }

        if (failure.Kind == DiscoverySettingsChangeFailureKind.InvalidMo2ExecutablePath)
            await _messageDialog.ShowErrorAsync(
                "File Not Found",
                failure.SafeMessage);
    }

    private void ApplyRefreshConfiguration(PluginRefreshConfigurationProjection projection)
    {
        LoadOrderPath = projection.LoadOrderPath;
        GameDataFolder = projection.GameDataFolder;
        HasGameDataFolderOverride = projection.HasGameDataFolderOverride;

        if (Mo2ModeEnabled && SelectedGame != GameType.Unknown)
        {
            Mo2InstancePath = projection.Mo2InstancePath;
            IsMo2InstanceOverride = projection.IsMo2InstanceOverride;
            IsMo2InstanceValid = projection.IsMo2InstanceValid;
            SetAvailableProfiles(projection.AvailableProfiles);
            SetSelectedProfileWithoutPersistence(projection.SelectedProfile);
            return;
        }

        ClearMo2ProfileState();
    }

    private void SetAvailableProfiles(IReadOnlyList<string> profiles)
    {
        AvailableProfiles.Clear();
        foreach (var profile in profiles) AvailableProfiles.Add(profile);

        OnPropertyChanged(nameof(ShowProfileSelector));
    }

    private void ClearAvailableProfiles()
    {
        SetAvailableProfiles([]);
    }

    private void ClearMo2ProfileState()
    {
        Mo2InstancePath = null;
        IsMo2InstanceOverride = false;
        IsMo2InstanceValid = null;
        SetSelectedProfileWithoutPersistence(null);
        ClearAvailableProfiles();
    }

    /// <summary>Projects accepted configuration notifications without creating a second settings change.</summary>
    public void OnUserConfigurationChanged(UserConfiguration config)
    {
        SetDisableSkipListsWithoutPersistence(config.Settings.DisableSkipLists);
    }

    /// <summary>Prevents state projection from reissuing the same MO2 mode mutation.</summary>
    private void SetMo2ModeWithoutPersistence(bool enabled)
    {
        _suppressMo2ModeChanged = true;
        try { Mo2ModeEnabled = enabled; }
        finally { _suppressMo2ModeChanged = false; }
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

    private void SetSelectedGameWithoutPersistence(GameType gameType)
    {
        _suppressSelectedGameChanged = true;
        try
        {
            SelectedGame = gameType;
        }
        finally
        {
            _suppressSelectedGameChanged = false;
        }
    }

    private void SetDisableSkipListsWithoutPersistence(bool disabled)
    {
        _suppressDisableSkipListsChanged = true;
        try
        {
            DisableSkipListsEnabled = disabled;
        }
        finally
        {
            _suppressDisableSkipListsChanged = false;
        }
    }

    /// <summary>
    ///     Shows a non-modal migration warning banner in the main window.
    ///     Called from App.xaml.cs after legacy migration runs on startup.
    /// </summary>
    public void ShowMigrationWarning(string message)
    {
        MigrationWarningMessage = message;
        HasMigrationWarning = true;
    }
}