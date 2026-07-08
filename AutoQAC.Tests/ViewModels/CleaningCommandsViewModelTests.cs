using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using AutoQAC.Services.UI.Interactions;
using AutoQAC.Tests.TestInfrastructure;
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
        using var refreshModule = new RecordingPluginRefreshModule();
        refreshModule.CurrentPublication = RecordingPluginRefreshModule.CreateFreshPublication();
        var callOrder = new List<string>();

        cleaningSession.StartAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callOrder.Add("session");
                return Task.CompletedTask;
            });
        refreshModule.ExecuteHandler = (intent, _) =>
        {
            if (intent is PluginRefreshIntent.Cancel { Reason: PluginRefreshCancelReason.CleaningStarted })
            {
                callOrder.Add("cancel");
            }

            return Task.FromResult(refreshModule.CurrentSnapshot);
        };

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
            new CleaningCommandReadiness(refreshModule, stateService),
            refreshModule,
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
            viewModel.OnStateChanged(stateService.CurrentState);

            await viewModel.StartCleaningCommand.ExecuteAsync(null);

            callOrder.Should().Equal("cancel", "progress", "session");
            refreshModule.Intents.OfType<PluginRefreshIntent.Cancel>()
                .Should().ContainSingle(cancel => cancel.Reason == PluginRefreshCancelReason.CleaningStarted);
        }
        finally
        {
            viewModel.Dispose();
            stateService.Dispose();
            File.Delete(tempXEditPath);
        }
    }

    [Fact]
    public async Task StartCommand_WhenConfigPersistenceFails_ShouldProjectSpecificValidationError()
    {
        var tempXEditPath = Path.Combine(Path.GetTempPath(), $"AutoQAC-{Guid.NewGuid():N}.exe");
        await File.WriteAllTextAsync(tempXEditPath, string.Empty);

        var stateService = new StateService();
        var cleaningSession = Substitute.For<ICleaningSession>();
        using var refreshModule = new RecordingPluginRefreshModule();
        refreshModule.CurrentPublication = RecordingPluginRefreshModule.CreateFreshPublication();
        var failure = new ConfigPersistenceFailure(
            ConfigPersistenceOperationKind.Flush,
            ConfigPersistenceFailureKind.WriteFailed,
            "Could not write settings file (write_failed)",
            LogReference: null,
            Generation: 1);
        cleaningSession.StartAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new ConfigPersistenceFailureException(failure, failure.SafeSummary)));

        var progressInteraction = new Interaction<ICleaningSession, Unit>();
        using var progressRegistration = progressInteraction.RegisterHandler(_ => Task.FromResult(Unit.Default));

        var viewModel = new CleaningCommandsViewModel(
            stateService,
            cleaningSession,
            Substitute.For<IConfigurationService>(),
            new CleaningCommandReadiness(refreshModule, stateService),
            refreshModule,
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
            viewModel.OnStateChanged(stateService.CurrentState);

            await viewModel.StartCleaningCommand.ExecuteAsync(null);

            viewModel.StatusText.Should().Be("Configuration error");
            var error = viewModel.ValidationErrors.Should().ContainSingle().Subject;
            error.Title.Should().Be("Configuration save failed");
            error.Message.Should().Be(failure.SafeSummary);
            error.FixStep.Should().Be("Check the latest configuration save error and try again.");
        }
        finally
        {
            viewModel.Dispose();
            stateService.Dispose();
            File.Delete(tempXEditPath);
        }
    }

    [Theory]
    [InlineData(PluginRefreshStalenessReason.MissingPublication, "Plugins not refreshed")]
    [InlineData(PluginRefreshStalenessReason.SkipListSettingsChanged, "Plugins need refresh")]
    public async Task OnStateChanged_WhenPublicationIsMissingOrStale_ShouldProjectReadinessBanner(
        PluginRefreshStalenessReason stalenessReason,
        string expectedTitle)
    {
        var tempXEditPath = Path.Combine(Path.GetTempPath(), $"AutoQAC-{Guid.NewGuid():N}.exe");
        await File.WriteAllTextAsync(tempXEditPath, string.Empty);

        var stateService = new StateService();
        var cleaningSession = Substitute.For<ICleaningSession>();
        using var refreshModule = new RecordingPluginRefreshModule(
            RecordingPluginRefreshModule.CreateSnapshot(GameType.SkyrimSe));
        if (stalenessReason != PluginRefreshStalenessReason.MissingPublication)
        {
            var fresh = RecordingPluginRefreshModule.CreateFreshPublication();
            refreshModule.CurrentPublication = fresh with
            {
                Freshness = new PluginRefreshFreshness(false, stalenessReason)
            };
        }

        var viewModel = new CleaningCommandsViewModel(
            stateService,
            cleaningSession,
            Substitute.For<IConfigurationService>(),
            new CleaningCommandReadiness(refreshModule, stateService),
            refreshModule,
            Substitute.For<ILoggingService>(),
            Substitute.For<IMessageDialogService>(),
            Substitute.For<IAppLifetime>(),
            new Interaction<ICleaningSession, Unit>(),
            new Interaction<List<DryRunResult>, Unit>(),
            new Interaction<Unit, bool>(),
            new Interaction<Unit, bool>(),
            new Interaction<Unit, Unit>(),
            new Interaction<Unit, Unit>());

        try
        {
            stateService.UpdateConfigurationPaths(null, null, tempXEditPath);
            viewModel.OnStateChanged(stateService.CurrentState);

            viewModel.CanStartCleaning.Should().BeFalse();
            viewModel.HasValidationErrors.Should().BeTrue();
            viewModel.ValidationErrors.Should().ContainSingle().Which.Title.Should().Be(expectedTitle);
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
            Substitute.For<ICleaningCommandReadiness>(),
            new RecordingPluginRefreshModule(),
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
