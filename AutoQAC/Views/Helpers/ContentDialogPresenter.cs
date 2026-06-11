using System;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Services.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AutoQAC.Views.Helpers;

public static class ContentDialogPresenter
{
    private static readonly SemaphoreSlim DialogLock = new(1, 1);

    public static async Task<bool> ShowBooleanAsync(
        IWindowContextProvider windowContextProvider,
        string title,
        FrameworkElement content,
        Action<Action<bool>> subscribeCloseRequested,
        Action<Action<bool>> unsubscribeCloseRequested)
    {
        if (!windowContextProvider.TryGetContext(out _, out var xamlRoot))
        {
            return false;
        }

        await DialogLock.WaitAsync();
        try
        {
            var result = false;
            var hasResult = false;

            var dialog = CreateDialog(title, content, xamlRoot);

            void OnCloseRequested(bool closeResult)
            {
                result = closeResult;
                hasResult = true;
                dialog.Hide();
            }

            subscribeCloseRequested(OnCloseRequested);
            try
            {
                await dialog.ShowAsync();
            }
            finally
            {
                unsubscribeCloseRequested(OnCloseRequested);
            }

            return hasResult && result;
        }
        finally
        {
            DialogLock.Release();
        }
    }

    public static async Task ShowAsync(
        IWindowContextProvider windowContextProvider,
        string title,
        FrameworkElement content)
    {
        if (!windowContextProvider.TryGetContext(out _, out var xamlRoot))
        {
            return;
        }

        await DialogLock.WaitAsync();
        try
        {
            var dialog = CreateDialog(title, content, xamlRoot);
            dialog.CloseButtonText = "Close";
            await dialog.ShowAsync();
        }
        finally
        {
            DialogLock.Release();
        }
    }

    private static ContentDialog CreateDialog(string title, FrameworkElement content, XamlRoot xamlRoot)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = content,
            DefaultButton = ContentDialogButton.Primary
        };

        if (Application.Current.Resources.TryGetValue("DefaultContentDialogStyle", out var style) &&
            style is Style contentDialogStyle)
        {
            dialog.Style = contentDialogStyle;
        }

        return dialog;
    }
}
