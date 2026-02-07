using System;
using System.Reactive.Linq;
using Avalonia.Controls;
using AutoQAC.ViewModels;

namespace AutoQAC.Views;

public partial class RestoreWindow : Window
{
    public RestoreWindow()
    {
        InitializeComponent();
    }

    public RestoreWindow(RestoreViewModel viewModel) : this()
    {
        DataContext = viewModel;

        // Subscribe to CloseRequested to close the window
        viewModel.CloseRequested += OnCloseRequested;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Trigger session loading when the window opens
        if (DataContext is RestoreViewModel vm)
        {
            vm.LoadSessionsCommand.Execute().Subscribe();
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        Close();
    }
}
