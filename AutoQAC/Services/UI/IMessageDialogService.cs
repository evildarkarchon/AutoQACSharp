using System.Threading.Tasks;

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
    /// Shows a retry dialog.
    /// </summary>
    Task<bool> ShowRetryAsync(string title, string message, string? details = null);
}
