using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;
using Renderite.Shared;

namespace AudioClient;

public class Program
{
    private static bool _shutdownRequested = false;
    private static Engine? _engine;
    
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

        InitializeLogging();

        UniLog.Log("Starting Headless AudioClient...");
        
        // Force-load all managed assemblies in the directory into the AppDomain 
        // This simulates Unity/Mono's behavior of having all assemblies available in the domain.
        // It's required so FrooxEngine's type scanner registers ALL valid Data Model Assemblies (like Awwdio, PhotonDust, etc).
        foreach (string file in Directory.GetFiles(appDir, "*.dll"))
        {
            try
            {
                System.Reflection.Assembly.LoadFrom(file);
            }
            catch (BadImageFormatException)
            {
                // Ignore native DLLs like opus.dll, LibFreeImage.so, etc.
            }
            catch (Exception ex)
            {
                UniLog.Warning($"Non-critical failure loading {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        LaunchOptions options = LaunchOptions.GetLaunchOptions(args);
        options.OutputDevice = HeadOutputDevice.Screen;
        
        // Use Resonite default directories so it behaves perfectly but inside a console.
        options.DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "..", "LocalLow", "Yellow Dog Man Studios", "Resonite");
        options.CacheDirectory = Path.Combine(Path.GetTempPath(), "Yellow Dog Man Studios", "Resonite");
        
        // Locale defaults to the Locale folder in the game directory
        options.LocaleDirectory = Path.Combine(appDir, "Locale");

        _engine = new Engine();
        var systemInfo = new DummySystemInfo();
        var initProgress = new ConsoleInitProgress();

        // 1. Initialize Engine (Wait for completion)
        await _engine.Initialize(appDir, useRenderer: false, options, systemInfo, initProgress);
        
        // 2. Setup Userspace
        Userspace.SetupUserspace(_engine);
        
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

            InvokeNextUpdate(() => ProcessCommand(command, parts));
        }
        
        // Shutdown logic
        Userspace.ExitApp(saveHomes: false);
        updateThread.Join();
        _engine.Dispose();
        Console.WriteLine("Shutdown complete.");
    }

    private static void EngineUpdateLoop()
    {
        while (!_shutdownRequested)
        {
            _engine?.RunUpdateLoop();
            Thread.Sleep(10); // Sleep briefly to prevent 100% CPU usage
        }
    }

    private static void InvokeNextUpdate(Action action)
    {
        _engine?.GlobalCoroutineManager.Post((state) =>
        {
            action();
        }, null);
    }

    private static void ProcessCommand(string command, string[] args)
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
                    
                    if (Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
                    {
                        Userspace.JoinSession(uri);
                        Console.WriteLine("Session Join requested.");
                    }
                    else
                    {
                        Console.WriteLine("Invalid URL format.");
                    }
                    break;
                
                case "leave":
                    Console.WriteLine("Not fully implemented yet. Please use exit/restart.");
                    // Properly finding the world and closing it requires a bit more API exploration.
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
        string logname = UniLog.GenerateLogName(Engine.VersionNumber + "AudioClient");
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
                // To avoid spamming console with every UniLog message, we can just log errors to console
                // or optionally log everything. Let's log everything for debug purposes.
                Console.WriteLine(line);
            }

            // File output
            lock (logLock)
            {
                logStream.WriteLine(line);
                logStream.Flush();
            }
        }

        UniLog.OnLog += (msg) => WriteLog("INFO", msg);
        UniLog.OnWarning += (msg) => WriteLog("WARN", msg);
        UniLog.OnError += (msg) => WriteLog("ERR", msg);
        UniLog.OnFlush += () =>
        {
            lock (logLock) logStream.Flush();
        };
    }
}
