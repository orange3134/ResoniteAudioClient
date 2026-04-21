using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AudioClient.Core.Models;

namespace AudioClient.GUI.ViewModels;

public partial class SessionPreviewViewModel : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _host = "";
    [ObservableProperty] private string _userCountText = "";
    [ObservableProperty] private string _accessLevel = "";
    [ObservableProperty] private string _joinUrl = "";
    [ObservableProperty] private bool _isVisible = false;

    public ObservableCollection<string> Users { get; } = new();

    public Action<string>? OnJoinRequested { get; set; }

    [RelayCommand]
    private void Close() => IsVisible = false;

    [RelayCommand]
    private void Join()
    {
        if (!string.IsNullOrEmpty(JoinUrl))
            OnJoinRequested?.Invoke(JoinUrl);
        IsVisible = false;
    }

    public void ShowFromBrowse(ActiveSessionInfo info)
    {
        Name = info.Name;
        Host = info.HostUsername;
        UserCountText = $"{info.JoinedUsers}/{info.MaximumUsers}";
        AccessLevel = info.AccessLevel;
        JoinUrl = info.PreferredUrl;
        Users.Clear();
        foreach (var u in info.SessionUsers)
            Users.Add(u);
        IsVisible = true;
    }

    public void ShowFromContact(ContactInfo contact)
    {
        Name = contact.CurrentSessionName ?? $"{contact.Username}'s session";
        Host = contact.CurrentSessionHost ?? contact.Username;
        UserCountText = $"{contact.CurrentSessionUsers}/{contact.CurrentSessionMaxUsers}";
        AccessLevel = contact.CurrentSessionAccessLevel ?? "";
        JoinUrl = contact.CurrentSessionUrl ?? "";
        Users.Clear();
        IsVisible = true;
    }
}
