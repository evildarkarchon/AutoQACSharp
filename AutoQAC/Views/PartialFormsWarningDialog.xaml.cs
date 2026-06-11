using System.Threading.Tasks;
using AutoQAC.Services.UI;
using AutoQAC.ViewModels;
using AutoQAC.Views.Helpers;

namespace AutoQAC.Views;

public sealed class PartialFormsWarningDialog
{
    private readonly IWindowContextProvider _windowContextProvider;

    public PartialFormsWarningDialog(IWindowContextProvider windowContextProvider)
    {
        _windowContextProvider = windowContextProvider;
    }

    public Task<bool> ShowAsync(PartialFormsWarningViewModel viewModel)
    {
        var content = new PartialFormsWarningContent
        {
            DataContext = viewModel
        };

        return ContentDialogPresenter.ShowBooleanAsync(
            _windowContextProvider,
            "Partial Forms Warning",
            content,
            handler => viewModel.CloseRequested += handler,
            handler => viewModel.CloseRequested -= handler);
    }
}
