using System;
using AutoQAC.Services.UI;
using AutoQAC.ViewModels;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace AutoQAC.Views;

public partial class MessageDialog : Window
{
    public static readonly FuncValueConverter<bool, string> DetailsButtonConverter =
        new(isExpanded => isExpanded ? "Hide Details" : "Show Details");

    private MessageDialogViewModel? _vm;

    public MessageDialog()
    {
        InitializeComponent();
        Resources["DetailsButtonConverter"] = DetailsButtonConverter;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.CloseRequested -= OnCloseRequested;
            _vm = null;
        }
        if (DataContext is MessageDialogViewModel vm)
        {
            _vm = vm;
            vm.CloseRequested += OnCloseRequested;
        }
    }

    private void OnCloseRequested(MessageDialogResult result) => Close(result);

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
