using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AudioClient.Core.Models;

namespace AudioClient.GUI.ViewModels;

public partial class StatusBarViewModel : ObservableObject
{
    [ObservableProperty] private bool _isMuted = false;
    [ObservableProperty] private float _masterVolume = 1f;
    [ObservableProperty] private float _soundEffectVolume = 1f;
    [ObservableProperty] private float _multimediaVolume = 1f;
    [ObservableProperty] private float _voiceVolume = 1f;
    [ObservableProperty] private float _uIVolume = 1f;
    [ObservableProperty] private string _muteButtonText = "🎤";
    [ObservableProperty] private string _currentVoiceMode = "Normal";
    [ObservableProperty] private bool _isVoiceModePopupOpen = false;
    [ObservableProperty] private bool _isVolumePopupOpen = false;

    public Action? OnToggleMute { get; set; }
    public Action<float>? OnSetVolume { get; set; }
    public Action<float>? OnSetSoundEffectVolume { get; set; }
    public Action<float>? OnSetMultimediaVolume { get; set; }
    public Action<float>? OnSetVoiceVolume { get; set; }
    public Action<float>? OnSetUIVolume { get; set; }
    public Action<string>? OnSetVoiceMode { get; set; }
    public Action? OnShowLogin { get; set; }

    partial void OnIsMutedChanged(bool value)
        => MuteButtonText = value ? "🔇" : "🎤";

    partial void OnMasterVolumeChanged(float value)
        => OnSetVolume?.Invoke(value);

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
        MasterVolume = vol.Master;
        SoundEffectVolume = vol.SoundEffect;
        MultimediaVolume = vol.Multimedia;
        VoiceVolume = vol.Voice;
        UIVolume = vol.UI;
    }

    [RelayCommand]
    private void ToggleMute() => OnToggleMute?.Invoke();

    [RelayCommand]
    private void ShowLogin() => OnShowLogin?.Invoke();

    [RelayCommand]
    private void ToggleVoiceModePopup() => IsVoiceModePopupOpen = !IsVoiceModePopupOpen;

    [RelayCommand]
    private void ToggleVolumePopup() => IsVolumePopupOpen = !IsVolumePopupOpen;

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
