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
        IUiFrameworkVersionProvider uiFrameworkVersionProvider) : this()
    {
        Root.DataContext = viewModel;

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

    private void SetWindowIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AutoQAC.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }
    }
}
