using System;
using AutoQAC.ViewModels;
using Avalonia.Controls;

namespace AutoQAC.Views;

public partial class SkipListWindow : Window
{
    private SkipListViewModel? _vm;

    public SkipListWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    public SkipListWindow(SkipListViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.CloseRequested -= OnCloseRequested;
            _vm = null;
        }
        if (DataContext is SkipListViewModel vm)
        {
            _vm = vm;
            vm.CloseRequested += OnCloseRequested;
        }
    }

    private void OnCloseRequested(bool result) => Close(result);

    protected override void OnClosed(EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.CloseRequested -= OnCloseRequested;
            _vm = null;
        }
        DataContextChanged -= OnDataContextChanged;
        base.OnClosed(e);
    }
}
