using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace AudioClient.GUI.Services;

public static class RuntimeBootstrap
{
    private static string _appDir = string.Empty;
    private static string[] _launchArgs = [];
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static string? CurrentEngineDir { get; private set; }
    public static string? SavedEngineDir { get; private set; }
    public static string? SuggestedEngineDir { get; private set; }

    public static string AppDir => _appDir;
    public static string[] LaunchArgs => _launchArgs;

    public static void Initialize(string appDir, string[] args)
    {
        _appDir = Path.GetFullPath(appDir);
        _launchArgs = args;

        var settings = GuiSettingsStore.Load();
        SavedEngineDir = NormalizeDirectory(settings.ResoniteInstallPath);
        SuggestedEngineDir = DetectSteamResoniteDirectory();
        CurrentEngineDir = null;

        foreach (string? candidate in EnumerateInitialCandidates())
        {
            if (IsValidEngineDirectory(candidate))
            {
                ApplyEngineDirectory(candidate!, persist: false);
                break;
            }
        }
    }

    public static bool HasResolvedEngineDirectory()
        => IsValidEngineDirectory(CurrentEngineDir);

    public static void ApplyEngineDirectory(string engineDir, bool persist)
    {
        string normalized = NormalizeDirectory(engineDir)
            ?? throw new ArgumentException("Engine directory is required.", nameof(engineDir));

        CurrentEngineDir = normalized;
        AddNativeRuntimePaths();
        PreloadAssemblies();

        if (persist)
        {
            SavedEngineDir = normalized;
            var settings = GuiSettingsStore.Load();
            settings.ResoniteInstallPath = normalized;
            GuiSettingsStore.Save(settings);
        }
    }

    public static Assembly? ResolveAssembly(object? sender, ResolveEventArgs resolveArgs)
    {
        var assemblyName = new AssemblyName(resolveArgs.Name);
        foreach (string probeDir in EnumerateProbeDirectories())
        {
            string path = Path.Combine(probeDir, assemblyName.Name + ".dll");
            if (File.Exists(path))
                return Assembly.LoadFrom(path);
        }

        return null;
    }

    public static void PrimeAssemblyLoading()
    {
        AddNativeRuntimePaths();
        PreloadAssemblies();
    }

    public static bool IsValidEngineDirectory(string? path)
    {
        string? normalized = NormalizeDirectory(path);
        if (normalized is null)
            return false;

        return File.Exists(Path.Combine(normalized, "FrooxEngine.dll"))
            && File.Exists(Path.Combine(normalized, "Elements.Core.dll"))
            && File.Exists(Path.Combine(normalized, "SkyFrost.Base.dll"))
            && Directory.Exists(Path.Combine(normalized, "Locale"));
    }

    private static IEnumerable<string?> EnumerateInitialCandidates()
    {
        yield return SavedEngineDir;
        yield return SuggestedEngineDir;
        yield return ResolveBundledGameDirectory(_appDir);
    }

    private static IEnumerable<string> EnumerateProbeDirectories()
    {
        yield return _appDir;

        if (IsValidEngineDirectory(CurrentEngineDir) &&
            !string.Equals(_appDir, CurrentEngineDir, GetPathComparison()))
        {
            yield return CurrentEngineDir!;
        }
    }

    private static void AddNativeRuntimePaths()
    {
        var runtimePaths = new List<string>();
        foreach (string probeDir in EnumerateProbeDirectories())
        {
            foreach (string runtimesPath in EnumerateNativeRuntimePaths(probeDir))
            {
                if (!Directory.Exists(runtimesPath))
                    continue;

                runtimePaths.Add(runtimesPath);
                AddToSearchPath("PATH", runtimesPath);
                if (!OperatingSystem.IsWindows())
                {
                    AddToSearchPath("LD_LIBRARY_PATH", runtimesPath);
                }
            }
        }

        PreloadNativeLibraries(runtimePaths);
    }

    private static void PreloadAssemblies()
    {
        foreach (string probeDir in EnumerateProbeDirectories())
        {
            if (!Directory.Exists(probeDir))
                continue;

            foreach (string file in Directory.GetFiles(probeDir, "*.dll"))
            {
                try { Assembly.LoadFrom(file); }
                catch (BadImageFormatException) { }
                catch (Exception) { }
            }
        }
    }

    private static string? DetectSteamResoniteDirectory()
    {
        if (OperatingSystem.IsWindows())
            return DetectSteamResoniteDirectoryWindows();

        if (OperatingSystem.IsLinux())
            return DetectSteamResoniteDirectoryLinux();

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static string? DetectSteamResoniteDirectoryWindows()
    {
        foreach (string? steamRoot in EnumerateSteamRoots())
        {
            if (string.IsNullOrWhiteSpace(steamRoot))
                continue;

            string candidate = Path.Combine(steamRoot, "steamapps", "common", "Resonite");
            if (Directory.Exists(candidate))
                return NormalizeDirectory(candidate);
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string?> EnumerateSteamRoots()
    {
        yield return ReadSteamRegistryPath(RegistryHive.CurrentUser, RegistryView.Registry64);
        yield return ReadSteamRegistryPath(RegistryHive.CurrentUser, RegistryView.Registry32);
        yield return ReadSteamRegistryPath(RegistryHive.LocalMachine, RegistryView.Registry64);
        yield return ReadSteamRegistryPath(RegistryHive.LocalMachine, RegistryView.Registry32);
        yield return @"C:\Program Files (x86)\Steam";
        yield return @"C:\Program Files\Steam";
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadSteamRegistryPath(RegistryHive hive, RegistryView view)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
            using RegistryKey? steamKey = baseKey.OpenSubKey(@"Software\Valve\Steam");
            string? path = steamKey?.GetValue("SteamPath") as string ?? steamKey?.GetValue("InstallPath") as string;
            return NormalizeDirectory(path?.Replace('/', Path.DirectorySeparatorChar));
        }
        catch
        {
            return null;
        }
    }

    private static string? DetectSteamResoniteDirectoryLinux()
    {
        foreach (string? steamRoot in EnumerateLinuxSteamRoots())
        {
            if (string.IsNullOrWhiteSpace(steamRoot))
                continue;

            string candidate = Path.Combine(steamRoot, "steamapps", "common", "Resonite");
            if (Directory.Exists(candidate))
                return NormalizeDirectory(candidate);
        }

        return null;
    }

    private static IEnumerable<string?> EnumerateLinuxSteamRoots()
    {
        string? home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
            yield break;

        yield return Path.Combine(home, ".local", "share", "Steam");
        yield return Path.Combine(home, ".steam", "steam");
        yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam");
    }

    private static string? ResolveBundledGameDirectory(string appDir)
    {
        string normalized = appDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? parent = Directory.GetParent(normalized)?.FullName;
        return NormalizeDirectory(parent);
    }

    private static string? NormalizeDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return Path.GetFullPath(path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    private static IEnumerable<string> EnumerateNativeRuntimePaths(string probeDir)
    {
        foreach (string rid in GetNativeRuntimeIdentifiers())
            yield return Path.Combine(probeDir, "runtimes", rid, "native");
    }

    private static IEnumerable<string> GetNativeRuntimeIdentifiers()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return "win-x64";
            yield return "win";
            yield break;
        }

        if (OperatingSystem.IsLinux())
        {
            yield return "linux-x64";
            yield return "linux-musl-x64";
            yield return "linux";
            yield break;
        }

        if (OperatingSystem.IsMacOS())
        {
            yield return "osx-x64";
            yield return "osx-arm64";
            yield return "osx";
            yield break;
        }
    }

    private static StringComparison GetPathComparison()
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static void AddToSearchPath(string variableName, string path)
    {
        string currentValue = Environment.GetEnvironmentVariable(variableName) ?? string.Empty;
        string[] entries = currentValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        if (entries.Contains(path, PathComparer))
            return;

        Environment.SetEnvironmentVariable(variableName, path + Path.PathSeparator + currentValue);
    }

    private static void PreloadNativeLibraries(IEnumerable<string> runtimePaths)
    {
        foreach (string runtimePath in runtimePaths)
        {
            if (!Directory.Exists(runtimePath))
                continue;

            foreach (string nativeFile in Directory.GetFiles(runtimePath))
            {
                if (!(nativeFile.EndsWith(".so", StringComparison.OrdinalIgnoreCase)
                    || nativeFile.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    || nativeFile.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                try { NativeLibrary.TryLoad(nativeFile, out _); }
                catch { }
            }
        }
    }
}
