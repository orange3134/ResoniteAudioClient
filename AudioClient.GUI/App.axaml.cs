using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using AudioClient.Core;
using AudioClient.GUI.Services;
using AudioClient.GUI.ViewModels;
using AudioClient.GUI.Views;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace AudioClient.GUI;

public partial class App : Application
{
    public static string AppDir { get; set; } = string.Empty;
    public static string[] LaunchArgs { get; set; } = [];
    public static bool MinimalGuiMode { get; set; }
    public static bool LoadAppResourcesInMinimalMode { get; set; }
    public static bool MinimalMainWindowMode { get; set; }
    public static bool MinimalEngineTestMode { get; set; }
    public static bool MainWindowWithVmNoEngineMode { get; set; }
    public static bool MainWindowWithDelayedEngineMode { get; set; }

    public override void Initialize()
    {
        if (!MinimalGuiMode || LoadAppResourcesInMinimalMode)
        {
            AvaloniaXamlLoader.Load(this);
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (MinimalMainWindowMode)
            {
                desktop.MainWindow = new MainWindow();
                base.OnFrameworkInitializationCompleted();
                return;
            }

            if (MinimalGuiMode)
            {
                desktop.MainWindow = CreateDiagnosticWindow();
                base.OnFrameworkInitializationCompleted();
                return;
            }

            if (MinimalEngineTestMode)
            {
                var window = CreateDiagnosticWindow();
                desktop.MainWindow = window;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        string? engineDir = RuntimeBootstrap.CurrentEngineDir;
                        if (string.IsNullOrWhiteSpace(engineDir))
                            return;

                        var host = await EngineHost.StartAsync(AppDir, engineDir, LaunchArgs);
                        await Task.Delay(1000);
                        host.Shutdown();
                    }
                    catch
                    {
                    }
                });
                base.OnFrameworkInitializationCompleted();
                return;
            }

            if (MainWindowWithVmNoEngineMode)
            {
                var vm = new MainViewModel(AppDir, RuntimeBootstrap.CurrentEngineDir ?? string.Empty, LaunchArgs, skipEngineInitialization: true);
                desktop.MainWindow = new MainWindow { DataContext = vm };
                base.OnFrameworkInitializationCompleted();
                return;
            }

            if (MainWindowWithDelayedEngineMode)
            {
                var vm = new MainViewModel(AppDir, RuntimeBootstrap.CurrentEngineDir ?? string.Empty, LaunchArgs, skipEngineInitialization: true);
                desktop.MainWindow = new MainWindow { DataContext = vm };
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1500);
                    vm.StartEngineInitialization();
                });
                base.OnFrameworkInitializationCompleted();
                return;
            }

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

    private static Window CreateDiagnosticWindow()
    {
        return new Window
        {
            Width = 640,
            Height = 360,
            Title = string.Empty,
            Background = Brushes.Black,
            Content = new Border { Background = Brushes.Black }
        };
    }
}
