using System.Threading.Tasks;
using AutoQAC.Services.UI;
using AutoQAC.ViewModels;
using AutoQAC.Views.Helpers;

namespace AutoQAC.Views;

public sealed class SkipListWindow
{
    private readonly IWindowContextProvider _windowContextProvider;
    private readonly SkipListViewModel _viewModel;

    public SkipListWindow(IWindowContextProvider windowContextProvider, SkipListViewModel viewModel)
    {
        _windowContextProvider = windowContextProvider;
        _viewModel = viewModel;
    }

    public Task<bool> ShowAsync()
    {
        var content = new SkipListContent
        {
            DataContext = _viewModel
        };

        return ContentDialogPresenter.ShowBooleanAsync(
            _windowContextProvider,
            "Skip List",
            content,
            handler => _viewModel.CloseRequested += handler,
            handler => _viewModel.CloseRequested -= handler);
    }
}
