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
using System.Reflection;

namespace AutoQAC.Tests.Integration;

public sealed class DependencyInjectionTests
{
    /// <summary>
    /// Guards private startup diagnostics and migration warning copy where dependency-injection seams are not available.
    /// </summary>
    [Fact]
    public void AppStartupSource_ShouldUseSafeDiagnosticFieldsAndMigrationWarningCopy()
    {
        // Arrange
        var appSourcePath = LocateRepositoryFile("AutoQAC", "App.xaml.cs");

        // Act
        var appSource = File.ReadAllText(appSourcePath);

        // Assert
        appSource.Should().NotContain("xEdit Path: {XEditPath}");
        appSource.Should().Contain("DiagnosticTextFormatter.SafeFileIdentifier(\"xEdit Path\"");
        appSource.Should()
            .Contain("Some legacy settings could not be migrated. See the latest AutoQAC log for technical details.");
        appSource.Should().Contain("DiagnosticTextFormatter.SafeFailureSummary(result.WarningMessage");
        appSource.Should().NotContain("Legacy config migration failed unexpectedly: {ex.Message}");
        appSource.Should().NotContain("ShowMigrationWarning($\"Legacy config migration failed unexpectedly");
        appSource.Should().NotContain("ShowMigrationWarning(result.WarningMessage)");
    }

    [Fact]
    public void ServiceCollection_ShouldResolveAllServices()
    {
        // Arrange
        var services = new ServiceCollection();

        services.AddInfrastructure();
        services.AddConfiguration();
        services.AddState();
        services.AddBusinessLogic();
        services.AddUiServices();
        services.AddViewModels();
        services.AddViews();

        var provider = services.BuildServiceProvider();

        // Act & Assert - Verify key services resolve

        // Infrastructure
        provider.GetService<IConfigurationService>().Should().NotBeNull();
        provider.GetService<IConfigWatcherService>().Should().NotBeNull();
        provider.GetService<IStateService>().Should().NotBeNull();

        // Business Logic
        provider.GetService<IGameDetectionService>().Should().NotBeNull();
        provider.GetService<IPluginValidationService>().Should().NotBeNull();
        provider.GetService<IPluginLoadingService>().Should().NotBeNull();
        provider.GetService<IPluginRefreshCoordinator>().Should().NotBeNull();
        provider.GetService<IPluginRefreshCapabilityPolicy>().Should().NotBeNull();
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
        vm1.Should().BeSameAs(vm2); // Singleton

        var sharedCoordinator = provider.GetRequiredService<IPluginRefreshCoordinator>();
        GetPrivateField<IPluginRefreshCoordinator>(vm1.Configuration, "_pluginRefreshCoordinator")
            .Should().BeSameAs(sharedCoordinator);
        GetPrivateField<IPluginRefreshCoordinator>(vm1.PluginList, "_pluginRefreshCoordinator")
            .Should().BeSameAs(sharedCoordinator);
        GetPrivateField<IPluginRefreshCoordinator>(vm1.Commands, "_pluginRefreshCoordinator")
            .Should().BeSameAs(sharedCoordinator);
    }

    /// <summary>
    /// Verifies production DI keeps the configuration facade and watcher on the same persistence coordinator.
    /// </summary>
    [Fact]
    public async Task AddConfiguration_ShouldWireConfigurationFacadeAndWatcherThroughSharedCoordinator()
    {
        // Arrange
        var services = new ServiceCollection();

        services.AddInfrastructure();
        services.AddConfiguration();
        services.AddState();
        services.AddBusinessLogic();
        services.AddUiServices();
        services.AddViewModels();
        services.AddViews();

        await using var provider = services.BuildServiceProvider();
        var configuration = provider.GetRequiredService<IConfigurationService>();
        var watcher = provider.GetRequiredService<IConfigWatcherService>();
        var sharedCoordinator = provider.GetRequiredService<IConfigPersistenceCoordinator>();
        var results = new List<ConfigPersistenceResult>();

        using var resultSubscription = configuration.PersistenceResults.Subscribe(results.Add);

        // Act
        var configurationCoordinator = GetPrivateField<IConfigPersistenceCoordinator>(configuration, "_coordinator");
        var watcherCoordinator = GetPrivateField<IConfigPersistenceCoordinator>(watcher, "_coordinator");
        sharedCoordinator.NotifySettingsFileChanged(ConfigFileSignalKind.Error);
        await configuration.FlushPendingSavesAsync(CancellationToken.None);

        // Assert
        configurationCoordinator.Should().BeSameAs(sharedCoordinator);
        watcherCoordinator.Should().BeSameAs(sharedCoordinator);
        results.Any(r =>
                r is
                {
                    Operation: ConfigPersistenceOperationKind.Watcher,
                    Status: ConfigPersistenceStatusKind.Failed,
                    Failure.Kind: ConfigPersistenceFailureKind.ReadFailed
                })
            .Should().BeTrue("watcher errors must flow through the configuration facade result stream");
    }

    /// <summary>
    /// Reads a private constructor-injected collaborator so DI integration tests can verify shared service wiring.
    /// </summary>
    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull($"{instance.GetType().Name} should store {fieldName} from DI");
        return field.GetValue(instance).Should().BeAssignableTo<T>().Subject;
    }

    /// <summary>
    /// Locates a repository-relative file from the test output directory without assuming a fixed bin depth.
    /// </summary>
    private static string LocateRepositoryFile(params string[] relativeSegments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(relativeSegments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(relativeSegments)}");
    }
}
