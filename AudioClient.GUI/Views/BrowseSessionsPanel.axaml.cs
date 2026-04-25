using Avalonia.Controls;
using Avalonia.Input;
using AudioClient.GUI.Controls;
using AudioClient.GUI.ViewModels;

namespace AudioClient.GUI.Views;

public partial class BrowseSessionsPanel : UserControl
{
    public BrowseSessionsPanel()
    {
        InitializeComponent();
        var searchBox = this.FindControl<ImeAwareTextBox>("SearchBox");
        if (searchBox != null)
            searchBox.KeyDown += OnSearchKeyDown;
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not BrowseSessionsViewModel vm || sender is not ImeAwareTextBox textBox)
            return;

        if (textBox.HasActiveImeComposition)
            return;

        textBox.FlushTextBindingToSource();
        vm.RefreshCommand.Execute(null);
        e.Handled = true;
    }
}
