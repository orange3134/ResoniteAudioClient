using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using AudioClient.GUI.ViewModels;

namespace AudioClient.GUI.Views;

public partial class SettingsPanel : UserControl
{
    private SettingsViewModel? _vm;
    private PropertyChangedEventHandler? _handler;

    public SettingsPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null && _handler != null)
            _vm.PropertyChanged -= _handler;

        _vm = DataContext as SettingsViewModel;

        if (_vm != null)
        {
            _handler = (_, args) =>
            {
                if (args.PropertyName == nameof(SettingsViewModel.IsVisible))
                    IsVisible = _vm.IsVisible;
            };
            _vm.PropertyChanged += _handler;
            IsVisible = _vm.IsVisible;
        }
    }

    private void Backdrop_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _vm?.CloseCommand.Execute(null);
    }

    private void Card_PointerPressed(object? sender, PointerPressedEventArgs e)
        => e.Handled = true;
}
