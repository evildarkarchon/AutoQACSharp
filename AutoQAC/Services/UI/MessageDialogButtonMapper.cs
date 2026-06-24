namespace AutoQAC.Services.UI;

internal sealed record MessageDialogButtonConfiguration(
    string? PrimaryButtonText,
    string? SecondaryButtonText,
    string? CloseButtonText,
    MessageDialogResult PrimaryResult,
    MessageDialogResult SecondaryResult,
    MessageDialogResult CloseResult);

internal static class MessageDialogButtonMapper
{
    public static MessageDialogButtonConfiguration Build(MessageDialogButtons buttons) =>
        buttons switch
        {
            MessageDialogButtons.Ok => new MessageDialogButtonConfiguration(
                PrimaryButtonText: null,
                SecondaryButtonText: null,
                CloseButtonText: "OK",
                PrimaryResult: MessageDialogResult.None,
                SecondaryResult: MessageDialogResult.None,
                CloseResult: MessageDialogResult.Ok),

            MessageDialogButtons.OkCancel => new MessageDialogButtonConfiguration(
                PrimaryButtonText: "OK",
                SecondaryButtonText: null,
                CloseButtonText: "Cancel",
                PrimaryResult: MessageDialogResult.Ok,
                SecondaryResult: MessageDialogResult.None,
                CloseResult: MessageDialogResult.Cancel),

            MessageDialogButtons.YesNo => new MessageDialogButtonConfiguration(
                PrimaryButtonText: "Yes",
                SecondaryButtonText: "No",
                CloseButtonText: null,
                PrimaryResult: MessageDialogResult.Yes,
                SecondaryResult: MessageDialogResult.No,
                CloseResult: MessageDialogResult.None),

            MessageDialogButtons.YesNoCancel => new MessageDialogButtonConfiguration(
                PrimaryButtonText: "Yes",
                SecondaryButtonText: "No",
                CloseButtonText: "Cancel",
                PrimaryResult: MessageDialogResult.Yes,
                SecondaryResult: MessageDialogResult.No,
                CloseResult: MessageDialogResult.Cancel),

            MessageDialogButtons.RetryCancel => new MessageDialogButtonConfiguration(
                PrimaryButtonText: "Retry",
                SecondaryButtonText: null,
                CloseButtonText: "Cancel",
                PrimaryResult: MessageDialogResult.Retry,
                SecondaryResult: MessageDialogResult.None,
                CloseResult: MessageDialogResult.Cancel),

            _ => Build(MessageDialogButtons.Ok)
        };

    public static MessageDialogButtonConfiguration BuildChoice(
        string primaryButtonText,
        string secondaryButtonText) =>
        new(
            PrimaryButtonText: primaryButtonText,
            SecondaryButtonText: secondaryButtonText,
            CloseButtonText: null,
            PrimaryResult: MessageDialogResult.Yes,
            SecondaryResult: MessageDialogResult.No,
            CloseResult: MessageDialogResult.None);
}
