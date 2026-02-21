using System;
using AutoQAC.ViewModels;
using Avalonia.Controls;

namespace AutoQAC.Views;

public partial class ProgressWindow : Window
{
    private ProgressViewModel? _subscribedViewModel;
    private bool _disposeHandled;

    public ProgressWindow()
    {
        InitializeComponent();

        // Subscribe to ViewModel's CloseRequested event when DataContext changes
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.CloseRequested -= OnCloseRequested;
            _subscribedViewModel = null;
        }

        if (DataContext is ProgressViewModel viewModel && !_disposeHandled)
        {
            _subscribedViewModel = viewModel;
            viewModel.CloseRequested += OnCloseRequested;
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        DisposeViewModelIfNeeded();
        Close();
    }

    /// <summary>
    /// Prevents closing the window while cleaning is in progress.
    /// The user must use the Stop button first, then close after cleaning completes.
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is ProgressViewModel { IsCleaning: true })
        {
            e.Cancel = true;
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        DisposeViewModelIfNeeded();
        base.OnClosed(e);
    }

    private void DisposeViewModelIfNeeded()
    {
        if (_disposeHandled)
        {
            return;
        }

        _disposeHandled = true;
        DataContextChanged -= OnDataContextChanged;

        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.CloseRequested -= OnCloseRequested;
            _subscribedViewModel.Dispose();
            _subscribedViewModel = null;
        }
    }
}
