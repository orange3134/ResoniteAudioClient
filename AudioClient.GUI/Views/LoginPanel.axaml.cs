using System.ComponentModel;
using Avalonia.Controls;
using AudioClient.GUI.ViewModels;

namespace AudioClient.GUI.Views;

public partial class LoginPanel : UserControl
{
    private LoginViewModel? _vm;
    private PropertyChangedEventHandler? _handler;

    public LoginPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_vm != null && _handler != null)
            _vm.PropertyChanged -= _handler;

        _vm = DataContext as LoginViewModel;

        if (_vm != null)
        {
            _handler = (_, args) =>
            {
                if (args.PropertyName == nameof(LoginViewModel.IsVisible))
                    IsVisible = _vm.IsVisible;
            };
            _vm.PropertyChanged += _handler;
            IsVisible = _vm.IsVisible;
        }
    }
}
