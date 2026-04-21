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
        string gameDir = ResolveGameDirectory(appDir);

        foreach (string runtimesPath in EnumerateNativeRuntimePaths(appDir, gameDir))
        {
            string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            var pathEntries = currentPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            if (!pathEntries.Contains(runtimesPath, StringComparer.OrdinalIgnoreCase))
            {
                Environment.SetEnvironmentVariable("PATH", runtimesPath + Path.PathSeparator + currentPath);
                currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            }
        }

        AppDomain.CurrentDomain.AssemblyResolve += (sender, resolveArgs) =>
        {
            var assemblyName = new AssemblyName(resolveArgs.Name);
            foreach (string probeDir in EnumerateProbeDirectories(appDir, gameDir))
            {
                string path = Path.Combine(probeDir, assemblyName.Name + ".dll");
                if (File.Exists(path))
                {
                    return Assembly.LoadFrom(path);
                }
            }

            return null;
        };

        foreach (string probeDir in EnumerateProbeDirectories(appDir, gameDir))
        {
            foreach (string file in Directory.GetFiles(probeDir, "*.dll"))
            {
                try { Assembly.LoadFrom(file); }
                catch (BadImageFormatException) { }
                catch (Exception) { }
            }
        }

        StartGui(args, appDir, gameDir);
    }

    private static string ResolveGameDirectory(string appDir)
    {
        var normalized = appDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dirName = Path.GetFileName(normalized);
        if (dirName.Equals("AudioClient", StringComparison.OrdinalIgnoreCase))
        {
            return Directory.GetParent(normalized)?.FullName ?? normalized;
        }

        return normalized;
    }

    private static IEnumerable<string> EnumerateProbeDirectories(string appDir, string gameDir)
    {
        yield return appDir;
        if (!string.Equals(appDir, gameDir, StringComparison.OrdinalIgnoreCase))
        {
            yield return gameDir;
        }
    }

    private static IEnumerable<string> EnumerateNativeRuntimePaths(string appDir, string gameDir)
    {
        foreach (string probeDir in EnumerateProbeDirectories(appDir, gameDir))
        {
            string runtimesPath = Path.Combine(probeDir, "runtimes", "win-x64", "native");
            if (Directory.Exists(runtimesPath))
            {
                yield return runtimesPath;
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StartGui(string[] args, string appDir, string gameDir)
    {
        App.AppDir = appDir;
        App.EngineDir = gameDir;
        App.LaunchArgs = args;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
