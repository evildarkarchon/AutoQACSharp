using AutoQAC.Infrastructure;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using AutoQAC.ViewModels;
using AutoQAC.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace AutoQAC
{
    public partial class App : Application
    {
        public IServiceProvider? Services { get; private set; }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            var services = new ServiceCollection();

            // Register services
            services.AddInfrastructure();
            services.AddConfiguration();
            services.AddState();
            services.AddBusinessLogic();
            services.AddUiServices();
            services.AddViewModels();
            services.AddViews();

            Services = services.BuildServiceProvider();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Create MainWindow with all required dependencies for interaction handlers
                var viewModel = Services.GetRequiredService<MainWindowViewModel>();
                var logger = Services.GetRequiredService<ILoggingService>();
                var fileDialog = Services.GetRequiredService<IFileDialogService>();
                var configService = Services.GetRequiredService<IConfigurationService>();
                var stateService = Services.GetRequiredService<IStateService>();
                var orchestrator = Services.GetRequiredService<ICleaningOrchestrator>();

                var mainWindow = new MainWindow(viewModel, logger, fileDialog, configService, stateService, orchestrator);
                desktop.MainWindow = mainWindow;

                // Start config file watcher for external YAML edits
                var configWatcher = Services.GetRequiredService<IConfigWatcherService>();
                configWatcher.StartWatching();

                // Run legacy migration on startup (fire-and-forget with error handling)
                var migrationService = Services.GetRequiredService<ILegacyMigrationService>();
                _ = RunMigrationAsync(migrationService, viewModel, logger);

                // Run log retention cleanup on startup (fire-and-forget with error handling)
                var logRetention = Services.GetRequiredService<ILogRetentionService>();
                _ = RunLogRetentionAsync(logRetention, logger);

                desktop.ShutdownRequested += (sender, args) =>
                {
                    // Stop config watcher
                    configWatcher.Dispose();

                    // Flush pending config saves before app exits (per user decision)
                    // Synchronous wait is acceptable during app shutdown
                    try
                    {
                        configService.FlushPendingSavesAsync().GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        // Log but don't prevent shutdown
                        logger.Warning("[Config] Failed to flush config on shutdown: {Message}", ex.Message);
                    }
                };
            }

            base.OnFrameworkInitializationCompleted();
        }

        private static async System.Threading.Tasks.Task RunLogRetentionAsync(
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

        private static async System.Threading.Tasks.Task RunMigrationAsync(
            ILegacyMigrationService migrationService,
            MainWindowViewModel viewModel,
            ILoggingService logger)
        {
            try
            {
                var result = await migrationService.MigrateIfNeededAsync();
                if (result.Attempted && !result.Success && result.WarningMessage != null)
                {
                    viewModel.ShowMigrationWarning(result.WarningMessage);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[Migration] Unexpected error during legacy migration");
                viewModel.ShowMigrationWarning($"Legacy config migration failed unexpectedly: {ex.Message}");
            }
        }
    }
}