using System.Reactive;
using ReactiveUI;

namespace AutoQAC.ViewModels;

public sealed class PartialFormsWarningViewModel : ViewModelBase
{
    public ReactiveCommand<Unit, bool> EnableCommand { get; } = ReactiveCommand.Create(() => true);
    public ReactiveCommand<Unit, bool> CancelCommand { get; } = ReactiveCommand.Create(() => false);
}
