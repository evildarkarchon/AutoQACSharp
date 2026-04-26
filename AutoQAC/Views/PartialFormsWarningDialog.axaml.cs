using System;
using AutoQAC.ViewModels;
using Avalonia.Controls;

namespace AutoQAC.Views;

public partial class PartialFormsWarningDialog : Window
{
    private PartialFormsWarningViewModel? _vm;

    public PartialFormsWarningDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.CloseRequested -= OnCloseRequested;
            _vm = null;
        }
        if (DataContext is PartialFormsWarningViewModel vm)
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
