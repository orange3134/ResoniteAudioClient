using System.ComponentModel;
using Avalonia.Controls;
using AudioClient.GUI.ViewModels;

namespace AudioClient.GUI.Views;

public partial class UserInfoPanel : UserControl
{
    private UserInfoViewModel? _vm;
    private PropertyChangedEventHandler? _handler;

    public UserInfoPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_vm != null && _handler != null)
            _vm.PropertyChanged -= _handler;

        _vm = DataContext as UserInfoViewModel;

        if (_vm != null)
        {
            _handler = (_, args) =>
            {
                if (args.PropertyName == nameof(UserInfoViewModel.IsVisible))
                    IsVisible = _vm.IsVisible;
            };
            _vm.PropertyChanged += _handler;
            IsVisible = _vm.IsVisible;
        }
    }
}
