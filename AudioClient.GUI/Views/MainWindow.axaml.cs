using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using AudioClient.GUI.ViewModels;

namespace AudioClient.GUI.Views;

public partial class MainWindow : Window
{
    private bool _isShuttingDown = false;
    private const double MinChatHeight = 150;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ConfigureViewModel();
        CenterContentGrid.SizeChanged += OnCenterContentGridSizeChanged;
    }

    private void OnCenterContentGridSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        VideoListPanelCtrl.MaxHeight = System.Math.Max(80, e.NewSize.Height - MinChatHeight);
    }

    private void ConfigureViewModel()
    {
        if (DataContext is MainViewModel vm)
        {
            vm.ImageViewer.OnSaveRequested = SaveImageAsync;
            SubscribeToOverlayVisibility(vm);
        }
    }

    private void SubscribeToOverlayVisibility(MainViewModel vm)
    {
        void Check()
        {
            var active = vm.Login.IsVisible
                || vm.UserInfoPopup.IsVisible
                || vm.SessionPreview.IsVisible
                || vm.NewSession.IsVisible
                || vm.Settings.IsVisible
                || vm.ImageViewer.IsVisible
                || vm.SessionDetail.IsSettingsOpen
                || vm.Videos.HasExpandedVideo;
            VlcVideoView.SetOverlayActive(active);
        }

        void Subscribe(INotifyPropertyChanged source, string prop)
        {
            source.PropertyChanged += (_, e) => { if (e.PropertyName == prop) Check(); };
        }

        Subscribe(vm.Login, nameof(LoginViewModel.IsVisible));
        Subscribe(vm.UserInfoPopup, nameof(UserInfoViewModel.IsVisible));
        Subscribe(vm.SessionPreview, nameof(SessionPreviewViewModel.IsVisible));
        Subscribe(vm.NewSession, nameof(NewSessionViewModel.IsVisible));
        Subscribe(vm.Settings, nameof(SettingsViewModel.IsVisible));
        Subscribe(vm.ImageViewer, nameof(ImageViewerViewModel.IsVisible));
        Subscribe(vm.SessionDetail, nameof(SessionDetailViewModel.IsSettingsOpen));
        Subscribe(vm.Videos, nameof(VideoListViewModel.ExpandedVideo));
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
