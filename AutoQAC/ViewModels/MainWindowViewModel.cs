using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using Avalonia.Controls.ApplicationLifetimes;
using ReactiveUI;

namespace AutoQAC.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly IConfigurationService _configService;
    private readonly IStateService _stateService;
    private readonly ICleaningOrchestrator _orchestrator;
    private readonly ILoggingService _logger;
    private readonly IFileDialogService _fileDialog;

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

    public MainWindowViewModel(
        IConfigurationService configService,
        IStateService stateService,
        ICleaningOrchestrator orchestrator,
        ILoggingService logger,
        IFileDialogService fileDialog)
    {
        _configService = configService;
        _stateService = stateService;
        _orchestrator = orchestrator;
        _logger = logger;
        _fileDialog = fileDialog;

        // Initialize OAPHs first
        _isCleaning = _stateService.StateChanged
            .Select(s => s.IsCleaning)
            .ToProperty(this, x => x.IsCleaning);

        // Initialize commands
        ConfigureLoadOrderCommand = ReactiveCommand.CreateFromTask(ConfigureLoadOrderAsync);
        ConfigureXEditCommand = ReactiveCommand.CreateFromTask(ConfigureXEditAsync);
        ConfigureMO2Command = ReactiveCommand.CreateFromTask(ConfigureMO2Async);

        TogglePartialFormsCommand = ReactiveCommand.Create(TogglePartialForms);

        // Define canStart observable
        var canStart = this.WhenAnyValue(
            x => x.LoadOrderPath,
            x => x.XEditPath,
            x => x.IsCleaning,
            (loadOrder, xEdit, isCleaning) =>
                !string.IsNullOrEmpty(loadOrder) &&
                !string.IsNullOrEmpty(xEdit) &&
                !isCleaning);

        _canStartCleaning = canStart
            .ToProperty(this, x => x.CanStartCleaning);

        StartCleaningCommand = ReactiveCommand.CreateFromTask(
            StartCleaningAsync,
            canStart);

        StopCleaningCommand = ReactiveCommand.Create(
            StopCleaning,
            this.WhenAnyValue(x => x.IsCleaning));

        ExitCommand = ReactiveCommand.Create(Exit);
        ShowAboutCommand = ReactiveCommand.Create(ShowAbout);

        // Subscribe to state changes
        _stateService.StateChanged
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(OnStateChanged);
            
        // Initialize UI from current state
        OnStateChanged(_stateService.CurrentState);
    }

    private async Task ConfigureLoadOrderAsync()
    {
        var path = await _fileDialog.OpenFileDialogAsync(
            "Select Load Order File",
            "Text Files (*.txt)|*.txt|All Files (*.*)|*.*");
            
        if (!string.IsNullOrEmpty(path))
        {
            _stateService.UpdateConfigurationPaths(path, MO2Path, XEditPath);
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

    private void OnStateChanged(AppState state)
    {
        LoadOrderPath = state.LoadOrderPath;
        XEditPath = state.XEditExecutablePath;
        MO2Path = state.MO2ExecutablePath;
        MO2ModeEnabled = state.MO2ModeEnabled;
        PartialFormsEnabled = state.PartialFormsEnabled;
        
        // Plugins list update - optimized to avoid recreation if possible
        // For now simple clear and add
        if (state.PluginsToClean != null && 
           (PluginsToClean.Count != state.PluginsToClean.Count || 
            !PluginsToClean.Select(p => p.FileName).SequenceEqual(state.PluginsToClean)))
        {
            PluginsToClean.Clear();
            foreach (var p in state.PluginsToClean)
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
}
