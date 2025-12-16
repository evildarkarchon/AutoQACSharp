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
            services.AddUIServices();
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
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}