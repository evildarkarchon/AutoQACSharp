using System.Reactive;
using AutoQAC.Models;
using ReactiveUI;

namespace AutoQAC.ViewModels;

public sealed class PartialFormsWarningViewModel : ViewModelBase
{
    public ReactiveCommand<Unit, bool> EnableCommand { get; }
    public ReactiveCommand<Unit, bool> CancelCommand { get; }

    public PartialFormsWarningViewModel()
    {
        EnableCommand = ReactiveCommand.Create(() => true);
        CancelCommand = ReactiveCommand.Create(() => false);
    }
}
