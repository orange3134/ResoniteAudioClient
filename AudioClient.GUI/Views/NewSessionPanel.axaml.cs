using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using AudioClient.GUI.ViewModels;

namespace AudioClient.GUI.Views;

public partial class NewSessionPanel : UserControl
{
    private NewSessionViewModel? _vm;
    private PropertyChangedEventHandler? _handler;

    public NewSessionPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null && _handler != null)
            _vm.PropertyChanged -= _handler;

        _vm = DataContext as NewSessionViewModel;

        if (_vm != null)
        {
            _handler = (_, args) =>
            {
                if (args.PropertyName == nameof(NewSessionViewModel.IsVisible))
                    IsVisible = _vm.IsVisible;
            };
            _vm.PropertyChanged += _handler;
            IsVisible = _vm.IsVisible;
        }
    }

    private void Backdrop_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _vm?.CancelCommand.Execute(null);
    }

    private void Card_PointerPressed(object? sender, PointerPressedEventArgs e)
        => e.Handled = true;
}
