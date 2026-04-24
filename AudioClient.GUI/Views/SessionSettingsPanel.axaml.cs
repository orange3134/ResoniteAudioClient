using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using AudioClient.GUI.ViewModels;

namespace AudioClient.GUI.Views;

public partial class SessionSettingsPanel : UserControl
{
    private SessionDetailViewModel? _vm;
    private PropertyChangedEventHandler? _handler;

    public SessionSettingsPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null && _handler != null)
            _vm.PropertyChanged -= _handler;

        _vm = DataContext as SessionDetailViewModel;

        if (_vm != null)
        {
            _handler = (_, args) =>
            {
                if (args.PropertyName == nameof(SessionDetailViewModel.IsSettingsOpen))
                    IsVisible = _vm.IsSettingsOpen;
            };
            _vm.PropertyChanged += _handler;
            IsVisible = _vm.IsSettingsOpen;
        }
    }

    private void Backdrop_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _vm?.CloseSettingsCommand.Execute(null);
    }

    private void Card_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }
}
