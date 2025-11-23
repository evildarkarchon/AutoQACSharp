using System.Threading.Tasks;
using AutoQAC.ViewModels;
using FluentAssertions;
using Xunit;
using System.Reactive.Linq;

namespace AutoQAC.Tests.ViewModels;

public sealed class PartialFormsWarningViewModelTests
{
    [Fact]
    public async Task EnableCommand_ShouldReturnTrue()
    {
        var vm = new PartialFormsWarningViewModel();
        var result = await vm.EnableCommand.Execute();
        result.Should().BeTrue();
    }

    [Fact]
    public async Task CancelCommand_ShouldReturnFalse()
    {
        var vm = new PartialFormsWarningViewModel();
        var result = await vm.CancelCommand.Execute();
        result.Should().BeFalse();
    }
}
