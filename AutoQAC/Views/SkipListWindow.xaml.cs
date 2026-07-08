using System.Threading.Tasks;
using AutoQAC.Services.UI;
using AutoQAC.ViewModels;
using AutoQAC.Views.Helpers;

namespace AutoQAC.Views;

public sealed class SkipListWindow(IWindowContextProvider windowContextProvider, SkipListViewModel viewModel)
{
    public Task<bool> ShowAsync()
    {
        var content = new SkipListContent
        {
            DataContext = viewModel
        };

        return ContentDialogPresenter.ShowBooleanAsync(
            windowContextProvider,
            "Skip List",
            content,
            handler => viewModel.CloseRequested += handler,
            handler => viewModel.CloseRequested -= handler);
    }
}
