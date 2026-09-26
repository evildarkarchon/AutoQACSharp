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
    /// <summary>Start and Preview disable immediately when a Cleaning session reserves admission.</summary>
    [Fact]
    public void OnCleaningAdmissionChanged_ShouldUpdateStartAndPreviewAvailability()
    {
        using var refreshModule = new RecordingPluginRefreshModule();
        var readiness = Substitute.For<ICleaningCommandReadiness>();
        readiness.EvaluateAsync(Arg.Any<CancellationToken>())
            .Returns(CleaningCommandReadinessResult.Ready);
        using var viewModel = CreateViewModel(
            Substitute.For<ICleaningSession>(),
            readiness,
            refreshModule);
        viewModel.CanStartCleaning = true;
        var startNotifications = 0;
        var previewNotifications = 0;
        viewModel.StartCleaningCommand.CanExecuteChanged += (_, _) => startNotifications++;
        viewModel.PreviewCommand.CanExecuteChanged += (_, _) => previewNotifications++;

        viewModel.OnCleaningAdmissionChanged(true);

        viewModel.CanStartCleaning.Should().BeFalse();
        viewModel.StartCleaningCommand.CanExecute(null).Should().BeFalse();
        viewModel.PreviewCommand.CanExecute(null).Should().BeFalse();
        startNotifications.Should().Be(1);
        previewNotifications.Should().Be(1);

        viewModel.OnCleaningAdmissionChanged(false);

        viewModel.CanStartCleaning.Should().BeTrue();
        viewModel.StartCleaningCommand.CanExecute(null).Should().BeTrue();
        viewModel.PreviewCommand.CanExecute(null).Should().BeTrue();
        startNotifications.Should().Be(2);
        previewNotifications.Should().Be(2);
    }

    /// <summary>A direct preview invocation must not validate plugins after admission is reserved.</summary>
    [Fact]
    public async Task PreviewCommand_WhenAdmissionAlreadyReserved_ShouldNotRunSessionPreview()
    {
        using var refreshModule = new RecordingPluginRefreshModule();
        var cleaningSession = Substitute.For<ICleaningSession>();
        var readiness = Substitute.For<ICleaningCommandReadiness>();
        readiness.EvaluateAsync(Arg.Any<CancellationToken>())
            .Returns(CleaningCommandReadinessResult.Ready);
        using var viewModel = CreateViewModel(cleaningSession, readiness, refreshModule);
        viewModel.CanStartCleaning = true;
        viewModel.OnCleaningAdmissionChanged(true);

        await viewModel.PreviewCommand.ExecuteAsync(null);

        await cleaningSession.DidNotReceive().PreviewAsync(Arg.Any<CancellationToken>());
        viewModel.StatusText.Should().Be("Ready");
    }

    /// <summary>Preview rechecks admission after its asynchronous readiness check completes.</summary>
    [Fact]
    public async Task PreviewCommand_WhenAdmissionReservedDuringReadiness_ShouldNotRunSessionPreview()
    {
        using var refreshModule = new RecordingPluginRefreshModule();
        var cleaningSession = Substitute.For<ICleaningSession>();
        var readiness = Substitute.For<ICleaningCommandReadiness>();
        var evaluationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completeReadiness = new TaskCompletionSource<CleaningCommandReadinessResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        readiness.EvaluateAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            evaluationStarted.TrySetResult();
            return completeReadiness.Task;
        });
        using var viewModel = CreateViewModel(cleaningSession, readiness, refreshModule);
        viewModel.CanStartCleaning = true;

        var previewTask = viewModel.PreviewCommand.ExecuteAsync(null);
        await evaluationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.OnCleaningAdmissionChanged(true);
        completeReadiness.SetResult(CleaningCommandReadinessResult.Ready);
        await previewTask.WaitAsync(TimeSpan.FromSeconds(5));

        await cleaningSession.DidNotReceive().PreviewAsync(Arg.Any<CancellationToken>());
        viewModel.StatusText.Should().Be("Ready");
    }

    /// <summary>A stale preview readiness failure cannot replace status after an intervening Cleaning session.</summary>
    [Fact]
    public async Task PreviewCommand_WhenAdmissionReservesAndReleasesDuringReadiness_ShouldNotProjectStaleFailure()
    {
        using var refreshModule = new RecordingPluginRefreshModule();
        var readiness = Substitute.For<ICleaningCommandReadiness>();
        var firstEvaluationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completeFirstEvaluation = new TaskCompletionSource<CleaningCommandReadinessResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var evaluations = 0;
        readiness.EvaluateAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (Interlocked.Increment(ref evaluations) != 1)
                return Task.FromResult(CleaningCommandReadinessResult.Ready);
            firstEvaluationStarted.TrySetResult();
            return completeFirstEvaluation.Task;
        });
        var cleaningSession = Substitute.For<ICleaningSession>();
        using var viewModel = CreateViewModel(cleaningSession, readiness, refreshModule);
        viewModel.CanStartCleaning = true;

        var previewTask = viewModel.PreviewCommand.ExecuteAsync(null);
        await firstEvaluationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.OnCleaningAdmissionChanged(true);
        viewModel.OnCleaningAdmissionChanged(false);
        viewModel.StatusText = "Cleaning completed.";
        completeFirstEvaluation.SetResult(CleaningCommandReadinessResult.Blocked(
            new CleaningPreflightFailure(CleaningPreflightFailureKind.NoGameSelected, "No game selected")));
        await previewTask.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.StatusText.Should().Be("Cleaning completed.");
        viewModel.HasValidationErrors.Should().BeFalse();
        await cleaningSession.DidNotReceive().PreviewAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>An older Start command cannot proceed after another Cleaning admission crossed its readiness await.</summary>
    [Fact]
    public async Task StartCommand_WhenAdmissionReservesAndReleasesDuringReadiness_ShouldNotStartSession()
    {
        using var refreshModule = new RecordingPluginRefreshModule();
        var readiness = Substitute.For<ICleaningCommandReadiness>();
        var firstEvaluationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completeFirstEvaluation = new TaskCompletionSource<CleaningCommandReadinessResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var evaluations = 0;
        readiness.EvaluateAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (Interlocked.Increment(ref evaluations) != 1)
                return Task.FromResult(CleaningCommandReadinessResult.Ready);
            firstEvaluationStarted.TrySetResult();
            return completeFirstEvaluation.Task;
        });
        var cleaningSession = Substitute.For<ICleaningSession>();
        var progressInteraction = new Interaction<ICleaningSession, Unit>();
        using var progressRegistration = progressInteraction.RegisterHandler(_ => Task.FromResult(Unit.Default));
        using var viewModel = CreateViewModel(
            cleaningSession, readiness, refreshModule, progressInteraction: progressInteraction);
        viewModel.CanStartCleaning = true;

        var startTask = viewModel.StartCleaningCommand.ExecuteAsync(null);
        await firstEvaluationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.OnCleaningAdmissionChanged(true);
        viewModel.OnCleaningAdmissionChanged(false);
        completeFirstEvaluation.SetResult(CleaningCommandReadinessResult.Ready);
        await startTask.WaitAsync(TimeSpan.FromSeconds(5));

        await cleaningSession.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
        refreshModule.Intents.Should().BeEmpty();
    }

    /// <summary>A preview that finishes after admission is reserved must not replace Cleaning session status.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task PreviewCommand_WhenAdmissionReservedDuringSessionPreview_ShouldPreserveCleaningStatus(
        bool previewFails,
        bool releaseAdmissionBeforeCompletion)
    {
        using var refreshModule = new RecordingPluginRefreshModule();
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
        using var viewModel = CreateViewModel(cleaningSession, readiness, refreshModule, previewInteraction);
        viewModel.CanStartCleaning = true;

        var previewTask = viewModel.PreviewCommand.ExecuteAsync(null);
        await previewStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.OnCleaningAdmissionChanged(true);
        viewModel.StatusText = "Cleaning: NeedsCleaning.esp (1/1)";
        if (releaseAdmissionBeforeCompletion) viewModel.OnCleaningAdmissionChanged(false);
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
        using var refreshModule = new RecordingPluginRefreshModule();
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
        using var viewModel = CreateViewModel(cleaningSession, readiness, refreshModule, previewInteraction);
        viewModel.CanStartCleaning = true;

        var previewTask = viewModel.PreviewCommand.ExecuteAsync(null);
        await interactionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.OnCleaningAdmissionChanged(true);
        viewModel.StatusText = "Cleaning: NeedsCleaning.esp (1/1)";
        completeInteraction.SetResult(Unit.Default);
        await previewTask.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.StatusText.Should().Be("Cleaning: NeedsCleaning.esp (1/1)");
    }

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
                callOrder.Add("cancel");

            return Task.FromResult(refreshModule.CurrentSnapshot);
        };

        var progressInteraction = new Interaction<ICleaningSession, Unit>();
        using var progressRegistration = progressInteraction.RegisterHandler(_ =>
        {
            callOrder.Add("progress");
            return Task.FromResult(Unit.Default);
        });

        var viewModel = new CleaningCommandsViewModel(
            cleaningSession,
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
            null,
            1);
        cleaningSession.StartAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new ConfigPersistenceFailureException(failure, failure.SafeSummary)));

        var progressInteraction = new Interaction<ICleaningSession, Unit>();
        using var progressRegistration = progressInteraction.RegisterHandler(_ => Task.FromResult(Unit.Default));

        var viewModel = new CleaningCommandsViewModel(
            cleaningSession,
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
            cleaningSession,
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

    /// <summary>Creates the command view model with only the session and readiness seams under test.</summary>
    private static CleaningCommandsViewModel CreateViewModel(
        ICleaningSession cleaningSession,
        ICleaningCommandReadiness readiness,
        IPluginRefreshModule refreshModule,
        Interaction<List<DryRunResult>, Unit>? previewInteraction = null,
        Interaction<ICleaningSession, Unit>? progressInteraction = null)
    {
        return new CleaningCommandsViewModel(
            cleaningSession,
            readiness,
            refreshModule,
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
