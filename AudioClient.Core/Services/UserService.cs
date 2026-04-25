using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using AudioClient.Core.Models;
using Elements.Assets;
using FrooxEngine;
using UserInfo = AudioClient.Core.Models.UserInfo;

namespace AudioClient.Core.Services;

public class UserService
{
    private readonly Engine _engine;
    private List<UserInfo> _lastUsers = new();
    private string? _lastVoiceMode;

    private readonly ConcurrentDictionary<string, VolumeMeter> _volumeMeters = new();
    private readonly ConcurrentDictionary<string, IWorldAudioDataSource> _audioStreams = new();
    private Dictionary<string, bool> _lastSpeaking = new();
    private volatile Dictionary<string, bool>? _speakingSnapshot = null;
    private const float SpeakingThreshold = 0.02f;

    private static readonly ConcurrentDictionary<Type, FieldInfo?> _bufferFieldCache = new();

    public event EventHandler<List<UserInfo>>? UsersChanged;
    public event EventHandler<string?>? VoiceModeChanged;
    public event EventHandler<Dictionary<string, bool>>? SpeakingChanged;

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal UserService(Engine engine)
    {
        _engine = engine;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public List<UserInfo> GetCurrentUsers()
    {
        var world = _engine.WorldManager.FocusedWorld;
        if (world == null || world == Userspace.UserspaceWorld) return new List<UserInfo>();
        var contactIds = new HashSet<string>();
        var contactIconMap = new Dictionary<string, string?>();
        _engine.Cloud.Contacts.ForeachContactData(cd =>
        {
            if (cd.Contact.ContactUserId != null)
            {
                contactIds.Add(cd.Contact.ContactUserId);
                contactIconMap[cd.Contact.ContactUserId] = ToHttpIconUrl(cd.Contact.Profile?.IconUrl);
            }
        });
        return world.AllUsers.Select(u => new UserInfo(
            u.UserName, u.UserID, u.IsHost, u.IsLocalUser, u.IsPresentInWorld, u.Ping,
            u.UserID != null && contactIds.Contains(u.UserID),
            u.UserID != null ? contactIconMap.GetValueOrDefault(u.UserID) : null)).ToList();
    }

    private static string? ToHttpIconUrl(string? url)
    {
        if (url == null) return null;
        if (url.StartsWith("resdb:///"))
        {
            var path = url.Substring("resdb:///".Length);
            var dot = path.LastIndexOf('.');
            if (dot >= 0) path = path.Substring(0, dot);
            return "https://assets.resonite.com/" + path;
        }
        return url;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void MoveToUser(string targetUserName)
    {
        var world = _engine.WorldManager.FocusedWorld;
        if (world == null) return;
        var localUser = world.LocalUser;
        if (localUser?.Root == null) return;

        User? targetUser = null;
        foreach (var user in world.AllUsers)
        {
            if (string.Equals(user.UserName, targetUserName, StringComparison.OrdinalIgnoreCase))
            { targetUser = user; break; }
        }
        if (targetUser == null || targetUser.IsLocalUser || targetUser.Root == null) return;

        var capturedTarget = targetUser.Root;
        var capturedLocal = localUser.Root;
        world.RunSynchronously(() =>
        {
            var headPos = capturedTarget.HeadPosition;
            capturedLocal.JumpToPoint(headPos, 1.0f);

            var loco = capturedLocal.Slot.GetComponentInChildren<LocomotionController>();
            if (loco != null) SwitchToNoClip(loco);
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public List<string> GetLocomotionModules()
    {
        var world = _engine.WorldManager.FocusedWorld;
        if (world == null) return new List<string>();
        var localUser = world.LocalUser;
        if (localUser?.Root == null) return new List<string>();
        var loco = localUser.Root.Slot.GetComponentInChildren<LocomotionController>();
        if (loco == null) return new List<string>();
        var result = new List<string>();
        foreach (var m in loco.LocomotionModules)
        {
            if (m == null) continue;
            string name;
            try { name = m.LocomotionName.ToString(); } catch { name = m.GetType().Name; }
            bool isActive = loco.ActiveModule == m;
            result.Add(isActive ? $"[ACTIVE] {name}" : name);
        }
        return result;
    }

    private static void SwitchToNoClip(LocomotionController loco)
    {
        foreach (var m in loco.LocomotionModules)
        {
            if (m == null) continue;
            string name;
            try { name = m.LocomotionName.ToString(); } catch { name = m.GetType().Name; }
            if (name.ToLowerInvariant().Contains("noclip") ||
                m.GetType().Name.ToLowerInvariant().Contains("noclip"))
            {
                if (loco.ActiveModule != m)
                    loco.ActiveModule = m;
                return;
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void SetLocomotionModule(string targetName)
    {
        var world = _engine.WorldManager.FocusedWorld;
        if (world == null) return;
        var localUser = world.LocalUser;
        if (localUser?.Root == null) return;
        var loco = localUser.Root.Slot.GetComponentInChildren<LocomotionController>();
        if (loco == null) return;

        ILocomotionModule? target = null;
        foreach (var m in loco.LocomotionModules)
        {
            if (m == null) continue;
            string name;
            try { name = m.LocomotionName.ToString(); } catch { name = m.GetType().Name; }
            if (name.ToLowerInvariant().Contains(targetName.ToLowerInvariant()) ||
                m.GetType().Name.ToLowerInvariant().Contains(targetName.ToLowerInvariant()))
            { target = m; break; }
        }
        if (target != null)
            world.RunSynchronously(() => loco.ActiveModule = target);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public string? GetVoiceMode()
    {
        var world = _engine.WorldManager.FocusedWorld;
        if (world == null) return null;
        return world.LocalUser?.VoiceMode.ToString();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool SetVoiceMode(string modeName)
    {
        var world = _engine.WorldManager.FocusedWorld;
        if (world == null) return false;
        var localUser = world.LocalUser;
        if (localUser == null) return false;

        VoiceMode targetMode = modeName.ToLowerInvariant() switch
        {
            "normal" => VoiceMode.Normal,
            "shout" => VoiceMode.Shout,
            "broadcast" => VoiceMode.Broadcast,
            "whisper" => VoiceMode.Whisper,
            "mute" => VoiceMode.Mute,
            _ => (VoiceMode)(-1)
        };
        if ((int)targetMode == -1) return false;

        world.RunSynchronously(() => localUser.VoiceMode = targetMode);
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal void Refresh()
    {
        try
        {
            var current = GetCurrentUsers();
            if (!UserListsEqual(current, _lastUsers))
            {
                _lastUsers = current;
                UsersChanged?.Invoke(this, current);
            }
        }
        catch { }
        try
        {
            var mode = GetVoiceMode();
            if (mode != _lastVoiceMode)
            {
                _lastVoiceMode = mode;
                VoiceModeChanged?.Invoke(this, mode);
            }
        }
        catch { }
        try
        {
            var world = _engine.WorldManager.FocusedWorld;
            if (world != null && world != Userspace.UserspaceWorld)
            {
                // Compute speaking state entirely on the engine thread to avoid races
                // between _audioStreams cleanup and poll-thread reads.
                world.RunSynchronously(() =>
                {
                    EnsureVolumeMeters(world);
                    var speaking = new Dictionary<string, bool>();
                    foreach (var kv in _volumeMeters)
                        if (!kv.Value.IsDestroyed)
                            speaking[kv.Key] = kv.Value.Volume.Value > SpeakingThreshold;
                    foreach (var kv in _audioStreams)
                        if (!speaking.ContainsKey(kv.Key))
                            speaking[kv.Key] = ComputeStreamAmplitude(kv.Value) > SpeakingThreshold;
                    _speakingSnapshot = speaking;
                });

                // Read the snapshot produced by the PREVIOUS RunSynchronously execution.
                var snapshot = _speakingSnapshot;
                if (snapshot != null && !SpeakingEqual(snapshot, _lastSpeaking))
                {
                    _lastSpeaking = snapshot;
                    SpeakingChanged?.Invoke(this, snapshot);
                }
            }
        }
        catch { }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void EnsureVolumeMeters(World world)
    {
        var presentIds = new HashSet<string>();
        foreach (var user in world.AllUsers)
        {
            if (user.IsLocalUser || user.UserID == null) continue;
            presentIds.Add(user.UserID);

            if (!_volumeMeters.ContainsKey(user.UserID))
            {
                var root = user.Root?.Slot;
                if (root != null)
                {
                    var meter = root.GetComponentInChildren<VolumeMeter>();
                    if (meter != null)
                        _volumeMeters[user.UserID] = meter;
                }
            }

            if (!_audioStreams.ContainsKey(user.UserID))
            {
                int streamCount = 0;
                foreach (var stream in user.Streams)
                {
                    streamCount++;
                    if (stream is IWorldAudioDataSource audioSource)
                    {
                        _audioStreams[user.UserID] = audioSource;
                        Elements.Core.UniLog.Log($"[AudioClient] Stream found for {user.UserName}: type={audioSource.GetType().Name}");
                        break;
                    }
                }
                if (!_audioStreams.ContainsKey(user.UserID))
                    Elements.Core.UniLog.Log($"[AudioClient] No audio stream found for {user.UserName}, total streams={streamCount}");
            }
        }

        foreach (var id in _volumeMeters.Keys.ToList())
            if (!presentIds.Contains(id)) _volumeMeters.TryRemove(id, out _);

        foreach (var id in _audioStreams.Keys.ToList())
            if (!presentIds.Contains(id)) _audioStreams.TryRemove(id, out _);
    }

    private static FieldInfo? GetAudioBufferField(Type type)
    {
        return _bufferFieldCache.GetOrAdd(type, t =>
        {
            while (t != null)
            {
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(AudioStream<>))
                    return t.GetField("audioBuffer", BindingFlags.NonPublic | BindingFlags.Instance);
                t = t.BaseType!;
            }
            return null;
        });
    }

    private static float ComputeStreamAmplitude(IWorldAudioDataSource stream)
    {
        try
        {
            var field = GetAudioBufferField(stream.GetType());
            if (field == null)
            {
                Elements.Core.UniLog.Log($"[AudioClient] audioBuffer field not found for type {stream.GetType().Name}");
                return 0f;
            }
            var rawBuffer = field.GetValue(stream);
            if (rawBuffer is CircularAudioBuffer<MonoSample> monoBuf)
            {
                float amp = ComputeAmplitude(monoBuf);
                if (amp > 0.001f) Elements.Core.UniLog.Log($"[AudioClient] Mono amplitude: {amp:F4}");
                return amp;
            }
            if (rawBuffer is CircularAudioBuffer<StereoSample> stereoBuf)
            {
                float amp = ComputeAmplitude(stereoBuf);
                if (amp > 0.001f) Elements.Core.UniLog.Log($"[AudioClient] Stereo amplitude: {amp:F4}");
                return amp;
            }
            Elements.Core.UniLog.Log($"[AudioClient] Unknown buffer type: {rawBuffer?.GetType().Name ?? "null"}");
            return 0f;
        }
        catch (Exception ex)
        {
            Elements.Core.UniLog.Log($"[AudioClient] ComputeStreamAmplitude error: {ex.Message}");
            return 0f;
        }
    }

    private static float ComputeAmplitude<S>(CircularAudioBuffer<S> buffer) where S : unmanaged, IAudioSample<S>
    {
        float max = 0f;
        foreach (var s in buffer)
        {
            float a = s.AbsoluteAmplitude;
            if (a > max) max = a;
        }
        return max;
    }

    private static bool SpeakingEqual(Dictionary<string, bool> a, Dictionary<string, bool> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var kv in a)
            if (!b.TryGetValue(kv.Key, out var v) || v != kv.Value) return false;
        return true;
    }

    private static bool UserListsEqual(List<UserInfo> a, List<UserInfo> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i] != b[i]) return false;
        return true;
    }
}
