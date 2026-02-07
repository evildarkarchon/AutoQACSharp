using AutoQAC.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AutoQAC.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
    }

    public AboutWindow(AboutViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
