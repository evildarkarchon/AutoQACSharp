using System;
using Avalonia.Controls;
using AutoQAC.ViewModels;

namespace AutoQAC.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    public SettingsWindow(SettingsViewModel viewModel) : this()
    {
        DataContext = viewModel;

        // Wire up commands to close the window with appropriate result
        viewModel.SaveCommand.Subscribe(result => Close(result));
        viewModel.CancelCommand.Subscribe(result => Close(result));
    }
}
