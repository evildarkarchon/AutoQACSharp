using System.Threading.Tasks;
using AutoQAC.Models;

namespace AutoQAC.Services.UI;

/// <summary>
/// Phase 1 WinUI bootstrap stub. Phase 2 replaces this with serialized
/// ContentDialog-backed behavior.
/// </summary>
public sealed class MessageDialogService : IMessageDialogService
{
    public Task<MessageDialogResult> ShowAsync(
        string title,
        string message,
        MessageDialogButtons buttons = MessageDialogButtons.Ok,
        MessageDialogIcon icon = MessageDialogIcon.None,
        string? details = null)
    {
        var result = buttons == MessageDialogButtons.Ok ? MessageDialogResult.Ok : MessageDialogResult.None;
        return Task.FromResult(result);
    }

    public Task ShowErrorAsync(string title, string message, string? details = null)
    {
        return Task.CompletedTask;
    }

    public Task ShowWarningAsync(string title, string message, string? details = null)
    {
        return Task.CompletedTask;
    }

    public Task ShowInfoAsync(string title, string message)
    {
        return Task.CompletedTask;
    }

    public Task<bool> ShowConfirmAsync(string title, string message)
    {
        return Task.FromResult(false);
    }

    public Task<MessageDialogResult> ShowChoiceAsync(
        string title,
        string message,
        string primaryButtonText,
        string secondaryButtonText,
        MessageDialogIcon icon = MessageDialogIcon.Question,
        string? details = null)
    {
        return Task.FromResult(MessageDialogResult.None);
    }

    public Task<bool> ShowRetryAsync(string title, string message, string? details = null)
    {
        return Task.FromResult(false);
    }

    public Task<BackupFailureChoice> ShowBackupFailureDialogAsync(string pluginName, string errorMessage)
    {
        return Task.FromResult(BackupFailureChoice.SkipPlugin);
    }
}
