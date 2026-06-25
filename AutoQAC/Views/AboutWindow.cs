using System.Threading.Tasks;
using AutoQAC.Services.UI;
using AutoQAC.ViewModels;
using AutoQAC.Views.Helpers;

namespace AutoQAC.Views;

public sealed class AboutWindow(IWindowContextProvider windowContextProvider, AboutViewModel viewModel)
{
    public Task ShowAsync()
    {
        var content = new AboutContent
        {
            DataContext = viewModel
        };

        return ContentDialogPresenter.ShowAsync(
            windowContextProvider,
            "About AutoQAC",
            content);
    }
}
