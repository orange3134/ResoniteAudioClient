using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace AudioClient;

public class Program
{
    private static bool _shutdownRequested = false;
    private static object? _engine; // typed as object to avoid early FrooxEngine resolution
    
    public static async Task Main(string[] args)
    {
        string appDir = AppDomain.CurrentDomain.BaseDirectory;
        
        // Add native runtimes folder to PATH so raw DllImports (like SteamAudio's phonon) succeed
        string runtimesPath = Path.Combine(appDir, "runtimes", "win-x64", "native");
        if (Directory.Exists(runtimesPath))
        {
            string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            Environment.SetEnvironmentVariable("PATH", runtimesPath + Path.PathSeparator + currentPath);
        }

        // Register assembly resolver BEFORE any FrooxEngine types are touched.
        // This allows loading FrooxEngine.dll etc. regardless of version mismatch.
        AppDomain.CurrentDomain.AssemblyResolve += (sender, resolveArgs) =>
        {
            var assemblyName = new AssemblyName(resolveArgs.Name);
            string path = Path.Combine(appDir, assemblyName.Name + ".dll");
            if (File.Exists(path))
            {
                return Assembly.LoadFrom(path);
            }
            return null;
        };

        // Force-load all managed assemblies in the directory into the AppDomain 
        // This simulates Unity/Mono's behavior of having all assemblies available in the domain.
        // It's required so FrooxEngine's type scanner registers ALL valid Data Model Assemblies (like Awwdio, PhotonDust, etc).
        foreach (string file in Directory.GetFiles(appDir, "*.dll"))
        {
            try
            {
                Assembly.LoadFrom(file);
            }
            catch (BadImageFormatException)
            {
                // Ignore native DLLs like opus.dll, LibFreeImage.so, etc.
            }
            catch (Exception)
            {
                // Non-critical, skip silently
            }
        }

        // Now it's safe to call into FrooxEngine code
        await RunEngine(args, appDir);
    }

    // NoInlining ensures the JIT won't try to resolve FrooxEngine types
    // until this method is actually called (after our AssemblyResolve handler is registered)
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task RunEngine(string[] args, string appDir)
    {
        
        InitializeLogging();

        Elements.Core.UniLog.Log("Starting Headless AudioClient...");

        var options = FrooxEngine.LaunchOptions.GetLaunchOptions(args);
        options.OutputDevice = Renderite.Shared.HeadOutputDevice.Screen;
        
        // Use Resonite default directories so it behaves perfectly but inside a console.
        options.DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "..", "LocalLow", "Yellow Dog Man Studios", "Resonite");
        options.CacheDirectory = Path.Combine(Path.GetTempPath(), "Yellow Dog Man Studios", "Resonite");
        
        // Locale defaults to the Locale folder in the game directory
        options.LocaleDirectory = Path.Combine(appDir, "Locale");

        var engine = new FrooxEngine.Engine();
        _engine = engine;
        var systemInfo = new DummySystemInfo();
        var initProgress = new ConsoleInitProgress();

        // 1. Initialize Engine (Wait for completion)
        await engine.Initialize(appDir, useRenderer: false, options, systemInfo, initProgress);
        
        // 2. Setup Userspace
        FrooxEngine.Userspace.SetupUserspace(engine);
        
        // 3. Start Update Loop Thread
        Thread updateThread = new Thread(EngineUpdateLoop)
        {
            Name = "Engine Update Loop",
            IsBackground = false // Keep the process alive while the engine runs
        };
        updateThread.Start();

        // 4. Command Line Interface
        Console.WriteLine("==================================================================");
        Console.WriteLine("AudioClient is ready.");
        Console.WriteLine("Commands: join <session_id/url> | leave | exit");
        Console.WriteLine("==================================================================");

        while (!_shutdownRequested)
        {
            string? input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) continue;

            string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string command = parts[0].ToLowerInvariant();

            engine.GlobalCoroutineManager.Post((state) =>
            {
                ProcessCommand(engine, command, parts);
            }, null);
        }
        
        // Shutdown logic
        FrooxEngine.Userspace.ExitApp(saveHomes: false);
        updateThread.Join();
        engine.Dispose();
        Console.WriteLine("Shutdown complete.");
    }

    private static void EngineUpdateLoop()
    {
        while (!_shutdownRequested)
        {
            (_engine as FrooxEngine.Engine)?.RunUpdateLoop();
            Thread.Sleep(10); // Sleep briefly to prevent 100% CPU usage
        }
    }

    private static void ProcessCommand(FrooxEngine.Engine engine, string command, string[] args)
    {
        try
        {
            switch (command)
            {
                case "join":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: join <session_url>");
                        break;
                    }
                    string url = args[1];
                    Console.WriteLine($"Joining session: {url}...");

                    // Parse the URL to URI format if it's not
                    if (!url.Contains("://"))
                    {
                        url = $"resrec:///{url}";
                        Console.WriteLine($"Normalized URL: {url}");
                    }
                    
                    if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
                    {
                        FrooxEngine.Userspace.JoinSession(uri);
                        Console.WriteLine("Session Join requested.");
                    }
                    else
                    {
                        Console.WriteLine("Invalid URL format.");
                    }
                    break;
                
                case "leave":
                    Console.WriteLine("Not fully implemented yet. Please use exit/restart.");
                    break;

                case "exit":
                case "quit":
                    _shutdownRequested = true;
                    break;

                default:
                    Console.WriteLine($"Unknown command: {command}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing command: {ex.Message}");
        }
    }

    private static void InitializeLogging()
    {
        string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
        Directory.CreateDirectory(logDir);
        string logname = Elements.Core.UniLog.GenerateLogName(FrooxEngine.Engine.VersionNumber + "AudioClient");
        StreamWriter logStream = File.CreateText(Path.Combine(logDir, logname));
        
        object logLock = new object();

        void WriteLog(string prefix, string msg)
        {
            string line = $"{DateTime.Now:HH:mm:ss.fff} [{prefix}] {msg}";
            
            // Console output depending on prefix
            if (prefix == "ERR")
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(line);
                Console.ResetColor();
            }
            else if (prefix == "WARN")
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(line);
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine(line);
            }

            // File output
            lock (logLock)
            {
                logStream.WriteLine(line);
                logStream.Flush();
            }
        }

        Elements.Core.UniLog.OnLog += (msg) => WriteLog("INFO", msg);
        Elements.Core.UniLog.OnWarning += (msg) => WriteLog("WARN", msg);
        Elements.Core.UniLog.OnError += (msg) => WriteLog("ERR", msg);
        Elements.Core.UniLog.OnFlush += () =>
        {
            lock (logLock) logStream.Flush();
        };
    }
}

