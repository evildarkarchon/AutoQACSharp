using System.Threading.Tasks;
using AutoQAC.Models;

namespace AutoQAC.Services.UI;

/// <summary>
/// Button options for message dialogs.
/// </summary>
public enum MessageDialogButtons
{
    Ok,
    OkCancel,
    YesNo,
    YesNoCancel,
    RetryCancel
}

/// <summary>
/// Icon type for message dialogs.
/// </summary>
public enum MessageDialogIcon
{
    None,
    Information,
    Warning,
    Error,
    Question
}

/// <summary>
/// Result from a message dialog.
/// </summary>
public enum MessageDialogResult
{
    None,
    Ok,
    Cancel,
    Yes,
    No,
    Retry
}

/// <summary>
/// Service for displaying message dialogs to the user.
/// </summary>
public interface IMessageDialogService
{
    /// <summary>
    /// Shows a message dialog with the specified options.
    /// </summary>
    Task<MessageDialogResult> ShowAsync(
        string title,
        string message,
        MessageDialogButtons buttons = MessageDialogButtons.Ok,
        MessageDialogIcon icon = MessageDialogIcon.None,
        string? details = null);

    /// <summary>
    /// Shows an error dialog.
    /// </summary>
    Task ShowErrorAsync(string title, string message, string? details = null);

    /// <summary>
    /// Shows a warning dialog.
    /// </summary>
    Task ShowWarningAsync(string title, string message, string? details = null);

    /// <summary>
    /// Shows an information dialog.
    /// </summary>
    Task ShowInfoAsync(string title, string message);

    /// <summary>
    /// Shows a confirmation dialog (Yes/No).
    /// </summary>
    Task<bool> ShowConfirmAsync(string title, string message);

    /// <summary>
    /// Shows a two-choice dialog with custom button labels.
    /// </summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="message">Dialog message shown to the user.</param>
    /// <param name="primaryButtonText">Text for the primary button, returned as <see cref="MessageDialogResult.Yes" />.</param>
    /// <param name="secondaryButtonText">Text for the secondary button, returned as <see cref="MessageDialogResult.No" />.</param>
    /// <param name="icon">Icon shown with the dialog.</param>
    /// <param name="details">Optional details text.</param>
    /// <returns>The selected choice result.</returns>
    Task<MessageDialogResult> ShowChoiceAsync(string title, string message, string primaryButtonText,
        string secondaryButtonText, MessageDialogIcon icon = MessageDialogIcon.Question, string? details = null);

    /// <summary>
    /// Shows a retry dialog.
    /// </summary>
    Task<bool> ShowRetryAsync(string title, string message, string? details = null);

    /// <summary>
    /// Shows a backup failure dialog with three choices: Skip Plugin, Abort Session, or Continue Without Backup.
    /// </summary>
    /// <param name="pluginName">Name of the plugin that failed to back up.</param>
    /// <param name="errorMessage">Description of the backup failure.</param>
    /// <returns>The user's choice for how to proceed.</returns>
    Task<BackupFailureChoice> ShowBackupFailureDialogAsync(string pluginName, string errorMessage);
}
