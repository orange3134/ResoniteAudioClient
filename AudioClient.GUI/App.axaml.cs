using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AudioClient.GUI.ViewModels;
using AudioClient.GUI.Views;

namespace AudioClient.GUI;

public partial class App : Application
{
    public static string AppDir { get; set; } = string.Empty;
    public static string[] LaunchArgs { get; set; } = [];

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainViewModel(AppDir, LaunchArgs);
            var window = new MainWindow { DataContext = vm };
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
