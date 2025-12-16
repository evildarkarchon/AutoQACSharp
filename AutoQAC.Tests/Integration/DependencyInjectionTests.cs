using System;
using AutoQAC.Infrastructure;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameDetection;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.Process;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using AutoQAC.ViewModels;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoQAC.Tests.Integration;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void ServiceCollection_ShouldResolveAllServices()
    {
        // Arrange
        var services = new ServiceCollection();
        
        services.AddInfrastructure();
        services.AddConfiguration();
        services.AddState();
        services.AddBusinessLogic();
        services.AddUIServices();
        services.AddViewModels();
        services.AddViews();

        var provider = services.BuildServiceProvider();

        // Act & Assert - Verify key services resolve
        
        // Infrastructure
        provider.GetService<IConfigurationService>().Should().NotBeNull();
        provider.GetService<IStateService>().Should().NotBeNull();
        
        // Business Logic
        provider.GetService<IGameDetectionService>().Should().NotBeNull();
        provider.GetService<IPluginValidationService>().Should().NotBeNull();
        provider.GetService<IPluginLoadingService>().Should().NotBeNull();
        provider.GetService<IProcessExecutionService>().Should().NotBeNull();
        provider.GetService<IXEditCommandBuilder>().Should().NotBeNull();
        provider.GetService<IXEditOutputParser>().Should().NotBeNull();
        provider.GetService<ICleaningService>().Should().NotBeNull();
        provider.GetService<ICleaningOrchestrator>().Should().NotBeNull();
        
        // UI Services
        provider.GetService<IFileDialogService>().Should().NotBeNull();
        
        // ViewModels
        provider.GetService<MainWindowViewModel>().Should().NotBeNull();
        provider.GetService<ProgressViewModel>().Should().NotBeNull();
        
        // Verify Scopes (Singleton vs Transient)
        var state1 = provider.GetRequiredService<IStateService>();
        var state2 = provider.GetRequiredService<IStateService>();
        state1.Should().BeSameAs(state2); // Singleton
        
        var vm1 = provider.GetRequiredService<MainWindowViewModel>();
        var vm2 = provider.GetRequiredService<MainWindowViewModel>();
        vm1.Should().NotBeSameAs(vm2); // Transient
    }
}
