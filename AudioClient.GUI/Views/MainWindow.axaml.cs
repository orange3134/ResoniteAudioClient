using Avalonia.Controls;
using Avalonia.Input;
using AudioClient.GUI.ViewModels;

namespace AudioClient.GUI.Views;

public partial class MainWindow : Window
{
    private bool _isShuttingDown = false;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_isShuttingDown)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        _isShuttingDown = true;

        if (DataContext is MainViewModel vm)
            await vm.ShutdownAsync();

        Close();
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Close();
}
