using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AudioClient.Core.Models;

namespace AudioClient.GUI.ViewModels;

public partial class MemberItemViewModel : ObservableObject
{
    public string UserName { get; }
    public string? UserId { get; }
    public bool IsHost { get; }
    public bool IsLocal { get; }
    public bool IsPresent { get; }
    public bool IsContact { get; }
    public int Ping { get; }
    public string StatusDot => IsPresent ? "●" : "◌";
    public string Label => IsLocal ? $"{UserName} (You){(IsHost ? " ♔" : "")}" : $"{UserName}{(IsHost ? " ♔" : "")}";

    public MemberItemViewModel(UserInfo u)
    {
        UserName = u.UserName; UserId = u.UserId; IsHost = u.IsHost; IsLocal = u.IsLocal;
        IsPresent = u.IsPresentInWorld; Ping = u.Ping; IsContact = u.IsContact;
    }
}

public partial class MemberListViewModel : ObservableObject
{
    [ObservableProperty] private int _memberCount = 0;

    public ObservableCollection<MemberItemViewModel> Members { get; } = new();

    public Action<MemberItemViewModel>? OnMoveToRequested { get; set; }
    public Action<MemberItemViewModel>? OnShowInfoRequested { get; set; }

    public void Update(List<UserInfo> users)
    {
        Members.Clear();
        foreach (var u in users)
            Members.Add(new MemberItemViewModel(u));
        MemberCount = users.Count;
    }

    [RelayCommand]
    private void MoveTo(MemberItemViewModel? item)
    {
        if (item == null) return;
        OnMoveToRequested?.Invoke(item);
    }

    [RelayCommand]
    private void ShowInfo(MemberItemViewModel? item)
    {
        if (item == null) return;
        OnShowInfoRequested?.Invoke(item);
    }
}
