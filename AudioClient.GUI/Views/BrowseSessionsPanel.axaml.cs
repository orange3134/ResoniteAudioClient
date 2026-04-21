using Avalonia.Controls;
using Avalonia.Input;
using AudioClient.GUI.ViewModels;

namespace AudioClient.GUI.Views;

public partial class BrowseSessionsPanel : UserControl
{
    public BrowseSessionsPanel()
    {
        InitializeComponent();
        var searchBox = this.FindControl<TextBox>("SearchBox");
        if (searchBox != null)
            searchBox.KeyDown += OnSearchKeyDown;
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is BrowseSessionsViewModel vm)
            vm.RefreshCommand.Execute(null);
    }
}
