using System.Threading.Tasks;
using AutoQAC.Services.UI;
using AutoQAC.ViewModels;
using AutoQAC.Views.Helpers;

namespace AutoQAC.Views;

public sealed class AboutWindow
{
    private readonly IWindowContextProvider _windowContextProvider;
    private readonly AboutViewModel _viewModel;

    public AboutWindow(IWindowContextProvider windowContextProvider, AboutViewModel viewModel)
    {
        _windowContextProvider = windowContextProvider;
        _viewModel = viewModel;
    }

    public Task ShowAsync()
    {
        var content = new AboutContent
        {
            DataContext = _viewModel
        };

        return ContentDialogPresenter.ShowAsync(
            _windowContextProvider,
            "About AutoQAC",
            content);
    }
}
