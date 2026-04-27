using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using AudioClient.GUI.Services;

namespace AudioClient.GUI;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        bool hasExplicitModeArg = args.Any(a => a.StartsWith("--", StringComparison.Ordinal));
        bool minimalGui = args.Any(a => string.Equals(a, "--minimal-gui", StringComparison.OrdinalIgnoreCase));
        bool minimalGuiWithApp = args.Any(a => string.Equals(a, "--minimal-gui-with-app", StringComparison.OrdinalIgnoreCase));
        bool minimalMainWindow = args.Any(a => string.Equals(a, "--minimal-main-window", StringComparison.OrdinalIgnoreCase));
        bool minimalMainWindowWithBootstrap = args.Any(a => string.Equals(a, "--minimal-main-window-with-bootstrap", StringComparison.OrdinalIgnoreCase));
        bool minimalEngineTest = args.Any(a => string.Equals(a, "--minimal-engine-test", StringComparison.OrdinalIgnoreCase));
        bool mainWindowWithVmNoEngine = args.Any(a => string.Equals(a, "--main-window-with-vm-no-engine", StringComparison.OrdinalIgnoreCase));
        bool mainWindowWithDelayedEngine = args.Any(a => string.Equals(a, "--main-window-with-delayed-engine", StringComparison.OrdinalIgnoreCase));
        if (OperatingSystem.IsLinux() && !hasExplicitModeArg)
        {
            // Linux default: start stable UI first, then initialize engine.
            mainWindowWithDelayedEngine = true;
        }
        bool useMinimalWindow = minimalGui || minimalGuiWithApp;
        string appDir = AppDomain.CurrentDomain.BaseDirectory;

        if (!useMinimalWindow && (!minimalMainWindow || minimalMainWindowWithBootstrap || minimalEngineTest || mainWindowWithVmNoEngine || mainWindowWithDelayedEngine))
        {
            RuntimeBootstrap.Initialize(appDir, args);
            AppDomain.CurrentDomain.AssemblyResolve += RuntimeBootstrap.ResolveAssembly;
            RuntimeBootstrap.PrimeAssemblyLoading();
        }

        StartGui(
            appDir,
            args,
            useMinimalWindow,
            loadAppResources: minimalGuiWithApp,
            minimalMainWindow: minimalMainWindow || minimalMainWindowWithBootstrap,
            minimalEngineTest,
            mainWindowWithVmNoEngine,
            mainWindowWithDelayedEngine);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StartGui(
        string appDir,
        string[] args,
        bool minimalGui,
        bool loadAppResources,
        bool minimalMainWindow,
        bool minimalEngineTest,
        bool mainWindowWithVmNoEngine,
        bool mainWindowWithDelayedEngine)
    {
        App.AppDir = appDir;
        App.LaunchArgs = args;
        App.MinimalGuiMode = minimalGui;
        App.LoadAppResourcesInMinimalMode = loadAppResources;
        App.MinimalMainWindowMode = minimalMainWindow;
        App.MinimalEngineTestMode = minimalEngineTest;
        App.MainWindowWithVmNoEngineMode = mainWindowWithVmNoEngine;
        App.MainWindowWithDelayedEngineMode = mainWindowWithDelayedEngine;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
