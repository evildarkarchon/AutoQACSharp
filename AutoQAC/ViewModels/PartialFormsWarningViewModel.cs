using System;
using CommunityToolkit.Mvvm.Input;

namespace AutoQAC.ViewModels;

public sealed partial class PartialFormsWarningViewModel : ViewModelBase
{
    /// <summary>Raised when the user picks a button. The view closes the dialog with this value.</summary>
    public event Action<bool>? CloseRequested;

    [RelayCommand]
    private void Enable() => CloseRequested?.Invoke(true);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
