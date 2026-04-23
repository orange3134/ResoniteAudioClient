using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AudioClient.Core.Services;
using FrooxEngine;
using Renderite.Shared;

namespace AudioClient.Core;

public class EngineHost : IDisposable
{
    private readonly Engine _engine;
    private readonly Thread _updateThread;
    private volatile bool _shutdownRequested;
    private readonly Timer _pollTimer;

    public AuthService Auth { get; }
    public AudioService Audio { get; }
    public SessionService Sessions { get; }
    public UserService Users { get; }
    public ContactService Contacts { get; }
    public InventoryService Inventory { get; }
    public ChatService Chat { get; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private EngineHost(Engine engine)
    {
        _engine = engine;
        Auth = new AuthService(engine);
        Audio = new AudioService(engine);
        Sessions = new SessionService(engine);
        Users = new UserService(engine);
        Contacts = new ContactService(engine);
        Inventory = new InventoryService(engine);
        Chat = new ChatService(engine);

        _updateThread = new Thread(UpdateLoop) { Name = "Engine Update Loop", IsBackground = false };
        _updateThread.Start();

        _pollTimer = new Timer(PollCallback, null, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(500));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<EngineHost> StartAsync(
        string appDir,
        string engineDir,
        string[] args,
        IEngineInitProgress? progress = null,
        Action<string>? logHandler = null)
    {
        LoggingHelper.Initialize(appDir, (prefix, msg, line) =>
        {
            logHandler?.Invoke(line);
        });

        Elements.Core.UniLog.Log("Starting AudioClient...");

        var options = LaunchOptions.GetLaunchOptions(args);
        options.OutputDevice = HeadOutputDevice.Screen;

        if (string.IsNullOrEmpty(options.DataDirectory))
            options.DataDirectory = Path.Combine(appDir, "DataPath");
        if (string.IsNullOrEmpty(options.CacheDirectory))
            options.CacheDirectory = Path.Combine(Path.GetTempPath(), "Yellow Dog Man Studios", "Resonite");
        options.LocaleDirectory = Path.Combine(engineDir, "Locale");

        var engine = new Engine();
        var systemInfo = new DummySystemInfo();
        IEngineInitProgress initProgress = progress ?? new EngineInitProgress(
            msg => Elements.Core.UniLog.Log(msg),
            msg => Elements.Core.UniLog.Log(msg),
            () => Elements.Core.UniLog.Log("Engine ready."));

        await engine.Initialize(engineDir, useRenderer: false, options, systemInfo, initProgress);
        Engine.Config.DisableIntroTutorial = true;
        Userspace.SetupUserspace(engine);

        return new EngineHost(engine);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Shutdown()
    {
        _pollTimer.Dispose();
        // ExitApp triggers AppEnder which calls Engine.RequestShutdown() after saves complete.
        // The update loop must keep running until then so AppEnder can execute.
        Userspace.ExitApp(saveHomes: false);
        // Wait up to 10 seconds; if the engine doesn't shut down cleanly, force-stop the loop.
        _updateThread.Join(TimeSpan.FromSeconds(10));
        _shutdownRequested = true;
        try { _engine.Dispose(); } catch { }
    }

    public void Dispose() => Shutdown();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void PostToEngine(Action action)
    {
        _engine.GlobalCoroutineManager.Post((state) => action(), null!);
    }

    private void UpdateLoop()
    {
        while (!_shutdownRequested && !_engine.ShutdownRequested)
        {
            _engine.RunUpdateLoop();
            Thread.Sleep(10);
        }
    }

    private void PollCallback(object? state)
    {
        if (_shutdownRequested) return;
        try
        {
            Auth.Refresh();
            Audio.Refresh();
            Sessions.Refresh();
            Users.Refresh();
            Contacts.Refresh();
            Chat.Refresh();
        }
        catch { }
    }
}
