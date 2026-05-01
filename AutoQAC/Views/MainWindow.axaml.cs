using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Backup;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using AutoQAC.Services.UI.Interactions;
using AutoQAC.ViewModels;
using Avalonia.Controls;

namespace AutoQAC.Views;

public partial class MainWindow : Window
{
    private readonly ILoggingService? _logger;
    private readonly IFileDialogService? _fileDialog;
    private readonly IConfigurationService? _configService;
    private readonly IStateService? _stateService;
    private readonly ICleaningOrchestrator? _orchestrator;
    private readonly IBackupService? _backupService;
    private readonly IMessageDialogService? _messageDialog;
    private readonly IUiDispatcher? _uiDispatcher;

    private readonly List<IDisposable> _interactionRegistrations = new();

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(
        MainWindowViewModel viewModel,
        ILoggingService logger,
        IFileDialogService fileDialog,
        IConfigurationService configService,
        IStateService stateService,
        ICleaningOrchestrator orchestrator,
        IBackupService backupService,
        IMessageDialogService messageDialog,
        IUiDispatcher uiDispatcher) : this()
    {
        DataContext = viewModel;
        _logger = logger;
        _fileDialog = fileDialog;
        _configService = configService;
        _stateService = stateService;
        _orchestrator = orchestrator;
        _backupService = backupService;
        _messageDialog = messageDialog;
        _uiDispatcher = uiDispatcher;

        _interactionRegistrations.Add(viewModel.ShowCleaningResultsInteraction.RegisterHandler(ShowCleaningResultsAsync));
        _interactionRegistrations.Add(viewModel.ShowSettingsInteraction.RegisterHandler(ShowSettingsAsync));
        _interactionRegistrations.Add(viewModel.ShowSkipListInteraction.RegisterHandler(ShowSkipListAsync));
        _interactionRegistrations.Add(viewModel.ShowProgressInteraction.RegisterHandler(ShowProgressAsync));
        _interactionRegistrations.Add(viewModel.ShowPreviewInteraction.RegisterHandler(ShowPreviewAsync));
        _interactionRegistrations.Add(viewModel.ShowRestoreInteraction.RegisterHandler(ShowRestoreAsync));
        _interactionRegistrations.Add(viewModel.ShowAboutInteraction.RegisterHandler(ShowAboutAsync));
    }

    protected override void OnClosed(EventArgs e)
    {
        foreach (var registration in _interactionRegistrations)
        {
            registration.Dispose();
        }
        _interactionRegistrations.Clear();
        base.OnClosed(e);
    }

    private async Task<Unit> ShowCleaningResultsAsync(CleaningSessionResult input)
    {
        if (_logger == null || _fileDialog == null)
        {
            return Unit.Default;
        }

        var resultsViewModel = new CleaningResultsViewModel(input, _logger, _fileDialog);
        var resultsWindow = new CleaningResultsWindow(resultsViewModel);

        await resultsWindow.ShowDialog(this);
        return Unit.Default;
    }

    private async Task<bool> ShowSettingsAsync(Unit input)
    {
        if (_logger == null || _configService == null || _uiDispatcher == null)
        {
            return false;
        }

        var settingsViewModel = new SettingsViewModel(_configService, _logger, _uiDispatcher, _fileDialog);

        await settingsViewModel.LoadSettingsAsync();

        var settingsWindow = new SettingsWindow(settingsViewModel);

        var result = await settingsWindow.ShowDialog<bool?>(this);

        settingsViewModel.Dispose();

        return result ?? false;
    }

    private async Task<bool> ShowSkipListAsync(Unit input)
    {
        if (_logger == null || _configService == null || _stateService == null)
        {
            return false;
        }

        var skipListViewModel = new SkipListViewModel(_configService, _stateService, _logger);

        await skipListViewModel.LoadSkipListAsync();

        var skipListWindow = new SkipListWindow(skipListViewModel);

        var result = await skipListWindow.ShowDialog<bool?>(this);

        skipListViewModel.Dispose();

        return result ?? false;
    }

    private Task<Unit> ShowProgressAsync(Unit input)
    {
        if (_stateService == null || _orchestrator == null || _messageDialog == null || _uiDispatcher == null)
        {
            return Task.FromResult(Unit.Default);
        }

        var progressViewModel = new ProgressViewModel(_stateService, _orchestrator, _messageDialog, _uiDispatcher);
        var progressWindow = new ProgressWindow
        {
            DataContext = progressViewModel
        };

        // Defense in depth: ProgressWindow.OnDataContextChanged also subscribes to CloseRequested
        // and ProgressWindow.OnClosed disposes the ViewModel via DisposeViewModelIfNeeded. The
        // local guard below keeps disposal idempotent so the normal ShowProgressAsync path stays
        // safe even if the ProgressWindow contract changes.
        var progressDisposed = false;
        void DisposeProgressViewModel()
        {
            if (progressDisposed)
            {
                return;
            }
            progressDisposed = true;
            progressViewModel.Dispose();
        }

        progressViewModel.CloseRequested += (_, _) => progressWindow.Close();
        progressWindow.Closed += (_, _) => DisposeProgressViewModel();

        progressWindow.Show(this);

        return Task.FromResult(Unit.Default);
    }

    private Task<Unit> ShowPreviewAsync(List<DryRunResult> input)
    {
        if (_stateService == null || _orchestrator == null || _messageDialog == null || _uiDispatcher == null)
        {
            return Task.FromResult(Unit.Default);
        }

        var progressViewModel = new ProgressViewModel(_stateService, _orchestrator, _messageDialog, _uiDispatcher);
        progressViewModel.LoadDryRunResults(input);

        var progressWindow = new ProgressWindow
        {
            DataContext = progressViewModel,
            Title = "Dry-Run Preview"
        };

        progressViewModel.CloseRequested += (_, _) => progressWindow.Close();

        progressWindow.Show(this);

        return Task.FromResult(Unit.Default);
    }

    private async Task<Unit> ShowRestoreAsync(Unit input)
    {
        if (_backupService == null || _messageDialog == null || _logger == null || _uiDispatcher == null)
        {
            return Unit.Default;
        }

        var vm = DataContext as MainWindowViewModel;
        var dataFolderPath = vm?.Configuration.GameDataFolder;

        var restoreViewModel = new RestoreViewModel(_backupService, _messageDialog, _logger, _uiDispatcher);

        await restoreViewModel.LoadSessionsAsync(dataFolderPath);

        var restoreWindow = new RestoreWindow(restoreViewModel);

        await restoreWindow.ShowDialog(this);

        restoreViewModel.Dispose();

        return Unit.Default;
    }

    private async Task<Unit> ShowAboutAsync(Unit input)
    {
        var aboutViewModel = new AboutViewModel();
        var aboutWindow = new AboutWindow(aboutViewModel);

        await aboutWindow.ShowDialog(this);

        return Unit.Default;
    }
}
