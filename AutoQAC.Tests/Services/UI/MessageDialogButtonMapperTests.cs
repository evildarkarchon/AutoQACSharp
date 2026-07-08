using AutoQAC.Services.UI;
using FluentAssertions;

namespace AutoQAC.Tests.Services.UI;

public sealed class MessageDialogButtonMapperTests
{
    [Fact]
    public void Build_ShouldMapYesNoCancelButtons()
    {
        var result = MessageDialogButtonMapper.Build(MessageDialogButtons.YesNoCancel);

        result.PrimaryButtonText.Should().Be("Yes");
        result.SecondaryButtonText.Should().Be("No");
        result.CloseButtonText.Should().Be("Cancel");
        result.PrimaryResult.Should().Be(MessageDialogResult.Yes);
        result.SecondaryResult.Should().Be(MessageDialogResult.No);
        result.CloseResult.Should().Be(MessageDialogResult.Cancel);
    }

    [Fact]
    public void BuildChoice_ShouldUseCustomButtonTextWithYesNoResultContract()
    {
        var result = MessageDialogButtonMapper.BuildChoice("Continue", "Skip");

        result.PrimaryButtonText.Should().Be("Continue");
        result.SecondaryButtonText.Should().Be("Skip");
        result.CloseButtonText.Should().BeNull();
        result.PrimaryResult.Should().Be(MessageDialogResult.Yes);
        result.SecondaryResult.Should().Be(MessageDialogResult.No);
    }

    [Fact]
    public void Build_ShouldMapRetryCancelButtons()
    {
        var result = MessageDialogButtonMapper.Build(MessageDialogButtons.RetryCancel);

        result.PrimaryButtonText.Should().Be("Retry");
        result.SecondaryButtonText.Should().BeNull();
        result.CloseButtonText.Should().Be("Cancel");
        result.PrimaryResult.Should().Be(MessageDialogResult.Retry);
        result.CloseResult.Should().Be(MessageDialogResult.Cancel);
    }
}
