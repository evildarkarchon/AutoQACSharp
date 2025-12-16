using System;
using Avalonia.Controls;
using AutoQAC.ViewModels;

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

        // Wire up the close command to close the window
        viewModel.CloseCommand.Subscribe(_ => Close());
    }
}
