using System.Threading.Tasks;
using AutoQAC.Models;
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

    public async Task<BackupFailureChoice> ShowBackupFailureDialogAsync(string pluginName, string errorMessage)
    {
        // Ensure we're on the UI thread
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
                ShowBackupFailureDialogAsync(pluginName, errorMessage));
        }

        var tcs = new TaskCompletionSource<BackupFailureChoice>();

        var window = new Window
        {
            Title = "Backup Failed",
            Width = 450,
            Height = 220,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var panel = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 12
        };

        panel.Children.Add(new TextBlock
        {
            Text = $"Failed to back up '{pluginName}'",
            FontWeight = Avalonia.Media.FontWeight.Bold,
            FontSize = 14,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        panel.Children.Add(new TextBlock
        {
            Text = errorMessage,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 12
        });

        panel.Children.Add(new TextBlock
        {
            Text = "How would you like to proceed?",
            Margin = new Avalonia.Thickness(0, 4, 0, 0)
        });

        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8
        };

        var skipButton = new Button { Content = "Skip Plugin", MinWidth = 100 };
        skipButton.Click += (_, _) =>
        {
            tcs.TrySetResult(BackupFailureChoice.SkipPlugin);
            window.Close();
        };

        var continueButton = new Button { Content = "Continue Anyway", MinWidth = 120 };
        continueButton.Click += (_, _) =>
        {
            tcs.TrySetResult(BackupFailureChoice.ContinueWithoutBackup);
            window.Close();
        };

        var abortButton = new Button { Content = "Abort Session", MinWidth = 110 };
        abortButton.Click += (_, _) =>
        {
            tcs.TrySetResult(BackupFailureChoice.AbortSession);
            window.Close();
        };

        buttonPanel.Children.Add(skipButton);
        buttonPanel.Children.Add(continueButton);
        buttonPanel.Children.Add(abortButton);

        panel.Children.Add(buttonPanel);
        window.Content = panel;

        // Handle window close without button click (X button)
        window.Closed += (_, _) => tcs.TrySetResult(BackupFailureChoice.SkipPlugin);

        var mainWindow = GetMainWindow();
        if (mainWindow != null)
        {
            await window.ShowDialog(mainWindow);
        }
        else
        {
            window.Show();
        }

        return await tcs.Task;
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
