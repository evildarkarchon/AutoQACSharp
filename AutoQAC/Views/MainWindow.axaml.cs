using System.Reactive;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using AutoQAC.ViewModels;
using Avalonia.Controls;
using ReactiveUI;

namespace AutoQAC.Views;

public partial class MainWindow : Window
{
    private readonly ILoggingService? _logger;
    private readonly IFileDialogService? _fileDialog;
    private readonly IConfigurationService? _configService;
    private readonly IStateService? _stateService;
    private readonly ICleaningOrchestrator? _orchestrator;

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
        ICleaningOrchestrator orchestrator) : this()
    {
        DataContext = viewModel;
        _logger = logger;
        _fileDialog = fileDialog;
        _configService = configService;
        _stateService = stateService;
        _orchestrator = orchestrator;

        // Register the interaction handler for showing cleaning results
        viewModel.ShowCleaningResultsInteraction.RegisterHandler(ShowCleaningResultsAsync);

        // Register the interaction handler for showing settings window
        viewModel.ShowSettingsInteraction.RegisterHandler(ShowSettingsAsync);

        // Register the interaction handler for showing skip list window
        viewModel.ShowSkipListInteraction.RegisterHandler(ShowSkipListAsync);

        // Register the interaction handler for showing progress window
        viewModel.ShowProgressInteraction.RegisterHandler(ShowProgressAsync);
    }

    private async Task ShowCleaningResultsAsync(IInteractionContext<CleaningSessionResult, Unit> context)
    {
        if (_logger == null || _fileDialog == null)
        {
            context.SetOutput(Unit.Default);
            return;
        }

        var resultsViewModel = new CleaningResultsViewModel(
            context.Input,
            _logger,
            _fileDialog);

        var resultsWindow = new CleaningResultsWindow(resultsViewModel)
        {
            // Ensure the window is shown relative to this window
        };

        await resultsWindow.ShowDialog(this);
        context.SetOutput(Unit.Default);
    }

    private async Task ShowSettingsAsync(IInteractionContext<Unit, bool> context)
    {
        if (_logger == null || _configService == null)
        {
            context.SetOutput(false);
            return;
        }

        var settingsViewModel = new SettingsViewModel(_configService, _logger);

        // Load current settings before showing
        await settingsViewModel.LoadSettingsAsync();

        var settingsWindow = new SettingsWindow(settingsViewModel);

        var result = await settingsWindow.ShowDialog<bool?>(this);

        // Dispose the ViewModel after dialog closes
        settingsViewModel.Dispose();

        context.SetOutput(result ?? false);
    }

    private async Task ShowSkipListAsync(IInteractionContext<Unit, bool> context)
    {
        if (_logger == null || _configService == null || _stateService == null)
        {
            context.SetOutput(false);
            return;
        }

        var skipListViewModel = new SkipListViewModel(_configService, _stateService, _logger);

        // Load current skip list before showing
        await skipListViewModel.LoadSkipListAsync();

        var skipListWindow = new SkipListWindow(skipListViewModel);

        var result = await skipListWindow.ShowDialog<bool?>(this);

        // Dispose the ViewModel after dialog closes
        skipListViewModel.Dispose();

        context.SetOutput(result ?? false);
    }

    private Task ShowProgressAsync(IInteractionContext<Unit, Unit> context)
    {
        if (_stateService == null || _orchestrator == null)
        {
            context.SetOutput(Unit.Default);
            return Task.CompletedTask;
        }

        var progressViewModel = new ProgressViewModel(_stateService, _orchestrator);
        var progressWindow = new ProgressWindow
        {
            DataContext = progressViewModel
        };

        // Show non-modal - the window stays open and user can close it when done
        progressWindow.Show(this);

        context.SetOutput(Unit.Default);
        return Task.CompletedTask;
    }
}