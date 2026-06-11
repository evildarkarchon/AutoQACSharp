using System;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Models;
using AutoQAC.Models.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AutoQAC.Services.UI;

public sealed class MessageDialogService : IMessageDialogService
{
    private readonly IWindowContextProvider _windowContextProvider;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly SemaphoreSlim _dialogLock = new(1, 1);

    public MessageDialogService(
        IWindowContextProvider windowContextProvider,
        IUiDispatcher uiDispatcher)
    {
        _windowContextProvider = windowContextProvider;
        _uiDispatcher = uiDispatcher;
    }

    public Task<MessageDialogResult> ShowAsync(
        string title,
        string message,
        MessageDialogButtons buttons = MessageDialogButtons.Ok,
        MessageDialogIcon icon = MessageDialogIcon.None,
        string? details = null)
    {
        var buttonConfiguration = MessageDialogButtonMapper.Build(buttons);
        return ShowMessageDialogAsync(title, message, buttonConfiguration, icon, details);
    }

    public Task ShowErrorAsync(string title, string message, string? details = null)
    {
        return ShowAsync(title, message, MessageDialogButtons.Ok, MessageDialogIcon.Error, details);
    }

    public Task ShowWarningAsync(string title, string message, string? details = null)
    {
        return ShowAsync(title, message, MessageDialogButtons.Ok, MessageDialogIcon.Warning, details);
    }

    public Task ShowInfoAsync(string title, string message)
    {
        return ShowAsync(title, message, MessageDialogButtons.Ok, MessageDialogIcon.Information);
    }

    public async Task<bool> ShowConfirmAsync(string title, string message)
    {
        var result = await ShowAsync(title, message, MessageDialogButtons.YesNo, MessageDialogIcon.Question);
        return result == MessageDialogResult.Yes;
    }

    public Task<MessageDialogResult> ShowChoiceAsync(
        string title,
        string message,
        string primaryButtonText,
        string secondaryButtonText,
        MessageDialogIcon icon = MessageDialogIcon.Question,
        string? details = null)
    {
        var buttonConfiguration = MessageDialogButtonMapper.BuildChoice(
            primaryButtonText,
            secondaryButtonText);

        return ShowMessageDialogAsync(title, message, buttonConfiguration, icon, details);
    }

    public async Task<bool> ShowRetryAsync(string title, string message, string? details = null)
    {
        var result = await ShowAsync(title, message, MessageDialogButtons.RetryCancel, MessageDialogIcon.Warning, details);
        return result == MessageDialogResult.Retry;
    }

    public Task<BackupFailureChoice> ShowBackupFailureDialogAsync(string pluginName, string errorMessage)
    {
        var safePluginName = DiagnosticTextFormatter.SafePluginName(pluginName);
        var safeErrorMessage = DiagnosticTextFormatter.SafeFailureSummary(
            errorMessage,
            DiagnosticTextFormatter.CleaningFailedForPlugin(pluginName));

        return ShowSerializedAsync(async () =>
        {
            if (!_windowContextProvider.TryGetContext(out _, out var xamlRoot))
            {
                return BackupFailureChoice.SkipPlugin;
            }

            var dialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = "Backup Failed",
                Content = BuildBackupFailureContent(safePluginName, safeErrorMessage),
                PrimaryButtonText = "Skip Plugin",
                SecondaryButtonText = "Continue Anyway",
                CloseButtonText = "Abort Session",
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await dialog.ShowAsync();
            return result switch
            {
                ContentDialogResult.Primary => BackupFailureChoice.SkipPlugin,
                ContentDialogResult.Secondary => BackupFailureChoice.ContinueWithoutBackup,
                _ => BackupFailureChoice.AbortSession
            };
        });
    }

    private Task<MessageDialogResult> ShowMessageDialogAsync(
        string title,
        string message,
        MessageDialogButtonConfiguration buttonConfiguration,
        MessageDialogIcon icon,
        string? details)
    {
        return ShowSerializedAsync(async () =>
        {
            if (!_windowContextProvider.TryGetContext(out _, out var xamlRoot))
            {
                return buttonConfiguration.CloseResult;
            }

            var dialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = title,
                Content = BuildMessageContent(message, icon, details),
                PrimaryButtonText = buttonConfiguration.PrimaryButtonText,
                SecondaryButtonText = buttonConfiguration.SecondaryButtonText,
                CloseButtonText = buttonConfiguration.CloseButtonText,
                DefaultButton = buttonConfiguration.PrimaryButtonText is not null
                    ? ContentDialogButton.Primary
                    : ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            return result switch
            {
                ContentDialogResult.Primary => buttonConfiguration.PrimaryResult,
                ContentDialogResult.Secondary => buttonConfiguration.SecondaryResult,
                _ => buttonConfiguration.CloseResult
            };
        });
    }

    private async Task<T> ShowSerializedAsync<T>(Func<Task<T>> show)
    {
        await _dialogLock.WaitAsync();
        try
        {
            T result = default!;
            await _uiDispatcher.InvokeAsync(async () => result = await show());
            return result;
        }
        finally
        {
            _dialogLock.Release();
        }
    }

    private static FrameworkElement BuildMessageContent(
        string message,
        MessageDialogIcon icon,
        string? details)
    {
        var panel = new StackPanel
        {
            Spacing = 12,
            MaxWidth = 520
        };

        var messagePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12
        };

        var iconGlyph = GetIconGlyph(icon);
        if (!string.IsNullOrEmpty(iconGlyph))
        {
            messagePanel.Children.Add(new TextBlock
            {
                Text = iconGlyph,
                FontSize = 28,
                VerticalAlignment = VerticalAlignment.Top
            });
        }

        messagePanel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(messagePanel);

        if (!string.IsNullOrWhiteSpace(details))
        {
            panel.Children.Add(new Expander
            {
                Header = "Details",
                Content = new ScrollViewer
                {
                    MaxHeight = 180,
                    Content = new TextBlock
                    {
                        Text = details,
                        FontFamily = new FontFamily("Consolas"),
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            });
        }

        return panel;
    }

    private static FrameworkElement BuildBackupFailureContent(string pluginName, string errorMessage) =>
        new StackPanel
        {
            Spacing = 12,
            MaxWidth = 520,
            Children =
            {
                new TextBlock
                {
                    Text = $"Failed to back up '{pluginName}'",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock
                {
                    Text = errorMessage,
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock
                {
                    Text = "How would you like to proceed?",
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };

    private static string GetIconGlyph(MessageDialogIcon icon) =>
        icon switch
        {
            MessageDialogIcon.Information => "ℹ",
            MessageDialogIcon.Warning => "⚠",
            MessageDialogIcon.Error => "❌",
            MessageDialogIcon.Question => "?",
            _ => string.Empty
        };
}
