using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AudioClient.Core.Models;

namespace AudioClient.GUI.ViewModels;

public partial class BrowseSessionItemViewModel : ObservableObject
{
    public ActiveSessionInfo Info { get; }
    public string Name => Info.Name;
    public string Host => Info.HostUsername;
    public string Users => $"{Info.JoinedUsers}/{Info.MaximumUsers}";
    public string AccessLevel => Info.AccessLevel;
    public string Url => Info.PreferredUrl;

    public BrowseSessionItemViewModel(ActiveSessionInfo info) => Info = info;
}

public partial class BrowseSessionsViewModel : ObservableObject
{
    [ObservableProperty] private bool _isLoading = false;
    [ObservableProperty] private bool _activeOnly = true;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private BrowseSessionItemViewModel? _selectedSession;

    public ObservableCollection<BrowseSessionItemViewModel> Sessions { get; } = new();

    public Action? OnRefreshRequested { get; set; }
    public Action<ActiveSessionInfo>? OnPreviewRequested { get; set; }

    [RelayCommand]
    private void Refresh() => OnRefreshRequested?.Invoke();

    [RelayCommand]
    private void Preview(BrowseSessionItemViewModel? item)
    {
        if (item == null) return;
        OnPreviewRequested?.Invoke(item.Info);
    }

    public void Update(List<ActiveSessionInfo> sessions, string search = "")
    {
        Sessions.Clear();
        foreach (var s in sessions)
        {
            if (!string.IsNullOrWhiteSpace(search) &&
                !s.Name.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                !s.HostUsername.Contains(search, StringComparison.OrdinalIgnoreCase))
                continue;
            Sessions.Add(new BrowseSessionItemViewModel(s));
        }
        IsLoading = false;
    }
}
