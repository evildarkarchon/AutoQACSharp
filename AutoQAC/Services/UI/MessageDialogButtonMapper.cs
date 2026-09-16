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
    public static MessageDialogButtonConfiguration Build(MessageDialogButtons buttons)
    {
        return buttons switch
        {
            MessageDialogButtons.Ok => new MessageDialogButtonConfiguration(
                null,
                null,
                "OK",
                MessageDialogResult.None,
                MessageDialogResult.None,
                MessageDialogResult.Ok),

            MessageDialogButtons.OkCancel => new MessageDialogButtonConfiguration(
                "OK",
                null,
                "Cancel",
                MessageDialogResult.Ok,
                MessageDialogResult.None,
                MessageDialogResult.Cancel),

            MessageDialogButtons.YesNo => new MessageDialogButtonConfiguration(
                "Yes",
                "No",
                null,
                MessageDialogResult.Yes,
                MessageDialogResult.No,
                MessageDialogResult.None),

            MessageDialogButtons.YesNoCancel => new MessageDialogButtonConfiguration(
                "Yes",
                "No",
                "Cancel",
                MessageDialogResult.Yes,
                MessageDialogResult.No,
                MessageDialogResult.Cancel),

            MessageDialogButtons.RetryCancel => new MessageDialogButtonConfiguration(
                "Retry",
                null,
                "Cancel",
                MessageDialogResult.Retry,
                MessageDialogResult.None,
                MessageDialogResult.Cancel),

            _ => Build(MessageDialogButtons.Ok)
        };
    }

    public static MessageDialogButtonConfiguration BuildChoice(
        string primaryButtonText,
        string secondaryButtonText)
    {
        return new MessageDialogButtonConfiguration(
            primaryButtonText,
            secondaryButtonText,
            null,
            MessageDialogResult.Yes,
            MessageDialogResult.No,
            MessageDialogResult.None);
    }
}