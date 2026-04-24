using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AudioClient.Core.Models;

namespace AudioClient.GUI.ViewModels;

public partial class SessionDetailViewModel : ObservableObject
{
    [ObservableProperty] private string _sessionName = "";
    [ObservableProperty] private string _accessLevel = "";
    [ObservableProperty] private string _userCountText = "";
    [ObservableProperty] private bool _isHost = false;
    [ObservableProperty] private bool _hasSession = false;
    [ObservableProperty] private bool _isEditingName = false;
    [ObservableProperty] private string _editNameValue = "";
    [ObservableProperty] private bool _isSettingsOpen = false;

    public Action? OnLeaveRequested { get; set; }
    public Action<string>? OnSetName { get; set; }
    public Action<string>? OnSetAccessLevel { get; set; }

    public void Update(WorldInfo? info)
    {
        HasSession = info != null;
        if (info == null) { SessionName = ""; AccessLevel = ""; UserCountText = ""; IsHost = false; IsSettingsOpen = false; return; }
        SessionName = info.Name;
        AccessLevel = info.AccessLevel;
        UserCountText = $"{info.UserCount}/{info.MaxUserCount}";
        IsHost = info.IsHost;
    }

    [RelayCommand]
    private void Leave() => OnLeaveRequested?.Invoke();

    [RelayCommand]
    private void BeginEditName()
    {
        EditNameValue = SessionName;
        IsEditingName = true;
    }

    [RelayCommand]
    private void CommitName()
    {
        IsEditingName = false;
        if (!string.IsNullOrWhiteSpace(EditNameValue))
            OnSetName?.Invoke(EditNameValue);
    }

    [RelayCommand]
    private void CancelEditName() => IsEditingName = false;

    [RelayCommand]
    private void SetAccessLevel(string level) => OnSetAccessLevel?.Invoke(level);

    [RelayCommand]
    private void OpenSettings() => IsSettingsOpen = true;

    [RelayCommand]
    private void CloseSettings()
    {
        IsSettingsOpen = false;
        IsEditingName = false;
    }
}
