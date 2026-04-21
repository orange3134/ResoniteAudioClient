using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AudioClient.Core.Models;
using AudioClient.GUI.Helpers;

namespace AudioClient.GUI.ViewModels;

public partial class ContactItemViewModel : ObservableObject
{
    public ContactInfo Info { get; }
    public string Username => Info.Username;
    public string? SessionName => Info.CurrentSessionName;
    public bool HasSession => Info.CurrentSessionUrl != null;
    public string IconLetter => Info.Username.Length > 0 ? Info.Username[0].ToString().ToUpperInvariant() : "?";

    public ISolidColorBrush StatusBrush { get; }

    [ObservableProperty] private Bitmap? _iconBitmap;

    public ContactItemViewModel(ContactInfo info, Func<string, Task<string?>>? fetchIcon)
    {
        Info = info;
        StatusBrush = info.OnlineStatus switch
        {
            "Online"    => new SolidColorBrush(Color.Parse("#43b581")),
            "Sociable"  => new SolidColorBrush(Color.Parse("#00b8d4")),
            "Away"      => new SolidColorBrush(Color.Parse("#faa61a")),
            "Busy"      => new SolidColorBrush(Color.Parse("#f04747")),
            _           => new SolidColorBrush(Color.Parse("#72767d"))
        };
        _ = LoadIconAsync(info.IconUrl, info.UserId, fetchIcon);
    }

    private async Task LoadIconAsync(string? knownUrl, string userId, Func<string, Task<string?>>? fetchIcon)
    {
        var url = knownUrl;
        if (url == null && fetchIcon != null)
            url = await fetchIcon(userId).ConfigureAwait(false);
        if (url != null)
            IconBitmap = await IconLoader.LoadAsync(url).ConfigureAwait(false);
    }
}

public partial class ContactListViewModel : ObservableObject
{
    [ObservableProperty] private int _contactCount = 0;

    public ObservableCollection<ContactItemViewModel> Contacts { get; } = new();

    public Func<string, Task<string?>>? FetchIconUrl { get; set; }

    public Action<ContactItemViewModel>? OnPreviewRequested { get; set; }

    public void Update(List<ContactInfo> contacts)
    {
        Contacts.Clear();
        foreach (var c in contacts)
            Contacts.Add(new ContactItemViewModel(c, FetchIconUrl));
        ContactCount = contacts.Count;
    }

    [RelayCommand]
    private void Preview(ContactItemViewModel? item)
    {
        if (item == null || !item.HasSession) return;
        OnPreviewRequested?.Invoke(item);
    }
}
