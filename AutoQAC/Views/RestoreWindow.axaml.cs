using System;
using AutoQAC.ViewModels;
using Avalonia.Controls;

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
        viewModel.CloseRequested += OnCloseRequested;
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (DataContext is RestoreViewModel vm)
        {
            await vm.LoadSessionsCommand.ExecuteAsync(null);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is RestoreViewModel vm)
        {
            vm.CloseRequested -= OnCloseRequested;
        }
        base.OnClosed(e);
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        Close();
    }
}
