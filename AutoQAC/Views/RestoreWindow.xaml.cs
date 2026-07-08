using System;
using AutoQAC.ViewModels;
using AutoQAC.Views.Helpers;
using Microsoft.UI.Xaml;

namespace AutoQAC.Views;

public sealed partial class RestoreWindow
{
    private RestoreViewModel? _subscribedViewModel;

    public RestoreWindow()
    {
        InitializeComponent();
        WindowSizing.Resize(this, 700, 500);
        Closed += OnClosed;
    }

    public RestoreWindow(RestoreViewModel viewModel) : this()
    {
        Root.DataContext = viewModel;
        _subscribedViewModel = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
    }

    private void OnClosed(object sender, WindowEventArgs e)
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.CloseRequested -= OnCloseRequested;
            _subscribedViewModel = null;
        }

        Closed -= OnClosed;
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        Close();
    }
}
