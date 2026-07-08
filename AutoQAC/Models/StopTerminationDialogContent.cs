namespace AutoQAC.Models;

/// <summary>
/// Provides the shared user-facing copy and action labels for xEdit stop escalation outcomes.
/// </summary>
public static class StopTerminationDialogContent
{
    /// <summary>
    /// Title shown when xEdit did not exit after the graceful stop request.
    /// </summary>
    public const string ConfirmationTitle = "Force Terminate xEdit?";

    /// <summary>
    /// Safe explanation shown before the user chooses whether AutoQAC force terminates xEdit.
    /// </summary>
    public const string ConfirmationMessage =
        "xEdit did not exit after the stop request. AutoQAC can leave xEdit running, or force terminate it now. Force terminating can interrupt remaining file or log writes.";

    /// <summary>
    /// Primary confirmation label for the affirmative force-termination action.
    /// </summary>
    public const string ForceTerminateButton = "Force Terminate";

    /// <summary>
    /// Secondary confirmation label for leaving xEdit running after graceful stop expires.
    /// </summary>
    public const string LeaveRunningButton = "Leave Running";

    /// <summary>
    /// Title shown when the force-termination request fails.
    /// </summary>
    public const string ForceFailureTitle = "Could Not Force Terminate xEdit";

    /// <summary>
    /// Safe failure message that avoids raw exception, path, stack, and command-line details.
    /// </summary>
    public const string ForceFailureMessage =
        "AutoQAC could not force terminate xEdit. xEdit may still be running; close it manually before starting another cleaning session. Technical details are in the latest AutoQAC log.";

    /// <summary>
    /// Title shown when the user chooses not to force terminate xEdit.
    /// </summary>
    public const string LeftRunningTitle = "Cleaning Stopped";

    /// <summary>
    /// Safe warning message for the user-selected leave-running outcome.
    /// </summary>
    public const string LeftRunningMessage =
        "AutoQAC stopped the cleaning session. xEdit was left running by your choice; close it manually when it is safe.";
}
