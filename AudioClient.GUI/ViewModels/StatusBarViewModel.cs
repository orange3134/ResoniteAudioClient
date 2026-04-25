using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AudioClient.Core.Models;

namespace AudioClient.GUI.ViewModels;

public partial class StatusBarViewModel : ObservableObject
{
    [ObservableProperty] private bool _isMuted = false;
    [ObservableProperty] private bool _isMicActive = false;
    [ObservableProperty] private bool _isSpeakerMuted = false;
    [ObservableProperty] private float _masterVolume = 1f;
    [ObservableProperty] private float _soundEffectVolume = 1f;
    [ObservableProperty] private float _multimediaVolume = 1f;
    [ObservableProperty] private float _voiceVolume = 1f;
    [ObservableProperty] private float _uIVolume = 1f;
    [ObservableProperty] private string _muteButtonText = "🎤";
    [ObservableProperty] private string _currentVoiceMode = "Normal";
    [ObservableProperty] private bool _isVoiceModePopupOpen = false;
    [ObservableProperty] private bool _isVolumePopupOpen = false;
    [ObservableProperty] private bool _isInputDevicePopupOpen = false;
    [ObservableProperty] private bool _isOutputDevicePopupOpen = false;

    private float _savedMasterVolume = 1f;

    public ObservableCollection<DeviceInfo> InputDevices { get; } = new();
    public ObservableCollection<DeviceInfo> OutputDevices { get; } = new();

    public Action? OnToggleMute { get; set; }
    public Action<float>? OnSetVolume { get; set; }
    public Action<float>? OnSetSoundEffectVolume { get; set; }
    public Action<float>? OnSetMultimediaVolume { get; set; }
    public Action<float>? OnSetVoiceVolume { get; set; }
    public Action<float>? OnSetUIVolume { get; set; }
    public Action<string>? OnSetVoiceMode { get; set; }
    public Action? OnShowLogin { get; set; }
    public Action? OnOpenSettings { get; set; }
    public Func<List<DeviceInfo>>? OnGetInputDevices { get; set; }
    public Func<List<DeviceInfo>>? OnGetOutputDevices { get; set; }
    public Action<int>? OnSetInputDevice { get; set; }
    public Action<int>? OnSetOutputDevice { get; set; }

    partial void OnIsMutedChanged(bool value)
        => MuteButtonText = value ? "🔇" : "🎤";

    partial void OnIsSpeakerMutedChanged(bool value)
        => OnPropertyChanged(nameof(SpeakerButtonText));

    public string SpeakerButtonText => IsSpeakerMuted ? "🔇" : "🔊";

    partial void OnMasterVolumeChanged(float value)
    {
        if (IsSpeakerMuted && value > 0f)
            IsSpeakerMuted = false;
        OnSetVolume?.Invoke(value);
    }

    partial void OnSoundEffectVolumeChanged(float value)
        => OnSetSoundEffectVolume?.Invoke(value);

    partial void OnMultimediaVolumeChanged(float value)
        => OnSetMultimediaVolume?.Invoke(value);

    partial void OnVoiceVolumeChanged(float value)
        => OnSetVoiceVolume?.Invoke(value);

    partial void OnUIVolumeChanged(float value)
        => OnSetUIVolume?.Invoke(value);

    public void UpdateVolumes(VolumeInfo vol)
    {
        if (!IsSpeakerMuted)
            MasterVolume = vol.Master;
        SoundEffectVolume = vol.SoundEffect;
        MultimediaVolume = vol.Multimedia;
        VoiceVolume = vol.Voice;
        UIVolume = vol.UI;
    }

    [RelayCommand]
    private void ToggleMute() => OnToggleMute?.Invoke();

    [RelayCommand]
    private void ToggleSpeakerMute()
    {
        if (IsSpeakerMuted)
        {
            IsSpeakerMuted = false;
            MasterVolume = _savedMasterVolume;
        }
        else
        {
            _savedMasterVolume = MasterVolume > 0f ? MasterVolume : 1f;
            IsSpeakerMuted = true;
            MasterVolume = 0f;
        }
    }

    [RelayCommand]
    private void ShowLogin() => OnShowLogin?.Invoke();

    [RelayCommand]
    private void OpenSettings() => OnOpenSettings?.Invoke();

    [RelayCommand]
    private void ToggleVoiceModePopup() => IsVoiceModePopupOpen = !IsVoiceModePopupOpen;

    [RelayCommand]
    private void ToggleVolumePopup() => IsVolumePopupOpen = !IsVolumePopupOpen;

    [RelayCommand]
    private void ToggleInputDevicePopup()
    {
        if (!IsInputDevicePopupOpen)
            RefreshDeviceList(InputDevices, OnGetInputDevices);
        IsInputDevicePopupOpen = !IsInputDevicePopupOpen;
    }

    [RelayCommand]
    private void ToggleOutputDevicePopup()
    {
        if (!IsOutputDevicePopupOpen)
            RefreshDeviceList(OutputDevices, OnGetOutputDevices);
        IsOutputDevicePopupOpen = !IsOutputDevicePopupOpen;
    }

    [RelayCommand]
    private void SetInputDevice(int index)
    {
        IsInputDevicePopupOpen = false;
        OnSetInputDevice?.Invoke(index);
    }

    [RelayCommand]
    private void SetOutputDevice(int index)
    {
        IsOutputDevicePopupOpen = false;
        OnSetOutputDevice?.Invoke(index);
    }

    private static void RefreshDeviceList(ObservableCollection<DeviceInfo> target, Func<List<DeviceInfo>>? source)
    {
        var devices = source?.Invoke();
        if (devices == null) return;
        target.Clear();
        foreach (var d in devices) target.Add(d);
    }

    [RelayCommand]
    private void SetVoiceMode(string mode)
    {
        IsVoiceModePopupOpen = false;
        CurrentVoiceMode = mode;
        OnSetVoiceMode?.Invoke(mode);
    }

    public string VoiceModeLabel => CurrentVoiceMode switch
    {
        "Whisper" => "🟣",
        "Normal"  => "🟢",
        "Shout"   => "🟡",
        "Broadcast" => "🔵",
        _ => "🎙"
    };

    partial void OnCurrentVoiceModeChanged(string value)
        => OnPropertyChanged(nameof(VoiceModeLabel));
}
