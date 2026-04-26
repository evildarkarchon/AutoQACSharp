using System;
using AutoQAC.ViewModels;
using Avalonia.Controls;

namespace AutoQAC.Views;

public partial class CleaningResultsWindow : Window
{
    public CleaningResultsWindow()
    {
        InitializeComponent();
    }

    public CleaningResultsWindow(CleaningResultsViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is CleaningResultsViewModel vm)
        {
            vm.CloseRequested -= OnCloseRequested;
        }
        base.OnClosed(e);
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();
}
