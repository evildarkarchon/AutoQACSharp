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
    /// <summary>Start and Preview disable when a snapshot reports reserved admission and re-enable after release.</summary>
    [Fact]
    public void OnPluginRefreshSnapshot_WhenAdmissionReservedThenReleased_ShouldUpdateStartAndPreviewAvailability()
    {
        var readiness = Substitute.For<ICleaningCommandReadiness>();
        readiness.EvaluateAsync(Arg.Any<CancellationToken>())
            .Returns(CleaningCommandReadinessResult.Ready);
        using var viewModel = CreateViewModel(Substitute.For<ICleaningSession>(), readiness);
        viewModel.CanStartCleaning = true;
        var startNotifications = 0;
        var previewNotifications = 0;
        viewModel.StartCleaningCommand.CanExecuteChanged += (_, _) => startNotifications++;
        viewModel.PreviewCommand.CanExecuteChanged += (_, _) => previewNotifications++;

        viewModel.OnPluginRefreshSnapshot(AdmissionSnapshot(reserved: true));

        viewModel.CanStartCleaning.Should().BeFalse();
        viewModel.StartCleaningCommand.CanExecute(null).Should().BeFalse();
        viewModel.PreviewCommand.CanExecute(null).Should().BeFalse();
        startNotifications.Should().Be(1);
        previewNotifications.Should().Be(1);

        viewModel.OnPluginRefreshSnapshot(AdmissionSnapshot(reserved: false));

        viewModel.CanStartCleaning.Should().BeTrue();
        viewModel.StartCleaningCommand.CanExecute(null).Should().BeTrue();
        viewModel.PreviewCommand.CanExecute(null).Should().BeTrue();
        startNotifications.Should().Be(2);
        previewNotifications.Should().Be(2);
    }

    /// <summary>Settings, Skip list, and Restore lock for the whole reservation, not just while AppState is cleaning.</summary>
    [Fact]
    public void OnPluginRefreshSnapshot_WhenAdmissionReservedThenReleased_ShouldLockMutationDialogs()
    {
        var readiness = Substitute.For<ICleaningCommandReadiness>();
        readiness.EvaluateAsync(Arg.Any<CancellationToken>())
            .Returns(CleaningCommandReadinessResult.Ready);
        using var viewModel = CreateViewModel(Substitute.For<ICleaningSession>(), readiness);
        var settingsNotifications = 0;
        viewModel.ShowSettingsCommand.CanExecuteChanged += (_, _) => settingsNotifications++;

        viewModel.OnPluginRefreshSnapshot(AdmissionSnapshot(reserved: true));

        viewModel.IsCleaning.Should().BeFalse("AppState has not yet published active cleaning");
        viewModel.ShowSettingsCommand.CanExecute(null).Should().BeFalse();
        viewModel.ShowSkipListCommand.CanExecute(null).Should().BeFalse();
        viewModel.RestoreBackupsCommand.CanExecute(null).Should().BeFalse();
        settingsNotifications.Should().Be(1);

        viewModel.OnPluginRefreshSnapshot(AdmissionSnapshot(reserved: false));

        viewModel.ShowSettingsCommand.CanExecute(null).Should().BeTrue();
        viewModel.ShowSkipListCommand.CanExecute(null).Should().BeTrue();
        viewModel.RestoreBackupsCommand.CanExecute(null).Should().BeTrue();
        settingsNotifications.Should().Be(2);
    }

    /// <summary>A direct preview invocation must not validate plugins after admission is reserved.</summary>
    [Fact]
    public async Task PreviewCommand_WhenAdmissionAlreadyReserved_ShouldNotRunSessionPreview()
    {
        var cleaningSession = Substitute.For<ICleaningSession>();
        var readiness = Substitute.For<ICleaningCommandReadiness>();
        readiness.EvaluateAsync(Arg.Any<CancellationToken>())
            .Returns(CleaningCommandReadinessResult.Ready);
        using var viewModel = CreateViewModel(cleaningSession, readiness);
        viewModel.CanStartCleaning = true;
        viewModel.OnPluginRefreshSnapshot(AdmissionSnapshot(reserved: true));

        await viewModel.PreviewCommand.ExecuteAsync(null);

        await cleaningSession.DidNotReceive().PreviewAsync(Arg.Any<CancellationToken>());
        viewModel.StatusText.Should().Be("Ready");
    }

    /// <summary>Preview rechecks admission after its asynchronous readiness check completes.</summary>
    [Fact]
    public async Task PreviewCommand_WhenAdmissionReservedDuringReadiness_ShouldNotRunSessionPreview()
    {
        var cleaningSession = Substitute.For<ICleaningSession>();
        var readiness = Substitute.For<ICleaningCommandReadiness>();
        var evaluationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completeReadiness = new TaskCompletionSource<CleaningCommandReadinessResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var evaluations = 0;
        readiness.EvaluateAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            // Only the command's own readiness check blocks; snapshot-triggered refreshes complete immediately.
            if (Interlocked.Increment(ref evaluations) != 1)
                return Task.FromResult(CleaningCommandReadinessResult.Ready);
            evaluationStarted.TrySetResult();
            return completeReadiness.Task;
        });
        using var viewModel = CreateViewModel(cleaningSession, readiness);
        viewModel.CanStartCleaning = true;

        var previewTask = viewModel.PreviewCommand.ExecuteAsync(null);
        await evaluationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.OnPluginRefreshSnapshot(AdmissionSnapshot(reserved: true));
        completeReadiness.SetResult(CleaningCommandReadinessResult.Ready);
        await previewTask.WaitAsync(TimeSpan.FromSeconds(5));

        await cleaningSession.DidNotReceive().PreviewAsync(Arg.Any<CancellationToken>());
        viewModel.StatusText.Should().Be("Ready");
    }

    /// <summary>A preview that finishes while admission is reserved must not replace Cleaning session status.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreviewCommand_WhenAdmissionReservedDuringSessionPreview_ShouldPreserveCleaningStatus(
        bool previewFails)
    {
        var cleaningSession = Substitute.For<ICleaningSession>();
        var readiness = Substitute.For<ICleaningCommandReadiness>();
        readiness.EvaluateAsync(Arg.Any<CancellationToken>())
            .Returns(CleaningCommandReadinessResult.Ready);
        var previewStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completePreview = new TaskCompletionSource<IReadOnlyList<DryRunResult>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        cleaningSession.PreviewAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            previewStarted.TrySetResult();
            return completePreview.Task;
        });
        var previewInteraction = new Interaction<List<DryRunResult>, Unit>();
        var previewShown = false;
        using var previewRegistration = previewInteraction.RegisterHandler(_ =>
        {
            previewShown = true;
            return Task.FromResult(Unit.Default);
        });
        using var viewModel = CreateViewModel(cleaningSession, readiness, previewInteraction);
        viewModel.CanStartCleaning = true;

        var previewTask = viewModel.PreviewCommand.ExecuteAsync(null);
        await previewStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.OnPluginRefreshSnapshot(AdmissionSnapshot(reserved: true));
        viewModel.StatusText = "Cleaning: NeedsCleaning.esp (1/1)";
        if (previewFails)
            completePreview.SetException(new IOException("Preview failed after admission"));
        else
            completePreview.SetResult([]);
        await previewTask.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.StatusText.Should().Be("Cleaning: NeedsCleaning.esp (1/1)");
        previewShown.Should().BeFalse();
    }

    /// <summary>A preview dialog finishing after admission must not overwrite the Cleaning session status.</summary>
    [Fact]
    public async Task PreviewCommand_WhenAdmissionReservedDuringPreviewInteraction_ShouldPreserveCleaningStatus()
    {
        var cleaningSession = Substitute.For<ICleaningSession>();
        cleaningSession.PreviewAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<DryRunResult>>([]));
        var readiness = Substitute.For<ICleaningCommandReadiness>();
        readiness.EvaluateAsync(Arg.Any<CancellationToken>())
            .Returns(CleaningCommandReadinessResult.Ready);
        var interactionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completeInteraction = new TaskCompletionSource<Unit>(TaskCreationOptions.RunContinuationsAsynchronously);
        var previewInteraction = new Interaction<List<DryRunResult>, Unit>();
        using var previewRegistration = previewInteraction.RegisterHandler(_ =>
        {
            interactionStarted.TrySetResult();
            return completeInteraction.Task;
        });
        using var viewModel = CreateViewModel(cleaningSession, readiness, previewInteraction);
        viewModel.CanStartCleaning = true;

        var previewTask = viewModel.PreviewCommand.ExecuteAsync(null);
        await interactionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.OnPluginRefreshSnapshot(AdmissionSnapshot(reserved: true));
        viewModel.StatusText = "Cleaning: NeedsCleaning.esp (1/1)";
        completeInteraction.SetResult(Unit.Default);
        await previewTask.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.StatusText.Should().Be("Cleaning: NeedsCleaning.esp (1/1)");
    }

    /// <summary>
    /// Start shows progress and then starts the session; reserving Cleaning admission inside the session cancels
    /// Plugin refresh work, so the command issues no refresh intent of its own.
    /// </summary>
    [Fact]
    public async Task StartCommand_ShouldShowProgressThenStartSessionWithoutRefreshIntent()
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

        var progressInteraction = new Interaction<ICleaningSession, Unit>();
        using var progressRegistration = progressInteraction.RegisterHandler(_ =>
        {
            callOrder.Add("progress");
            return Task.FromResult(Unit.Default);
        });

        var viewModel = new CleaningCommandsViewModel(
            cleaningSession,
            new CleaningCommandReadiness(refreshModule, stateService, new CleaningAdmission()),
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

            callOrder.Should().Equal("progress", "session");
            refreshModule.Intents.Should().BeEmpty();
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
            null,
            1);
        cleaningSession.StartAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new ConfigPersistenceFailureException(failure, failure.SafeSummary)));

        var progressInteraction = new Interaction<ICleaningSession, Unit>();
        using var progressRegistration = progressInteraction.RegisterHandler(_ => Task.FromResult(Unit.Default));

        var viewModel = new CleaningCommandsViewModel(
            cleaningSession,
            new CleaningCommandReadiness(refreshModule, stateService, new CleaningAdmission()),
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
            cleaningSession,
            new CleaningCommandReadiness(refreshModule, stateService, new CleaningAdmission()),
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

    /// <summary>
    /// Verifies that the no-game banner omits the explanatory sentence that merely repeats its title and action.
    /// </summary>
    [Fact]
    public void OnStateChanged_WhenNoGameIsSelected_ShouldOmitRedundantValidationMessage()
    {
        var stateService = new StateService();
        using var refreshModule = new RecordingPluginRefreshModule();
        var viewModel = new CleaningCommandsViewModel(
            Substitute.For<ICleaningSession>(),
            new CleaningCommandReadiness(refreshModule, stateService, new CleaningAdmission()),
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
            viewModel.OnStateChanged(stateService.CurrentState);

            var error = viewModel.ValidationErrors.Should().ContainSingle().Subject;
            error.Title.Should().Be("No game selected");
            error.Message.Should().BeEmpty();
            error.FixStep.Should().Be("Select a game before cleaning.");
        }
        finally
        {
            viewModel.Dispose();
            stateService.Dispose();
        }
    }

    [Fact]
    public void ExitCommand_ShouldRequestApplicationShutdown()
    {
        var appLifetime = Substitute.For<IAppLifetime>();
        var viewModel = new CleaningCommandsViewModel(
            Substitute.For<ICleaningSession>(),
            Substitute.For<ICleaningCommandReadiness>(),
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

    /// <summary>Creates a snapshot whose commands were computed under the given Cleaning admission state.</summary>
    private static PluginRefreshSnapshot AdmissionSnapshot(bool reserved)
    {
        return RecordingPluginRefreshModule.CreateSnapshot(
            GameType.SkyrimSe,
            commands: new PluginRefreshCommandAvailability(!reserved, !reserved, false, false, reserved));
    }

    /// <summary>Creates the command view model with only the session and readiness seams under test.</summary>
    private static CleaningCommandsViewModel CreateViewModel(
        ICleaningSession cleaningSession,
        ICleaningCommandReadiness readiness,
        Interaction<List<DryRunResult>, Unit>? previewInteraction = null,
        Interaction<ICleaningSession, Unit>? progressInteraction = null)
    {
        return new CleaningCommandsViewModel(
            cleaningSession,
            readiness,
            Substitute.For<ILoggingService>(),
            Substitute.For<IMessageDialogService>(),
            Substitute.For<IAppLifetime>(),
            progressInteraction ?? new Interaction<ICleaningSession, Unit>(),
            previewInteraction ?? new Interaction<List<DryRunResult>, Unit>(),
            new Interaction<Unit, bool>(),
            new Interaction<Unit, bool>(),
            new Interaction<Unit, Unit>(),
            new Interaction<Unit, Unit>());
    }
}
