using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AudioClient.Core.Models;
using AudioClient.GUI.Helpers;

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
    public string IconLetter => UserName.Length > 0 ? UserName[0].ToString().ToUpperInvariant() : "?";

    [ObservableProperty] private Bitmap? _iconBitmap;
    [ObservableProperty] private bool _isSpeaking;

    public MemberItemViewModel(UserInfo u, Func<string, Task<string?>>? fetchIcon)
    {
        UserName = u.UserName; UserId = u.UserId; IsHost = u.IsHost; IsLocal = u.IsLocal;
        IsPresent = u.IsPresentInWorld; Ping = u.Ping; IsContact = u.IsContact;
        _ = LoadIconAsync(u.IconUrl, u.UserId, fetchIcon);
    }

    private async Task LoadIconAsync(string? knownUrl, string? userId, Func<string, Task<string?>>? fetchIcon)
    {
        var url = knownUrl;
        if (url == null && userId != null && fetchIcon != null)
            url = await fetchIcon(userId).ConfigureAwait(false);
        if (url != null)
            IconBitmap = await IconLoader.LoadAsync(url).ConfigureAwait(false);
    }
}

public partial class MemberListViewModel : ObservableObject
{
    [ObservableProperty] private int _memberCount = 0;

    public ObservableCollection<MemberItemViewModel> Members { get; } = new();

    public Func<string, Task<string?>>? FetchIconUrl { get; set; }

    public Action<MemberItemViewModel>? OnMoveToRequested { get; set; }
    public Action<MemberItemViewModel>? OnShowInfoRequested { get; set; }

    public void Update(List<UserInfo> users)
    {
        var prevSpeaking = Members.Where(m => m.UserId != null && m.IsSpeaking)
                                  .Select(m => m.UserId!)
                                  .ToHashSet();
        Members.Clear();
        foreach (var u in users)
        {
            var vm = new MemberItemViewModel(u, FetchIconUrl);
            if (u.UserId != null && prevSpeaking.Contains(u.UserId))
                vm.IsSpeaking = true;
            Members.Add(vm);
        }
        MemberCount = users.Count;
    }

    public void UpdateSpeaking(Dictionary<string, bool> speaking)
    {
        foreach (var member in Members)
        {
            if (member.UserId != null && speaking.TryGetValue(member.UserId, out var isSpeaking))
                member.IsSpeaking = isSpeaking;
            else
                member.IsSpeaking = false;
        }
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
