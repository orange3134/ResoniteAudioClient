using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AudioClient.Core.Models;
using FrooxEngine;

namespace AudioClient.Core.Services;

public class VideoService
{
    private readonly Engine _engine;
    private World? _lastWorld;
    private List<VideoPlayerInfo> _lastVideos = new();
    private DateTime _lastDiagnosticLog = DateTime.MinValue;
    private string _lastDiagnosticSignature = "";
    private readonly ConcurrentDictionary<string, VideoMetadataState> _metadata = new(StringComparer.Ordinal);
    private static readonly FieldInfo? PlaybackField = typeof(VideoTextureProvider)
        .GetField("Playback", BindingFlags.Instance | BindingFlags.NonPublic);

    public event EventHandler<List<VideoPlayerInfo>>? VideoListChanged;

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal VideoService(Engine engine)
    {
        _engine = engine;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public List<VideoPlayerInfo> GetCurrentVideos()
    {
        var world = GetVideoWorld();
        if (world == null)
            return new List<VideoPlayerInfo>();

        return SnapshotVideos(world);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Play(string id) => ModifyPlayable(id, playable => playable.Play());

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Resume(string id) => ModifyPlayable(id, playable => playable.Resume());

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Pause(string id) => ModifyPlayable(id, playable => playable.Pause());

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Stop(string id) => ModifyPlayable(id, playable => playable.Stop());

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void SetPosition(string id, float seconds)
        => ModifyPlayable(id, playable => playable.Position = seconds);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void SetLoop(string id, bool loop)
        => ModifyPlayable(id, playable => playable.Loop = loop);

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal void Refresh()
    {
        try
        {
            var world = GetVideoWorld();

            var current = world == _lastWorld ? GetCurrentVideos() : new List<VideoPlayerInfo>();
            if (world != _lastWorld)
            {
                _lastWorld = world;
                current = world == null ? new List<VideoPlayerInfo>() : GetCurrentVideos();
            }

            if (!VideoListsEqual(current, _lastVideos))
            {
                _lastVideos = current;
                var summary = string.Join(", ", current.Select(v =>
                    $"{v.Title}[play={v.IsPlaying} len={v.ClipLength:F0} pos={v.Position:F1} url={v.PlaybackUrl}]"));
                Elements.Core.UniLog.Log($"[AudioClient] Video list changed: {current.Count} video(s). World: {world?.Name ?? "<none>"}. Videos: {summary}");
                VideoListChanged?.Invoke(this, current);
            }

            LogDiagnosticsIfNeeded(world, current.Count);
        }
        catch (Exception ex)
        {
            Elements.Core.UniLog.Error("[AudioClient] Video refresh failed: " + ex);
        }
    }

    private void ModifyPlayable(string id, Action<IPlayable> action)
    {
        var world = GetVideoWorld();
        if (world == null)
            return;

        world.RunSynchronously(() =>
        {
            var playable = FindPlayableProvider(world, id);
            if (playable == null || playable.IsRemoved) return;
            if (playable is VideoTextureProvider provider)
                ApplyResolvedClipLength(provider, EnsureMetadata(provider));
            action(playable);
        });
    }

    private static IPlayable? FindPlayableProvider(World world, string id)
        => GetVideoPlayers(world)
            .FirstOrDefault(p => p.ReferenceID.ToString() == id);

    private static VideoPlayerInfo ToInfo(VideoTextureProvider provider, VideoMetadataState? metadata)
    {
        var title = provider.VideoTitle.Value;
        if (string.IsNullOrWhiteSpace(title))
            title = provider.Slot.Name;
        if (string.IsNullOrWhiteSpace(title))
            title = provider.URL.Value?.ToString() ?? "Untitled video";

        var clipLength = provider.ClipLength;
        if ((clipLength <= 0 || double.IsNaN(clipLength)) && metadata?.DurationSeconds is > 0)
            clipLength = metadata.DurationSeconds.Value;

        var url = provider.URL.Value?.ToString() ?? "";

        return new VideoPlayerInfo(
            provider.ReferenceID.ToString(),
            provider.Slot.Name ?? "",
            title,
            url,
            metadata?.PlaybackUrl ?? url,
            provider.IsPlaying,
            provider.Loop,
            provider.Position,
            clipLength,
            provider.Speed);
    }

    private World? GetVideoWorld()
    {
        var focused = _engine.WorldManager.FocusedWorld;
        if (IsUsableWorld(focused) && HasVideoProvider(focused))
            return focused;

        var worlds = new List<World>();
        _engine.WorldManager.GetWorlds(worlds);

        return worlds
            .Where(IsUsableWorld)
            .OrderByDescending(w => w == focused)
            .FirstOrDefault(HasVideoProvider);
    }

    private void LogDiagnosticsIfNeeded(World? selectedWorld, int videoCount)
    {
        var worlds = new List<World>();
        _engine.WorldManager.GetWorlds(worlds);

        var parts = new List<string>();
        foreach (var world in worlds)
        {
            VideoDiagnostics diagnostics = default;
            if (world.State == World.WorldState.Running)
            {
                diagnostics = GetVideoDiagnostics(world);
            }

            var marker = world == Userspace.UserspaceWorld ? ":userspace" : "";
            var videoTypes = string.IsNullOrWhiteSpace(diagnostics.VideoTypeSummary)
                ? "-"
                : diagnostics.VideoTypeSummary;
            parts.Add($"{world.Name ?? "<unnamed>"}:{world.State}{marker}:videos={diagnostics.ProviderCount}:root={diagnostics.RootProviderCount}:videoTypes={videoTypes}");
        }

        var signature = $"selected={selectedWorld?.Name ?? "<none>"};videos={videoCount};worlds={string.Join("|", parts)}";
        var now = DateTime.UtcNow;
        if (signature == _lastDiagnosticSignature && now - _lastDiagnosticLog < TimeSpan.FromSeconds(5))
            return;

        _lastDiagnosticSignature = signature;
        _lastDiagnosticLog = now;
        Elements.Core.UniLog.Log("[AudioClient] Video diagnostics: " + signature);
    }

    private static bool IsUsableWorld(World? world)
        => world != null && world != Userspace.UserspaceWorld && world.State == World.WorldState.Running;

    private static bool HasVideoProvider(World world)
        => GetVideoPlayers(world).Count > 0;

    private List<VideoPlayerInfo> SnapshotVideos(World world)
    {
        var result = new List<VideoPlayerInfo>();
        var providers = GetVideoPlayers(world)
            .OrderBy(p => p.Slot.Name)
            .ThenBy(p => p.ReferenceID.ToString());

        foreach (var provider in providers)
        {
            var metadata = EnsureMetadata(provider);
            ApplyResolvedClipLength(provider, metadata);
            result.Add(ToInfo(provider, metadata));
        }

        return result;
    }

    private VideoMetadataState? EnsureMetadata(VideoTextureProvider provider)
    {
        var url = provider.URL.Value?.ToString();
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var state = _metadata.GetOrAdd(url, _ => new VideoMetadataState());
        if (!state.Started)
        {
            lock (state)
            {
                if (!state.Started)
                {
                    state.Started = true;
                    _ = Task.Run(() => ResolveMetadataAsync(url, state));
                }
            }
        }

        return state;
    }

    private static bool IsDirectStreamUrl(string url)
        => url.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase) ||
           url.StartsWith("rtsps://", StringComparison.OrdinalIgnoreCase) ||
           url.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase) ||
           url.StartsWith("rtmps://", StringComparison.OrdinalIgnoreCase);

    private async Task ResolveMetadataAsync(string url, VideoMetadataState state)
    {
        try
        {
            if (IsDirectStreamUrl(url))
            {
                state.PlaybackUrl = url;
                Elements.Core.UniLog.Log($"[AudioClient] Direct stream URL, skipping yt-dlp: {url}");
                return;
            }

            var executable = Path.Combine(_engine.AppPath, "RuntimeData", _engine.Platform == Platform.Windows ? "yt-dlp.exe" : "yt-dlp_linux");
            if (!File.Exists(executable))
            {
                state.Error = "yt-dlp not found";
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            startInfo.ArgumentList.Add("--no-playlist");
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add("best[ext=mp4]/best");
            startInfo.ArgumentList.Add("--print");
            startInfo.ArgumentList.Add("%(duration)s");
            startInfo.ArgumentList.Add("--print");
            startInfo.ArgumentList.Add("%(url)s");
            startInfo.ArgumentList.Add(url);

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                state.Error = "failed to start yt-dlp";
                return;
            }

            var stdout = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            var stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                state.Error = stderr.Trim();
                Elements.Core.UniLog.Warning($"[AudioClient] Failed to resolve video metadata for {url}: {state.Error}");
                return;
            }

            var lines = stdout
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .ToArray();

            if (lines.Length > 0 && double.TryParse(lines[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var duration) && duration > 0)
                state.DurationSeconds = duration;
            if (lines.Length > 1 && Uri.TryCreate(lines[1], UriKind.Absolute, out _))
                state.PlaybackUrl = lines[1];

            Elements.Core.UniLog.Log($"[AudioClient] Resolved video metadata: duration={state.DurationSeconds?.ToString(CultureInfo.InvariantCulture) ?? "<unknown>"}, playbackUrl={(string.IsNullOrWhiteSpace(state.PlaybackUrl) ? "<original>" : "<resolved>")}");
        }
        catch (Exception ex)
        {
            state.Error = ex.Message;
            Elements.Core.UniLog.Warning($"[AudioClient] Exception resolving video metadata for {url}: {ex}");
        }
    }

    private static void ApplyResolvedClipLength(VideoTextureProvider provider, VideoMetadataState? metadata = null)
    {
        if (provider.ClipLength > 0)
            return;

        metadata ??= null;
        var duration = metadata?.DurationSeconds;
        if (duration is not > 0)
            return;

        if (PlaybackField?.GetValue(provider) is SyncPlayback playback)
            playback.ClipLength = duration.Value;
    }

    private static List<VideoTextureProvider> GetVideoPlayers(World world)
    {
        var result = new List<VideoTextureProvider>();

        foreach (var provider in world.RootSlot.GetComponentsInChildren<VideoTextureProvider>(includeLocal: true))
        {
            if (provider.IsRemoved)
                continue;

            result.Add(provider);
        }

        return result;
    }

    private static VideoDiagnostics GetVideoDiagnostics(World world)
    {
        var allProviders = world.RootSlot
            .GetComponentsInChildren<VideoTextureProvider>(includeLocal: true)
            .ToList();

        var rootProviderCount = allProviders.Count(p => !p.IsRemoved);

        var videoTypes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var provider in allProviders)
            AddVideoType(videoTypes, provider.GetType().FullName ?? provider.GetType().Name);

        var summary = videoTypes.Count == 0
            ? "-"
            : string.Join(",", videoTypes.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"));

        return new VideoDiagnostics(
            GetVideoPlayers(world).Count,
            rootProviderCount,
            summary);
    }

    private static void AddVideoType(Dictionary<string, int> videoTypes, string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName) ||
            typeName.IndexOf("Video", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return;
        }

        var shortName = typeName.Split('.').LastOrDefault() ?? typeName;
        videoTypes.TryGetValue(shortName, out var count);
        videoTypes[shortName] = count + 1;
    }

    private readonly record struct VideoDiagnostics(
        int ProviderCount,
        int RootProviderCount,
        string VideoTypeSummary);

    private sealed class VideoMetadataState
    {
        public bool Started { get; set; }
        public double? DurationSeconds { get; set; }
        public string? PlaybackUrl { get; set; }
        public string? Error { get; set; }
    }

    private static bool VideoListsEqual(List<VideoPlayerInfo> a, List<VideoPlayerInfo> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i]) return false;
        }
        return true;
    }
}
