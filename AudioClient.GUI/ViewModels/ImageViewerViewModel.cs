using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AudioClient.GUI.ViewModels;

public partial class ImageViewerViewModel : ObservableObject
{
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private Bitmap? _imageBitmap;
    [ObservableProperty] private byte[]? _imageBytes;
    [ObservableProperty] private string _suggestedFileName = "chat-image.png";

    public Func<ImageViewerViewModel, Task>? OnSaveRequested { get; set; }

    public bool CanSave => ImageBytes is { Length: > 0 };

    public void Show(Bitmap bitmap, byte[]? bytes, string? sourceUrl)
    {
        ImageBitmap = bitmap;
        ImageBytes = bytes;
        SuggestedFileName = CreateSuggestedFileName(sourceUrl);
        IsVisible = true;
        OnPropertyChanged(nameof(CanSave));
    }

    [RelayCommand]
    private void Close()
    {
        IsVisible = false;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        if (OnSaveRequested != null)
            await OnSaveRequested(this).ConfigureAwait(false);
    }

    partial void OnImageBytesChanged(byte[]? value)
    {
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private static string CreateSuggestedFileName(string? sourceUrl)
    {
        const string fallback = "chat-image.png";
        if (string.IsNullOrWhiteSpace(sourceUrl))
            return fallback;

        string candidate;
        try
        {
            candidate = Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri)
                ? Path.GetFileName(uri.LocalPath)
                : Path.GetFileName(sourceUrl);
        }
        catch
        {
            return fallback;
        }

        if (string.IsNullOrWhiteSpace(candidate))
            return fallback;

        var invalidChars = Path.GetInvalidFileNameChars();
        candidate = new string(candidate.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(Path.GetExtension(candidate))
            ? candidate + ".png"
            : candidate;
    }
}
