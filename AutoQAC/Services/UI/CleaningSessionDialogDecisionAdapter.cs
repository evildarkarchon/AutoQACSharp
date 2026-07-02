using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;
using AutoQAC.Models.Diagnostics;
using AutoQAC.Services.Cleaning;

namespace AutoQAC.Services.UI;

/// <summary>
/// Uses the application dialog service to collect user decisions required by a Cleaning session.
/// </summary>
public sealed class CleaningSessionDialogDecisionAdapter(IMessageDialogService messageDialog)
    : ICleaningSessionDecisionAdapter
{
    /// <inheritdoc />
    public async Task<bool> ShouldRetryTimedOutPluginAsync(
        string pluginName,
        int timeoutSeconds,
        int attemptNumber,
        int maxAttempts,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var safePluginName = DiagnosticTextFormatter.SafePluginName(pluginName);
        var message = $"Cleaning of '{safePluginName}' timed out after {timeoutSeconds} seconds.\n\n" +
                      $"Attempt {attemptNumber} of {maxAttempts} failed.\n\n" +
                      "Would you like to retry cleaning this plugin?";

        const string details = "Possible causes:\n" +
                               "- The plugin is very large\n" +
                               "- xEdit is processing slowly\n" +
                               "- The system is under heavy load\n\n" +
                               "You can increase the timeout in Edit > Settings if plugins regularly time out.";

        var shouldRetry = await messageDialog.ShowRetryAsync("Plugin Timeout", message, details)
            .ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return shouldRetry;
    }

    /// <inheritdoc />
    public async Task<BackupFailureChoice> ChooseBackupFailureAsync(
        string pluginName,
        string errorMessage,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var safePluginName = DiagnosticTextFormatter.SafePluginName(pluginName);
        var safeErrorMessage = DiagnosticTextFormatter.SafeFailureSummary(
            errorMessage,
            DiagnosticTextFormatter.CleaningFailedForPlugin(pluginName));

        var choice = await messageDialog.ShowBackupFailureDialogAsync(safePluginName, safeErrorMessage)
            .ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return choice;
    }

    /// <inheritdoc />
    public async Task<CleaningSessionStopDecision> ChooseAfterGracePeriodExpiredAsync(
        TerminationResult terminationResult,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var choice = await messageDialog.ShowChoiceAsync(
                StopTerminationDialogContent.ConfirmationTitle,
                StopTerminationDialogContent.ConfirmationMessage,
                StopTerminationDialogContent.ForceTerminateButton,
                StopTerminationDialogContent.LeaveRunningButton)
            .ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        return choice == MessageDialogResult.Yes
            ? CleaningSessionStopDecision.ForceTerminate
            : CleaningSessionStopDecision.LeaveRunning;
    }
}
