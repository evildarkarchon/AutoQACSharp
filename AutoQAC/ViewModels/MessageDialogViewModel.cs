using System;
using AutoQAC.Services.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoQAC.ViewModels;

public sealed partial class MessageDialogViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetails))]
    private string? _details;

    [ObservableProperty]
    private bool _showDetailsExpanded;

    [ObservableProperty]
    private string _iconGlyph = string.Empty;

    [ObservableProperty]
    private string _iconColor = "Gray";

    [ObservableProperty]
    private bool _showOkButton;

    [ObservableProperty]
    private bool _showCancelButton;

    [ObservableProperty]
    private bool _showYesButton;

    [ObservableProperty]
    private bool _showNoButton;

    [ObservableProperty]
    private bool _showRetryButton;

    public bool HasDetails => !string.IsNullOrEmpty(Details);

    /// <summary>Raised when the user picks a button. The view closes the dialog with this value.</summary>
    public event Action<MessageDialogResult>? CloseRequested;

    [RelayCommand]
    private void Ok() => CloseRequested?.Invoke(MessageDialogResult.Ok);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(MessageDialogResult.Cancel);

    [RelayCommand]
    private void Yes() => CloseRequested?.Invoke(MessageDialogResult.Yes);

    [RelayCommand]
    private void No() => CloseRequested?.Invoke(MessageDialogResult.No);

    [RelayCommand]
    private void Retry() => CloseRequested?.Invoke(MessageDialogResult.Retry);

    [RelayCommand]
    private void ToggleDetails() => ShowDetailsExpanded = !ShowDetailsExpanded;

    public void ConfigureButtons(MessageDialogButtons buttons)
    {
        ShowOkButton = false;
        ShowCancelButton = false;
        ShowYesButton = false;
        ShowNoButton = false;
        ShowRetryButton = false;

        switch (buttons)
        {
            case MessageDialogButtons.Ok:
                ShowOkButton = true;
                break;
            case MessageDialogButtons.OkCancel:
                ShowOkButton = true;
                ShowCancelButton = true;
                break;
            case MessageDialogButtons.YesNo:
                ShowYesButton = true;
                ShowNoButton = true;
                break;
            case MessageDialogButtons.YesNoCancel:
                ShowYesButton = true;
                ShowNoButton = true;
                ShowCancelButton = true;
                break;
            case MessageDialogButtons.RetryCancel:
                ShowRetryButton = true;
                ShowCancelButton = true;
                break;
        }
    }

    public void ConfigureIcon(MessageDialogIcon icon)
    {
        switch (icon)
        {
            case MessageDialogIcon.Information:
                IconGlyph = "ℹ";
                IconColor = "#0078D4";
                break;
            case MessageDialogIcon.Warning:
                IconGlyph = "⚠";
                IconColor = "#FF8C00";
                break;
            case MessageDialogIcon.Error:
                IconGlyph = "❌";
                IconColor = "#D13438";
                break;
            case MessageDialogIcon.Question:
                IconGlyph = "?";
                IconColor = "#0078D4";
                break;
            case MessageDialogIcon.None:
            default:
                IconGlyph = string.Empty;
                IconColor = "Gray";
                break;
        }
    }
}
