using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;

namespace AudioClient.GUI;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        string appDir = AppDomain.CurrentDomain.BaseDirectory;

        string runtimesPath = Path.Combine(appDir, "runtimes", "win-x64", "native");
        if (Directory.Exists(runtimesPath))
        {
            string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            Environment.SetEnvironmentVariable("PATH", runtimesPath + Path.PathSeparator + currentPath);
        }

        AppDomain.CurrentDomain.AssemblyResolve += (sender, resolveArgs) =>
        {
            var assemblyName = new AssemblyName(resolveArgs.Name);
            string path = Path.Combine(appDir, assemblyName.Name + ".dll");
            if (File.Exists(path)) return Assembly.LoadFrom(path);
            return null;
        };

        foreach (string file in Directory.GetFiles(appDir, "*.dll"))
        {
            try { Assembly.LoadFrom(file); }
            catch (BadImageFormatException) { }
            catch (Exception) { }
        }

        StartGui(args, appDir);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StartGui(string[] args, string appDir)
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
