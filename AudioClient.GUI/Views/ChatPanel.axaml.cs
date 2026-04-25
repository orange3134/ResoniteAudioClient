using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
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

    private static readonly string[] SupportedImageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".gif"];

    public ChatPanel()
    {
        InitializeComponent();
        _scrollViewer = this.FindControl<ScrollViewer>("MessagesScrollViewer");

        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);

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
        if (e.Key != Key.Enter || _vm == null || sender is not ImeAwareTextBox textBox)
            return;

        if (textBox.HasActiveImeComposition)
            return;

        textBox.FlushTextBindingToSource();
        _vm.SendCommand.Execute(null);
        e.Handled = true;
    }
}
