using System.Reactive.Linq;
using System.Reactive.Subjects;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;
using AutoQAC.Models.Configuration;
using AutoQAC.Services.Cleaning;
using AutoQAC.Services.Configuration;
using AutoQAC.Services.GameCapability;
using AutoQAC.Services.Plugin;
using AutoQAC.Services.State;
using AutoQAC.Services.UI;
using AutoQAC.Tests.Helpers;
using AutoQAC.Tests.TestInfrastructure;
using AutoQAC.ViewModels;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AutoQAC.Tests.ViewModels;

/// <summary>
/// Phase 11 cross-surface regression guards for user-facing diagnostic disclosure boundaries.
/// </summary>
public sealed class Phase11DiagnosticsBoundaryTests
{
    /// <summary>
    /// Verifies a real StartCleaning command catch path maps unsafe technical exception text to safe UI copy.
    /// </summary>
    [Fact]
    public async Task StartCleaningCommand_WhenOrchestratorThrowsUnsafeException_ShouldExcludeSharedSentinelsFromUserFacingText()
    {
        // Arrange
        var configService = Substitute.For<IConfigurationService>();
        var stateService = Substitute.For<IStateService>();
        var cleaningSession = Substitute.For<ICleaningSession>();
        var logger = Substitute.For<ILoggingService>();
        var fileDialog = Substitute.For<IFileDialogService>();
        var messageDialog = Substitute.For<IMessageDialogService>();
        var pluginValidation = Substitute.For<IPluginValidationService>();
        var pluginLoading = Substitute.For<IPluginLoadingService>();
        var uiDispatcher = new SynchronousUiDispatcher();
        var xEditPath = Path.GetTempFileName();

        try
        {
            var appState = new AppState
            {
                XEditExecutablePath = xEditPath,
                PluginsToClean =
                [
                    new PluginInfo
                    {
                        FileName = "Sentinel.esp",
                        FullPath = @"C:\Games\Skyrim Special Edition\Data\Sentinel.esp"
                    }
                ]
            };
            stateService.StateChanged.Returns(new BehaviorSubject<AppState>(appState));
            stateService.CurrentState.Returns(appState);
            stateService.CleaningCompleted.Returns(Observable.Never<CleaningSessionResult>());
            configService.SkipListChanged.Returns(Observable.Never<GameType>());
            using var refreshModule = new RecordingPluginRefreshModule();
            refreshModule.CurrentPublication = RecordingPluginRefreshModule.CreateFreshPublication(
                rows: appState.PluginsToClean
                    .Select(plugin => RecordingPluginRefreshModule.CreatePublishedRow(plugin))
                    .ToList());
            var discoveryPlanner = new PluginRefreshDiscoveryPlanner(
                configService,
                pluginLoading,
                Substitute.For<AutoQAC.Services.MO2.IMo2InstanceService>());
            cleaningSession.StartAsync(Arg.Any<CancellationToken>())
                .ThrowsAsync(new Exception(DiagnosticSentinels.CreateUnsafePayload()));

            var viewModel = new MainWindowViewModel(
                configService,
                stateService,
                cleaningSession,
                logger,
                fileDialog,
                messageDialog,
                pluginValidation,
                pluginLoading,
                uiDispatcher,
                refreshModule,
                discoveryPlanner,
                new CleaningCommandReadiness(refreshModule, stateService));
            using var _ = viewModel.ShowProgressInteraction.RegisterHandler(_ => Task.FromResult(default(AutoQAC.Services.UI.Interactions.Unit)));

            // Act
            await viewModel.Commands.StartCleaningCommand.ExecuteAsync(null);

            // Assert
            var dialogCall = messageDialog.ReceivedCalls()
                .Single(call => call.GetMethodInfo().Name == nameof(IMessageDialogService.ShowErrorAsync));
            var userFacingText = dialogCall.GetArguments()
                .OfType<string>()
                .Append(viewModel.Commands.StatusText)
                .Concat(viewModel.Commands.ValidationErrors.SelectMany(error => new[] { error.Title, error.Message, error.FixStep }))
                .ToArray();

            userFacingText.Should().Contain("Cleaning Failed");
            userFacingText.Should().Contain("Cleaning failed. See the latest AutoQAC log for technical details.");
            userFacingText.Should().Contain("Technical details were written to the latest AutoQAC log.");
            AssertNoUnsafeSentinels(userFacingText);
        }
        finally
        {
            File.Delete(xEditPath);
        }
    }

    /// <summary>
    /// Asserts every covered user-facing string excludes the shared Phase 11 unsafe sentinel set.
    /// </summary>
    private static void AssertNoUnsafeSentinels(IEnumerable<string?> values)
    {
        var text = string.Join(Environment.NewLine, values.Where(value => value is not null));
        foreach (var sentinel in DiagnosticSentinels.UnsafeDiagnosticSentinels)
        {
            text.Should().NotContain(sentinel);
        }
    }
}
