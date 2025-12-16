using AutoQAC.Infrastructure.Logging;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameDetection;
using AutoQAC.Services.MO2;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.Process;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using AutoQAC.ViewModels;
using AutoQAC.Views;
using Microsoft.Extensions.DependencyInjection;

namespace AutoQAC.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<ILoggingService, LoggingService>();
        return services;
    }

    public static IServiceCollection AddConfiguration(this IServiceCollection services)
    {
        services.AddSingleton<IConfigurationService, ConfigurationService>();
        return services;
    }

    public static IServiceCollection AddState(this IServiceCollection services)
    {
        services.AddSingleton<IStateService, StateService>();
        return services;
    }

    public static IServiceCollection AddBusinessLogic(this IServiceCollection services)
    {
        services.AddSingleton<IGameDetectionService, GameDetectionService>();
        services.AddSingleton<IPluginValidationService, PluginValidationService>();
        services.AddSingleton<IPluginLoadingService, PluginLoadingService>();
        services.AddSingleton<IProcessExecutionService, ProcessExecutionService>();
        services.AddSingleton<IMO2ValidationService, MO2ValidationService>();
        services.AddSingleton<IXEditCommandBuilder, XEditCommandBuilder>();
        services.AddSingleton<IXEditOutputParser, XEditOutputParser>();
        services.AddSingleton<ICleaningService, CleaningService>();
        services.AddSingleton<ICleaningOrchestrator, CleaningOrchestrator>();
        return services;
    }

    public static IServiceCollection AddUIServices(this IServiceCollection services)
    {
        services.AddSingleton<IFileDialogService, FileDialogService>();
        return services;
    }

    public static IServiceCollection AddViewModels(this IServiceCollection services)
    {
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<ProgressViewModel>();
        services.AddTransient<PartialFormsWarningViewModel>();
        return services;
    }

    public static IServiceCollection AddViews(this IServiceCollection services)
    {
        services.AddTransient<MainWindow>();
        services.AddTransient<ProgressWindow>();
        services.AddTransient<PartialFormsWarningDialog>();
        return services;
    }
}
