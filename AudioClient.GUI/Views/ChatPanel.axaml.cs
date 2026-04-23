using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using AudioClient.GUI.ViewModels;

namespace AudioClient.GUI.Views;

public partial class ChatPanel : UserControl
{
    private ScrollViewer? _scrollViewer;
    private ChatViewModel? _vm;

    public ChatPanel()
    {
        InitializeComponent();
        _scrollViewer = this.FindControl<ScrollViewer>("MessagesScrollViewer");

        DataContextChanged += (_, _) =>
        {
            if (_vm != null)
                _vm.Posts.CollectionChanged -= OnPostsChanged;

            _vm = DataContext as ChatViewModel;

            if (_vm != null)
                _vm.Posts.CollectionChanged += OnPostsChanged;
        };
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
