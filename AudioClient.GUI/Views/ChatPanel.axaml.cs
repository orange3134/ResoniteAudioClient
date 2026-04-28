using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AudioClient.GUI.Controls;
using AudioClient.GUI.ViewModels;

namespace AudioClient.GUI.Views;

public partial class ChatPanel : UserControl
{
    private ScrollViewer? _scrollViewer;
    private ChatViewModel? _vm;
    private DateTime _scrollToBottomUntil = DateTime.MinValue;

    private static readonly string[] SupportedImageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp"];

    public ChatPanel()
    {
        InitializeComponent();
        _scrollViewer = this.FindControl<ScrollViewer>("MessagesScrollViewer");

        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        // 画像の非同期ロードでコンテンツが伸びても追従するため、ウィンドウ内はレイアウト更新のたびに再スクロール
        LayoutUpdated += (_, _) =>
        {
            if (DateTime.UtcNow < _scrollToBottomUntil)
                _scrollViewer?.ScrollToEnd();
        };

        DataContextChanged += (_, _) =>
        {
            if (_vm != null)
            {
                _vm.Posts.CollectionChanged -= OnPostsChanged;
                _vm.ScrollToBottomRequested -= OnScrollToBottomRequested;
            }

            _vm = DataContext as ChatViewModel;

            if (_vm != null)
            {
                _vm.Posts.CollectionChanged += OnPostsChanged;
                _vm.ScrollToBottomRequested += OnScrollToBottomRequested;
            }
        };
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (_vm == null || !_vm.IsChatAvailable) return;

        var files = e.Data.GetFiles();
        if (files == null) return;

        foreach (var item in files)
        {
            var path = item.TryGetLocalPath();
            if (path == null) continue;

            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (SupportedImageExtensions.Contains(ext))
            {
                _vm.SetAttachment(path);
                e.Handled = true;
                return;
            }
        }
    }

    private void OnPostsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
            Dispatcher.UIThread.Post(() => _scrollViewer?.ScrollToEnd(), Avalonia.Threading.DispatcherPriority.Background);
    }

    private void OnScrollToBottomRequested(object? sender, System.EventArgs e)
    {
        _scrollToBottomUntil = DateTime.UtcNow.AddSeconds(5);
        Dispatcher.UIThread.Post(() => _scrollViewer?.ScrollToEnd(), Avalonia.Threading.DispatcherPriority.Background);
    }

    private void InputTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm == null || sender is not ImeAwareTextBox textBox)
            return;

        if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _ = PasteIntoInputAsync(textBox);
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter)
            return;

        if (textBox.HasActiveImeComposition)
            return;

        textBox.FlushTextBindingToSource();
        _vm.SendCommand.Execute(null);
        e.Handled = true;
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm == null || e.Key != Key.V || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
            return;

        var textBox = this.FindControl<ImeAwareTextBox>("InputTextBox");
        if (textBox == null)
            return;

        _ = PasteIntoInputAsync(textBox);
        e.Handled = true;
    }

    private void InputTextBox_PastingFromClipboard(object? sender, RoutedEventArgs e)
    {
        if (_vm == null || sender is not ImeAwareTextBox textBox)
            return;

        e.Handled = true;
        _ = PasteIntoInputAsync(textBox);
    }

    private async Task PasteIntoInputAsync(ImeAwareTextBox textBox)
    {
        if (_vm == null)
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null)
            return;

        if (_vm.IsChatAvailable && await TryAttachClipboardImageAsync(clipboard).ConfigureAwait(true))
            return;

        var text = await clipboard.TryGetTextAsync().ConfigureAwait(true);
        if (!string.IsNullOrEmpty(text))
            InsertText(textBox, text);
    }

    private async Task<bool> TryAttachClipboardImageAsync(IClipboard clipboard)
    {
        var bitmap = await clipboard.TryGetBitmapAsync().ConfigureAwait(true);
        if (bitmap != null)
        {
            var path = CreateClipboardImagePath(".png");
            using (bitmap)
            {
                bitmap.Save(path);
            }
            _vm?.SetAttachment(path);
            return true;
        }

        var nativeImagePath = CreateClipboardImagePath(".png");
        if (OperatingSystem.IsWindows() && WindowsClipboardImage.TrySaveToPng(nativeImagePath))
        {
            _vm?.SetAttachment(nativeImagePath);
            return true;
        }

        var files = await clipboard.TryGetFilesAsync().ConfigureAwait(true);
        if (files != null && TryAttachFirstImageFile(files))
            return true;

        return false;
    }

    private bool TryAttachFirstImageFile(System.Collections.IEnumerable items)
    {
        foreach (var item in items)
        {
            var path = item switch
            {
                IStorageFile file => file.TryGetLocalPath(),
                string filePath => filePath,
                _ => null
            };

            if (path == null)
                continue;

            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (!SupportedImageExtensions.Contains(ext))
                continue;

            _vm?.SetAttachment(path);
            return true;
        }

        return false;
    }

    private static string CreateClipboardImagePath(string extension)
    {
        var dir = Path.Combine(Path.GetTempPath(), "AudioClient", "Clipboard");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"clipboard-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}{extension}");
    }

    private static void InsertText(TextBox textBox, string text)
    {
        var value = textBox.Text ?? "";
        var start = Math.Clamp(textBox.SelectionStart, 0, value.Length);
        var end = Math.Clamp(textBox.SelectionEnd, 0, value.Length);
        if (end < start)
            (start, end) = (end, start);

        textBox.Text = value.Remove(start, end - start).Insert(start, text);
        textBox.SelectionStart = textBox.SelectionEnd = start + text.Length;
    }

    private static class WindowsClipboardImage
    {
        private const uint CfDib = 8;
        private const uint CfDibV5 = 17;

        public static bool TrySaveToPng(string path)
        {
            if (!OpenClipboard(IntPtr.Zero))
                return false;

            try
            {
                var dib = TryReadDib(CfDibV5) ?? TryReadDib(CfDib);
                if (dib == null)
                    return false;

                using var stream = new MemoryStream(CreateBmp(dib));
                using var bitmap = new Bitmap(stream);
                bitmap.Save(path);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                CloseClipboard();
            }
        }

        private static byte[]? TryReadDib(uint format)
        {
            if (!IsClipboardFormatAvailable(format))
                return null;

            var handle = GetClipboardData(format);
            if (handle == IntPtr.Zero)
                return null;

            var size = GlobalSize(handle);
            if (size == UIntPtr.Zero || size.ToUInt64() > int.MaxValue)
                return null;

            var ptr = GlobalLock(handle);
            if (ptr == IntPtr.Zero)
                return null;

            try
            {
                var bytes = new byte[(int)size.ToUInt64()];
                Marshal.Copy(ptr, bytes, 0, bytes.Length);
                return bytes;
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }

        private static byte[] CreateBmp(byte[] dib)
        {
            var pixelOffset = 14 + CalculateDibPixelOffset(dib);
            var fileSize = 14 + dib.Length;
            var bmp = new byte[fileSize];

            bmp[0] = (byte)'B';
            bmp[1] = (byte)'M';
            WriteInt32(bmp, 2, fileSize);
            WriteInt32(bmp, 10, pixelOffset);
            Buffer.BlockCopy(dib, 0, bmp, 14, dib.Length);
            return bmp;
        }

        private static int CalculateDibPixelOffset(byte[] dib)
        {
            if (dib.Length < 16)
                throw new InvalidDataException("Clipboard DIB is too short.");

            var headerSize = BitConverter.ToInt32(dib, 0);
            if (headerSize == 12)
            {
                var bitCount = BitConverter.ToUInt16(dib, 10);
                var colors = bitCount <= 8 ? 1 << bitCount : 0;
                return headerSize + colors * 3;
            }

            if (headerSize < 40 || dib.Length < headerSize)
                throw new InvalidDataException("Clipboard DIB header is invalid.");

            var bitCountInfo = BitConverter.ToUInt16(dib, 14);
            var compression = BitConverter.ToInt32(dib, 16);
            var colorsUsed = dib.Length >= 40 ? BitConverter.ToInt32(dib, 32) : 0;
            var colorsInfo = colorsUsed > 0 ? colorsUsed : bitCountInfo <= 8 ? 1 << bitCountInfo : 0;
            var extraMasks = headerSize == 40 && (compression == 3 || compression == 6)
                ? compression == 6 ? 16 : 12
                : 0;

            return headerSize + extraMasks + colorsInfo * 4;
        }

        private static void WriteInt32(byte[] bytes, int offset, int value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern UIntPtr GlobalSize(IntPtr hMem);
    }
}
