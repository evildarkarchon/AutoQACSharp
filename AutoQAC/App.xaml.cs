using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AutoQAC.Infrastructure;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models.Diagnostics;
using AutoQAC.Services.Backup;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using AutoQAC.ViewModels;
using AutoQAC.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Serilog;
using WinRT.Interop;

namespace AutoQAC;

public sealed partial class App
{
    private MainWindow? _mainWindow;

    public IServiceProvider? Services { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var services = new ServiceCollection();
        var dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        services.AddInfrastructure();
        services.AddConfiguration();
        services.AddState();
        services.AddBusinessLogic();
        services.AddUiServices(dispatcherQueue);
        services.AddViewModels();
        services.AddViews();

        Services = services.BuildServiceProvider();

        var viewModel = Services.GetRequiredService<MainWindowViewModel>();
        var logger = Services.GetRequiredService<ILoggingService>();
        var fileDialog = Services.GetRequiredService<IFileDialogService>();
        var configService = Services.GetRequiredService<IConfigurationService>();
        var stateService = Services.GetRequiredService<IStateService>();
        var orchestrator = Services.GetRequiredService<ICleaningOrchestrator>();
        var backupService = Services.GetRequiredService<IBackupService>();
        var messageDialog = Services.GetRequiredService<IMessageDialogService>();
        var uiDispatcher = Services.GetRequiredService<IUiDispatcher>();
        var uiFrameworkVersionProvider = Services.GetRequiredService<IUiFrameworkVersionProvider>();
        var windowContextProvider = Services.GetRequiredService<IWindowContextProvider>();

        _mainWindow = new MainWindow(viewModel, logger, fileDialog, configService, stateService,
            orchestrator, backupService, messageDialog, uiDispatcher, uiFrameworkVersionProvider,
            windowContextProvider);

        LogStartupInfo(logger, stateService);

        var configWatcher = Services.GetRequiredService<IConfigWatcherService>();
        configWatcher.StartWatching();

        var migrationService = Services.GetRequiredService<ILegacyMigrationService>();
        _ = RunMigrationAsync(migrationService, viewModel, logger);

        var logRetention = Services.GetRequiredService<ILogRetentionService>();
        _ = RunLogRetentionAsync(logRetention, logger);

        _mainWindow.Closed += (_, _) => Shutdown(configWatcher, logger);
        _mainWindow.Activate();
    }

    internal static void ActivateMainWindow()
    {
        if (Current is not App { _mainWindow: { } mainWindow })
        {
            return;
        }

        mainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            mainWindow.Activate();
            var hWnd = WindowNative.GetWindowHandle(mainWindow);
            if (hWnd != IntPtr.Zero)
            {
                SetForegroundWindow(hWnd);
            }
        });
    }

    private void Shutdown(IConfigWatcherService configWatcher, ILoggingService logger)
    {
        try
        {
            configWatcher.Dispose();
            if (Services is IDisposable disposableServices)
            {
                disposableServices.Dispose();
            }
        }
        catch (Exception ex)
        {
            logger.Warning("[Shutdown] Cleanup failed during shutdown: {Message}", ex.Message);
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void LogStartupInfo(ILoggingService logger, IStateService stateService)
    {
        try
        {
            var assembly = Assembly.GetEntryAssembly();
            var version = assembly?.GetName().Version;
            var versionStr = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "Unknown";

            var state = stateService.CurrentState;
            var xEditIdentifier = DiagnosticTextFormatter.SafeFileIdentifier("xEdit Path", state.XEditExecutablePath, "not configured");
            var xEditConfigured = !string.IsNullOrWhiteSpace(state.XEditExecutablePath);

            logger.Information("=== AutoQAC Session Start ===");
            logger.Information("Version: {Version}", versionStr);
            logger.Information(".NET Runtime: {Runtime}", RuntimeInformation.FrameworkDescription);
            logger.Information(
                "xEdit configuration: configured={XEditConfigured}, identifier={XEditIdentifier}",
                xEditConfigured,
                xEditIdentifier);
            logger.Information("Game Type: {GameType}", state.CurrentGameType);
            logger.Information("MO2 Mode: {Mo2Mode}", state.Mo2ModeEnabled);
            logger.Information("Load Order: {PluginCount} plugins", state.PluginsToClean.Count);
        }
        catch (Exception ex)
        {
            logger.Warning("[Startup] Failed to log startup info: {Message}", ex.Message);
        }
    }

    private static async Task RunLogRetentionAsync(
        ILogRetentionService logRetention,
        ILoggingService logger)
    {
        try
        {
            await logRetention.CleanupAsync();
        }
        catch (Exception ex)
        {
            logger.Warning("[LogRetention] Failed to clean up old log files on startup: {Message}", ex.Message);
        }
    }

    private static async Task RunMigrationAsync(
        ILegacyMigrationService migrationService,
        MainWindowViewModel viewModel,
        ILoggingService logger)
    {
        try
        {
            var result = await migrationService.MigrateIfNeededAsync();
            if (result is { Attempted: true, Success: false, WarningMessage: not null })
            {
                var warningMessage = DiagnosticTextFormatter.SafeFailureSummary(result.WarningMessage,
                    "Some legacy settings could not be migrated. See the latest AutoQAC log for technical details.");
                viewModel.ShowMigrationWarning(warningMessage);
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "[Migration] Unexpected error during legacy migration");
            viewModel.ShowMigrationWarning("Some legacy settings could not be migrated. See the latest AutoQAC log for technical details.");
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
