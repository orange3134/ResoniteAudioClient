using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace AudioClient.GUI.Views;

public class VlcVideoView : NativeControlHost
{
    public static Action<string>? DiagnosticLog;

    internal static void Log(string message) => DiagnosticLog?.Invoke(message);

    public static readonly StyledProperty<string?> SourceProperty =
        AvaloniaProperty.Register<VlcVideoView, string?>(nameof(Source));

    public static readonly StyledProperty<bool> IsPlaybackActiveProperty =
        AvaloniaProperty.Register<VlcVideoView, bool>(nameof(IsPlaybackActive));

    public static readonly StyledProperty<float> PositionProperty =
        AvaloniaProperty.Register<VlcVideoView, float>(nameof(Position));

    public static readonly StyledProperty<double> VolumeProperty =
        AvaloniaProperty.Register<VlcVideoView, double>(nameof(Volume), 100);

    public static readonly StyledProperty<bool> CanSeekProperty =
        AvaloniaProperty.Register<VlcVideoView, bool>(nameof(CanSeek), true);

    public string? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public bool IsPlaybackActive
    {
        get => GetValue(IsPlaybackActiveProperty);
        set => SetValue(IsPlaybackActiveProperty, value);
    }

    public float Position
    {
        get => GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    public double Volume
    {
        get => GetValue(VolumeProperty);
        set => SetValue(VolumeProperty, value);
    }

    public bool CanSeek
    {
        get => GetValue(CanSeekProperty);
        set => SetValue(CanSeekProperty, value);
    }

    private IntPtr _hostHwnd;
    private LibVlcPlayer? _player;
    private string? _currentSource;
    private DateTime _lastSeek = DateTime.MinValue;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("VLC video output is currently implemented for Windows only.");

        _hostHwnd = Win32.CreateWindowEx(
            0,
            "STATIC",
            "",
            Win32.WS_CHILD | Win32.WS_VISIBLE | Win32.WS_CLIPSIBLINGS | Win32.WS_CLIPCHILDREN,
            0,
            0,
            1,
            1,
            parent.Handle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        _player = new LibVlcPlayer(_hostHwnd);
        ApplyState();

        return new PlatformHandle(_hostHwnd, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _player?.Dispose();
        _player = null;

        if (_hostHwnd != IntPtr.Zero)
        {
            Win32.DestroyWindow(_hostHwnd);
            _hostHwnd = IntPtr.Zero;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SourceProperty)
            Log($"[VLC] Source changed → {Source}");
        else if (change.Property == IsPlaybackActiveProperty)
            Log($"[VLC] IsPlaybackActive changed → {IsPlaybackActive}");

        if (change.Property == SourceProperty ||
            change.Property == IsPlaybackActiveProperty ||
            change.Property == PositionProperty ||
            change.Property == VolumeProperty ||
            change.Property == CanSeekProperty)
        {
            ApplyState();
        }
    }

    private void ApplyState()
    {
        if (_player == null)
            return;

        var source = Source;
        if (!string.Equals(source, _currentSource, StringComparison.Ordinal))
        {
            _currentSource = source;
            _lastSeek = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(source))
                _player.Stop();
            else
                _player.Open(source);
        }

        var isLiveStream = !string.IsNullOrEmpty(_currentSource) && IsRtspUrl(_currentSource);
        if (IsPlaybackActive || isLiveStream)
            _player.Play();
        else
            _player.Pause();

        _player.Volume = (int)Math.Round(Volume);

        if (CanSeek)
        {
            var now = DateTime.UtcNow;
            if (now - _lastSeek > TimeSpan.FromMilliseconds(700))
            {
                var targetMs = Math.Max(0, (long)(Position * 1000));
                var currentMs = _player.Time;
                if (currentMs < 0 || Math.Abs(currentMs - targetMs) > 1200)
                {
                    _player.Time = targetMs;
                    _lastSeek = now;
                }
            }
        }
    }

    private static bool IsRtspUrl(string source)
        => source.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase) ||
           source.StartsWith("rtsps://", StringComparison.OrdinalIgnoreCase) ||
           source.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase) ||
           source.StartsWith("rtmps://", StringComparison.OrdinalIgnoreCase);

    private sealed class LibVlcPlayer : IDisposable
    {
        private readonly IntPtr _libvlc;
        private readonly IntPtr _player;
        private bool _disposed;

        public LibVlcPlayer(IntPtr hwnd)
        {
            VlcNative.EnsureLoaded();

            var args = new[]
            {
                "--quiet",
                "--no-video-title-show",
                "--no-osd",
                "--avcodec-hw=none",
            };

            _libvlc = VlcNative.libvlc_new(args.Length, args);
            if (_libvlc == IntPtr.Zero)
                throw new InvalidOperationException("Failed to initialize libVLC.");

            _player = VlcNative.libvlc_media_player_new(_libvlc);
            if (_player == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create libVLC media player.");

            VlcNative.libvlc_media_player_set_hwnd(_player, hwnd);
        }

        public long Time
        {
            get => _player == IntPtr.Zero ? -1 : VlcNative.libvlc_media_player_get_time(_player);
            set
            {
                if (_player != IntPtr.Zero)
                    VlcNative.libvlc_media_player_set_time(_player, value);
            }
        }

        public int Volume
        {
            get => _player == IntPtr.Zero ? 0 : VlcNative.libvlc_audio_get_volume(_player);
            set
            {
                if (_player != IntPtr.Zero)
                    VlcNative.libvlc_audio_set_volume(_player, Math.Clamp(value, 0, 150));
            }
        }

        public void Open(string source)
        {
            if (_disposed)
                return;

            Stop();
            var media = VlcNative.libvlc_media_new_location(_libvlc, source);
            if (media == IntPtr.Zero && File.Exists(source))
                media = VlcNative.libvlc_media_new_path(_libvlc, source);
            if (media == IntPtr.Zero)
            {
                VlcVideoView.Log($"[VLC] media_new_location returned null for: {source} err={VlcNative.GetLastError()}");
                return;
            }

            VlcVideoView.Log($"[VLC] Opening: {source} isRtsp={IsRtspUrl(source)}");

            if (IsRtspUrl(source))
            {
                VlcNative.libvlc_media_add_option(media, ":network-caching=300");
                VlcNative.libvlc_media_add_option(media, ":clock-jitter=0");
                VlcNative.libvlc_media_add_option(media, ":clock-synchro=0");
            }

            VlcNative.libvlc_media_player_set_media(_player, media);
            VlcNative.libvlc_media_release(media);
        }

        public void Play()
        {
            if (!_disposed && _player != IntPtr.Zero)
            {
                int ret = VlcNative.libvlc_media_player_play(_player);
                if (ret != 0)
                {
                    var state = VlcNative.libvlc_media_player_get_state(_player);
                    VlcVideoView.Log($"[VLC] Play() failed ret={ret} state={state} err={VlcNative.GetLastError()}");
                }
            }
        }

        public void Pause()
        {
            if (!_disposed && _player != IntPtr.Zero)
                VlcNative.libvlc_media_player_set_pause(_player, 1);
        }

        public void Stop()
        {
            if (!_disposed && _player != IntPtr.Zero)
                VlcNative.libvlc_media_player_stop(_player);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_player != IntPtr.Zero)
            {
                VlcNative.libvlc_media_player_stop(_player);
                VlcNative.libvlc_media_player_release(_player);
            }

            if (_libvlc != IntPtr.Zero)
                VlcNative.libvlc_release(_libvlc);
        }
    }

    private static class VlcNative
    {
        private static bool _resolverRegistered;
        private static string? _vlcDirectory;

        public static void EnsureLoaded()
        {
            if (_resolverRegistered)
                return;

            _vlcDirectory = FindVlcDirectory();
            if (_vlcDirectory == null)
                throw new FileNotFoundException("Could not find Resonite libVLC.");

            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            if (!path.Contains(_vlcDirectory, StringComparison.OrdinalIgnoreCase))
                Environment.SetEnvironmentVariable("PATH", _vlcDirectory + Path.PathSeparator + path);

            var pluginsPath = Path.Combine(_vlcDirectory, "plugins");
            if (Directory.Exists(pluginsPath))
                Environment.SetEnvironmentVariable("VLC_PLUGIN_PATH", pluginsPath);

            NativeLibrary.SetDllImportResolver(typeof(VlcNative).Assembly, Resolve);
            _resolverRegistered = true;
        }

        private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (_vlcDirectory != null &&
                (libraryName.Equals("libvlc", StringComparison.OrdinalIgnoreCase) ||
                 libraryName.Equals("libvlccore", StringComparison.OrdinalIgnoreCase)))
            {
                var fileName = libraryName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    ? libraryName
                    : libraryName + ".dll";
                var path = Path.Combine(_vlcDirectory, fileName);
                if (File.Exists(path))
                    return NativeLibrary.Load(path);
            }

            return IntPtr.Zero;
        }

        private static string? FindVlcDirectory()
        {
            var candidates = new List<string>();
            var appDir = AppContext.BaseDirectory;
            var parent = Directory.GetParent(appDir)?.FullName;
            var grandParent = parent == null ? null : Directory.GetParent(parent)?.FullName;

            AddCandidate(appDir);
            if (parent != null) AddCandidate(parent);
            if (grandParent != null) AddCandidate(grandParent);
            AddCandidate(@"C:\Program Files (x86)\Steam\steamapps\common\Resonite");

            foreach (var candidate in candidates)
            {
                if (File.Exists(Path.Combine(candidate, "libvlc.dll")) &&
                    Directory.Exists(Path.Combine(candidate, "plugins")))
                {
                    return candidate;
                }
            }

            return null;

            void AddCandidate(string baseDir)
            {
                candidates.Add(Path.Combine(baseDir, "Renderer", "Renderite.Renderer_Data", "Plugins", "x86_64"));
            }
        }

        public static string GetLastError()
        {
            var ptr = libvlc_errmsg();
            return ptr == IntPtr.Zero ? "(no error)" : Marshal.PtrToStringAnsi(ptr) ?? "(null)";
        }

        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr libvlc_errmsg();

        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
        public static extern void libvlc_media_add_option(
            IntPtr media,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string options);

        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr libvlc_new(
            int argc,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)] string[] argv);

        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
        public static extern void libvlc_release(IntPtr instance);

        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr libvlc_media_new_location(
            IntPtr instance,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string mrl);

        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr libvlc_media_new_path(
            IntPtr instance,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string path);

        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
        public static extern void libvlc_media_release(IntPtr media);

        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr libvlc_media_player_new(IntPtr instance);

        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
        public static extern void libvlc_media_player_release(IntPtr mediaPlayer);

        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
        public static extern void libvlc_media_player_set_media(IntPtr mediaPlayer, IntPtr media);

        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
        public static extern int libvlc_media_player_play(IntPtr mediaPlayer);

        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
        public static extern void libvlc_media_player_set_pause(IntPtr mediaPlayer, int pause);

        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
        public static extern void libvlc_media_player_stop(IntPtr mediaPlayer);

        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
        public static extern int libvlc_media_player_get_state(IntPtr mediaPlayer);

        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
        public static extern long libvlc_media_player_get_time(IntPtr mediaPlayer);

        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
        public static extern void libvlc_media_player_set_time(IntPtr mediaPlayer, long time);

        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
        public static extern void libvlc_media_player_set_hwnd(IntPtr mediaPlayer, IntPtr hwnd);

        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
        public static extern int libvlc_audio_get_volume(IntPtr mediaPlayer);

        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
        public static extern int libvlc_audio_set_volume(IntPtr mediaPlayer, int volume);
    }

    private static class Win32
    {
        public const int WS_CHILD = 0x40000000;
        public const int WS_VISIBLE = 0x10000000;
        public const int WS_CLIPSIBLINGS = 0x04000000;
        public const int WS_CLIPCHILDREN = 0x02000000;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateWindowEx(
            int dwExStyle,
            string lpClassName,
            string lpWindowName,
            int dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyWindow(IntPtr hWnd);
    }
}
