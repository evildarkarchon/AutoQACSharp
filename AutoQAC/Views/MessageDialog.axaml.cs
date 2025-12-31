using System;
using AutoQAC.ViewModels;
using Avalonia.Data.Converters;
using ReactiveUI;
using ReactiveUI.Avalonia;

namespace AutoQAC.Views;

public partial class MessageDialog : ReactiveWindow<MessageDialogViewModel>
{
    /// <summary>
    /// Converter for the details expand/collapse button text.
    /// </summary>
    public static readonly FuncValueConverter<bool, string> DetailsButtonConverter =
        new(isExpanded => isExpanded ? "Hide Details" : "Show Details");

    public MessageDialog()
    {
        InitializeComponent();

        // Make converter available to XAML
        Resources["DetailsButtonConverter"] = DetailsButtonConverter;

        this.WhenActivated(d =>
        {
            if (ViewModel == null) return;

            d(ViewModel.OkCommand.Subscribe(result => Close(result)));
            d(ViewModel.CancelCommand.Subscribe(result => Close(result)));
            d(ViewModel.YesCommand.Subscribe(result => Close(result)));
            d(ViewModel.NoCommand.Subscribe(result => Close(result)));
            d(ViewModel.RetryCommand.Subscribe(result => Close(result)));
        });
    }
}
