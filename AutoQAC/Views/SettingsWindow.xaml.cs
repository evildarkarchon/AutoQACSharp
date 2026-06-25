using System.Threading.Tasks;
using AutoQAC.Services.UI;
using AutoQAC.ViewModels;
using AutoQAC.Views.Helpers;

namespace AutoQAC.Views;

public sealed class SettingsWindow(IWindowContextProvider windowContextProvider, SettingsViewModel viewModel)
{
    public Task<bool> ShowAsync()
    {
        var content = new SettingsContent
        {
            DataContext = viewModel
        };

        return ContentDialogPresenter.ShowBooleanAsync(
            windowContextProvider,
            "Settings",
            content,
            handler => viewModel.CloseRequested += handler,
            handler => viewModel.CloseRequested -= handler);
    }
}
