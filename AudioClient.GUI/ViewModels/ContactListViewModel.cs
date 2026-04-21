using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AudioClient.Core.Models;

namespace AudioClient.GUI.ViewModels;

public partial class ContactItemViewModel : ObservableObject
{
    public ContactInfo Info { get; }
    public string Username => Info.Username;
    public string? SessionName => Info.CurrentSessionName;
    public bool HasSession => Info.CurrentSessionUrl != null;

    public string StatusEmoji => Info.OnlineStatus switch
    {
        "Online" or "Sociable" => "🟢",
        "Away"                 => "🟡",
        "Busy"                 => "🔴",
        _                      => "⚪"
    };

    public ContactItemViewModel(ContactInfo info) => Info = info;
}

public partial class ContactListViewModel : ObservableObject
{
    [ObservableProperty] private int _contactCount = 0;

    public ObservableCollection<ContactItemViewModel> Contacts { get; } = new();

    public Action<ContactItemViewModel>? OnPreviewRequested { get; set; }

    public void Update(List<ContactInfo> contacts)
    {
        Contacts.Clear();
        foreach (var c in contacts)
            Contacts.Add(new ContactItemViewModel(c));
        ContactCount = contacts.Count;
    }

    [RelayCommand]
    private void Preview(ContactItemViewModel? item)
    {
        if (item == null || !item.HasSession) return;
        OnPreviewRequested?.Invoke(item);
    }
}
