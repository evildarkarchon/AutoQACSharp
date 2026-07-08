using AutoQAC.Infrastructure.Logging;
using AutoQAC.Services.Backup;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameCapability;
using AutoQAC.Services.GameDetection;
using AutoQAC.Services.MO2;
using AutoQAC.Services.Monitoring;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.Process;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using AutoQAC.ViewModels;
using AutoQAC.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;

namespace AutoQAC.Infrastructure;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddInfrastructure()
        {
            services.AddSingleton<ILoggingService, LoggingService>();
            return services;
        }

        public IServiceCollection AddConfiguration()
        {
            // Persistence coordinator + file store registered first; ConfigurationService and ConfigWatcherService both depend on the coordinator (Phase 10 D-08, D-15).
            services.AddSingleton<IUserConfigFileStore, UserConfigFileStore>();
            services.AddSingleton<ConfigPersistenceCoordinator>();
            services.AddSingleton<IConfigPersistenceCoordinator>(sp =>
                sp.GetRequiredService<ConfigPersistenceCoordinator>());
            services.AddSingleton<IConfigurationService>(sp => new ConfigurationService(
                sp.GetRequiredService<IConfigPersistenceCoordinator>(),
                sp.GetRequiredService<ILoggingService>()));
            services.AddSingleton<IConfigWatcherService, ConfigWatcherService>();
            services.AddSingleton<ILegacyMigrationService, LegacyMigrationService>();
            services.AddSingleton<ILogRetentionService, LogRetentionService>();
            return services;
        }

        public IServiceCollection AddState()
        {
            services.AddSingleton<IStateService, StateService>();
            return services;
        }

        public IServiceCollection AddBusinessLogic()
        {
            services.AddSingleton<IGameDetectionService, GameDetectionService>();
            services.AddSingleton<IPluginValidationService, PluginValidationService>();
            services.AddSingleton<IPluginLoadingService, PluginLoadingService>();
            services.AddSingleton<IPluginIssueApproximationService>(sp => new PluginIssueApproximationService(
                sp.GetRequiredService<ILoggingService>()));
            services.AddSingleton<ISkipListPolicy, SkipListPolicy>();
            services.AddSingleton<IPluginRefreshDiscoveryPlanner, PluginRefreshDiscoveryPlanner>();
            services.AddSingleton(sp => new PluginRefreshAppStateMirror(
                sp.GetRequiredService<IStateService>()));
            services.AddSingleton(_ => new PluginRefreshCommandAvailabilityPolicy());
            services.AddSingleton<PluginRefreshPublicationStore>(sp =>
            {
                var state = sp.GetRequiredService<IStateService>().CurrentState;
                var configuration = PluginRefreshAppStateMirror.CreateConfigurationProjection(state);
                var initialAffordance = sp.GetRequiredService<IPluginRefreshDiscoveryPlanner>()
                    .GetAffordance(state.CurrentGameType, configuration.Mo2ModeEnabled);
                return new PluginRefreshPublicationStore(
                    sp.GetRequiredService<PluginRefreshAppStateMirror>(),
                    sp.GetRequiredService<PluginRefreshCommandAvailabilityPolicy>(),
                    initialAffordance);
            });
            services.AddSingleton<IPluginRefreshModule>(sp => new PluginRefreshModule(
                sp.GetRequiredService<IPluginRefreshDiscoveryPlanner>(),
                sp.GetRequiredService<IPluginIssueApproximationService>(),
                sp.GetRequiredService<IStateService>(),
                sp.GetRequiredService<ISkipListPolicy>(),
                sp.GetRequiredService<PluginRefreshPublicationStore>(),
                sp.GetRequiredService<ILoggingService>(),
                sp.GetRequiredService<IConfigurationService>()));
            services.AddSingleton<IDiscoverySettingsModule, DiscoverySettingsModule>();
            services.AddSingleton<IPidStorePathProvider, DefaultPidStorePathProvider>();
            services.AddSingleton<IProcessSessionIdProvider, ProcessSessionIdProvider>();
            services.AddSingleton<IPidStore, JsonPidStore>();
            services.AddSingleton<IProcessExitWaiter, ProcessExitWaiter>();
            services.AddSingleton<IProcessExecutionService, ProcessExecutionService>();
            services.AddSingleton<IMo2ValidationService, Mo2ValidationService>();
            services.AddSingleton<IMo2InstanceService, Mo2InstanceService>();
            services.AddSingleton<IXEditCommandBuilder, XEditCommandBuilder>();
            services.AddSingleton<IXEditOutputParser, XEditOutputParser>();
            services.AddSingleton<IXEditLogFileService, XEditLogFileService>();
            services.AddSingleton<ICleaningService, CleaningService>();
            services.AddSingleton<ICleaningCommandReadiness>(sp => new CleaningCommandReadiness(
                sp.GetRequiredService<IPluginRefreshModule>(),
                sp.GetRequiredService<IStateService>()));
            services.AddSingleton<IBackupFileCopier, BackupFileCopier>();
            services.AddSingleton<IBackupSessionDeleter, DirectoryBackupSessionDeleter>();
            services.AddSingleton<IBackupService, BackupService>();
            services.AddSingleton<IHangDetectionService, HangDetectionService>();
            services.AddSingleton<ICleaningPreflight>(sp => new CleaningPreflight(
                sp.GetRequiredService<IConfigurationService>(),
                sp.GetRequiredService<IPluginValidationService>(),
                sp.GetRequiredService<IPluginRefreshModule>(),
                sp.GetRequiredService<IMo2ValidationService>(),
                sp.GetRequiredService<IStateService>(),
                sp.GetRequiredService<ILoggingService>()));
            services.AddSingleton<IBackupSessionCoordinator, BackupSessionCoordinator>();
            services.AddSingleton<ICleaningTerminationCoordinator, CleaningTerminationCoordinator>();
            services.AddSingleton<IPluginCleaningRunner, PluginCleaningRunner>();
            services.AddSingleton<IPluginResultFinalizer, PluginResultFinalizer>();
            services.AddSingleton<IPluginCleaning, PluginCleaning>();
            services.AddSingleton<ICleaningSessionStatePublisher, StateServiceCleaningSessionStatePublisher>();
            services.AddSingleton<ICleaningSessionDecisionAdapter, CleaningSessionDialogDecisionAdapter>();
            services.AddSingleton<ICleaningSession, CleaningSession>();
            return services;
        }

        public IServiceCollection AddUiServices(DispatcherQueue? dispatcherQueue = null)
        {
            services.AddSingleton<IAppLifetime, WinAppLifetime>();
            services.AddSingleton<IUiFrameworkVersionProvider, WinUiFrameworkVersionProvider>();
            services.AddSingleton<IUiDispatcher>(_ => new WinUiDispatcher(dispatcherQueue));
            services.AddSingleton<IWindowContextProvider, WindowContextProvider>();
            services.AddSingleton<FileDialogService>();
            services.AddSingleton<IFileDialogService>(sp => sp.GetRequiredService<FileDialogService>());
            services.AddSingleton<MessageDialogService>();
            services.AddSingleton<IMessageDialogService>(sp => sp.GetRequiredService<MessageDialogService>());
            return services;
        }

        public IServiceCollection AddViewModels()
        {
            services.AddSingleton<MainWindowViewModel>();
            services.AddTransient<ProgressViewModel>();
            services.AddTransient<PartialFormsWarningViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<RestoreViewModel>();
            services.AddTransient<MessageDialogViewModel>();
            return services;
        }

        public IServiceCollection AddViews()
        {
            services.AddTransient<MainWindow>();
            services.AddTransient<ProgressWindow>();
            services.AddTransient<PartialFormsWarningDialog>();
            services.AddTransient<SettingsWindow>();
            services.AddTransient<RestoreWindow>();
            return services;
        }
    }
}
