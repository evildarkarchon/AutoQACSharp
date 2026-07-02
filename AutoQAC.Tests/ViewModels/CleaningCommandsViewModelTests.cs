using System.Reactive.Linq;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using AutoQAC.Services.UI.Interactions;
using AutoQAC.ViewModels.MainWindow;
using FluentAssertions;
using NSubstitute;

namespace AutoQAC.Tests.ViewModels;

/// <summary>
/// Verifies the command ViewModel integration points that protect sequential cleaning from background refresh work.
/// </summary>
public sealed class CleaningCommandsViewModelTests
{
    /// <summary>
    /// Ensures approximation refresh cancellation happens before progress display and before xEdit cleaning starts.
    /// </summary>
    [Fact]
    public async Task StartCommand_ShouldCancelActiveRefreshBeforeProgressAndSessionStart()
    {
        var tempXEditPath = Path.Combine(Path.GetTempPath(), $"AutoQAC-{Guid.NewGuid():N}.exe");
        await File.WriteAllTextAsync(tempXEditPath, string.Empty);

        var stateService = new StateService();
        var cleaningSession = Substitute.For<ICleaningSession>();
        var pluginLoadingService = Substitute.For<IPluginLoadingService>();
        var coordinator = Substitute.For<IPluginRefreshCoordinator>();
        coordinator.StatusChanged.Returns(Observable.Never<PluginRefreshStatus>());
        var callOrder = new List<string>();

        cleaningSession.StartAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callOrder.Add("session");
                return Task.CompletedTask;
            });
        coordinator.When(c => c.CancelActiveRefresh(PluginRefreshCancelReason.CleaningStarted))
            .Do(_ => callOrder.Add("cancel"));

        var progressInteraction = new Interaction<ICleaningSession, Unit>();
        using var progressRegistration = progressInteraction.RegisterHandler(_ =>
        {
            callOrder.Add("progress");
            return Task.FromResult(Unit.Default);
        });

        var viewModel = new CleaningCommandsViewModel(
            stateService,
            cleaningSession,
            Substitute.For<IConfigurationService>(),
            pluginLoadingService,
            coordinator,
            Substitute.For<ILoggingService>(),
            Substitute.For<IMessageDialogService>(),
            Substitute.For<IAppLifetime>(),
            progressInteraction,
            new Interaction<List<DryRunResult>, Unit>(),
            new Interaction<Unit, bool>(),
            new Interaction<Unit, bool>(),
            new Interaction<Unit, Unit>(),
            new Interaction<Unit, Unit>());

        try
        {
            stateService.UpdateConfigurationPaths(null, null, tempXEditPath);
            stateService.SetPluginsToClean([
                new PluginInfo { FileName = "NeedsCleaning.esp", FullPath = @"C:\Game\Data\NeedsCleaning.esp" }
            ]);
            viewModel.OnStateChanged(stateService.CurrentState);

            await viewModel.StartCleaningCommand.ExecuteAsync(null);

            callOrder.Should().Equal("cancel", "progress", "session");
            coordinator.Received(1).CancelActiveRefresh(PluginRefreshCancelReason.CleaningStarted);
        }
        finally
        {
            viewModel.Dispose();
            stateService.Dispose();
            File.Delete(tempXEditPath);
        }
    }

    [Fact]
    public void ExitCommand_ShouldRequestApplicationShutdown()
    {
        var appLifetime = Substitute.For<IAppLifetime>();
        var viewModel = new CleaningCommandsViewModel(
            Substitute.For<IStateService>(),
            Substitute.For<ICleaningSession>(),
            Substitute.For<IConfigurationService>(),
            Substitute.For<IPluginLoadingService>(),
            Substitute.For<IPluginRefreshCoordinator>(),
            Substitute.For<ILoggingService>(),
            Substitute.For<IMessageDialogService>(),
            appLifetime,
            new Interaction<ICleaningSession, Unit>(),
            new Interaction<List<DryRunResult>, Unit>(),
            new Interaction<Unit, bool>(),
            new Interaction<Unit, bool>(),
            new Interaction<Unit, Unit>(),
            new Interaction<Unit, Unit>());

        viewModel.ExitCommand.Execute(null);

        appLifetime.Received(1).Shutdown();
    }
}
