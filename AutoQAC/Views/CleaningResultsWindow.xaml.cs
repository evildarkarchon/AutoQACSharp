using System;
using AutoQAC.ViewModels;
using AutoQAC.Views.Helpers;
using Microsoft.UI.Xaml;

namespace AutoQAC.Views;

public sealed partial class CleaningResultsWindow
{
    private CleaningResultsViewModel? _viewModel;

    public CleaningResultsWindow()
    {
        InitializeComponent();
        WindowSizing.Resize(this, 600, 500);
        Closed += OnClosed;
    }

    public CleaningResultsWindow(CleaningResultsViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        Root.DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        Close();
    }

    private void OnClosed(object sender, WindowEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.CloseRequested -= OnCloseRequested;
            _viewModel = null;
        }

        Closed -= OnClosed;
    }
}
