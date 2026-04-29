using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using AudioClient.GUI.ViewModels;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AudioClient.GUI.Views;

public partial class MainWindow : Window
{
    private bool _isShuttingDown = false;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ConfigureViewModel();
    }

    private void ConfigureViewModel()
    {
        if (DataContext is MainViewModel vm)
            vm.ImageViewer.OnSaveRequested = SaveImageAsync;
    }

    private async Task SaveImageAsync(ImageViewerViewModel viewer)
    {
        var bytes = viewer.ImageBytes;
        if (bytes == null || bytes.Length == 0)
            return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save chat image",
            SuggestedFileName = viewer.SuggestedFileName,
            FileTypeChoices = new List<FilePickerFileType>
            {
                new("Image files")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.gif", "*.bmp" }
                },
                FilePickerFileTypes.All
            }
        });

        if (file == null)
            return;

        await using var stream = await file.OpenWriteAsync();
        await stream.WriteAsync(bytes);
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
