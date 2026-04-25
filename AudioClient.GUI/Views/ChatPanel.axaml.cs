using System.Collections.Specialized;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AudioClient.GUI.ViewModels;

namespace AudioClient.GUI.Views;

public partial class ChatPanel : UserControl
{
    private ScrollViewer? _scrollViewer;
    private ChatViewModel? _vm;

    private static readonly string[] SupportedImageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".gif"];

    public ChatPanel()
    {
        InitializeComponent();
        _scrollViewer = this.FindControl<ScrollViewer>("MessagesScrollViewer");

        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);

        DataContextChanged += (_, _) =>
        {
            if (_vm != null)
                _vm.Posts.CollectionChanged -= OnPostsChanged;

            _vm = DataContext as ChatViewModel;

            if (_vm != null)
                _vm.Posts.CollectionChanged += OnPostsChanged;
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
            Dispatcher.UIThread.Post(() => _scrollViewer?.ScrollToEnd());
    }

    private void InputTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _vm != null)
        {
            _vm.SendCommand.Execute(null);
            e.Handled = true;
        }
    }
}
