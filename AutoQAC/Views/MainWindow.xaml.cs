using System;
using System.IO;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Services.Backup;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using AutoQAC.ViewModels;
using Microsoft.UI.Xaml;

namespace AutoQAC.Views;

public sealed partial class MainWindow : Window
{
    private IWindowContextProvider? _windowContextProvider;

    public MainWindow()
    {
        InitializeComponent();
        SetWindowIcon();
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
        IUiDispatcher uiDispatcher,
        IUiFrameworkVersionProvider uiFrameworkVersionProvider,
        IWindowContextProvider windowContextProvider) : this()
    {
        _windowContextProvider = windowContextProvider;
        Root.DataContext = viewModel;
        RegisterWindowContext();
        Root.Loaded += OnRootLoaded;
        Closed += OnClosed;

        _ = logger;
        _ = fileDialog;
        _ = configService;
        _ = stateService;
        _ = orchestrator;
        _ = backupService;
        _ = messageDialog;
        _ = uiDispatcher;
        _ = uiFrameworkVersionProvider;
    }

    private void OnRootLoaded(object sender, RoutedEventArgs e) => RegisterWindowContext();

    private void OnClosed(object sender, WindowEventArgs e)
    {
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
}
