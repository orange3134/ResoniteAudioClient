using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using AudioClient.GUI.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace AudioClient.GUI.Views;

public class VlcVideoView : Control
{
    public static Action<string>? DiagnosticLog;

    internal static void Log(string message) => DiagnosticLog?.Invoke(message);

    public static void SetOverlayActive(bool active)
    {
        // Kept for the old HWND-backed implementation. The software-rendered view
        // is a normal Avalonia control, so overlays and clipping are handled by Avalonia.
    }

    public static readonly StyledProperty<string?> SourceProperty =
        AvaloniaProperty.Register<VlcVideoView, string?>(nameof(Source));

    public static readonly StyledProperty<string?> PlayerKeyProperty =
        AvaloniaProperty.Register<VlcVideoView, string?>(nameof(PlayerKey));

    public static readonly StyledProperty<bool> IsPlaybackActiveProperty =
        AvaloniaProperty.Register<VlcVideoView, bool>(nameof(IsPlaybackActive));

    public static readonly StyledProperty<float> PositionProperty =
        AvaloniaProperty.Register<VlcVideoView, float>(nameof(Position));

    public static readonly StyledProperty<double> VolumeProperty =
        AvaloniaProperty.Register<VlcVideoView, double>(nameof(Volume), 100);

    public static readonly StyledProperty<bool> CanSeekProperty =
        AvaloniaProperty.Register<VlcVideoView, bool>(nameof(CanSeek), true);

    public static readonly StyledProperty<bool> IsFullScreenVideoProperty =
        AvaloniaProperty.Register<VlcVideoView, bool>(nameof(IsFullScreenVideo));

    public string? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public string? PlayerKey
    {
        get => GetValue(PlayerKeyProperty);
        set => SetValue(PlayerKeyProperty, value);
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

    public bool IsFullScreenVideo
    {
        get => GetValue(IsFullScreenVideoProperty);
        set => SetValue(IsFullScreenVideoProperty, value);
    }

    private static readonly object SharedPlayersLock = new();
    private static readonly Dictionary<string, SharedVideoPlayer> SharedPlayers = new(StringComparer.Ordinal);

    private SharedVideoPlayer? _sharedPlayer;
    private string? _registeredKey;
    private DateTime _lastSeek = DateTime.MinValue;
    private WriteableBitmap? _bitmap;
    private byte[]? _pendingFrame;
    private int _frameWidth;
    private int _frameHeight;
    private bool _frameUpdateQueued;
    private bool _isAttached;
    private string? _playbackError;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        RegisterSharedPlayer();
        ApplyState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        UnregisterSharedPlayer();
        _pendingFrame = null;
        _frameUpdateQueued = false;
        _bitmap?.Dispose();
        _bitmap = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SourceProperty)
            Log($"[VLC] Source changed -> {Source}");
        else if (change.Property == IsPlaybackActiveProperty)
            Log($"[VLC] IsPlaybackActive changed -> {IsPlaybackActive}");

        if (change.Property == PlayerKeyProperty)
            RegisterSharedPlayer();

        if (change.Property == SourceProperty ||
            change.Property == PlayerKeyProperty ||
            change.Property == IsPlaybackActiveProperty ||
            change.Property == PositionProperty ||
            change.Property == VolumeProperty ||
            change.Property == CanSeekProperty)
        {
            ApplyState();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Brushes.Black, bounds);

        if (!string.IsNullOrWhiteSpace(_playbackError))
        {
            var text = new FormattedText(
                _playbackError,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Normal),
                12,
                Brushes.LightGray);
            var origin = new Point(
                Math.Max(8, (bounds.Width - text.Width) / 2),
                Math.Max(8, (bounds.Height - text.Height) / 2));
            context.DrawText(text, origin);
            return;
        }

        if (_bitmap == null || _frameWidth <= 0 || _frameHeight <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var source = new Rect(0, 0, _frameWidth, _frameHeight);
        var dest = FitUniform(source.Size, bounds);
        context.DrawImage(_bitmap, source, dest);
    }

    private static Rect FitUniform(Size source, Rect bounds)
    {
        var scale = Math.Min(bounds.Width / source.Width, bounds.Height / source.Height);
        var width = source.Width * scale;
        var height = source.Height * scale;
        return new Rect(
            bounds.X + (bounds.Width - width) / 2,
            bounds.Y + (bounds.Height - height) / 2,
            width,
            height);
    }

    private void RegisterSharedPlayer()
    {
        if (!_isAttached)
        {
            UnregisterSharedPlayer();
            return;
        }

        var key = GetEffectivePlayerKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            UnregisterSharedPlayer();
            return;
        }

        SharedVideoPlayer? playerToDispose = null;
        lock (SharedPlayersLock)
        {
            if (string.Equals(_registeredKey, key, StringComparison.Ordinal) && _sharedPlayer != null)
                return;

            playerToDispose = UnregisterSharedPlayerCore();

            if (!SharedPlayers.TryGetValue(key, out var sharedPlayer))
            {
                try
                {
                    sharedPlayer = new SharedVideoPlayer(key);
                }
                catch (Exception ex) when (ex is FileNotFoundException or DllNotFoundException or BadImageFormatException or InvalidOperationException)
                {
                    SetPlaybackError("Video playback unavailable: Resonite libVLC was not found.");
                    Log($"[VLC] Failed to initialize shared player for {key}: {ex.GetType().Name}: {ex.Message}");
                    return;
                }

                SharedPlayers.Add(key, sharedPlayer);
            }

            _registeredKey = key;
            _sharedPlayer = sharedPlayer;
            SetPlaybackError(null);
            sharedPlayer.Attach(this);
        }

        playerToDispose?.Dispose();
    }

    private void UnregisterSharedPlayer()
    {
        SharedVideoPlayer? playerToDispose = null;
        lock (SharedPlayersLock)
        {
            playerToDispose = UnregisterSharedPlayerCore();
        }

        playerToDispose?.Dispose();
    }

    private SharedVideoPlayer? UnregisterSharedPlayerCore()
    {
        if (_sharedPlayer == null || _registeredKey == null)
            return null;

        SharedVideoPlayer? playerToDispose = null;
        _sharedPlayer.Detach(this);
        if (_sharedPlayer.IsUnused)
        {
            SharedPlayers.Remove(_registeredKey);
            playerToDispose = _sharedPlayer;
        }

        _sharedPlayer = null;
        _registeredKey = null;
        return playerToDispose;
    }

    private string? GetEffectivePlayerKey()
        => PlayerKey;

    private void ReceiveFrame(byte[] frame, int width, int height)
    {
        if (!_isAttached)
            return;

        SetPlaybackError(null);
        OnFrameReady(frame, width, height);
    }

    private void SetPlaybackError(string? message)
    {
        if (string.Equals(_playbackError, message, StringComparison.Ordinal))
            return;

        _playbackError = message;
        InvalidateVisual();
    }

    private void ApplyState()
    {
        RegisterSharedPlayer();
        if (_sharedPlayer == null)
            return;

        var source = Source;
        if (string.IsNullOrWhiteSpace(source))
            return;

        _sharedPlayer.Apply(source, IsPlaybackActive, Volume);

        if (CanSeek)
        {
            var now = DateTime.UtcNow;
            if (now - _lastSeek > TimeSpan.FromMilliseconds(700))
            {
                var targetMs = Math.Max(0, (long)(Position * 1000));
                var currentMs = _sharedPlayer.Time;
                if (currentMs < 0 || Math.Abs(currentMs - targetMs) > 1200)
                {
                    _sharedPlayer.Time = targetMs;
                    _lastSeek = now;
                }
            }
        }
    }

    private void OnFrameReady(byte[] frame, int width, int height)
    {
        if (!_isAttached)
            return;

        _pendingFrame = frame;
        _frameWidth = width;
        _frameHeight = height;

        if (_frameUpdateQueued)
            return;

        _frameUpdateQueued = true;
        Dispatcher.UIThread.Post(UpdateFrameBitmap, DispatcherPriority.Render);
    }

    private void UpdateFrameBitmap()
    {
        _frameUpdateQueued = false;
        if (!_isAttached)
        {
            _pendingFrame = null;
            return;
        }

        var frame = _pendingFrame;
        if (frame == null || _frameWidth <= 0 || _frameHeight <= 0)
            return;

        var size = new PixelSize(_frameWidth, _frameHeight);
        if (_bitmap == null || _bitmap.PixelSize != size)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        }

        using (var locked = _bitmap.Lock())
        {
            var expectedStride = _frameWidth * 4;
            if (locked.RowBytes == expectedStride)
            {
                Marshal.Copy(frame, 0, locked.Address, Math.Min(frame.Length, locked.RowBytes * _frameHeight));
            }
            else
            {
                for (var y = 0; y < _frameHeight; y++)
                {
                    var sourceOffset = y * expectedStride;
                    var destination = IntPtr.Add(locked.Address, y * locked.RowBytes);
                    Marshal.Copy(frame, sourceOffset, destination, Math.Min(expectedStride, locked.RowBytes));
                }
            }
        }

        InvalidateVisual();
    }

    private static bool IsRtspUrl(string source)
        => source.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase) ||
           source.StartsWith("rtsps://", StringComparison.OrdinalIgnoreCase) ||
           source.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase) ||
           source.StartsWith("rtmps://", StringComparison.OrdinalIgnoreCase);

    private sealed class SharedVideoPlayer : IDisposable
    {
        private readonly string _key;
        private readonly List<VlcVideoView> _views = new();
        private readonly SoftwareVlcPlayer _player;
        private string? _currentSource;
        private bool _disposed;

        public SharedVideoPlayer(string key)
        {
            _key = key;
            _player = new SoftwareVlcPlayer(OnFrameReady);
        }

        public bool IsUnused => _views.Count == 0;

        public long Time
        {
            get => _player.Time;
            set => _player.Time = value;
        }

        public void Attach(VlcVideoView view)
        {
            if (!_views.Contains(view))
                _views.Add(view);
        }

        public void Detach(VlcVideoView view)
            => _views.Remove(view);

        public void Apply(string source, bool isPlaybackActive, double volume)
        {
            if (_disposed)
                return;

            if (!string.Equals(source, _currentSource, StringComparison.Ordinal))
            {
                _currentSource = source;
                Log($"[VLC] Shared player {_key} opening source");
                _player.Open(source);
            }

            var isLiveStream = IsRtspUrl(source);
            if (isPlaybackActive || isLiveStream)
                _player.Play();
            else
                _player.Pause();

            _player.Volume = (int)Math.Round(volume);
        }

        private void OnFrameReady(byte[] frame, int width, int height)
        {
            if (_disposed)
                return;

            VlcVideoView[] targets;
            lock (SharedPlayersLock)
            {
                targets = _views.ToArray();
            }

            foreach (var view in targets)
                view.ReceiveFrame(frame, width, height);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _player.Dispose();
        }
    }

    private sealed class SoftwareVlcPlayer : IDisposable
    {
        private readonly object _frameLock = new();
        private readonly Action<byte[], int, int> _onFrameReady;
        private readonly IntPtr _libvlc;
        private readonly IntPtr _player;
        private readonly VideoLockCallback _lockCallback;
        private readonly VideoUnlockCallback _unlockCallback;
        private readonly VideoDisplayCallback _displayCallback;
        private readonly VideoFormatCallback _formatCallback;
        private readonly VideoCleanupCallback _cleanupCallback;
        private byte[]? _buffer;
        private GCHandle _bufferHandle;
        private int _width;
        private int _height;
        private bool _disposed;

        public SoftwareVlcPlayer(Action<byte[], int, int> onFrameReady)
        {
            _onFrameReady = onFrameReady;
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

            _lockCallback = LockVideo;
            _unlockCallback = UnlockVideo;
            _displayCallback = DisplayVideo;
            _formatCallback = SetupVideoFormat;
            _cleanupCallback = CleanupVideoFormat;
            VlcNative.libvlc_video_set_callbacks(_player, _lockCallback, _unlockCallback, _displayCallback, IntPtr.Zero);
            VlcNative.libvlc_video_set_format_callbacks(_player, _formatCallback, _cleanupCallback);
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
                Log($"[VLC] media_new_location returned null for: {source} err={VlcNative.GetLastError()}");
                return;
            }

            Log($"[VLC] Opening: {source} isRtsp={IsRtspUrl(source)}");

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
                    Log($"[VLC] Play() failed ret={ret} state={state} err={VlcNative.GetLastError()}");
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

        private uint SetupVideoFormat(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height, IntPtr pitches, IntPtr lines)
        {
            if (_disposed)
                return 0;

            var rv32 = Encoding.ASCII.GetBytes("RV32");
            Marshal.Copy(rv32, 0, chroma, rv32.Length);

            _width = Math.Max(1, (int)width);
            _height = Math.Max(1, (int)height);
            var pitch = _width * 4;
            var bytes = pitch * _height;

            lock (_frameLock)
            {
                if (_bufferHandle.IsAllocated)
                    _bufferHandle.Free();
                _buffer = new byte[bytes];
                _bufferHandle = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
            }

            Marshal.WriteInt32(pitches, pitch);
            Marshal.WriteInt32(lines, _height);
            return 1;
        }

        private void CleanupVideoFormat(IntPtr opaque)
        {
            lock (_frameLock)
            {
                if (_bufferHandle.IsAllocated)
                    _bufferHandle.Free();
                _buffer = null;
            }
        }

        private IntPtr LockVideo(IntPtr opaque, IntPtr planes)
        {
            lock (_frameLock)
            {
                if (_disposed || _buffer == null || !_bufferHandle.IsAllocated)
                    return IntPtr.Zero;

                Marshal.WriteIntPtr(planes, _bufferHandle.AddrOfPinnedObject());
            }

            return IntPtr.Zero;
        }

        private void UnlockVideo(IntPtr opaque, IntPtr picture, IntPtr planes)
        {
            byte[]? copy;
            int width;
            int height;
            lock (_frameLock)
            {
                if (_disposed || _buffer == null || _width <= 0 || _height <= 0)
                    return;

                copy = new byte[_buffer.Length];
                Buffer.BlockCopy(_buffer, 0, copy, 0, _buffer.Length);
                width = _width;
                height = _height;
            }

            if (!_disposed)
                _onFrameReady(copy, width, height);
        }

        private static void DisplayVideo(IntPtr opaque, IntPtr picture)
        {
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_player != IntPtr.Zero)
            {
                VlcNative.libvlc_media_player_stop(_player);
                VlcNative.libvlc_video_set_callbacks_raw(_player, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                VlcNative.libvlc_video_set_format_callbacks_raw(_player, IntPtr.Zero, IntPtr.Zero);
                VlcNative.libvlc_media_player_release(_player);
            }

            if (_libvlc != IntPtr.Zero)
                VlcNative.libvlc_release(_libvlc);

            CleanupVideoFormat(IntPtr.Zero);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr VideoLockCallback(IntPtr opaque, IntPtr planes);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void VideoUnlockCallback(IntPtr opaque, IntPtr picture, IntPtr planes);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void VideoDisplayCallback(IntPtr opaque, IntPtr picture);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint VideoFormatCallback(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height, IntPtr pitches, IntPtr lines);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void VideoCleanupCallback(IntPtr opaque);

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
            var candidates = new System.Collections.Generic.List<string>();
            var appDir = AppContext.BaseDirectory;
            var parent = Directory.GetParent(appDir)?.FullName;
            var grandParent = parent == null ? null : Directory.GetParent(parent)?.FullName;

            AddCandidate(RuntimeBootstrap.CurrentEngineDir);
            AddCandidate(RuntimeBootstrap.SavedEngineDir);
            AddCandidate(RuntimeBootstrap.SuggestedEngineDir);
            AddCandidate(appDir);
            if (parent != null) AddCandidate(parent);
            if (grandParent != null) AddCandidate(grandParent);
            AddCandidate(@"C:\Program Files (x86)\Steam\steamapps\common\Resonite");

            foreach (var candidate in candidates.Distinct(GetPathComparer()))
            {
                if (File.Exists(Path.Combine(candidate, "libvlc.dll")) &&
                    Directory.Exists(Path.Combine(candidate, "plugins")))
                {
                    Log($"[VLC] Using libVLC from: {candidate}");
                    return candidate;
                }
            }

            Log($"[VLC] Could not find libVLC. Checked: {string.Join("; ", candidates.Distinct(GetPathComparer()))}");
            return null;

            void AddCandidate(string? baseDir)
            {
                if (string.IsNullOrWhiteSpace(baseDir))
                    return;

                candidates.Add(Path.Combine(baseDir, "Renderer", "Renderite.Renderer_Data", "Plugins", "x86_64"));
            }
        }

        private static StringComparer GetPathComparer()
            => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        public static string GetLastError()
        {
            var ptr = libvlc_errmsg();
            return ptr == IntPtr.Zero ? "(no error)" : Marshal.PtrToStringAnsi(ptr) ?? "(null)";
        }

        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr libvlc_errmsg();

        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
        public static extern void libvlc_video_set_callbacks(
            IntPtr mediaPlayer,
            VideoLockCallback lockCallback,
            VideoUnlockCallback unlockCallback,
            VideoDisplayCallback displayCallback,
            IntPtr opaque);

        [DllImport("libvlc", EntryPoint = "libvlc_video_set_callbacks", CallingConvention = CallingConvention.Cdecl)]
        public static extern void libvlc_video_set_callbacks_raw(
            IntPtr mediaPlayer,
            IntPtr lockCallback,
            IntPtr unlockCallback,
            IntPtr displayCallback,
            IntPtr opaque);

        [DllImport("libvlc", EntryPoint = "libvlc_video_set_format_callbacks", CallingConvention = CallingConvention.Cdecl)]
        public static extern void libvlc_video_set_format_callbacks(
            IntPtr mediaPlayer,
            VideoFormatCallback setupCallback,
            VideoCleanupCallback cleanupCallback);

        [DllImport("libvlc", EntryPoint = "libvlc_video_set_format_callbacks", CallingConvention = CallingConvention.Cdecl)]
        public static extern void libvlc_video_set_format_callbacks_raw(
            IntPtr mediaPlayer,
            IntPtr setupCallback,
            IntPtr cleanupCallback);

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
        public static extern int libvlc_audio_get_volume(IntPtr mediaPlayer);

        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
        public static extern int libvlc_audio_set_volume(IntPtr mediaPlayer, int volume);
    }
}
