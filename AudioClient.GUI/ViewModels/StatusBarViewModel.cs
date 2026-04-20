using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AudioClient.Core.Models;

namespace AudioClient.GUI.ViewModels;

public partial class StatusBarViewModel : ObservableObject
{
    [ObservableProperty] private bool _isMuted = false;
    [ObservableProperty] private float _masterVolume = 1f;
    [ObservableProperty] private string _muteButtonText = "🎤";

    public Action? OnToggleMute { get; set; }
    public Action<float>? OnSetVolume { get; set; }
    public Action? OnShowLogin { get; set; }

    partial void OnIsMutedChanged(bool value)
        => MuteButtonText = value ? "🔇" : "🎤";

    public void UpdateVolumes(VolumeInfo vol)
        => MasterVolume = vol.Master;

    [RelayCommand]
    private void ToggleMute() => OnToggleMute?.Invoke();

    [RelayCommand]
    private void ShowLogin() => OnShowLogin?.Invoke();

    partial void OnMasterVolumeChanged(float value)
        => OnSetVolume?.Invoke(value);
}
