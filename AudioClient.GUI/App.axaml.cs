using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AudioClient.GUI.Services;
using AudioClient.GUI.ViewModels;
using AudioClient.GUI.Views;
using System.Runtime.CompilerServices;

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
            if (RuntimeBootstrap.HasResolvedEngineDirectory())
            {
                desktop.MainWindow = CreateMainWindow(RuntimeBootstrap.CurrentEngineDir!);
            }
            else
            {
                desktop.MainWindow = CreatePathPromptWindow(desktop);
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    private Window CreatePathPromptWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var window = new ResonitePathPromptWindow();
        var vm = new ResonitePathPromptViewModel(RuntimeBootstrap.SavedEngineDir, RuntimeBootstrap.SuggestedEngineDir);

        vm.OnBrowse = () => window.PickFolderAsync(vm.InstallPath);
        vm.OnResolved = engineDir =>
        {
            var mainWindow = CreateMainWindow(engineDir);
            desktop.MainWindow = mainWindow;
            mainWindow.Show();
            window.Close();
        };
        vm.OnCancel = () => desktop.Shutdown();

        window.DataContext = vm;
        return window;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Window CreateMainWindow(string engineDir)
    {
        var vm = new MainViewModel(AppDir, engineDir, LaunchArgs);
        return new MainWindow { DataContext = vm };
    }
}
