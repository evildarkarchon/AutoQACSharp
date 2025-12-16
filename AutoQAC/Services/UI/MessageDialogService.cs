using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using AutoQAC.ViewModels;
using AutoQAC.Views;

namespace AutoQAC.Services.UI;

/// <summary>
/// Service for displaying message dialogs using Avalonia windows.
/// </summary>
public sealed class MessageDialogService : IMessageDialogService
{
    public async Task<MessageDialogResult> ShowAsync(
        string title,
        string message,
        MessageDialogButtons buttons = MessageDialogButtons.Ok,
        MessageDialogIcon icon = MessageDialogIcon.None,
        string? details = null)
    {
        // Ensure we're on the UI thread
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
                ShowAsync(title, message, buttons, icon, details));
        }

        var viewModel = new MessageDialogViewModel
        {
            Title = title,
            Message = message,
            Details = details
        };

        viewModel.ConfigureButtons(buttons);
        viewModel.ConfigureIcon(icon);

        var dialog = new MessageDialog
        {
            DataContext = viewModel
        };

        var mainWindow = GetMainWindow();
        if (mainWindow != null)
        {
            var result = await dialog.ShowDialog<MessageDialogResult?>(mainWindow);
            return result ?? MessageDialogResult.None;
        }

        // Fallback: show as standalone window
        dialog.Show();
        return MessageDialogResult.None;
    }

    public async Task ShowErrorAsync(string title, string message, string? details = null)
    {
        await ShowAsync(title, message, MessageDialogButtons.Ok, MessageDialogIcon.Error, details);
    }

    public async Task ShowWarningAsync(string title, string message, string? details = null)
    {
        await ShowAsync(title, message, MessageDialogButtons.Ok, MessageDialogIcon.Warning, details);
    }

    public async Task ShowInfoAsync(string title, string message)
    {
        await ShowAsync(title, message, MessageDialogButtons.Ok, MessageDialogIcon.Information);
    }

    public async Task<bool> ShowConfirmAsync(string title, string message)
    {
        var result = await ShowAsync(title, message, MessageDialogButtons.YesNo, MessageDialogIcon.Question);
        return result == MessageDialogResult.Yes;
    }

    public async Task<bool> ShowRetryAsync(string title, string message, string? details = null)
    {
        var result = await ShowAsync(title, message, MessageDialogButtons.RetryCancel, MessageDialogIcon.Warning, details);
        return result == MessageDialogResult.Retry;
    }

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }
}
