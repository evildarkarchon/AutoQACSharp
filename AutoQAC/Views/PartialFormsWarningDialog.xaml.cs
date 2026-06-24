using System.Threading.Tasks;
using AutoQAC.Services.UI;
using AutoQAC.ViewModels;
using AutoQAC.Views.Helpers;

namespace AutoQAC.Views;

public sealed class PartialFormsWarningDialog(IWindowContextProvider windowContextProvider)
{
    public Task<bool> ShowAsync(PartialFormsWarningViewModel viewModel)
    {
        var content = new PartialFormsWarningContent
        {
            DataContext = viewModel
        };

        return ContentDialogPresenter.ShowBooleanAsync(
            windowContextProvider,
            "Partial Forms Warning",
            content,
            handler => viewModel.CloseRequested += handler,
            handler => viewModel.CloseRequested -= handler);
    }
}
