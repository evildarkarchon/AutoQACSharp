using AutoQAC.ViewModels;
using AutoQAC.Views.Helpers;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace AutoQAC.Views;

public sealed partial class ProgressWindow
{
    private ProgressViewModel? _subscribedViewModel;
    private bool _disposeHandled;

    public ProgressWindow()
    {
        InitializeComponent();
        WindowSizing.Resize(this, 600, 450);
        Root.DataContextChanged += OnDataContextChanged;
        AppWindow.Closing += OnAppWindowClosing;
        Closed += OnClosed;
    }

    public ProgressWindow(ProgressViewModel viewModel) : this()
    {
        Root.DataContext = viewModel;
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.CloseRequested -= OnCloseRequested;
            _subscribedViewModel = null;
        }

        if (Root.DataContext is ProgressViewModel viewModel && !_disposeHandled)
        {
            _subscribedViewModel = viewModel;
            viewModel.CloseRequested += OnCloseRequested;
        }
    }

    private void OnCloseRequested(object? sender, System.EventArgs e)
    {
        DisposeViewModelIfNeeded();
        Close();
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (Root.DataContext is ProgressViewModel { IsCleaning: true })
        {
            args.Cancel = true;
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        DisposeViewModelIfNeeded();
    }

    private void DisposeViewModelIfNeeded()
    {
        if (_disposeHandled)
        {
            return;
        }

        _disposeHandled = true;
        Root.DataContextChanged -= OnDataContextChanged;
        AppWindow.Closing -= OnAppWindowClosing;
        Closed -= OnClosed;

        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.CloseRequested -= OnCloseRequested;
            _subscribedViewModel.Dispose();
            _subscribedViewModel = null;
        }
    }
}
