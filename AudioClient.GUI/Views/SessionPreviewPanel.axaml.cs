using System.ComponentModel;
using Avalonia.Controls;
using AudioClient.GUI.ViewModels;

namespace AudioClient.GUI.Views;

public partial class SessionPreviewPanel : UserControl
{
    private SessionPreviewViewModel? _vm;
    private PropertyChangedEventHandler? _handler;

    public SessionPreviewPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_vm != null && _handler != null)
            _vm.PropertyChanged -= _handler;

        _vm = DataContext as SessionPreviewViewModel;

        if (_vm != null)
        {
            _handler = (_, args) =>
            {
                if (args.PropertyName == nameof(SessionPreviewViewModel.IsVisible))
                    IsVisible = _vm.IsVisible;
            };
            _vm.PropertyChanged += _handler;
            IsVisible = _vm.IsVisible;
        }
    }
}
