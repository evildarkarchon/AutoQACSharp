using System.Threading.Tasks;
using AutoQAC.Services.UI;
using AutoQAC.ViewModels;
using AutoQAC.Views.Helpers;

namespace AutoQAC.Views;

public sealed class SettingsWindow
{
    private readonly IWindowContextProvider _windowContextProvider;
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(IWindowContextProvider windowContextProvider, SettingsViewModel viewModel)
    {
        _windowContextProvider = windowContextProvider;
        _viewModel = viewModel;
    }

    public Task<bool> ShowAsync()
    {
        var content = new SettingsContent
        {
            DataContext = _viewModel
        };

        return ContentDialogPresenter.ShowBooleanAsync(
            _windowContextProvider,
            "Settings",
            content,
            handler => _viewModel.CloseRequested += handler,
            handler => _viewModel.CloseRequested -= handler);
    }
}
