using System.Reactive;
using AutoQAC.Services.UI;
using ReactiveUI;

namespace AutoQAC.ViewModels;

/// <summary>
/// ViewModel for a generic message dialog.
/// </summary>
public sealed class MessageDialogViewModel : ViewModelBase
{
    private string _title = string.Empty;
    private string _message = string.Empty;
    private string? _details;
    private bool _hasDetails;
    private bool _showDetailsExpanded;
    private string _iconGlyph = string.Empty;
    private string _iconColor = "Gray";

    // Button visibility
    private bool _showOkButton;
    private bool _showCancelButton;
    private bool _showYesButton;
    private bool _showNoButton;
    private bool _showRetryButton;

    public string Title
    {
        get => _title;
        set => this.RaiseAndSetIfChanged(ref _title, value);
    }

    public string Message
    {
        get => _message;
        set => this.RaiseAndSetIfChanged(ref _message, value);
    }

    public string? Details
    {
        get => _details;
        set
        {
            this.RaiseAndSetIfChanged(ref _details, value);
            HasDetails = !string.IsNullOrEmpty(value);
        }
    }

    public bool HasDetails
    {
        get => _hasDetails;
        private set => this.RaiseAndSetIfChanged(ref _hasDetails, value);
    }

    public bool ShowDetailsExpanded
    {
        get => _showDetailsExpanded;
        set => this.RaiseAndSetIfChanged(ref _showDetailsExpanded, value);
    }

    public string IconGlyph
    {
        get => _iconGlyph;
        set => this.RaiseAndSetIfChanged(ref _iconGlyph, value);
    }

    public string IconColor
    {
        get => _iconColor;
        set => this.RaiseAndSetIfChanged(ref _iconColor, value);
    }

    public bool ShowOkButton
    {
        get => _showOkButton;
        set => this.RaiseAndSetIfChanged(ref _showOkButton, value);
    }

    public bool ShowCancelButton
    {
        get => _showCancelButton;
        set => this.RaiseAndSetIfChanged(ref _showCancelButton, value);
    }

    public bool ShowYesButton
    {
        get => _showYesButton;
        set => this.RaiseAndSetIfChanged(ref _showYesButton, value);
    }

    public bool ShowNoButton
    {
        get => _showNoButton;
        set => this.RaiseAndSetIfChanged(ref _showNoButton, value);
    }

    public bool ShowRetryButton
    {
        get => _showRetryButton;
        set => this.RaiseAndSetIfChanged(ref _showRetryButton, value);
    }

    // Commands
    public ReactiveCommand<Unit, MessageDialogResult> OkCommand { get; }
    public ReactiveCommand<Unit, MessageDialogResult> CancelCommand { get; }
    public ReactiveCommand<Unit, MessageDialogResult> YesCommand { get; }
    public ReactiveCommand<Unit, MessageDialogResult> NoCommand { get; }
    public ReactiveCommand<Unit, MessageDialogResult> RetryCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleDetailsCommand { get; }

    public MessageDialogViewModel()
    {
        OkCommand = ReactiveCommand.Create(() => MessageDialogResult.Ok);
        CancelCommand = ReactiveCommand.Create(() => MessageDialogResult.Cancel);
        YesCommand = ReactiveCommand.Create(() => MessageDialogResult.Yes);
        NoCommand = ReactiveCommand.Create(() => MessageDialogResult.No);
        RetryCommand = ReactiveCommand.Create(() => MessageDialogResult.Retry);
        ToggleDetailsCommand = ReactiveCommand.Create(() =>
        {
            ShowDetailsExpanded = !ShowDetailsExpanded;
        });
    }

    /// <summary>
    /// Configure the dialog for the specified button set.
    /// </summary>
    public void ConfigureButtons(MessageDialogButtons buttons)
    {
        // Reset all buttons
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

    /// <summary>
    /// Configure the icon for the specified type.
    /// </summary>
    public void ConfigureIcon(MessageDialogIcon icon)
    {
        switch (icon)
        {
            case MessageDialogIcon.Information:
                IconGlyph = "\u2139"; // Information symbol
                IconColor = "#0078D4"; // Blue
                break;
            case MessageDialogIcon.Warning:
                IconGlyph = "\u26A0"; // Warning triangle
                IconColor = "#FF8C00"; // Orange
                break;
            case MessageDialogIcon.Error:
                IconGlyph = "\u274C"; // Cross mark
                IconColor = "#D13438"; // Red
                break;
            case MessageDialogIcon.Question:
                IconGlyph = "?"; // Question mark
                IconColor = "#0078D4"; // Blue
                break;
            case MessageDialogIcon.None:
            default:
                IconGlyph = string.Empty;
                IconColor = "Gray";
                break;
        }
    }
}
