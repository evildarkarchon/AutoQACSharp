using System;
using AutoQAC.ViewModels;
using Avalonia.Controls;
using ReactiveUI.Avalonia;
using ReactiveUI;

namespace AutoQAC.Views;

public partial class PartialFormsWarningDialog : ReactiveWindow<PartialFormsWarningViewModel>
{
    public PartialFormsWarningDialog()
    {
        InitializeComponent();
        this.WhenActivated(d =>
        {
            if (ViewModel != null)
            {
                d(ViewModel.EnableCommand.Subscribe(result => Close(result)));
                d(ViewModel.CancelCommand.Subscribe(result => Close(result)));
            }
        });
    }
}
