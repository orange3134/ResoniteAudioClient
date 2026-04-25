using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AudioClient.GUI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private bool _isVisible = false;
    [ObservableProperty] private bool _autoEquipAudioClientAvatar = true;

    public Action<bool>? AutoEquipAudioClientAvatarChanged { get; set; }

    partial void OnAutoEquipAudioClientAvatarChanged(bool value)
        => AutoEquipAudioClientAvatarChanged?.Invoke(value);

    public void Show() => IsVisible = true;

    [RelayCommand]
    private void Close() => IsVisible = false;
}
