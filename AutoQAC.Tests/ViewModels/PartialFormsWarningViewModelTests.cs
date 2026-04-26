using AutoQAC.ViewModels;
using FluentAssertions;

namespace AutoQAC.Tests.ViewModels;

public sealed class PartialFormsWarningViewModelTests
{
    [Fact]
    public void EnableCommand_ShouldRequestCloseWithTrue()
    {
        var vm = new PartialFormsWarningViewModel();
        bool? result = null;
        vm.CloseRequested += r => result = r;

        vm.EnableCommand.Execute(null);

        result.Should().BeTrue();
    }

    [Fact]
    public void CancelCommand_ShouldRequestCloseWithFalse()
    {
        var vm = new PartialFormsWarningViewModel();
        bool? result = null;
        vm.CloseRequested += r => result = r;

        vm.CancelCommand.Execute(null);

        result.Should().BeFalse();
    }
}
