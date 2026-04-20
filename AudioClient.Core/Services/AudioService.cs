using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using AudioClient.Core.Models;
using FrooxEngine;

namespace AudioClient.Core.Services;

public class AudioService
{
    private readonly Engine _engine;
    private bool _lastMuted;
    private VolumeInfo? _lastVolumes;

    public event EventHandler<bool>? MuteChanged;
    public event EventHandler<VolumeInfo>? VolumeChanged;

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal AudioService(Engine engine)
    {
        _engine = engine;
        _lastMuted = engine.AudioSystem.IsMuted;
        _lastVolumes = GetVolumes();
    }

    public bool IsMuted => _engine.AudioSystem.IsMuted;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void ToggleMute()
    {
        _engine.AudioSystem.IsMuted = !_engine.AudioSystem.IsMuted;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public List<DeviceInfo> GetInputDevices()
    {
        var result = new List<DeviceInfo>();
        var inputs = _engine.AudioSystem.AudioInputs;
        int current = _engine.AudioSystem.DefaultAudioInputIndex;
        result.Add(new DeviceInfo(0, "System Default", current < 0, true));
        for (int i = 0; i < inputs.Count; i++)
            result.Add(new DeviceInfo(i + 1, inputs[i].Name, i == current, inputs[i].IsConnected));
        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public List<DeviceInfo> GetOutputDevices()
    {
        var result = new List<DeviceInfo>();
        var outputs = _engine.AudioSystem.AudioOutputs;
        int current = _engine.AudioSystem.DefaultAudioOutputIndex;
        result.Add(new DeviceInfo(0, "System Default", current < 0, true));
        for (int i = 0; i < outputs.Count; i++)
            result.Add(new DeviceInfo(i + 1, outputs[i].Name, i == current, outputs[i].IsConnected));
        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void SetInputDevice(int userIndex)
    {
        var inputs = _engine.AudioSystem.AudioInputs;
        if (userIndex < 0 || userIndex > inputs.Count) return;
        _engine.AudioSystem.DefaultAudioInputIndex = userIndex == 0 ? -1 : userIndex - 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void SetOutputDevice(int userIndex)
    {
        var outputs = _engine.AudioSystem.AudioOutputs;
        if (userIndex < 0 || userIndex > outputs.Count) return;
        _engine.AudioSystem.DefaultAudioOutputIndex = userIndex == 0 ? -1 : userIndex - 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public VolumeInfo? GetVolumes()
    {
        var s = Settings.GetActiveSetting<AudioVolumeSettings>();
        if (s == null) return null;
        return new VolumeInfo(s.MasterVolume.Value, s.SoundEffectVolume.Value,
            s.MultimediaVolume.Value, s.VoiceVolume.Value, s.UserInterfaceVolume.Value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void SetMasterVolume(float v) => SetVolume(s => s.MasterVolume, v);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void SetSoundEffectVolume(float v) => SetVolume(s => s.SoundEffectVolume, v);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void SetMultimediaVolume(float v) => SetVolume(s => s.MultimediaVolume, v);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void SetVoiceVolume(float v) => SetVolume(s => s.VoiceVolume, v);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void SetUIVolume(float v) => SetVolume(s => s.UserInterfaceVolume, v);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void SetVolume(Func<AudioVolumeSettings, Sync<float>> selector, float v)
    {
        if (v < 0f || v > 1f) return;
        Settings.UpdateActiveSetting<AudioVolumeSettings>(s => selector(s).Value = v);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal void Refresh()
    {
        bool muted = _engine.AudioSystem.IsMuted;
        if (muted != _lastMuted)
        {
            _lastMuted = muted;
            MuteChanged?.Invoke(this, muted);
        }

        var vol = GetVolumes();
        if (vol != null && vol != _lastVolumes)
        {
            _lastVolumes = vol;
            VolumeChanged?.Invoke(this, vol);
        }
    }
}
