using System;
using AutoQAC.Services.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoQAC.ViewModels;

public sealed partial class MessageDialogViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetails))]
    public partial string? Details { get; set; }

    [ObservableProperty]
    public partial bool ShowDetailsExpanded { get; set; }

    [ObservableProperty]
    public partial string IconGlyph { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string IconColor { get; set; } = "Gray";

    [ObservableProperty]
    public partial bool ShowOkButton { get; set; }

    [ObservableProperty]
    public partial bool ShowCancelButton { get; set; }

    [ObservableProperty]
    public partial bool ShowYesButton { get; set; }

    [ObservableProperty]
    public partial bool ShowNoButton { get; set; }

    [ObservableProperty]
    public partial string YesButtonText { get; set; } = "Yes";

    [ObservableProperty]
    public partial string NoButtonText { get; set; } = "No";

    [ObservableProperty]
    public partial bool ShowRetryButton { get; set; }

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
