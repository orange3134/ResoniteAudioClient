using System;
using System.Runtime.CompilerServices;
using Avalonia;
using AudioClient.GUI.Services;

namespace AudioClient.GUI;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        string appDir = AppDomain.CurrentDomain.BaseDirectory;
        RuntimeBootstrap.Initialize(appDir, args);
        AppDomain.CurrentDomain.AssemblyResolve += RuntimeBootstrap.ResolveAssembly;
        RuntimeBootstrap.PrimeAssemblyLoading();
        StartGui(appDir, args);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StartGui(string appDir, string[] args)
    {
        App.AppDir = appDir;
        App.LaunchArgs = args;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
