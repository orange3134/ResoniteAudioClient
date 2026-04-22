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

        try
        {
            if (DataContext is MainViewModel vm)
                await vm.ShutdownAsync();
        }
        finally
        {
            // FrooxEngine keeps internal non-background threads alive after Dispose(),
            // so we force-exit to guarantee the process terminates.
            Environment.Exit(0);
        }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Close();
}
