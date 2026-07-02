using System;
using System.Collections.Generic;
using System.IO;
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
using AutoQAC.Views.Helpers;
using Microsoft.UI.Xaml;

namespace AutoQAC.Views;

public sealed partial class MainWindow : Window
{
    private readonly List<IDisposable> _interactionRegistrations = [];
    private ILoggingService? _logger;
    private IFileDialogService? _fileDialog;
    private IConfigurationService? _configService;
    private IStateService? _stateService;
    private ICleaningSession? _cleaningSession;
    private IBackupService? _backupService;
    private IMessageDialogService? _messageDialog;
    private IUiDispatcher? _uiDispatcher;
    private IUiFrameworkVersionProvider? _uiFrameworkVersionProvider;
    private IWindowContextProvider? _windowContextProvider;

    public MainWindow()
    {
        InitializeComponent();
        SetWindowIcon();
        WindowSizing.Resize(this, 900, 600);
    }

    public MainWindow(
        MainWindowViewModel viewModel,
        ILoggingService logger,
        IFileDialogService fileDialog,
        IConfigurationService configService,
        IStateService stateService,
        ICleaningSession cleaningSession,
        IBackupService backupService,
        IMessageDialogService messageDialog,
        IUiDispatcher uiDispatcher,
        IUiFrameworkVersionProvider uiFrameworkVersionProvider,
        IWindowContextProvider windowContextProvider) : this()
    {
        _windowContextProvider = windowContextProvider;
        _logger = logger;
        _fileDialog = fileDialog;
        _configService = configService;
        _stateService = stateService;
        _cleaningSession = cleaningSession;
        _backupService = backupService;
        _messageDialog = messageDialog;
        _uiDispatcher = uiDispatcher;
        _uiFrameworkVersionProvider = uiFrameworkVersionProvider;

        Root.DataContext = viewModel;
        RegisterWindowContext();
        Root.Loaded += OnRootLoaded;
        Closed += OnClosed;

        _interactionRegistrations.Add(
            viewModel.ShowCleaningResultsInteraction.RegisterHandler(ShowCleaningResultsAsync));
        _interactionRegistrations.Add(viewModel.ShowSettingsInteraction.RegisterHandler(ShowSettingsAsync));
        _interactionRegistrations.Add(viewModel.ShowSkipListInteraction.RegisterHandler(ShowSkipListAsync));
        _interactionRegistrations.Add(viewModel.ShowProgressInteraction.RegisterHandler(ShowProgressAsync));
        _interactionRegistrations.Add(viewModel.ShowPreviewInteraction.RegisterHandler(ShowPreviewAsync));
        _interactionRegistrations.Add(viewModel.ShowRestoreInteraction.RegisterHandler(ShowRestoreAsync));
        _interactionRegistrations.Add(viewModel.ShowAboutInteraction.RegisterHandler(ShowAboutAsync));
    }

    private void OnRootLoaded(object sender, RoutedEventArgs e) => RegisterWindowContext();

    private void OnClosed(object sender, WindowEventArgs e)
    {
        foreach (var registration in _interactionRegistrations)
        {
            registration.Dispose();
        }

        _interactionRegistrations.Clear();
        Root.Loaded -= OnRootLoaded;
        Closed -= OnClosed;
    }

    private void RegisterWindowContext()
    {
        var xamlRoot = Root.XamlRoot;
        if (_windowContextProvider is null || xamlRoot is null)
        {
            return;
        }

        _windowContextProvider.SetContext(AppWindow.Id, xamlRoot);
    }

    private void SetWindowIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AutoQAC.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }
    }

    private async Task<Unit> ShowCleaningResultsAsync(CleaningSessionResult input)
    {
        if (_logger is null || _fileDialog is null)
        {
            return Unit.Default;
        }

        var resultsViewModel = new CleaningResultsViewModel(input, _logger, _fileDialog);
        var resultsWindow = new CleaningResultsWindow(resultsViewModel);
        await ShowWindowAndWaitAsync(resultsWindow);
        return Unit.Default;
    }

    private async Task<bool> ShowSettingsAsync(Unit input)
    {
        if (_logger is null || _configService is null || _uiDispatcher is null || _windowContextProvider is null)
        {
            return false;
        }

        var settingsViewModel = new SettingsViewModel(_configService, _logger, _uiDispatcher, _fileDialog);
        await settingsViewModel.LoadSettingsAsync();

        try
        {
            var settingsWindow = new SettingsWindow(_windowContextProvider, settingsViewModel);
            return await settingsWindow.ShowAsync();
        }
        finally
        {
            settingsViewModel.Dispose();
        }
    }

    private async Task<bool> ShowSkipListAsync(Unit input)
    {
        if (_logger is null || _configService is null || _stateService is null || _windowContextProvider is null)
        {
            return false;
        }

        var skipListViewModel = new SkipListViewModel(_configService, _stateService, _logger);
        await skipListViewModel.LoadSkipListAsync();

        try
        {
            var skipListWindow = new SkipListWindow(_windowContextProvider, skipListViewModel);
            return await skipListWindow.ShowAsync();
        }
        finally
        {
            skipListViewModel.Dispose();
        }
    }

    private Task<Unit> ShowProgressAsync(ICleaningSession input)
    {
        if (_stateService is null || _messageDialog is null || _logger is null ||
            _uiDispatcher is null)
        {
            return Task.FromResult(Unit.Default);
        }

        var progressViewModel =
            new ProgressViewModel(_stateService, input, _messageDialog, _logger, _uiDispatcher);
        var progressWindow = new ProgressWindow(progressViewModel);

        // Defense in depth: ProgressWindow subscribes to CloseRequested and disposes
        // its ViewModel. The local guard keeps this path safe if that contract changes.
        var progressDisposed = false;

        progressViewModel.CloseRequested += (_, _) => progressWindow.Close();
        progressWindow.Closed += (_, _) => DisposeProgressViewModel();
        progressWindow.Activate();

        return Task.FromResult(Unit.Default);

        void DisposeProgressViewModel()
        {
            if (progressDisposed)
            {
                return;
            }

            progressDisposed = true;
            progressViewModel.Dispose();
        }
    }

    private Task<Unit> ShowPreviewAsync(List<DryRunResult> input)
    {
        if (_stateService is null || _cleaningSession is null || _messageDialog is null || _logger is null ||
            _uiDispatcher is null)
        {
            return Task.FromResult(Unit.Default);
        }

        var progressViewModel =
            new ProgressViewModel(_stateService, _cleaningSession, _messageDialog, _logger, _uiDispatcher);
        progressViewModel.LoadDryRunResults(input);

        var progressWindow = new ProgressWindow(progressViewModel)
        {
            Title = "Dry-Run Preview"
        };

        progressViewModel.CloseRequested += (_, _) => progressWindow.Close();
        progressWindow.Activate();

        return Task.FromResult(Unit.Default);
    }

    private async Task<Unit> ShowRestoreAsync(Unit input)
    {
        if (_backupService is null || _messageDialog is null || _logger is null || _uiDispatcher is null)
        {
            return Unit.Default;
        }

        var vm = Root.DataContext as MainWindowViewModel;
        var dataFolderPath = vm?.Configuration.GameDataFolder;

        var restoreViewModel = new RestoreViewModel(_backupService, _messageDialog, _logger, _uiDispatcher);
        await restoreViewModel.LoadSessionsAsync(dataFolderPath);

        try
        {
            var restoreWindow = new RestoreWindow(restoreViewModel);
            await ShowWindowAndWaitAsync(restoreWindow);
        }
        finally
        {
            restoreViewModel.Dispose();
        }

        return Unit.Default;
    }

    private async Task<Unit> ShowAboutAsync(Unit input)
    {
        if (_uiFrameworkVersionProvider is null || _windowContextProvider is null)
        {
            return Unit.Default;
        }

        var aboutViewModel = new AboutViewModel(_uiFrameworkVersionProvider);
        var aboutWindow = new AboutWindow(_windowContextProvider, aboutViewModel);
        await aboutWindow.ShowAsync();
        return Unit.Default;
    }

    private static Task ShowWindowAndWaitAsync(Window window)
    {
        var completion = new TaskCompletionSource();
        window.Closed += OnClosed;
        window.Activate();
        return completion.Task;

        void OnClosed(object sender, WindowEventArgs args)
        {
            window.Closed -= OnClosed;
            completion.TrySetResult();
        }
    }
}
