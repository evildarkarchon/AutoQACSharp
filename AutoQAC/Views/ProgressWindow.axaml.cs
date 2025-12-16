using System;
using AutoQAC.ViewModels;
using Avalonia.Controls;

namespace AutoQAC.Views;

public partial class ProgressWindow : Window
{
    public ProgressWindow()
    {
        InitializeComponent();
        
        // Subscribe to ViewModel's CloseRequested event when DataContext changes
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ProgressViewModel viewModel)
        {
            viewModel.CloseRequested += OnCloseRequested;
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        // Dispose the ViewModel before closing
        if (DataContext is ProgressViewModel viewModel)
        {
            viewModel.CloseRequested -= OnCloseRequested;
            viewModel.Dispose();
        }
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        // Clean up subscription if window is closed via X button
        if (DataContext is ProgressViewModel viewModel)
        {
            viewModel.CloseRequested -= OnCloseRequested;
            viewModel.Dispose();
        }
        base.OnClosed(e);
    }
}
