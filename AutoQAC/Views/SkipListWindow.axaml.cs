using System;
using System.Reactive.Disposables;
using Avalonia.Controls;
using AutoQAC.ViewModels;

namespace AutoQAC.Views;

public partial class SkipListWindow : Window
{
    private readonly CompositeDisposable _disposables = new();

    public SkipListWindow()
    {
        InitializeComponent();
    }

    public SkipListWindow(SkipListViewModel viewModel) : this()
    {
        DataContext = viewModel;

        // Wire up commands to close the window with appropriate result
        _disposables.Add(viewModel.SaveCommand.Subscribe(result => Close(result)));
        _disposables.Add(viewModel.CancelCommand.Subscribe(result => Close(result)));
    }

    protected override void OnClosed(EventArgs e)
    {
        _disposables.Dispose();
        base.OnClosed(e);
    }
}
